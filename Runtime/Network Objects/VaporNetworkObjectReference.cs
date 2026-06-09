using System;
using Unity.Burst;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    public struct VaporNetworkObjectReference : INetworkPacket, INetworkSerializable, IEquatable<VaporNetworkObjectReference>
    {
        public static implicit operator VaporNetworkObjectReference(VaporNetworkObject networkObject)
        {
            return networkObject == null ? default : new VaporNetworkObjectReference { NetworkObjectId = networkObject.NetworkObjectId, _cachedNetworkObject = networkObject };
        }

        public ulong NetworkObjectId;
        private VaporNetworkObject _cachedNetworkObject;

        public bool TryGet<T>(out T reference) where T : VaporNetworkObject
        {
            if (_cachedNetworkObject != null && _cachedNetworkObject.NetworkObjectId == NetworkObjectId)
            {
                if (_cachedNetworkObject is T networkObject)
                {
                    reference = networkObject;
                    return true;
                }
            }
            else
            {
                if (!NetworkMessages.Instance.TryGetReference(NetworkObjectId, out _cachedNetworkObject))
                {
                    reference = null;
                    return false;
                }
                
                if (_cachedNetworkObject is T networkObject)
                {
                    reference = networkObject;
                    return true;
                }
            }

            reference = null;
            return false;
        }
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NetworkObjectId);
        }

        public void Serialize(FastBufferWriter writer, bool fullPacket)
        {
            writer.WriteValueSafe(NetworkObjectId);
        }

        public void Deserialize(FastBufferReader reader, out bool fullPacket)
        {
            fullPacket = true;
            reader.ReadValueSafe(out NetworkObjectId);
            NetworkMessages.Instance.TryGetReference(NetworkObjectId, out _cachedNetworkObject);
        }

        public bool Equals(VaporNetworkObjectReference other)
        {
            return NetworkObjectId == other.NetworkObjectId;
        }

        [BurstDiscard]
        public override bool Equals(object obj)
        {
            return obj is VaporNetworkObjectReference other && Equals(other);
        }

        public override int GetHashCode()
        {
            return NetworkObjectId.GetHashCode();
        }
    }
}