using System;
using Unity.Netcode;

namespace Vapor.GameplayFramework
{
    [GenerateSerializationForGenericParameter(0)]
    public struct ValueGameplayEventData<T> : IGameplayEventData where T : struct, IEquatable<T>
    {
        public T Value;
        
        public void Serialize(FastBufferWriter writer, bool fullPacket)
        {
            NetworkVariableSerialization<T>.Write(writer, ref Value);
        }
        public void Deserialize(FastBufferReader reader, out bool fullPacket)
        {
            fullPacket = true;
            NetworkVariableSerialization<T>.Read(reader, ref Value);
        }
    }
}