using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The session over loopback: handshake and approval, client ids, data both ways, ticks, time
    /// sync, and every way a connection can end.
    /// </summary>
    [TestFixture]
    public class NetworkSessionTests
    {
        /// <summary>A clock that reads a constant offset from another — a client whose wall clock disagrees with the server's.</summary>
        private sealed class OffsetClock : INetworkClock
        {
            private readonly INetworkClock _inner;
            private readonly double _offset;
            public OffsetClock(INetworkClock inner, double offset) { _inner = inner; _offset = offset; }
            public double Now => _inner.Now + _offset;
        }

        private ManualClock _clock;
        private LoopbackNetwork _network;
        private LoopbackTransport _serverTransport;
        private LoopbackTransport _clientTransport;
        private NetworkSession _server;
        private NetworkSession _client;

        private readonly List<ulong> _serverConnected = new();
        private readonly List<(ulong, SessionDisconnectReason)> _serverDisconnected = new();
        private readonly List<SessionDisconnectReason> _clientDisconnected = new();
        private int _clientConnectedCount;
        private readonly List<(ulong, Delivery, byte[])> _serverData = new();
        private readonly List<(ulong, Delivery, byte[])> _clientData = new();

        [SetUp]
        public void SetUp()
        {
            // NUnit reuses the fixture instance; the recorders must start empty for every test.
            _serverConnected.Clear();
            _serverDisconnected.Clear();
            _clientDisconnected.Clear();
            _clientConnectedCount = 0;
            _serverData.Clear();
            _clientData.Clear();

            // Process-wide, and another fixture may have left it counting.
            NetworkRoles.Reset();

            _clock = new ManualClock();
            _network = new LoopbackNetwork(_clock, seed: 3);
            _serverTransport = _network.CreateTransport();
            _clientTransport = _network.CreateTransport();
            _server = new NetworkSession(_serverTransport, _clock, new SessionConfig { TickRate = 30 });
            _client = new NetworkSession(_clientTransport, _clock, new SessionConfig { TickRate = 30 });
            Wire(_server, _client);
        }

        private void Wire(NetworkSession server, NetworkSession client)
        {
            server.ClientConnected += id => _serverConnected.Add(id);
            server.ClientDisconnected += (id, reason) => _serverDisconnected.Add((id, reason));
            server.Data += (id, delivery, reader) => _serverData.Add((id, delivery, reader.RemainingSpan.ToArray()));
            client.Connected += () => _clientConnectedCount++;
            client.Disconnected += reason => _clientDisconnected.Add(reason);
            client.Data += (id, delivery, reader) => _clientData.Add((id, delivery, reader.RemainingSpan.ToArray()));
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            _server.Dispose();
        }

        private void Step(double dt = 0)
        {
            _clock.Advance(dt);
            _server.Update();
            _client.Update();
        }

        private void Connect(ReadOnlySpan<byte> payload = default)
        {
            Assert.IsTrue(_server.StartServer(TransportEndpoint.Loopback(1)));
            Assert.IsTrue(_client.StartClient(TransportEndpoint.Loopback(1), payload));
            // connect events, hello, welcome — each a poll apart on a zero-latency link
            for (int i = 0; i < 4; i++) Step();
        }

        #region - Handshake -

        [Test]
        public void AClientIsWelcomedAndAssignedAnId()
        {
            Connect();
            Assert.IsTrue(_client.IsConnected);
            Assert.AreEqual(1, _clientConnectedCount);
            Assert.AreEqual(1ul, _client.LocalClientId);
            CollectionAssert.AreEqual(new[] { 1ul }, _serverConnected);
            CollectionAssert.AreEqual(new[] { 1ul }, _server.ConnectedClientIds);
            Assert.AreEqual(NetworkSession.ServerClientId, _server.LocalClientId);
            Assert.IsTrue(_server.IsClientConnected(1));
        }

        [Test]
        public void TheApprovalHookSeesThePayloadAndCanReject()
        {
            byte[] seen = null;
            _server.Approval = (id, payload) =>
            {
                seen = payload.ToArray();
                return ConnectionApproval.Reject("no");
            };
            Connect(new byte[] { 1, 2, 3 });

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, seen);
            Assert.IsFalse(_client.IsConnected);
            Assert.AreEqual(0, _clientConnectedCount);
            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.Rejected }, _clientDisconnected);
            Assert.IsEmpty(_serverConnected);
            Assert.IsEmpty(_serverDisconnected);
            Assert.IsFalse(_client.IsRunning);
        }

        [Test]
        public void AProtocolMismatchIsRejected()
        {
            _client = new NetworkSession(_clientTransport, _clock, new SessionConfig { ProtocolVersion = 99 });
            Wire(new NetworkSession(_network.CreateTransport(), _clock), _client);   // wire only the client side
            Connect();
            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.ProtocolMismatch }, _clientDisconnected);
            Assert.IsEmpty(_serverConnected);
        }

        [Test]
        public void AFullServerRejectsTheNextClient()
        {
            _server = new NetworkSession(_serverTransport, _clock, new SessionConfig { MaxClients = 1 });
            Wire(_server, new NetworkSession(_network.CreateTransport(), _clock));
            Connect();
            Assert.AreEqual(1, _server.ClientCount);

            var secondTransport = _network.CreateTransport();
            var second = new NetworkSession(secondTransport, _clock);
            var reasons = new List<SessionDisconnectReason>();
            second.Disconnected += reasons.Add;
            Assert.IsTrue(second.StartClient(TransportEndpoint.Loopback(1)));
            for (int i = 0; i < 4; i++)
            {
                Step();
                second.Update();
            }

            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.ServerFull }, reasons);
            Assert.AreEqual(1, _server.ClientCount);
            second.Dispose();
        }

        [Test]
        public void ClientIdsAreNeverReused()
        {
            Connect();
            _client.Disconnect();
            for (int i = 0; i < 3; i++) Step();

            var again = new NetworkSession(_network.CreateTransport(), _clock);
            Assert.IsTrue(again.StartClient(TransportEndpoint.Loopback(1)));
            for (int i = 0; i < 4; i++)
            {
                Step();
                again.Update();
            }

            Assert.AreEqual(2ul, again.LocalClientId);
            again.Dispose();
        }

        #endregion

        #region - Data -

        [Test]
        public void DataFlowsBothWaysWithTheSessionHeaderStripped()
        {
            Connect();
            Assert.AreEqual(SendResult.Ok, _client.SendToServer(Delivery.ReliableSequenced, new byte[] { 10, 11 }));
            Assert.AreEqual(SendResult.Ok, _server.Send(1, Delivery.Unreliable, new byte[] { 20 }));
            Assert.AreEqual(SendResult.Ok, _server.Broadcast(Delivery.UnreliableSequenced, new byte[] { 30, 31, 32 }));
            Step();

            Assert.AreEqual(1, _serverData.Count);
            Assert.AreEqual((1ul, Delivery.ReliableSequenced), (_serverData[0].Item1, _serverData[0].Item2));
            CollectionAssert.AreEqual(new byte[] { 10, 11 }, _serverData[0].Item3);

            Assert.AreEqual(2, _clientData.Count);
            Assert.AreEqual(NetworkSession.ServerClientId, _clientData[0].Item1);
            CollectionAssert.AreEqual(new byte[] { 20 }, _clientData[0].Item3);
            CollectionAssert.AreEqual(new byte[] { 30, 31, 32 }, _clientData[1].Item3);
        }

        [Test]
        public void SendsToUnknownOrDisconnectedClientsFail()
        {
            Connect();
            Assert.AreEqual(SendResult.Disconnected, _server.Send(42, Delivery.Unreliable, new byte[] { 1 }));
            Assert.AreEqual(SendResult.NotStarted, _client.Send(1, Delivery.Unreliable, new byte[] { 1 }));
        }

        [Test]
        public void MaxPayloadAccountsForTheHeader()
        {
            Connect();
            Assert.AreEqual(_clientTransport.MaxPayload(Delivery.ReliableSequenced) - 1, _client.MaxPayload(Delivery.ReliableSequenced));
            var exact = new byte[_client.MaxPayload(Delivery.ReliableSequenced)];
            Assert.AreEqual(SendResult.Ok, _client.SendToServer(Delivery.ReliableSequenced, exact));
            Assert.AreEqual(SendResult.TooLarge, _client.SendToServer(Delivery.ReliableSequenced, new byte[exact.Length + 1]));
        }

        #endregion

        #region - Ticks and time -

        [Test]
        public void TicksFireAtTheConfiguredRate()
        {
            Connect();
            var serverTicks = new List<uint>();
            _server.Tick += (tick, dt) => serverTicks.Add(tick);
            for (int i = 0; i < 105; i++) Step(0.01);   // 1.05 s in 10 ms steps: 31 ticks at 30 Hz, with margin either side
            Assert.AreEqual(31, serverTicks.Count);
            Assert.AreEqual(1u, serverTicks[0]);
            Assert.AreEqual(31u, serverTicks[30]);
        }

        [Test]
        public void ALongStallDoesNotReplayEveryMissedTick()
        {
            Connect();
            int ticks = 0;
            _server.Tick += (_, _) => ticks++;
            Step(10.0);
            Assert.AreEqual(_server.Config.MaxTicksPerUpdate, ticks);
            Step(0.05);
            Assert.AreEqual(_server.Config.MaxTicksPerUpdate + 1, ticks);
        }

        [Test]
        public void TheClientTickIsSeededFromTheServersAtWelcome()
        {
            Assert.IsTrue(_server.StartServer(TransportEndpoint.Loopback(1)));
            for (int i = 0; i < 60; i++) Step(1.0 / 30.0);   // server ticks ahead
            uint before = _server.LocalTick;
            Assert.IsTrue(_client.StartClient(TransportEndpoint.Loopback(1)));
            for (int i = 0; i < 4; i++) Step();
            Assert.GreaterOrEqual(_client.LocalTick, before);
            Assert.LessOrEqual(_client.LocalTick, _server.LocalTick);
        }

        [Test]
        public void TimeSyncRecoversTheServerClockThroughLatencyAndAClockOffset()
        {
            _clientTransport.Conditions.LatencySeconds = 0.05;
            _serverTransport.Conditions.LatencySeconds = 0.05;
            var skewed = new OffsetClock(_clock, +1000.0);
            _client = new NetworkSession(_clientTransport, skewed, new SessionConfig { PingIntervalSeconds = 0.2 });
            Wire(new NetworkSession(_network.CreateTransport(), _clock), _client);

            Assert.IsTrue(_server.StartServer(TransportEndpoint.Loopback(1)));
            Assert.IsTrue(_client.StartClient(TransportEndpoint.Loopback(1)));
            for (int i = 0; i < 300; i++) Step(0.01);   // 3 seconds

            Assert.IsTrue(_client.IsConnected);
            Assert.IsTrue(_client.HasTimeSync);
            Assert.AreEqual(0.1, _client.RoundTripTime, 0.02);
            Assert.AreEqual(_clock.Now, _client.EstimatedServerTime, 0.02);
            Assert.AreEqual(_server.LocalTick, _client.EstimatedServerTick, 2);
        }

        #endregion

        #region - Disconnects -

        [Test]
        public void AClientLeavingIsReportedOnBothSides()
        {
            Connect();
            _client.Disconnect();
            for (int i = 0; i < 3; i++) Step();

            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.Local }, _clientDisconnected);
            CollectionAssert.AreEqual(new[] { (1ul, SessionDisconnectReason.Remote) }, _serverDisconnected);
            Assert.AreEqual(0, _server.ClientCount);
            Assert.IsFalse(_client.IsRunning);
        }

        [Test]
        public void TheServerCanKickAClientWithAReason()
        {
            Connect();
            _server.DisconnectClient(1, SessionDisconnectReason.Rejected);
            for (int i = 0; i < 3; i++) Step();

            CollectionAssert.AreEqual(new[] { (1ul, SessionDisconnectReason.Rejected) }, _serverDisconnected);
            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.Rejected }, _clientDisconnected);
            Assert.IsFalse(_client.IsConnected);
        }

        [Test]
        public void DisconnectReasonCannotBeOvertakenByJitter()
        {
            Connect();
            // With seed 3, consuming two samples makes the reason's jitter later than the following
            // disconnect's jitter. The disconnect must still behave as a reliable-data barrier.
            _network.Random.NextDouble();
            _network.Random.NextDouble();
            _serverTransport.Conditions.LatencySeconds = 0.1;
            _serverTransport.Conditions.JitterSeconds = 0.05;

            _server.DisconnectClient(1, SessionDisconnectReason.Rejected);
            Step(0.2);
            Step(0.2);

            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.Rejected }, _clientDisconnected);
        }

        [Test]
        public void ServerShutdownTellsEveryClient()
        {
            Connect();
            _server.Shutdown();
            for (int i = 0; i < 3; i++) Step();
            CollectionAssert.AreEqual(new[] { (1ul, SessionDisconnectReason.ServerShutdown) }, _serverDisconnected);
            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.ServerShutdown }, _clientDisconnected);
            Assert.IsFalse(_server.IsRunning);
        }

        [Test]
        public void ASilentClientTimesOut()
        {
            Connect();
            // Only the server keeps running; the client stops updating (and so stops pinging).
            for (int i = 0; i < 20; i++)
            {
                _clock.Advance(1.0);
                _server.Update();
            }

            CollectionAssert.AreEqual(new[] { (1ul, SessionDisconnectReason.Timeout) }, _serverDisconnected);
            Assert.AreEqual(0, _server.ClientCount);
        }

        [Test]
        public void ASilentServerTimesOutOnTheClient()
        {
            Connect();
            for (int i = 0; i < 20; i++)
            {
                _clock.Advance(1.0);
                _client.Update();
            }

            CollectionAssert.AreEqual(new[] { SessionDisconnectReason.Timeout }, _clientDisconnected);
            Assert.IsFalse(_client.IsRunning);
        }

        [Test]
        public void AConnectionThatNeverSaysHelloIsDropped()
        {
            Assert.IsTrue(_server.StartServer(TransportEndpoint.Loopback(1)));
            var mute = _network.CreateTransport();
            Assert.IsTrue(mute.StartClient(TransportEndpoint.Loopback(1)));
            var sink = new NullSink();
            for (int i = 0; i < 20; i++)
            {
                _clock.Advance(1.0);
                _server.Update();
                mute.Poll(sink);
            }

            Assert.IsEmpty(_serverConnected);
            Assert.IsEmpty(_serverDisconnected);
            Assert.AreEqual(0, _serverTransport.ConnectionCount);
            Assert.AreEqual(1, sink.Disconnects);
            mute.Dispose();
        }

        private sealed class NullSink : ITransportEvents
        {
            public int Disconnects;
            public void OnConnected(ConnectionId connection) { }
            public void OnDisconnected(ConnectionId connection, DisconnectReason reason) => Disconnects++;
            public void OnData(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload) { }
        }

        [Test]
        public void ADedicatedServerIsNotAHostHoweverManyClientsAttach()
        {
            Connect();
            Assert.IsFalse(_server.IsHost);
            Assert.IsTrue(_server.IsServer);
            Assert.IsFalse(_server.IsClient, "a server with a client attached is still only a server");
        }

        [Test]
        public void AHostIsOneSessionThatIsBothHalves()
        {
            Assert.IsTrue(_server.StartHost(TransportEndpoint.Loopback(1)));

            Assert.IsTrue(_server.IsHost);
            Assert.IsTrue(_server.IsServer, "it is the authority");
            Assert.IsTrue(_server.IsClient, "and it carries a player");
            Assert.AreEqual(1, NetworkRoles.Servers, "one session, counted as each");
            Assert.AreEqual(1, NetworkRoles.Clients);

            // The player has an id of its own, not the server's: that is what keeps an owner-only
            // thing off everything the server owns.
            Assert.AreNotEqual(NetworkSession.ServerClientId, _server.LocalPlayerClientId);
            Assert.AreEqual(_server.LocalPlayerClientId, _server.LocalClientId, "ownership answers as the player");

            // Not announced until the world behind it is up.
            CollectionAssert.IsEmpty(_serverConnected);
            CollectionAssert.DoesNotContain(_server.ConnectedClientIds, _server.LocalPlayerClientId);

            _server.AdmitLocalPlayer();
            CollectionAssert.AreEqual(new[] { _server.LocalPlayerClientId }, _serverConnected, "the local player joins through the same event a remote one does");
            CollectionAssert.Contains(new List<ulong>(_server.ConnectedClientIds), _server.LocalPlayerClientId);
            Assert.IsTrue(_server.IsClientConnected(_server.LocalPlayerClientId), "it is connected, with no connection to ask");

            _server.AdmitLocalPlayer();
            Assert.AreEqual(1, _serverConnected.Count, "admitting twice is not two joins");

            _server.ReleaseLocalPlayer();
            Assert.AreEqual(1, _serverDisconnected.Count, "and it leaves the same way, which is the only chance to save it");
            Assert.AreEqual(_server.LocalPlayerClientId, _serverDisconnected[0].Item1);
        }

        [Test]
        public void AHostItsOwnApprovalRefusesDoesNotStart()
        {
            _server.Approval = (_, _) => ConnectionApproval.Reject("not today");

            LogAssert.Expect(LogType.Error, new Regex("refused by its own approval"));
            Assert.IsFalse(_server.StartHost(TransportEndpoint.Loopback(1)));
            Assert.IsFalse(_server.IsRunning, "nothing is left listening");
        }

        [Test]
        public void AHostsApprovalSeesThePayloadItWasStartedWith()
        {
            byte[] seen = null;
            _server.Approval = (_, payload) => { seen = payload.ToArray(); return ConnectionApproval.Approve(); };

            Assert.IsTrue(_server.StartHost(TransportEndpoint.Loopback(1), new byte[] { 7, 9 }));
            CollectionAssert.AreEqual(new byte[] { 7, 9 }, seen, "the host is judged on what StartHost was given, as a client is on what it sent");
        }

        #endregion
    }
}
