using System.ComponentModel;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// How each argument of a <see cref="VaporRpcAttribute"/> method crosses the wire.
    /// </summary>
    /// <remarks>
    /// Called only from code the VaporRpc source generator writes (Tools~/VaporRpcGenerator). Those
    /// generated send paths and receive handlers live in the assembly that declares the rpc, not in
    /// this one, which is why every member here is public.
    /// <para>
    /// Which method a given parameter type gets is decided at compile time, in the order the
    /// generator tests them: VaporNetworkObject, then NetworkBehaviour, then INetworkPacket, then
    /// INetworkSerializable, then <see cref="WriteValue{T}"/> for everything else. The order matters —
    /// VaporNetworkObject is itself an INetworkPacket, so it has to be tested first.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class RpcSerialization
    {
        /// <summary>
        /// Primitives, strings, vectors, enums, and plain managed types, through the same serializer
        /// network variables use.
        /// </summary>
        public static void WriteValue<T>(FastBufferWriter writer, T value)
        {
            NetworkVariableSerialization<T>.Write(writer, ref value);
        }

        public static T ReadValue<T>(FastBufferReader reader)
        {
            T value = default;
            NetworkVariableSerialization<T>.Read(reader, ref value);
            return value;
        }

        /// <summary>
        /// Sent by id and resolved against the receiving peer's spawned objects, so a network object
        /// arrives as the peer's own instance rather than a copy.
        /// </summary>
        public static void WriteNetworkObject(FastBufferWriter writer, VaporNetworkObject value)
        {
            if (value == null)
            {
                writer.WriteValueSafe(true);
                return;
            }

            writer.WriteValueSafe(false);
            writer.WriteValueSafe(value.NetworkObjectId);
        }

        public static VaporNetworkObject ReadNetworkObject(FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool isNull);
            if (isNull)
            {
                return null;
            }

            reader.ReadValueSafe(out ulong networkObjectId);
            NetworkMessages.Instance.NetworkObjects.TryGetValue(networkObjectId, out var value);
            return value;
        }

        public static void WriteNetworkBehaviour(FastBufferWriter writer, NetworkBehaviour value)
        {
            if (!value)
            {
                writer.WriteValueSafe(true);
                return;
            }

            writer.WriteValueSafe(false);
            writer.WriteValueSafe((NetworkBehaviourReference)value);
        }

        public static NetworkBehaviour ReadNetworkBehaviour(FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool isNull);
            if (isNull)
            {
                return null;
            }

            reader.ReadValueSafe(out NetworkBehaviourReference reference);
            reference.TryGet(out NetworkBehaviour value);
            return value;
        }

        /// <summary>
        /// Written with its type tag, so a parameter declared as an interface or a base type arrives
        /// as whatever concrete packet was passed.
        /// </summary>
        public static void WritePacket(FastBufferWriter writer, INetworkPacket value)
        {
            if (value == null)
            {
                writer.WriteValueSafe(true);
                return;
            }

            writer.WriteValueSafe(false);
            PacketHandler.CreatePacket(writer, value);
        }

        public static INetworkPacket ReadPacket(FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool isNull);
            return isNull ? null : PacketHandler.FromPacket(reader);
        }

        public static void WriteSerializable<T>(FastBufferWriter writer, T value) where T : INetworkSerializable, new()
        {
            bool isNull = (object)value == null;
            writer.WriteValueSafe(isNull);
            if (!isNull)
            {
                writer.WriteNetworkSerializable(value);
            }
        }

        public static T ReadSerializable<T>(FastBufferReader reader) where T : INetworkSerializable, new()
        {
            reader.ReadValueSafe(out bool isNull);
            if (isNull)
            {
                return default;
            }

            reader.ReadNetworkSerializable(out T value);
            return value;
        }
    }
}
