using System;
using System.Collections.Generic;

namespace Vapor.Networking
{
    /// <summary>
    /// An in-process transport over a <see cref="LoopbackNetwork"/>: real connections, real delivery
    /// classes, simulated latency, jitter, loss, reordering and backpressure — and no sockets.
    /// </summary>
    /// <remarks>
    /// This is the transport the tests use, and what a host uses for its local client so that host and
    /// dedicated server share one code path. Its semantics are the ones the replicator is allowed to
    /// rely on from any transport:
    /// <list type="bullet">
    /// <item><see cref="Delivery.Unreliable"/>: may be lost or reordered; delivered in arrival order.</item>
    /// <item><see cref="Delivery.UnreliableSequenced"/>: may be lost; a packet older than the newest delivered one is dropped.</item>
    /// <item>Reliable classes: never lost, delivered strictly in send order (head-of-line blocked while a gap is in flight).</item>
    /// <item>Control events (connect, disconnect) travel with the same latency as data.</item>
    /// </list>
    /// </remarks>
    public sealed class LoopbackTransport : IVaporTransport
    {
        private enum PacketKind : byte { Data, Connect, Disconnect }

        private sealed class Packet
        {
            public PacketKind Kind;
            public double DeliverAt;
            public long Stamp;
            public long Seq;
            public Delivery Delivery;
            public byte[] Data;
        }

        /// <summary>One end of a link, as seen from this transport. Its inbox holds what the remote sent us.</summary>
        private sealed class Peer
        {
            public ConnectionId LocalId;
            public LoopbackTransport Remote;
            public ConnectionId RemoteId;
            public readonly List<Packet> Inbox = new();
            public readonly long[] NextSendSeq = new long[DeliveryExtensions.Count];
            public readonly long[] NextExpectedSeq = new long[DeliveryExtensions.Count];   // reliable ordering
            public readonly long[] LastDeliveredSeq = { -1, -1, -1, -1 };                   // unreliable-sequenced
            public bool Closing;
        }

        private readonly LoopbackNetwork _network;
        private readonly Dictionary<int, Peer> _peers = new();
        private readonly List<Peer> _peerScratch = new();
        private readonly List<Packet> _readyScratch = new();
        private readonly List<(ConnectionId, DisconnectReason)> _pendingDisconnects = new();
        private readonly List<(ConnectionId, DisconnectReason)> _disconnectScratch = new();
        private TransportStats _stats;
        private int _nextConnectionId = 1;
        private ushort _listeningPort;
        private bool _listening;

        internal LoopbackTransport(LoopbackNetwork network)
        {
            _network = network;
            Conditions = network.DefaultConditions.Clone();
        }

        /// <summary>Applied to everything this transport sends. Tune per side for asymmetric links.</summary>
        public LinkConditions Conditions { get; set; }

        /// <summary>For log lines and test failure messages.</summary>
        public string Name { get; set; } = "loopback";

        public LoopbackNetwork Network => _network;

        public bool IsRunning { get; private set; }

        public bool IsServer { get; private set; }

        public ConnectionId ServerConnection { get; private set; } = ConnectionId.Invalid;

        public TransportStats Stats => _stats;

        public int ConnectionCount => _peers.Count;

        #region - Lifecycle -

        public bool StartServer(TransportEndpoint bind)
        {
            if (IsRunning)
            {
                return false;
            }

            if (!_network.TryListen(bind.Port, this))
            {
                return false;
            }

            _listening = true;
            _listeningPort = bind.Port;
            IsServer = true;
            IsRunning = true;
            return true;
        }

        public bool StartClient(TransportEndpoint remote)
        {
            if (IsRunning)
            {
                return false;
            }

            if (!_network.TryGetServer(remote.Port, out var server) || !server.IsRunning)
            {
                return false;
            }

            IsServer = false;
            IsRunning = true;

            var local = new Peer { LocalId = new ConnectionId(_nextConnectionId++), Remote = server };
            var onServer = new Peer { LocalId = new ConnectionId(server._nextConnectionId++), Remote = this, RemoteId = local.LocalId };
            local.RemoteId = onServer.LocalId;

            _peers.Add(local.LocalId.Value, local);
            server._peers.Add(onServer.LocalId.Value, onServer);
            ServerConnection = local.LocalId;

            double now = _network.Clock.Now;
            // Each side learns of the connection after one trip across the other's conditions.
            local.Inbox.Add(new Packet { Kind = PacketKind.Connect, DeliverAt = now + Delay(server.Conditions, Delivery.ReliableSequenced), Stamp = _network.NextStamp() });
            onServer.Inbox.Add(new Packet { Kind = PacketKind.Connect, DeliverAt = now + Delay(Conditions, Delivery.ReliableSequenced), Stamp = _network.NextStamp() });
            return true;
        }

