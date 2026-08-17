using System;
using Unity.Burst;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Networking
{
    /// <summary>
    /// A replication channel. An object belonging to one or more groups reaches only the clients
    /// subscribed to at least one of them; an object belonging to none is global and reaches everyone.
    /// </summary>
    /// <remarks>
    /// Groups express non-spatial relevance — a zone, a party, a guild, an instance, a GM channel —
    /// all the same way; spatial relevance is the grid's job and combines with them. Global by default
    /// is deliberate: an object that never mentions a group replicates exactly as if interest
    /// management did not exist.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public readonly struct InterestGroup : IEquatable<InterestGroup>
    {
        /// <summary>The empty group. An object is never a member of it and no client may subscribe.</summary>
        public static readonly InterestGroup None = default;

        public readonly uint Key;

        public InterestGroup(uint key) => Key = key;

        /// <summary>Hashes a name with xxHash32, the same scheme tags and rpc ids use, so a group can be named in one assembly and subscribed to from another.</summary>
        public InterestGroup(string name) => Key = string.IsNullOrEmpty(name) ? 0u : XxHash32.Hash(name);

        public bool IsNone => Key == 0u;

        public bool Equals(InterestGroup other) => Key == other.Key;
        [BurstDiscard]
        public override bool Equals(object obj) => obj is InterestGroup other && Equals(other);
        public override int GetHashCode() => unchecked((int)Key);
        public override string ToString() => $"InterestGroup[{Key:X8}]";
        public static bool operator ==(InterestGroup left, InterestGroup right) => left.Key == right.Key;
        public static bool operator !=(InterestGroup left, InterestGroup right) => left.Key != right.Key;
    }
}
