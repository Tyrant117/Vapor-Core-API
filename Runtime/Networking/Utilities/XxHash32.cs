using System;
using System.Buffers.Binary;
using System.Text;

namespace Vapor.Networking
{
    /// <summary>
    /// xxHash32, seed 0 — the same function and seed as <c>Vapor.Unsafe.XxHash.Hash32</c>, gameplay
    /// tags, data keys and rpc ids, re-implemented here without unsafe code so this assembly depends on
    /// nothing. Ids hashed on either side of the boundary are interchangeable; a test pins that.
    /// </summary>
    public static class XxHash32
    {
        private const uint k_Prime1 = 2654435761u;
        private const uint k_Prime2 = 2246822519u;
        private const uint k_Prime3 = 3266489917u;
        private const uint k_Prime4 = 668265263u;
        private const uint k_Prime5 = 374761393u;

        public static uint Hash(ReadOnlySpan<byte> input, uint seed = 0)
        {
            unchecked
            {
                int length = input.Length;
                int offset = 0;
                uint hash = seed + k_Prime5;

                if (length >= 16)
                {
                    uint v0 = seed + k_Prime1 + k_Prime2;
                    uint v1 = seed + k_Prime2;
                    uint v2 = seed;
                    uint v3 = seed - k_Prime1;

                    int blocks = length >> 4;
                    for (int i = 0; i < blocks; i++)
                    {
                        v0 = Round(v0, BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset, 4)));
                        v1 = Round(v1, BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset + 4, 4)));
                        v2 = Round(v2, BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset + 8, 4)));
                        v3 = Round(v3, BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset + 12, 4)));
                        offset += 16;
                    }

                    hash = RotateLeft(v0, 1) + RotateLeft(v1, 7) + RotateLeft(v2, 12) + RotateLeft(v3, 18);
                }

                hash += (uint)length;

                int remaining = length - offset;
                while (remaining >= 4)
                {
                    hash += BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset, 4)) * k_Prime3;
                    hash = RotateLeft(hash, 17) * k_Prime4;
                    offset += 4;
                    remaining -= 4;
                }

                while (remaining > 0)
                {
                    hash += input[offset] * k_Prime5;
                    hash = RotateLeft(hash, 11) * k_Prime1;
                    offset++;
                    remaining--;
                }

                hash ^= hash >> 15;
                hash *= k_Prime2;
                hash ^= hash >> 13;
                hash *= k_Prime3;
                hash ^= hash >> 16;
                return hash;
            }
        }

        /// <summary>Hashes the UTF-8 bytes of a string; null and empty both hash the empty input.</summary>
        public static uint Hash(string text, uint seed = 0)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Hash(ReadOnlySpan<byte>.Empty, seed);
            }

            int byteCount = Encoding.UTF8.GetByteCount(text);
            if (byteCount <= 256)
            {
                Span<byte> buffer = stackalloc byte[byteCount];
                Encoding.UTF8.GetBytes(text.AsSpan(), buffer);
                return Hash(buffer, seed);
            }

            return Hash(Encoding.UTF8.GetBytes(text), seed);
        }

        /// <summary>The convention used for opcodes and type tags: the hash of the type's full name.</summary>
        public static uint Hash(Type type) => Hash(type.FullName);

        private static uint Round(uint accumulator, uint input)
        {
            unchecked
            {
                accumulator += input * k_Prime2;
                accumulator = RotateLeft(accumulator, 13);
                accumulator *= k_Prime1;
                return accumulator;
            }
        }

        private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));
    }
}
