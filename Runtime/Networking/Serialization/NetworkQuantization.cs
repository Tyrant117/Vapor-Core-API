using System;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// Lossy encodings for the transform channel and anything else that trades precision for bytes:
    /// bounded floats on a fixed number of bits, fixed-point offsets, and smallest-three quaternions.
    /// </summary>
    /// <remarks>
    /// Everything here writes through the bit-level side channel, so a snapshot can pack several fields
    /// into a handful of bytes. Callers are responsible for pairing each write with its read; the
    /// parameters (range, bit count, precision) are part of the wire format and must match on both
    /// sides — they come from authored profiles, never from the packet.
    /// </remarks>
    public static class NetworkQuantization
    {
        #region - Bounded floats -

        /// <summary>Encodes a value in <c>[min, max]</c> on <paramref name="bits"/> bits (1–32). Values outside are clamped.</summary>
        public static void WriteRangedFloat(NetworkWriter writer, float value, float min, float max, int bits)
        {
            uint maxValue = bits == 32 ? uint.MaxValue : (1u << bits) - 1u;
            float t = Mathf.InverseLerp(min, max, value);
            uint quantized = (uint)Math.Round(t * maxValue);
            writer.WriteBits(quantized, bits);
        }

        public static float ReadRangedFloat(NetworkReader reader, float min, float max, int bits)
        {
            uint maxValue = bits == 32 ? uint.MaxValue : (1u << bits) - 1u;
            uint quantized = reader.ReadBits(bits);
            return Mathf.Lerp(min, max, quantized / (float)maxValue);
        }

        /// <summary>The largest error <see cref="WriteRangedFloat"/> introduces for the given parameters.</summary>
        public static float RangedFloatError(float min, float max, int bits)
        {
            double steps = bits == 32 ? uint.MaxValue : (1u << bits) - 1u;
            return (float)((max - min) / steps / 2.0);
        }

        #endregion

        #region - Fixed point -

        /// <summary>
        /// Rounds to a multiple of <paramref name="precision"/> and writes the multiple as a zig-zag
        /// varint: at 1 cm precision a coordinate costs two bytes under ±82 m and three under ±10 km.
        /// </summary>
        public static void WriteFixedPoint(NetworkWriter writer, float value, float precision)
        {
            long steps = (long)Math.Round(value / precision);
            writer.WriteVarInt64(steps);
        }

        public static float ReadFixedPoint(NetworkReader reader, float precision)
        {
            long steps = reader.ReadVarInt64();
            return steps * precision;
        }

        /// <summary>Fixed-point on a fixed bit width, for values known to lie in <c>±range</c>.</summary>
        public static void WriteFixedPointBits(NetworkWriter writer, float value, float precision, int bits)
        {
            long steps = (long)Math.Round(value / precision);
            long half = 1L << (bits - 1);
            steps = Math.Max(-half, Math.Min(half - 1, steps));
            writer.WriteBits((ulong)(steps + half), bits);
        }

        public static float ReadFixedPointBits(NetworkReader reader, float precision, int bits)
        {
            long half = 1L << (bits - 1);
            long steps = (long)reader.ReadBits64(bits) - half;
            return steps * precision;
        }

        public static void WriteFixedPointVector3(NetworkWriter writer, Vector3 value, float precision)
        {
            WriteFixedPoint(writer, value.x, precision);
            WriteFixedPoint(writer, value.y, precision);
            WriteFixedPoint(writer, value.z, precision);
        }

        public static Vector3 ReadFixedPointVector3(NetworkReader reader, float precision) =>
            new(ReadFixedPoint(reader, precision), ReadFixedPoint(reader, precision), ReadFixedPoint(reader, precision));

        #endregion

        #region - Smallest three -

        private const float k_InvSqrt2 = 0.70710678118f;

        /// <summary>
        /// Writes a unit quaternion as the index of its largest component (2 bits) plus the other three
        /// on <paramref name="bitsPerComponent"/> bits each. Ten bits per component is 32 bits total for
        /// ~0.1° of error, which is what most games ship.
        /// </summary>
        public static void WriteSmallestThree(NetworkWriter writer, Quaternion q, int bitsPerComponent = 10)
        {
            if (bitsPerComponent < 2 || bitsPerComponent > 30) throw new ArgumentOutOfRangeException(nameof(bitsPerComponent));

            // Normalise defensively; a slightly denormalised quaternion would otherwise leave the range.
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag > 0f && Mathf.Abs(mag - 1f) > 1e-5f)
            {
                q.x /= mag; q.y /= mag; q.z /= mag; q.w /= mag;
            }

            int largest = 0;
            float largestAbs = Mathf.Abs(q.x);
            if (Mathf.Abs(q.y) > largestAbs) { largest = 1; largestAbs = Mathf.Abs(q.y); }
            if (Mathf.Abs(q.z) > largestAbs) { largest = 2; largestAbs = Mathf.Abs(q.z); }
            if (Mathf.Abs(q.w) > largestAbs) { largest = 3; }

            // q and -q are the same rotation; flip so the largest component is positive and its sign
            // need not be sent.
            float sign = Component(q, largest) < 0f ? -1f : 1f;

            writer.WriteBits((uint)largest, 2);
            for (int i = 0; i < 4; i++)
            {
                if (i == largest)
                {
                    continue;
                }

                WriteRangedFloat(writer, Component(q, i) * sign, -k_InvSqrt2, k_InvSqrt2, bitsPerComponent);
            }
        }

        public static Quaternion ReadSmallestThree(NetworkReader reader, int bitsPerComponent = 10)
        {
            int largest = (int)reader.ReadBits(2);
            Span<float> components = stackalloc float[4];
            float sumSquares = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest)
                {
                    continue;
                }

                float c = ReadRangedFloat(reader, -k_InvSqrt2, k_InvSqrt2, bitsPerComponent);
                components[i] = c;
                sumSquares += c * c;
            }

            components[largest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sumSquares));
            return new Quaternion(components[0], components[1], components[2], components[3]);
        }

        private static float Component(in Quaternion q, int index) => index switch
        {
            0 => q.x,
            1 => q.y,
            2 => q.z,
            _ => q.w,
        };

        #endregion

        #region - Directions -

        /// <summary>A unit vector as two ranged floats (octahedral would be tighter; this is simpler and enough for velocities' directions).</summary>
        public static void WriteUnitVector(NetworkWriter writer, Vector3 direction, int bitsPerComponent = 8)
        {
            direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
            WriteRangedFloat(writer, direction.x, -1f, 1f, bitsPerComponent);
            WriteRangedFloat(writer, direction.y, -1f, 1f, bitsPerComponent);
            writer.WriteBit(direction.z < 0f);
        }

        public static Vector3 ReadUnitVector(NetworkReader reader, int bitsPerComponent = 8)
        {
            float x = ReadRangedFloat(reader, -1f, 1f, bitsPerComponent);
            float y = ReadRangedFloat(reader, -1f, 1f, bitsPerComponent);
            bool negativeZ = reader.ReadBit();
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
            return new Vector3(x, y, negativeZ ? -z : z);
        }

        #endregion
    }
}
