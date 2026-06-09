using System;
using JetBrains.Annotations;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    [GenerateSerializationForGenericParameter(0)]
    public class VaporNetworkPacketVariable<T> : VaporNetworkVariable<T> where T : INetworkPacket, IEquatable<T>
    {
        public VaporNetworkPacketVariable(T defaultValue, [NotNull] VaporNetworkObject owner) : base(defaultValue, owner)
        {
            
        }

        public override void Write(FastBufferWriter writer)
        {
            BytePacker.WriteValueBitPacked(writer, NetworkVariableId);
            InternalValue.Serialize(writer, true);
            IsDirty = false;
        }

        public override void Read(FastBufferReader reader)
        {
            LastValue = InternalValue;
            InternalValue.Deserialize(reader, out _);
            IsDirty = false;

            InvokeOnValueChanged();
        }
    }
}