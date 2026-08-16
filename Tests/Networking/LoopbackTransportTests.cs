using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The transport contract, exercised on the loopback implementation: connection events, the four
    /// delivery classes under latency/jitter/loss, backpressure and disconnects.
    /// </summary>
    [TestFixture]
    public class LoopbackTransportTests
    {
        private sealed class RecordingSink : ITransportEvents
        {
            public readonly List<ConnectionId> Connected = new();
            public readonly List<(ConnectionId, DisconnectReason)> Disconnected = new();
            public readonly List<(ConnectionId, Delivery, byte[])> Data = new();

            public void OnConnected(ConnectionId connection) => Connected.Add(connection);
            public void OnDisconnected(ConnectionId connection, DisconnectReason reason) => Disconnected.Add((connection, reason));
            public void OnData(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload) => Data.Add((connection, delivery, payload.ToArray()));
        }

        private ManualClock _clock;
        private LoopbackNetwork _network;
        private LoopbackTransport _server;
        private LoopbackTransport _client;
        private RecordingSink _serverSink;
        private RecordingSink _clientSink;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _network = new LoopbackNetwork(_clock, seed: 7);
            _server = _network.CreateTransport();
            _server.Name = "server";
            _client = _network.CreateTransport();
            _client.Name = "client";
            _serverSink = new RecordingSink();
            _clientSink = new RecordingSink();
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            _server.Dispose();
        }

        private void PollBoth()
        {
            _server.Poll(_serverSink);
            _client.Poll(_clientSink);
        }

        private void Connect()
        {
            Assert.IsTrue(_server.StartServer(TransportEndpoint.Loopback(7777)));
            Assert.IsTrue(_client.StartClient(TransportEndpoint.Loopback(7777)));
            PollBoth();
        }

        private static byte[] Payload(int index) => BitConverter.GetBytes(index);

        #region - Connection -

        [Test]
        public void ClientAndServerBothLearnOfTheConnection()
        {
            Connect();
            Assert.AreEqual(1, _serverSink.Connected.Count);
            Assert.AreEqual(1, _clientSink.Connected.Count);
            Assert.AreEqual(_client.ServerConnection, _clientSink.Connected[0]);
            Assert.IsTrue(_client.ServerConnection.IsValid);
            Assert.AreEqual(1, _server.ConnectionCount);
        }

        [Test]
        public void ConnectingToNothingFails()
        {
            Assert.IsFalse(_client.StartClient(TransportEndpoint.Loopback(9999)));
            Assert.IsFalse(_client.IsRunning);
        }

        [Test]
        public void TwoServersCannotShareAPort()
        {
            Assert.IsTrue(_server.StartServer(TransportEndpoint.Loopback(7777)));
            var other = _network.CreateTransport();
            Assert.IsFalse(other.StartServer(TransportEndpoint.Loopback(7777)));
        }

        [Test]
        public void EachClientGetsItsOwnConnectionOnTheServer()
        {
            Connect();
            var second = _network.CreateTransport();
            var secondSink = new RecordingSink();
            Assert.IsTrue(second.StartClient(TransportEndpoint.Loopback(7777)));
            _server.Poll(_serverSink);
            second.Poll(secondSink);

            Assert.AreEqual(2, _serverSink.Connected.Count);
            Assert.AreNotEqual(_serverSink.Connected[0], _serverSink.Connected[1]);
            second.Dispose();
        }

        [Test]
        public void ConnectionEventsAreDelayedByLatency()
        {
            _network.DefaultConditions.LatencySeconds = 0.1;
            _server = _network.CreateTransport();
            _client = _network.CreateTransport();
            Assert.IsTrue(_server.StartServer(TransportEndpoint.Loopback(1)));
            Assert.IsTrue(_client.StartClient(TransportEndpoint.Loopback(1)));

            PollBoth();
            Assert.IsEmpty(_serverSink.Connected);
            Assert.IsEmpty(_clientSink.Connected);

            _clock.Advance(0.1);
            PollBoth();
            Assert.AreEqual(1, _serverSink.Connected.Count);
            Assert.AreEqual(1, _clientSink.Connected.Count);
        }

        #endregion

        #region - Data -

        [Test]
        public void DataFlowsBothWaysOnEveryDeliveryClass()
        {
            Connect();
            var serverConn = _serverSink.Connected[0];
            foreach (Delivery delivery in Enum.GetValues(typeof(Delivery)))
            {
                Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, delivery, Payload((int)delivery)));
                Assert.AreEqual(SendResult.Ok, _server.Send(serverConn, delivery, Payload(100 + (int)delivery)));
            }

            PollBoth();
            Assert.AreEqual(4, _serverSink.Data.Count);
            Assert.AreEqual(4, _clientSink.Data.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual((Delivery)i, _serverSink.Data[i].Item2);
                Assert.AreEqual(i, BitConverter.ToInt32(_serverSink.Data[i].Item3, 0));
                Assert.AreEqual(100 + i, BitConverter.ToInt32(_clientSink.Data[i].Item3, 0));
            }
        }

        [Test]
        public void DataWaitsForLatency()
        {
            Connect();
            _client.Conditions.LatencySeconds = 0.05;
            _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(1));
            _server.Poll(_serverSink);
            Assert.IsEmpty(_serverSink.Data);
            _clock.Advance(0.049);
            _server.Poll(_serverSink);
            Assert.IsEmpty(_serverSink.Data);
            _clock.Advance(0.001);
            _server.Poll(_serverSink);
            Assert.AreEqual(1, _serverSink.Data.Count);
        }

        [Test]
        public void ReliablePacketsArriveInOrderAndCompleteUnderJitter()
        {
            Connect();
            _client.Conditions.LatencySeconds = 0.05;
            _client.Conditions.JitterSeconds = 0.04;
            _client.Conditions.LossFraction = 0.5f;   // must not apply to reliable

            const int count = 200;
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(i)));
            }

            for (int step = 0; step < 50; step++)
            {
                _clock.Advance(0.005);
                _server.Poll(_serverSink);
            }

            Assert.AreEqual(count, _serverSink.Data.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(i, BitConverter.ToInt32(_serverSink.Data[i].Item3, 0), $"packet {i} out of order");
            }
        }

        [Test]
        public void UnreliableSequencedNeverGoesBackwards()
        {
            Connect();
            _client.Conditions.LatencySeconds = 0.05;
            _client.Conditions.JitterSeconds = 0.04;
            _client.Conditions.ReorderFraction = 0.3f;

            const int count = 200;
            for (int i = 0; i < count; i++)
            {
                _client.Send(_client.ServerConnection, Delivery.UnreliableSequenced, Payload(i));
                _clock.Advance(0.001);
                _server.Poll(_serverSink);
            }

            for (int step = 0; step < 100; step++)
            {
                _clock.Advance(0.005);
                _server.Poll(_serverSink);
            }

            Assert.Greater(_serverSink.Data.Count, 0);
            Assert.LessOrEqual(_serverSink.Data.Count, count);
            int last = -1;
            foreach (var (_, _, bytes) in _serverSink.Data)
            {
                int index = BitConverter.ToInt32(bytes, 0);
                Assert.Greater(index, last);
                last = index;
            }

            // With that much reordering, something must have been dropped for being stale.
            Assert.Greater(_server.Stats.PacketsDropped, 0);
        }

        [Test]
        public void UnreliableIsLossyAndReliableIsNot()
        {
            Connect();
            _client.Conditions.LossFraction = 0.5f;
            const int count = 200;
            for (int i = 0; i < count; i++)
            {
                _client.Send(_client.ServerConnection, Delivery.Unreliable, Payload(i));
                _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(i));
            }

            _server.Poll(_serverSink);
            int unreliable = _serverSink.Data.Count(d => d.Item2 == Delivery.Unreliable);
            int reliable = _serverSink.Data.Count(d => d.Item2 == Delivery.ReliableSequenced);
            Assert.AreEqual(count, reliable);
            Assert.Less(unreliable, count);
            Assert.Greater(unreliable, 0);
            Assert.AreEqual(count - unreliable, _client.Stats.PacketsDropped);
        }

        [Test]
        public void OversizedPayloadsAreRejected()
        {
            Connect();
            var big = new byte[_client.MaxPayload(Delivery.ReliableSequenced) + 1];
            Assert.AreEqual(SendResult.TooLarge, _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, big));
            Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, Delivery.ReliableFragmentedSequenced, big));
            Assert.AreEqual(1, _client.Stats.SendsRejected);
        }

        [Test]
        public void BackpressureReportsQueueFullUntilTheReceiverDrains()
        {
            Connect();
            _client.Conditions.SendQueueCapacity = 2;
            Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(1)));
            Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(2)));
            Assert.AreEqual(SendResult.QueueFull, _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(3)));
            // Another delivery class has its own queue.
            Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, Delivery.Unreliable, Payload(4)));

            _server.Poll(_serverSink);
            Assert.AreEqual(SendResult.Ok, _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(3)));
        }

        [Test]
        public void SendingBeforeStartingOrToAnUnknownConnectionFails()
        {
            Assert.AreEqual(SendResult.NotStarted, _client.Send(new ConnectionId(1), Delivery.Unreliable, Payload(1)));
            Connect();
            Assert.AreEqual(SendResult.Disconnected, _client.Send(new ConnectionId(99), Delivery.Unreliable, Payload(1)));
        }

        #endregion

        #region - Disconnect -

        [Test]
        public void ALocalDisconnectIsReportedLocallyNowAndRemotelyAfterLatency()
        {
            Connect();
            _client.Conditions.LatencySeconds = 0.05;
            var serverConn = _serverSink.Connected[0];

            _client.Disconnect(_client.ServerConnection);
            Assert.IsFalse(_client.ServerConnection.IsValid);
            _client.Poll(_clientSink);
            Assert.AreEqual(1, _clientSink.Disconnected.Count);
            Assert.AreEqual(DisconnectReason.Local, _clientSink.Disconnected[0].Item2);

            _server.Poll(_serverSink);
            Assert.IsEmpty(_serverSink.Disconnected);
            _clock.Advance(0.05);
            _server.Poll(_serverSink);
            Assert.AreEqual(1, _serverSink.Disconnected.Count);
            Assert.AreEqual((serverConn, DisconnectReason.Remote), _serverSink.Disconnected[0]);
            Assert.AreEqual(0, _server.ConnectionCount);

            Assert.AreEqual(SendResult.Disconnected, _server.Send(serverConn, Delivery.Unreliable, Payload(1)));
        }

        [Test]
        public void DataInFlightStillArrivesBeforeTheDisconnect()
        {
            Connect();
            _client.Conditions.LatencySeconds = 0.05;
            _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(1));
            _client.Disconnect(_client.ServerConnection);
            _clock.Advance(0.05);
            _server.Poll(_serverSink);
            Assert.AreEqual(1, _serverSink.Data.Count);
            Assert.AreEqual(1, _serverSink.Disconnected.Count);
        }

        [Test]
        public void ServerShutdownDisconnectsEveryClient()
        {
            Connect();
            _server.Shutdown();
            PollBoth();
            Assert.AreEqual(1, _serverSink.Disconnected.Count);
            Assert.AreEqual(DisconnectReason.Local, _serverSink.Disconnected[0].Item2);
            Assert.AreEqual(1, _clientSink.Disconnected.Count);
            Assert.AreEqual(DisconnectReason.Remote, _clientSink.Disconnected[0].Item2);
            Assert.IsFalse(_client.ServerConnection.IsValid);
            Assert.IsFalse(_server.IsRunning);
        }

        [Test]
        public void ADisconnectRaisedInsideOnDataStopsFurtherDelivery()
        {
            Connect();
            var serverConn = _serverSink.Connected[0];
            _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(1));
            _client.Send(_client.ServerConnection, Delivery.ReliableSequenced, Payload(2));

            var closingSink = new ClosingSink(_server);
            _server.Poll(closingSink);
            Assert.AreEqual(1, closingSink.DataCount);
            _server.Poll(closingSink);
            Assert.AreEqual(1, closingSink.DataCount);
            Assert.AreEqual(1, closingSink.DisconnectCount);
            Assert.AreEqual(0, _server.ConnectionCount);
            _ = serverConn;
        }

        private sealed class ClosingSink : ITransportEvents
        {
            private readonly IVaporTransport _transport;
            public int DataCount;
            public int DisconnectCount;
            public ClosingSink(IVaporTransport transport) => _transport = transport;
            public void OnConnected(ConnectionId connection) { }
            public void OnDisconnected(ConnectionId connection, DisconnectReason reason) => DisconnectCount++;
            public void OnData(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload)
            {
                DataCount++;
                _transport.Disconnect(connection);
            }
        }

        #endregion
    }
}
