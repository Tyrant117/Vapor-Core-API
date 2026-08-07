using System;
using Vapor.Unsafe;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// A replication channel. An object belonging to one or more groups reaches only the clients
    /// subscribed to at least one of them; an object belonging to none is global and reaches everyone.
    /// </summary>
    /// <remarks>
    /// Groups are the unit of interest management here rather than distance, so a zone, a party, a
    /// guild and an instance are all expressed the same way. Nothing stops a group from standing for
    /// a region of space — a grid cell subscribed to as the player moves between cells behaves like
    /// proximity relevance — but the network layer never has to know what a group means.
    /// <para>
    /// Global by default is deliberate: an object that never mentions a group replicates exactly as it
    /// did before interest management existed.
    /// </para>
    /// </remarks>
    public readonly struct InterestGroup : IEquatable<InterestGroup>
    {
        /// <summary>The empty group. An object is never a member of it and no client may subscribe.</summary>
        public static readonly InterestGroup None = default;

        public readonly uint Key;

        public InterestGroup(uint key)
        {
            Key = key;
        }

        /// <summary>
        /// Hashes a name to a key with xxHash32, the same scheme gameplay tags and rpc ids use, so a
        /// group can be named in one assembly and subscribed to from another without a shared constant.
        /// </summary>
        public InterestGroup(string name)
        {
            Key = string.IsNullOrEmpty(name) ? 0u : name.Hash32();
        }

        public bool IsNone => Key == 0u;

        public bool Equals(InterestGroup other) => Key == other.Key;

        public override bool Equals(object obj) => obj is InterestGroup other && Equals(other);

        public override int GetHashCode() => unchecked((int)Key);

        public override string ToString() => $"InterestGroup[{Key:X8}]";

        public static bool operator ==(InterestGroup left, InterestGroup right) => left.Equals(right);

        public static bool operator !=(InterestGroup left, InterestGroup right) => !left.Equals(right);
    }
}
