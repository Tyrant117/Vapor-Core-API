using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Vapor.Unsafe;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// Writes and reads length-prefixed, type-tagged packets.
    /// </summary>
    /// <remarks>
    /// The layout is <c>[int byteLength][packed opcode][payload]</c>. The length is what lets a reader
    /// step over a packet whose type it does not know instead of losing the rest of the stream — peers
    /// on different builds, or a despawn racing an update, both land there.
    /// </remarks>
    public static class PacketHandler
    {
        private const int k_RpcMessageDefaultSize = 1024; // 1k
        private const int k_RpcMessageMaximumSize = 1024 * 64; // 64k

        /// <summary>Width of the length prefix, which is written twice: reserved, then patched.</summary>
        private const int k_LengthPrefixSize = sizeof(int);

        /// <summary>
        /// Writes a packet straight into <paramref name="writer"/>.
        /// </summary>
        /// <remarks>
        /// Serialized in place, with the length reserved up front and patched afterwards. Going via a
        /// scratch buffer would allocate a writer and copy the whole payload again for every packet,
        /// only to learn a number already known once the payload is written.
        /// </remarks>
        public static void CreatePacket(FastBufferWriter writer, INetworkPacket packet, bool fullPacket = false)
        {
            if (packet == null)
            {
                writer.WriteValueSafe(0);
                return;
            }

            writer.WriteValueSafe(0);
            int start = writer.Position;

            BytePacker.WriteValuePacked(writer, PacketRegistry.GetOpCode(packet.GetType()));
            packet.Serialize(writer, fullPacket);

            int end = writer.Position;
            writer.Seek(start - k_LengthPrefixSize);
            writer.WriteValueSafe(end - start);
            writer.Seek(end);
        }

        /// <summary>
        /// Serializes a packet into a buffer the caller owns.
        /// </summary>
        /// <remarks>
        /// Unlike the writer overload this has no length prefix — it is the raw
        /// <c>[opcode][payload]</c>, to be read back by <see cref="FromPacket{T}(NativeArray{byte})"/>.
        /// <para>
        /// The result is a copy. Returning <c>AsNativeArray</c> would hand back a view over the
        /// scratch writer's buffer, which is released on the way out of this method; it survived only
        /// because <see cref="Allocator.Temp"/> holds its memory to the end of the frame.
        /// </para>
        /// </remarks>
        public static NativeArray<byte> CreatePacket(INetworkPacket packet, bool fullPacket = false, Allocator allocator = Allocator.Temp)
        {
            if (packet == null)
            {
                return new NativeArray<byte>(0, allocator);
            }

            using var writer = new FastBufferWriter(k_RpcMessageDefaultSize, Allocator.Temp, k_RpcMessageMaximumSize);
            BytePacker.WriteValuePacked(writer, PacketRegistry.GetOpCode(packet.GetType()));
            packet.Serialize(writer, fullPacket);

            var owned = new NativeArray<byte>(writer.Length, allocator);
            NativeArray<byte>.Copy(writer.AsNativeArray(), owned, writer.Length);
            return owned;
        }

        /// <summary>
        /// Reads a length-prefixed packet and rebuilds it from its opcode.
        /// </summary>
        /// <returns>Null for an empty packet, or for a type this build does not know.</returns>
        public static INetworkPacket FromPacket(FastBufferReader reader, Allocator allocator = Allocator.Temp)
        {
            reader.ReadValueSafe(out int byteLength);
            if (byteLength == 0)
            {
                return null;
            }

            int end = reader.Position + byteLength;
            ByteUnpacker.ReadValuePacked(reader, out uint opcode);

            if (!PacketRegistry.TryCreate(opcode, out var instance))
            {
                Debug.LogWarning($"Received a packet with unknown opcode [{opcode:X8}]. The peers are likely running different builds.");
                reader.Seek(end);
                return null;
            }

            instance.Deserialize(reader, out _);

            // Positioned from the prefix rather than from wherever Deserialize stopped, so one packet
            // misreading its own payload cannot take the rest of the stream with it.
            reader.Seek(end);
            return instance;
        }

        /// <summary>
        /// Reads a length-prefixed packet into an instance the caller already has.
        /// </summary>
        /// <remarks>
        /// The opcode is checked rather than skipped. Deserializing whatever arrived into whatever was
        /// passed is how a type mismatch turns into corrupt state instead of an error.
        /// </remarks>
        public static bool FromPacketInto(FastBufferReader reader, INetworkPacket target)
        {
            reader.ReadValueSafe(out int byteLength);
            if (byteLength == 0 || target == null)
            {
                return false;
            }

            int end = reader.Position + byteLength;
            ByteUnpacker.ReadValuePacked(reader, out uint opcode);

            bool matches = PacketRegistry.TryGetOpCode(target.GetType(), out var expected) && expected == opcode;
            if (!matches)
            {
                Debug.LogError($"Packet type mismatch: the wire says [{opcode:X8}] but {target.GetType().Name} expects [{expected:X8}].");
                reader.Seek(end);
                return false;
            }

            target.Deserialize(reader, out _);
            reader.Seek(end);
            return true;
        }

        public static T FromPacket<T>(NativeArray<byte> packet, Allocator allocator = Allocator.Temp) where T : INetworkPacket
        {
            if (packet.Length == 0)
            {
                return default;
            }

            using var reader = new FastBufferReader(packet, allocator);
            ByteUnpacker.ReadValuePacked(reader, out uint opcode);

            if (!PacketRegistry.TryCreate(opcode, out var instance))
            {
                Debug.LogWarning($"Received a packet with unknown opcode [{opcode:X8}]. The peers are likely running different builds.");
                return default;
            }

            if (instance is not T typed)
            {
                Debug.LogError($"Packet type mismatch: opcode [{opcode:X8}] is {instance.GetType().Name}, which is not a {typeof(T).Name}.");
                return default;
            }

            typed.Deserialize(reader, out _);
            return typed;
        }

        public static void FromPacket<T>(ref T packetToFill, NativeArray<byte> packet, Allocator allocator = Allocator.Temp) where T : INetworkPacket
        {
            if (packet.Length == 0 || packetToFill == null)
            {
                return;
            }

            using var reader = new FastBufferReader(packet, allocator);
            ByteUnpacker.ReadValuePacked(reader, out uint opcode);

            if (PacketRegistry.TryGetOpCode(packetToFill.GetType(), out var expected) && expected != opcode)
            {
                Debug.LogError($"Packet type mismatch: the wire says [{opcode:X8}] but {packetToFill.GetType().Name} expects [{expected:X8}].");
                return;
            }

            packetToFill.Deserialize(reader, out _);
        }
    }
}
