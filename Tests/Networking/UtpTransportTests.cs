using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Vapor.Networking;
using Vapor.Networking.Utp;
using LogType = UnityEngine.LogType;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The UTP transport over real UDP on 127.0.0.1: connect, every delivery class including a
    /// fragmented payload, disconnect — and a full session handshake on top of it. Real sockets and
    /// real time, so these poll with small sleeps and generous timeouts.
    /// </summary>
    [TestFixture]
    public class UtpTransportTests
    {
        private const ushort k_Port = 47811;
        private const int k_TimeoutMs = 8000;

        private sealed class RecordingSink : ITransportEvents
        {
            public readonly List<ConnectionId> Connected = new();
            public readonly List<(ConnectionId, DisconnectReason)> Disconnected = new();
            public readonly List<(ConnectionId, Delivery, byte[])> Data = new();
            public void OnConnected(ConnectionId connection) => Connected.Add(connection);
            public void OnDisconnected(ConnectionId connection, DisconnectReason reason) => Disconnected.Add((connection, reason));
            public void OnData(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload) => Data.Add((connection, delivery, payload.ToArray()));
        }

        private UtpTransport _server;
        private UtpTransport _client;
        private RecordingSink _serverSink;
        private RecordingSink _clientSink;

        [SetUp]
        public void SetUp()
        {
            _server = new UtpTransport();
            _client = new UtpTransport();
            _serverSink = new RecordingSink();
            _clientSink = new RecordingSink();
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            _server.Dispose();
        }

        private bool PumpUntil(Func<bool> condition, Action extra = null)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < k_TimeoutMs)
            {
                _server.Poll(_serverSink);
                _client.Poll(_clientSink);
                extra?.Invoke();
                if (condition()) return true;
                Thread.Sleep(2);
            }

            return condition();
        }

        private void Connect()
        {
            Assert.IsTrue(_server.StartServer(TransportEndpoint.LocalHost(k_Port)), "bind");
            Assert.IsTrue(_client.StartClient(TransportEndpoint.LocalHost(k_Port)), "connect");
            Assert.IsTrue(_client.ServerConnection.IsValid);
            Assert.IsTrue(PumpUntil(() => _serverSink.Connected.Count == 1 && _clientSink.Connected.Count == 1), "connection did not complete");
        }

        [Test]
        public void ConnectsAndExchangesEveryDeliveryClass()
        {
            Connect();
            var serverConn = _serverSink.Connected[0];
            Assert.AreEqual(_client.ServerConnection, _clientSink.Connected[0]);

            foreach (Delivery delivery in Enum.GetValues(typeof(Delivery)))
            {
                Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, delivery, BitConverter.GetBytes((int)delivery)));
                Assert.AreEqual(SendResult.Ok, _server.Send(serverConn, delivery, BitConverter.GetBytes(100 + (int)delivery)));
            }

            Assert.IsTrue(PumpUntil(() => _serverSink.Data.Count >= 4 && _clientSink.Data.Count >= 4), $"data did not arrive: server {_serverSink.Data.Count}, client {_clientSink.Data.Count}");
            foreach (var (_, delivery, bytes) in _serverSink.Data)
            {
                Assert.AreEqual((int)delivery, BitConverter.ToInt32(bytes, 0));
            }

            foreach (var (_, delivery, bytes) in _clientSink.Data)
            {
                Assert.AreEqual(100 + (int)delivery, BitConverter.ToInt32(bytes, 0));
            }
        }

        [Test]
        public void FragmentedPayloadsLargerThanADatagramArriveIntact()
        {
            Connect();
            var big = new byte[20000];
            new Random(5).NextBytes(big);
            Assert.Greater(big.Length, _client.MaxPayload(Delivery.ReliableSequenced));
            Assert.LessOrEqual(big.Length, _client.MaxPayload(Delivery.ReliableFragmentedSequenced));
            Assert.AreEqual(SendResult.TooLarge, _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, big));
            Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, Delivery.ReliableFragmentedSequenced, big));
            Assert.IsTrue(PumpUntil(() => _serverSink.Data.Count >= 1), "fragmented payload did not arrive");
            CollectionAssert.AreEqual(big, _serverSink.Data[0].Item3);
            Assert.AreEqual(Delivery.ReliableFragmentedSequenced, _serverSink.Data[0].Item2);
        }

        [Test]
        public void ReliableSendsBackpressureInsteadOfFailing()
        {
            Connect();
            var payload = new byte[1000];
            int ok = 0, full = 0;
            // Do not poll the client: nothing acks, so the reliable window fills.
            for (int i = 0; i < 500; i++)
            {
                var result = _server.Send(_serverSink.Connected[0], Delivery.ReliableSequenced, payload);
                if (result == SendResult.Ok) ok++;
                else if (result == SendResult.QueueFull) full++;
                else Assert.Fail($"unexpected {result}");
                _server.Poll(_serverSink);
            }

            Assert.Greater(ok, 0);
            Assert.Greater(full, 0, "the reliable window never filled");
            // Once the client polls and acks, everything sent so far arrives, in order.
            Assert.IsTrue(PumpUntil(() => _clientSink.Data.Count >= ok), $"only {_clientSink.Data.Count} of {ok} arrived");
        }

        [Test]
        public void DisconnectIsSeenOnBothSides()
        {
            Connect();
            _client.Disconnect(_client.ServerConnection);
            Assert.IsTrue(PumpUntil(() => _serverSink.Disconnected.Count == 1 && _clientSink.Disconnected.Count == 1), "disconnect not observed on both sides");
            Assert.AreEqual(DisconnectReason.Local, _clientSink.Disconnected[0].Item2);
            Assert.AreEqual(DisconnectReason.Remote, _serverSink.Disconnected[0].Item2);
            Assert.AreEqual(0, _server.ConnectionCount);
        }

        [Test]
        public void ConnectingToNothingEventuallyReportsADisconnect()
        {
            var settings = new UtpTransportSettings { ConnectTimeoutMs = 100, MaxConnectAttempts = 3 };
            _client = new UtpTransport(settings);
            Assert.IsTrue(_client.StartClient(TransportEndpoint.LocalHost(47899)));
            Assert.IsTrue(PumpUntil(() => _clientSink.Disconnected.Count == 1), "no disconnect for a failed connect");
            Assert.AreEqual(DisconnectReason.Timeout, _clientSink.Disconnected[0].Item2);
        }

        [Test]
        public void InvalidEndpointFailsWithoutCreatingARunningDriver()
        {
            LogAssert.Expect(LogType.Error,
                "UtpTransport: invalid remote endpoint not a numeric address:7777: 'not a numeric address' is not an IP address. Resolve host names before starting the transport.");
            Assert.IsFalse(_client.StartClient(new TransportEndpoint("not a numeric address", 7777)));
            Assert.IsFalse(_client.IsRunning);
            Assert.DoesNotThrow(() => _client.Dispose());
        }

        [Test]
        public void ASessionHandshakesOverUtp()
        {
            var clock = new StopwatchClock();
            var server = new NetworkSession(_server, clock, new SessionConfig { TickRate = 60 });
            var client = new NetworkSession(_client, clock, new SessionConfig { TickRate = 60 });
            var received = new List<byte[]>();
            server.Data += (id, d, r) => received.Add(r.RemainingSpan.ToArray());
            try
            {
                Assert.IsTrue(server.StartServer(TransportEndpoint.LocalHost(k_Port)));
                Assert.IsTrue(client.StartClient(TransportEndpoint.LocalHost(k_Port)));

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < k_TimeoutMs && !client.IsConnected)
                {
                    server.Update();
                    client.Update();
                    Thread.Sleep(2);
                }

                Assert.IsTrue(client.IsConnected, "session did not connect over UTP");
                Assert.AreEqual(1ul, client.LocalClientId);
                Assert.AreEqual(1, server.ClientCount);

                client.SendToServer(Delivery.ReliableSequenced, new byte[] { 4, 2 });
                sw.Restart();
                while (sw.ElapsedMilliseconds < k_TimeoutMs && received.Count == 0)
                {
                    server.Update();
                    client.Update();
                    Thread.Sleep(2);
                }

                Assert.AreEqual(1, received.Count);
                CollectionAssert.AreEqual(new byte[] { 4, 2 }, received[0]);
                Assert.IsTrue(client.HasTimeSync || client.RoundTripTime >= 0);
            }
            finally
            {
                client.Dispose();
                server.Dispose();
            }
        }
    }
}
