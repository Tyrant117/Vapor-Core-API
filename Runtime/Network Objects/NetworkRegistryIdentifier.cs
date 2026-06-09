using System;
using Unity.Burst;

namespace Vapor.NetworkObjects
{
    public readonly struct NetworkRegistryIdentifier : IEquatable<NetworkRegistryIdentifier>
    {
        public static bool operator ==(NetworkRegistryIdentifier left, NetworkRegistryIdentifier right) => left.Equals(right);
        public static bool operator !=(NetworkRegistryIdentifier left, NetworkRegistryIdentifier right) => !(left == right);
        
        public readonly ulong OwnerClientId;
        public readonly ulong ParentNetworkId;
        public readonly uint Key;
        
        public NetworkRegistryIdentifier(ulong ownerClientId, ulong parentNetworkId, uint key)
        {
            OwnerClientId = ownerClientId;
            ParentNetworkId = parentNetworkId;
            Key = key;
        }

        public bool Equals(NetworkRegistryIdentifier other)
        {
            return OwnerClientId == other.OwnerClientId && ParentNetworkId == other.ParentNetworkId && Key == other.Key;
        }

        [BurstDiscard]
        public override bool Equals(object obj)
        {
            return obj is NetworkRegistryIdentifier other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(OwnerClientId, ParentNetworkId, Key);
        }
    }
}