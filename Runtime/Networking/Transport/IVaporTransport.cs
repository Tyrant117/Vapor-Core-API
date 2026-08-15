using System;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Networking
{
    /// <summary>A transport-level connection handle. Meaningful only to the transport that issued it.</summary>
    [NoAutoStaticsCleanup]
    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        public static readonly ConnectionId Invalid = new(-1);

        public readonly int Value;

        public ConnectionId(int value) => Value = value;

        public bool IsValid => Value >= 0;

        public bool Equals(ConnectionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ConnectionId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsValid ? $"Connection[{Value}]" : "Connection[invalid]";
        public static bool operator ==(ConnectionId a, ConnectionId b) => a.Value == b.Value;
        public static bool operator !=(ConnectionId a, ConnectionId b) => a.Value != b.Value;
    }

    /// <summary>Where to listen or connect. Interpretation is up to the transport (an IP and port, a Steam id, a loopback name).</summary>
    public readonly struct TransportEndpoint
    {
        public readonly string Address;
        public readonly ushort Port;

        public TransportEndpoint(string address, ushort port)
        {
            Address = address;
            Port = port;
        }

        public static TransportEndpoint Loopback(ushort port) => new("loopback", port);
        public static TransportEndpoint AnyIPv4(ushort port) => new("0.0.0.0", port);
        public static TransportEndpoint LocalHost(ushort port) => new("127.0.0.1", port);

        public override string ToString() => $"{Address}:{Port}";
    }

    public enum SendResult : byte
    {
        Ok,
        /// <summary>The transport's outbound queue for this connection and delivery is full; try again after the next poll.</summary>
        QueueFull,
        /// <summary>The payload exceeds <see cref="IVaporTransport.MaxPayload"/> for the delivery.</summary>
        TooLarge,
        /// <summary>The connection is gone.</summary>
        Disconnected,
        /// <summary>The transport is not running.</summary>
        NotStarted,
    }

    public enum DisconnectReason : byte
    {
        /// <summary>This side called <see cref="IVaporTransport.Disconnect"/> or shut down.</summary>
        Local,
        /// <summary>The peer closed the connection.</summary>
        Remote,
        /// <summary>No traffic within the transport's timeout.</summary>
        Timeout,
        /// <summary>The transport reported an error on the connection.</summary>
        TransportError,
    }

    public struct TransportStats
    {
        public long PacketsSent;
        public long PacketsReceived;
        public long BytesSent;
        public long BytesReceived;
        public long SendsRejected;
        public long PacketsDropped;
    }

    /// <summary>
    /// Receives transport events during <see cref="IVaporTransport.Poll"/>. Events for one connection
    /// arrive in the order the transport observed them; a connection's <c>OnDisconnected</c> is always
    /// last, and nothing follows it.
    /// </summary>
    public interface ITransportEvents
    {
        void OnConnected(ConnectionId connection);
        void OnDisconnected(ConnectionId connection, DisconnectReason reason);
        /// <summary>The payload span is valid only for the duration of the call.</summary>
        void OnData(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload);
    }

    /// <summary>
    /// The seam under everything: datagrams with a delivery class, connections, and a poll. UTP,
    /// loopback and (later) Steam sockets implement it; nothing above it knows which one it has.
    /// </summary>
    /// <remarks>
    /// The contract is deliberately minimal — no message framing, no batching, no reliability of its
    /// own beyond what <see cref="Delivery"/> promises. Backpressure is a normal outcome, not an error:
    /// <see cref="Send"/> returns <see cref="SendResult.QueueFull"/> and the caller keeps the payload
    /// until after the next <see cref="Poll"/>. A transport is single-threaded: <c>Poll</c> and
    /// <c>Send</c> are called from one thread, and events are raised on it during <c>Poll</c>.
    /// </remarks>
    public interface IVaporTransport : IDisposable
    {
        bool IsRunning { get; }
        bool IsServer { get; }

        /// <summary>Client side: the connection to the server, once <c>OnConnected</c> has fired.</summary>
        ConnectionId ServerConnection { get; }

        bool StartServer(TransportEndpoint bind);
        bool StartClient(TransportEndpoint remote);
        void Shutdown();

        /// <summary>Pumps the transport once and raises every pending event on <paramref name="sink"/>.</summary>
        void Poll(ITransportEvents sink);

        SendResult Send(ConnectionId connection, Delivery delivery, ReadOnlySpan<byte> payload);

        void Disconnect(ConnectionId connection);

        /// <summary>The largest payload <see cref="Send"/> accepts for the delivery. Batching budgets come from here.</summary>
        int MaxPayload(Delivery delivery);

        TransportStats Stats { get; }
    }
}
