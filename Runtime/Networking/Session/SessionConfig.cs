using System;

namespace Vapor.Networking
{
    /// <summary>
    /// What a session is. <see cref="Host"/> is a server that also carries the local player: one
    /// session, one world, and both <see cref="NetworkSession.IsServer"/> and
    /// <see cref="NetworkSession.IsClient"/> answer true for it.
    /// </summary>
    public enum SessionRole : byte { None, Server, Client, Host }

    /// <summary>Why a session-level connection ended. Superset of the transport's reasons.</summary>
    public enum SessionDisconnectReason : byte
    {
        Local,
        Remote,
        Timeout,
        TransportError,
        /// <summary>The server refused the connection during the handshake.</summary>
        Rejected,
        /// <summary>Protocol version or content hash did not match.</summary>
        ProtocolMismatch,
        ServerFull,
        /// <summary>The peer never completed the handshake.</summary>
        HandshakeTimeout,
        /// <summary>The server shut down.</summary>
        ServerShutdown,
    }

    /// <summary>The server's answer to a connection request.</summary>
    public readonly struct ConnectionApproval
    {
        public readonly bool Approved;
        public readonly string Reason;

        private ConnectionApproval(bool approved, string reason)
        {
            Approved = approved;
            Reason = reason;
        }

        public static ConnectionApproval Approve() => new(true, null);
        public static ConnectionApproval Reject(string reason) => new(false, reason);
    }

    /// <summary>
    /// Server-side approval hook. Runs once per connection after the protocol check, with whatever
    /// bytes the client put in its connection payload (a token, a character id, a build tag).
    /// </summary>
    public delegate ConnectionApproval ConnectionApprovalHandler(ulong prospectiveClientId, ReadOnlySpan<byte> payload);

    public sealed class SessionConfig
    {
        /// <summary>Fixed ticks per second. Drives replication cadence and the tick counters both sides agree on.</summary>
        public double TickRate = 30;

        /// <summary>Ticks processed in one <see cref="NetworkSession.Update"/> before the rest are dropped — a hitch never spirals.</summary>
        public int MaxTicksPerUpdate = 8;

        /// <summary>Must match on both sides; bump on any wire-format change.</summary>
        public uint ProtocolVersion = 1;

        /// <summary>Optional app-defined fingerprint (content, data registry) that must also match. 0 disables the check.</summary>
        public uint ContentHash;

        /// <summary>Server: connections above this are rejected with <see cref="SessionDisconnectReason.ServerFull"/>.</summary>
        public int MaxClients = 64;

        /// <summary>Server: how long a fresh connection may take to send its hello.</summary>
        public double HandshakeTimeoutSeconds = 10;

        /// <summary>Client: how often to ping. Also the keep-alive cadence.</summary>
        public double PingIntervalSeconds = 1.0;

        /// <summary>Both: no traffic for this long ends the connection.</summary>
        public double TimeoutSeconds = 15;

        /// <summary>Exponential smoothing factor for round-trip time and clock offset (0–1; higher follows faster).</summary>
        public float TimeSyncSmoothing = 0.2f;

        public SessionConfig Clone() => (SessionConfig)MemberwiseClone();
    }
}
