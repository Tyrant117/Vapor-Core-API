using System;
using System.Collections.Generic;

namespace Vapor.Networking
{
    /// <summary>What a simulated link does to the packets that cross it.</summary>
    public sealed class LinkConditions
    {
        /// <summary>One-way base latency in seconds.</summary>
        public double LatencySeconds;

        /// <summary>Uniform ± jitter added to each packet's latency, in seconds. Jitter is what reorders unreliable packets.</summary>
        public double JitterSeconds;

        /// <summary>Fraction of unreliable packets dropped. Reliable classes are never dropped, only delayed.</summary>
        public float LossFraction;

        /// <summary>Fraction of unreliable packets given an extra jitter's worth of delay, forcing reorder even with low jitter.</summary>
        public float ReorderFraction;

        /// <summary>Packets in flight per delivery class before <see cref="SendResult.QueueFull"/>. Lets backpressure be tested.</summary>
        public int SendQueueCapacity = int.MaxValue;

        public static LinkConditions Perfect => new();

        public static LinkConditions Lan => new() { LatencySeconds = 0.005, JitterSeconds = 0.001 };

        public static LinkConditions Wan => new() { LatencySeconds = 0.060, JitterSeconds = 0.015, LossFraction = 0.02f, ReorderFraction = 0.05f };

        public LinkConditions Clone() => (LinkConditions)MemberwiseClone();
    }

    /// <summary>
    /// The virtual wire that <see cref="LoopbackTransport"/>s plug into: a clock, a seeded random source,
    /// the servers listening by port, and the payload limits every link shares.
    /// </summary>
    /// <remarks>
    /// Everything is deterministic given the seed and the sequence of calls, which is what makes a
    /// replication test repeatable under injected latency, jitter, loss and reordering.
    /// </remarks>
    public sealed class LoopbackNetwork
    {
        private readonly Dictionary<ushort, LoopbackTransport> _servers = new();
        private readonly int[] _maxPayloads = { 1200, 1200, 1200, 64 * 1024 };
        private long _stamp;

        public LoopbackNetwork(INetworkClock clock, int seed = 12345, LinkConditions defaultConditions = null)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Random = new Random(seed);
            DefaultConditions = defaultConditions ?? LinkConditions.Perfect;
        }

        public INetworkClock Clock { get; }

        public Random Random { get; }

        /// <summary>Conditions a transport starts with. Each transport clones them and may be tuned individually.</summary>
        public LinkConditions DefaultConditions { get; }

        public int MaxPayload(Delivery delivery) => _maxPayloads[(int)delivery];

        public void SetMaxPayload(Delivery delivery, int bytes) => _maxPayloads[(int)delivery] = bytes;

        public LoopbackTransport CreateTransport() => new(this);

        internal bool TryListen(ushort port, LoopbackTransport server)
        {
            if (_servers.ContainsKey(port))
            {
                return false;
            }

            _servers.Add(port, server);
            return true;
        }

        internal void StopListening(ushort port, LoopbackTransport server)
        {
            if (_servers.TryGetValue(port, out var existing) && ReferenceEquals(existing, server))
            {
                _servers.Remove(port);
            }
        }

        internal bool TryGetServer(ushort port, out LoopbackTransport server) => _servers.TryGetValue(port, out server);

        /// <summary>A monotonic stamp per enqueued packet, so packets due at the same instant deliver in send order.</summary>
        internal long NextStamp() => ++_stamp;
    }
}