        public void Shutdown()
        {
            if (!IsRunning)
            {
                return;
            }

            // Not the poll scratch list: a sink may shut the transport down from inside Poll, which is
            // iterating that list at the time.
            var peers = new List<Peer>(_peers.Values);
            foreach (var peer in peers)
            {
                Disconnect(peer.LocalId);
            }

            if (_listening)
            {
                _network.StopListening(_listeningPort, this);
                _listening = false;
            }

            IsRunning = false;
            ServerConnection = ConnectionId.Invalid;
        }

        public void Dispose() => Shutdown();

        #endregion

        #region - Send -

        public int MaxPayload(Delivery delivery) => _network.MaxPayload(delivery);

        public SendResult Send(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload)
        {
            if (!IsRunning)
            {
                return SendResult.NotStarted;
            }

            if (!_peers.TryGetValue(connection.Value, out var peer) || peer.Closing)
            {
                return SendResult.Disconnected;
            }

            if (payload.Length > MaxPayload(delivery))
            {
                _stats.SendsRejected++;
                return SendResult.TooLarge;
            }

            // The far side's view of us is where our packets land.
            if (!peer.Remote._peers.TryGetValue(peer.RemoteId.Value, out var farPeer))
            {
                // The remote already dropped us and our disconnect notice is still in flight; the packet
                // vanishes exactly as it would on a real link.
                _stats.PacketsSent++;
                _stats.BytesSent += payload.Length;
                return SendResult.Ok;
            }

            if (InFlight(farPeer, delivery) >= Conditions.SendQueueCapacity)
            {
                _stats.SendsRejected++;
                return SendResult.QueueFull;
            }

            long seq = peer.NextSendSeq[(int)delivery]++;
            _stats.PacketsSent++;
            _stats.BytesSent += payload.Length;

            if (!delivery.IsReliable() && Conditions.LossFraction > 0f && _network.Random.NextDouble() < Conditions.LossFraction)
            {
                _stats.PacketsDropped++;
                return SendResult.Ok;
            }

            farPeer.Inbox.Add(new Packet
            {
                Kind = PacketKind.Data,
                DeliverAt = _network.Clock.Now + Delay(Conditions, delivery),
                Stamp = _network.NextStamp(),
                Seq = seq,
                Delivery = delivery,
                Data = payload.ToArray(),
            });
            return SendResult.Ok;
        }

        private static int InFlight(Peer farPeer, Delivery delivery)
        {
            int count = 0;
            var inbox = farPeer.Inbox;
            for (int i = 0; i < inbox.Count; i++)
            {
                if (inbox[i].Kind == PacketKind.Data && inbox[i].Delivery == delivery)
                {
                    count++;
                }
            }

            return count;
        }

        private double Delay(LinkConditions conditions, Delivery delivery)
        {
            double delay = conditions.LatencySeconds;
            if (conditions.JitterSeconds > 0)
            {
                delay += (_network.Random.NextDouble() * 2.0 - 1.0) * conditions.JitterSeconds;
                if (!delivery.IsReliable() && conditions.ReorderFraction > 0f && _network.Random.NextDouble() < conditions.ReorderFraction)
                {
                    delay += conditions.JitterSeconds * 2.0;
                }
            }

            return delay < 0 ? 0 : delay;
        }

        #endregion

        #region - Disconnect -

        public void Disconnect(ConnectionId connection)
        {
            if (!_peers.TryGetValue(connection.Value, out var peer) || peer.Closing)
            {
                return;
            }

            peer.Closing = true;
            _peers.Remove(connection.Value);
            _pendingDisconnects.Add((connection, DisconnectReason.Local));

            if (peer.Remote._peers.TryGetValue(peer.RemoteId.Value, out var farPeer) && !farPeer.Closing)
            {
                farPeer.Inbox.Add(new Packet
                {
                    Kind = PacketKind.Disconnect,
                    DeliverAt = _network.Clock.Now + Delay(Conditions, Delivery.ReliableSequenced),
                    Stamp = _network.NextStamp(),
                });
            }

            if (ServerConnection == connection)
            {
                ServerConnection = ConnectionId.Invalid;
            }
        }

        #endregion

        #region - Poll -

