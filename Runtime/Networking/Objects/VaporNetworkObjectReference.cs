using System;
using Unity.Burst;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Networking
{
    /// <summary>
    /// An object id that travels in rpc arguments and network variables and resolves against a
    /// world on arrival. Resolution can fail — the object may have despawned in flight — so
    /// <see cref="TryGet{T}(NetworkWorld, out T)"/> is the only accessor.
    /// </summary>
    [NoAutoStaticsCleanup]
    public readonly struct VaporNetworkObjectReference : IEquatable<VaporNetworkObjectReference>
    {
        public static readonly VaporNetworkObjectReference None = default;

        public readonly ulong NetworkObjectId;

        public VaporNetworkObjectReference(ulong networkObjectId) => NetworkObjectId = networkObjectId;

        public VaporNetworkObjectReference(VaporNetworkObject networkObject) =>
            NetworkObjectId = networkObject is { IsSpawned: true } ? networkObject.NetworkObjectId : 0;

        public bool IsNone => NetworkObjectId == 0;

        public bool TryGet<T>(NetworkWorld world, out T networkObject) where T : VaporNetworkObject
        {
            if (world != null && NetworkObjectId != 0 && world.TryGet(NetworkObjectId, out var found) && found is T typed)
            {
                networkObject = typed;
                return true;
            }

            networkObject = null;
            return false;
        }

        /// <summary>Resolves against <see cref="NetworkWorld.Current"/>.</summary>
        public bool TryGet<T>(out T networkObject) where T : VaporNetworkObject => TryGet(NetworkWorld.Current, out networkObject);

        public bool Equals(VaporNetworkObjectReference other) => NetworkObjectId == other.NetworkObjectId;
        [BurstDiscard]
        public override bool Equals(object obj) => obj is VaporNetworkObjectReference other && Equals(other);
        public override int GetHashCode() => NetworkObjectId.GetHashCode();
        public override string ToString() => IsNone ? "ObjectRef[none]" : $"ObjectRef[{NetworkObjectId}]";
        public static bool operator ==(VaporNetworkObjectReference a, VaporNetworkObjectReference b) => a.NetworkObjectId == b.NetworkObjectId;
        public static bool operator !=(VaporNetworkObjectReference a, VaporNetworkObjectReference b) => a.NetworkObjectId != b.NetworkObjectId;

        public static implicit operator VaporNetworkObjectReference(VaporNetworkObject networkObject) => new(networkObject);

        /// <summary>Wire form: the id as a varint. Registered by the world's static initializer.</summary>
        internal sealed class Formatter : NetworkFormatter<VaporNetworkObjectReference>
        {
            public override void Write(NetworkWriter writer, in VaporNetworkObjectReference value) => writer.WriteVarUInt64(value.NetworkObjectId);
            public override VaporNetworkObjectReference Read(NetworkReader reader) => new(reader.ReadVarUInt64());
        }
    }
}
