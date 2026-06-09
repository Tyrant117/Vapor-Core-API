using System;
using Unity.Collections;
using Unity.Netcode;
using Vapor.Unsafe;

namespace Vapor.NetworkObjects
{
    public static class PacketHandler
    {
        private const int k_RpcMessageDefaultSize = 1024; // 1k
        private const int k_RpcMessageMaximumSize = 1024 * 64; // 64k

        public static void CreatePacket(FastBufferWriter writer, INetworkPacket packet, bool fullPacket = false, Allocator allocator = Allocator.Temp)
        {
            var bytes = CreatePacket(packet, fullPacket, allocator);
            writer.WriteValueSafe(bytes);
        }

        public static NativeArray<byte> CreatePacket(INetworkPacket packet, bool fullPacket = false, Allocator allocator = Allocator.Temp)
        {
            // Defensively handle null packets
            if (packet == null)
            {
                return new NativeArray<byte>(0, Allocator.Temp);
            }

            // Allocate a temporary writer with some capacity
            using var writer = new FastBufferWriter(k_RpcMessageDefaultSize, allocator, k_RpcMessageMaximumSize);

            // Write opcode first (4 bytes header)
            var opcode = PacketRegistry.GetOpCode(packet.GetType());
            BytePacker.WriteValuePacked(writer, opcode);

            // Write payload with callback
            packet.Serialize(writer, fullPacket);

            // Copy into a NativeArray (so it can be sent via RPC)
            return writer.AsNativeArray();
        }
        
        public static T FromPacket<T>(NativeArray<byte> packet, Allocator allocator = Allocator.Temp) where T : INetworkPacket
        {
            // Defensively handle empty packets
            if(packet.Length == 0)
            {
                return default;
            }

            using var reader = new FastBufferReader(packet, allocator);
            
            ByteUnpacker.ReadValuePacked(reader, out uint opcode);
            
            var type = PacketRegistry.GetType(opcode);
            var instance = (T)Activator.CreateInstance(type, true);
            instance.Deserialize(reader, out _);
            return instance;
        }

        public static void FromPacket<T>(ref T packetToFill, NativeArray<byte> packet, Allocator allocator = Allocator.Temp) where T : INetworkPacket
        {
            if(packet.Length == 0)
            {
                return;
            }

            if (packetToFill == null)
            {
                return;
            }

            using var reader = new FastBufferReader(packet, allocator);
            
            ByteUnpacker.ReadValuePacked(reader, out uint _);
            packetToFill.Deserialize(reader, out _);
        }

        public static T FromPacket<T>(FastBufferReader reader, Allocator allocator = Allocator.Temp)
        {
            reader.ReadValueSafe(out NativeArray<byte> packet, Allocator.Temp);
            if (packet.Length == 0)
            {
                return default;
            }

            using var reader2 = new FastBufferReader(packet, allocator);

            ByteUnpacker.ReadValuePacked(reader, out uint opcode);

            var type = PacketRegistry.GetType(opcode);
            var instance = (T)Activator.CreateInstance(type, true);
            if (instance is INetworkPacket networkPacket)
            {
                networkPacket.Deserialize(reader2, out _);
            }

            return instance;
        }

        public static void FillPacket<T>(ref T packetToFill, FastBufferReader reader, Allocator allocator = Allocator.Temp)
        {
            reader.ReadValueSafe(out NativeArray<byte> packet, Allocator.Temp);

            if (packet.Length == 0)
            {
                return;
            }

            if (packetToFill == null || packetToFill is not INetworkPacket networkPacket)
            {
                return;
            }

            using var reader2 = new FastBufferReader(packet, allocator);

            ByteUnpacker.ReadValuePacked(reader2, out uint _);
            networkPacket.Deserialize(reader2, out _);
        }
    }
}