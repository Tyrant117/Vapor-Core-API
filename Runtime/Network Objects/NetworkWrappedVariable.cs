using System;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    [GenerateSerializationForType(typeof(Double))]
    public struct NetworkWrappedVariable<T> : INetworkPacket
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