        public void Poll(ITransportEvents sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            // Local disconnects are reported before anything else, so a caller never sees data from a
            // connection it already closed.
            if (_pendingDisconnects.Count > 0)
            {
                // The sink may disconnect more connections while handling these; iterate a snapshot.
                _disconnectScratch.Clear();
                _disconnectScratch.AddRange(_pendingDisconnects);
                _pendingDisconnects.Clear();
                foreach (var (connection, reason) in _disconnectScratch)
                {
                    sink.OnDisconnected(connection, reason);
                }
            }

            if (!IsRunning)
            {
                return;
            }

            double now = _network.Clock.Now;
            _peerScratch.Clear();
            _peerScratch.AddRange(_peers.Values);

            foreach (var peer in _peerScratch)
            {
                if (peer.Closing)
                {
                    continue;
                }

                DeliverReady(peer, now, sink);
            }
        }

        private void DeliverReady(Peer peer, double now, ITransportEvents sink)
        {
            var inbox = peer.Inbox;
            if (inbox.Count == 0)
            {
                return;
            }

            _readyScratch.Clear();
            for (int i = 0; i < inbox.Count; i++)
            {
                if (inbox[i].DeliverAt <= now)
                {
                    _readyScratch.Add(inbox[i]);
                }
            }

            if (_readyScratch.Count == 0)
            {
                return;
            }

            // Arrival order for everything, then send order for ties, so two packets due at the same
            // instant deliver in the order they were sent, which is what a real link would do.
            _readyScratch.Sort(static (a, b) =>
            {
                int byTime = a.DeliverAt.CompareTo(b.DeliverAt);
                return byTime != 0 ? byTime : a.Stamp.CompareTo(b.Stamp);
            });

            bool disconnected = false;
            foreach (var packet in _readyScratch)
            {
                // The sink may close this connection while handling a packet; nothing more is delivered.
                if (peer.Closing)
                {
                    return;
                }

                switch (packet.Kind)
                {
                    case PacketKind.Connect:
                        inbox.Remove(packet);
                        sink.OnConnected(peer.LocalId);
                        break;

                    case PacketKind.Disconnect:
                        disconnected = true;
                        break;

                    case PacketKind.Data:
                        DeliverData(peer, packet, sink);
                        break;
                }

                if (disconnected)
                {
                    break;
                }
            }

            // Reliable packets that arrived early sit in the inbox until the gap before them fills.
            if (disconnected)
            {
                peer.Closing = true;
                _peers.Remove(peer.LocalId.Value);
                if (ServerConnection == peer.LocalId)
                {
                    ServerConnection = ConnectionId.Invalid;
                }

                sink.OnDisconnected(peer.LocalId, DisconnectReason.Remote);
            }
        }

        private void DeliverData(Peer peer, Packet packet, ITransportEvents sink)
        {
            int channel = (int)packet.Delivery;
            var inbox = peer.Inbox;

            if (packet.Delivery.IsReliable())
            {
                // Deliver this packet only if it is the next expected one; then drain any later packets
                // that were waiting behind the gap it filled.
                if (packet.Seq != peer.NextExpectedSeq[channel])
                {
                    return;
                }

                inbox.Remove(packet);
                peer.NextExpectedSeq[channel]++;
                Emit(peer, packet, sink);
                if (!peer.Closing)
                {
                    DrainReliable(peer, packet.Delivery, sink);
                }

                return;
            }

            inbox.Remove(packet);
            if (packet.Delivery == Delivery.UnreliableSequenced)
            {
                if (packet.Seq <= peer.LastDeliveredSeq[channel])
                {
                    _stats.PacketsDropped++;
                    return;
                }

                peer.LastDeliveredSeq[channel] = packet.Seq;
            }

            Emit(peer, packet, sink);
        }

        private void DrainReliable(Peer peer, Delivery delivery, ITransportEvents sink)
        {
            int channel = (int)delivery;
            bool progressed = true;
            while (progressed && !peer.Closing)
            {
                progressed = false;
                var inbox = peer.Inbox;
                for (int i = 0; i < inbox.Count; i++)
                {
                    var candidate = inbox[i];
                    if (candidate.Kind == PacketKind.Data && candidate.Delivery == delivery
                        && candidate.Seq == peer.NextExpectedSeq[channel]
                        && candidate.DeliverAt <= _network.Clock.Now)
                    {
                        inbox.RemoveAt(i);
                        peer.NextExpectedSeq[channel]++;
                        Emit(peer, candidate, sink);
                        progressed = true;
                        break;
                    }
                }
            }
        }

        private void Emit(Peer peer, Packet packet, ITransportEvents sink)
        {
            _stats.PacketsReceived++;
            _stats.BytesReceived += packet.Data.Length;
            sink.OnData(peer.LocalId, packet.Delivery, packet.Data);
        }

        #endregion
    }
}
