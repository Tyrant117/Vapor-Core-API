using Unity.Collections;
using Unity.Netcode;

namespace Vapor.GameplayTags
{
    public struct GameplayTagPacket : INetworkSerializable
    {
        public uint Key;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Key);
        }

        public readonly GameplayTag FromPacket()
        {
            return new GameplayTag
            {
                Key = Key,
            };
        }
    }
}