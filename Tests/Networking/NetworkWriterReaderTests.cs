using System;
using NUnit.Framework;
using UnityEngine;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The byte-level contract between <see cref="NetworkWriter"/> and <see cref="NetworkReader"/>:
    /// every primitive round-trips, variable-length integers hit their size boundaries, and the bit
    /// side channel lands on the same offsets on both sides however it is interleaved with bytes.
    /// </summary>
    [TestFixture]
    public class NetworkWriterReaderTests
    {
        private static NetworkReader ReaderOver(NetworkWriter writer)
        {
            var segment = writer.WrittenSegment;
            return new NetworkReader(segment.Array, segment.Offset, segment.Count);
        }

        #region - Primitives -

        [Test]
        public void PrimitivesRoundTrip()
        {
            var w = new NetworkWriter();
            w.WriteByte(0xAB);
            w.WriteSByte(-5);
            w.WriteBool(true);
            w.WriteBool(false);
            w.WriteInt16(-1234);
            w.WriteUInt16(65000);
            w.WriteInt32(int.MinValue);
            w.WriteUInt32(0xDEADBEEF);
            w.WriteInt64(long.MaxValue);
            w.WriteUInt64(ulong.MaxValue);
            w.WriteSingle(3.25f);
            w.WriteDouble(Math.PI);
            w.WriteChar('ß');

            var r = ReaderOver(w);
            Assert.AreEqual(0xAB, r.ReadByte());
            Assert.AreEqual(-5, r.ReadSByte());
            Assert.IsTrue(r.ReadBool());
            Assert.IsFalse(r.ReadBool());
            Assert.AreEqual(-1234, r.ReadInt16());
            Assert.AreEqual(65000, r.ReadUInt16());
            Assert.AreEqual(int.MinValue, r.ReadInt32());
            Assert.AreEqual(0xDEADBEEF, r.ReadUInt32());
            Assert.AreEqual(long.MaxValue, r.ReadInt64());
            Assert.AreEqual(ulong.MaxValue, r.ReadUInt64());
            Assert.AreEqual(3.25f, r.ReadSingle());
            Assert.AreEqual(Math.PI, r.ReadDouble());
            Assert.AreEqual('ß', r.ReadChar());
            Assert.IsTrue(r.IsAtEnd);
        }

        [Test]
        public void MultiBytePrimitivesAreLittleEndian()
        {
            var w = new NetworkWriter();
            w.WriteUInt32(0x04030201);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, w.ToArray());
        }

        [Test]
        public void HalfPrecisionRoundTripsWithinTolerance()
        {
            var w = new NetworkWriter();
            w.WriteHalf(12.375f);
            Assert.AreEqual(2, w.Length);
            Assert.AreEqual(12.375f, ReaderOver(w).ReadHalf(), 0.01f);
        }

        #endregion

        #region - Variable-length integers -

        [TestCase(0u, 1)]
        [TestCase(127u, 1)]
        [TestCase(128u, 2)]
        [TestCase(16383u, 2)]
        [TestCase(16384u, 3)]
        [TestCase(uint.MaxValue, 5)]
        public void VarUInt32UsesTheExpectedByteCount(uint value, int expectedBytes)
        {
            var w = new NetworkWriter();
            w.WriteVarUInt32(value);
            Assert.AreEqual(expectedBytes, w.Length);
            Assert.AreEqual(value, ReaderOver(w).ReadVarUInt32());
        }

        [TestCase(0L, 1)]
        [TestCase(127L, 1)]
        [TestCase(128L, 2)]
        [TestCase(long.MaxValue, 9)]
        public void VarUInt64UsesTheExpectedByteCount(long value, int expectedBytes)
        {
            var w = new NetworkWriter();
            w.WriteVarUInt64((ulong)value);
            Assert.AreEqual(expectedBytes, w.Length);
            Assert.AreEqual((ulong)value, ReaderOver(w).ReadVarUInt64());
        }

        [Test]
        public void VarUInt64MaxValueTakesTenBytes()
        {
            var w = new NetworkWriter();
            w.WriteVarUInt64(ulong.MaxValue);
            Assert.AreEqual(10, w.Length);
            Assert.AreEqual(ulong.MaxValue, ReaderOver(w).ReadVarUInt64());
        }

        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(1, 1)]
        [TestCase(-64, 1)]
        [TestCase(63, 1)]
        [TestCase(64, 2)]
        [TestCase(-65, 2)]
        [TestCase(int.MinValue, 5)]
        [TestCase(int.MaxValue, 5)]
        public void ZigZagKeepsSmallNegativesSmall(int value, int expectedBytes)
        {
            var w = new NetworkWriter();
            w.WriteVarInt32(value);
            Assert.AreEqual(expectedBytes, w.Length);
            Assert.AreEqual(value, ReaderOver(w).ReadVarInt32());
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1L << 40)]
        public void VarInt64RoundTrips(long value)
        {
            var w = new NetworkWriter();
            w.WriteVarInt64(value);
            Assert.AreEqual(value, ReaderOver(w).ReadVarInt64());
        }

        [Test]
        public void AMalformedVarIntThrowsInsteadOfLooping()
        {
            var r = new NetworkReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
            Assert.Throws<NetworkSerializationException>(() => r.ReadVarUInt32());
        }

        #endregion

        #region - Strings and bytes -

        [TestCase(null)]
        [TestCase("")]
        [TestCase("ascii")]
        [TestCase("ünïcödé — 日本語 🎭")]
        public void StringsRoundTripIncludingNullAndEmpty(string value)
        {
            var w = new NetworkWriter();
            w.WriteString(value);
            Assert.AreEqual(value, ReaderOver(w).ReadString());
        }

        [Test]
        public void NullAndEmptyStringsAreDistinguishable()
        {
            var w = new NetworkWriter();
            w.WriteString(null);
            w.WriteString(string.Empty);
            CollectionAssert.AreEqual(new byte[] { 0, 1 }, w.ToArray());
        }

        [Test]
        public void BytesWithLengthRoundTrip()
        {
            var payload = new byte[] { 9, 8, 7, 6, 5 };
            var w = new NetworkWriter();
            w.WriteBytesWithLength(payload);
            w.WriteBytesWithLength(ReadOnlySpan<byte>.Empty);

            var r = ReaderOver(w);
            CollectionAssert.AreEqual(payload, r.ReadBytesWithLength().ToArray());
            Assert.AreEqual(0, r.ReadBytesWithLength().Length);
        }

        [Test]
        public void BlittableStructsRoundTrip()
        {
            var value = new ProbeStruct { A = 42, B = -7.5f, C = 123456789012345UL };
            var w = new NetworkWriter();
            w.WriteBlittable(value);
            Assert.AreEqual(16, w.Length);
            var back = ReaderOver(w).ReadBlittable<ProbeStruct>();
            Assert.AreEqual(value.A, back.A);
            Assert.AreEqual(value.B, back.B);
            Assert.AreEqual(value.C, back.C);
        }

        [Test]
        public void UnityTypesRoundTrip()
        {
            var w = new NetworkWriter();
            w.WriteVector2(new Vector2(1, 2));
            w.WriteVector3(new Vector3(1, 2, 3));
            w.WriteVector4(new Vector4(1, 2, 3, 4));
            w.WriteQuaternion(Quaternion.Euler(10, 20, 30));

            var r = ReaderOver(w);
            Assert.AreEqual(new Vector2(1, 2), r.ReadVector2());
            Assert.AreEqual(new Vector3(1, 2, 3), r.ReadVector3());
            Assert.AreEqual(new Vector4(1, 2, 3, 4), r.ReadVector4());
            Assert.AreEqual(Quaternion.Euler(10, 20, 30), r.ReadQuaternion());
        }

        #endregion

        #region - Bits -

        [Test]
        public void BitsRoundTripAcrossByteBoundaries()
        {
            var w = new NetworkWriter();
            w.WriteBits(0b101u, 3);
            w.WriteBits(0x3FFu, 10);
            w.WriteBit(true);
            w.WriteBits(0xDEADBEEFu, 32);
            w.WriteBits(0x1FFFFFFFFFFul, 41);
            w.FlushBits();

            // 3 + 10 + 1 + 32 + 41 = 87 bits => 11 bytes.
            Assert.AreEqual(11, w.Length);

            var r = ReaderOver(w);
            Assert.AreEqual(0b101u, r.ReadBits(3));
            Assert.AreEqual(0x3FFu, r.ReadBits(10));
            Assert.IsTrue(r.ReadBit());
            Assert.AreEqual(0xDEADBEEFu, r.ReadBits(32));
            Assert.AreEqual(0x1FFFFFFFFFFul, r.ReadBits64(41));
        }

        [Test]
        public void ByteWritesPadPendingBitsAndReadersAlignTheSameWay()
        {
            var w = new NetworkWriter();
            w.WriteBits(0b11u, 2);   // pending
            w.WriteByte(0x55);       // pads the 2 bits to a byte first
            w.WriteBits(0x1Fu, 5);   // pending again
            w.WriteVarUInt32(300);   // pads again
            w.WriteBits(1u, 1);
            w.FlushBits();

            Assert.AreEqual(1 + 1 + 1 + 2 + 1, w.Length);

            var r = ReaderOver(w);
            Assert.AreEqual(0b11u, r.ReadBits(2));
            Assert.AreEqual(0x55, r.ReadByte());
            Assert.AreEqual(0x1Fu, r.ReadBits(5));
            Assert.AreEqual(300u, r.ReadVarUInt32());
            Assert.AreEqual(1u, r.ReadBits(1));
            r.AlignBits();
            Assert.IsTrue(r.IsAtEnd);
        }

        [Test]
        public void ExactMultiplesOfEightBitsNeedNoPadding()
        {
            var w = new NetworkWriter();
            w.WriteBits(0xABCDu, 16);
            w.WriteByte(1);
            Assert.AreEqual(3, w.Length);
            var r = ReaderOver(w);
            Assert.AreEqual(0xABCDu, r.ReadBits(16));
            Assert.AreEqual(1, r.ReadByte());
        }

        [Test]
        public void HigherBitsAreMaskedOffOnWrite()
        {
            var w = new NetworkWriter();
            w.WriteBits(0xFFFFFFFFu, 4);
            w.FlushBits();
            Assert.AreEqual(0x0F, w.ToArray()[0]);
        }

        #endregion

        #region - Cursor, growth, bounds -

        [Test]
        public void ReserveAndPatchWriteALengthPrefixAfterTheFact()
        {
            var w = new NetworkWriter();
            int at = w.ReserveUInt16();
            w.WriteString("payload");
            w.PatchUInt16(at, (ushort)(w.Length - 2));

            var r = ReaderOver(w);
            Assert.AreEqual(w.Length - 2, r.ReadUInt16());
            Assert.AreEqual("payload", r.ReadString());
        }

        [Test]
        public void SeekingBackwardsKeepsTheHighWaterMark()
        {
            var w = new NetworkWriter();
            w.WriteInt32(1);
            w.WriteInt32(2);
            w.Seek(0);
            w.WriteInt32(9);
            Assert.AreEqual(8, w.Length);
            Assert.AreEqual(4, w.Position);
            var r = ReaderOver(w);
            Assert.AreEqual(9, r.ReadInt32());
            Assert.AreEqual(2, r.ReadInt32());
        }

        [Test]
        public void TheBufferGrowsOnDemand()
        {
            var w = new NetworkWriter(initialCapacity: 16);
            for (int i = 0; i < 1000; i++)
            {
                w.WriteInt32(i);
            }

            Assert.AreEqual(4000, w.Length);
            Assert.GreaterOrEqual(w.Capacity, 4000);

            var r = ReaderOver(w);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(i, r.ReadInt32());
            }
        }

        [Test]
        public void GrowthStopsAtTheMaximumCapacity()
        {
            var w = new NetworkWriter(initialCapacity: 16, maxCapacity: 32);
            w.WriteBytes(new byte[32]);
            Assert.Throws<BufferCapacityException>(() => w.WriteByte(1));
        }

        [Test]
        public void ReadingPastTheEndThrows()
        {
            var r = new NetworkReader(new byte[] { 1, 2, 3 });
            r.ReadInt16();
            Assert.Throws<EndOfBufferException>(() => r.ReadInt32());
            Assert.Throws<EndOfBufferException>(() => r.ReadBits(16));
        }

        [Test]
        public void ResetForgetsEverythingButKeepsTheArray()
        {
            var w = new NetworkWriter();
            w.WriteInt64(1);
            var array = w.Buffer;
            w.Reset();
            Assert.AreEqual(0, w.Length);
            Assert.AreEqual(0, w.Position);
            Assert.AreSame(array, w.Buffer);
        }

        [Test]
        public void ReaderCanBeRePointedAtASubRange()
        {
            var bytes = new byte[] { 0, 0, 7, 0, 0, 0, 0 };
            var r = new NetworkReader();
            r.SetSource(bytes, 2, 4);
            Assert.AreEqual(4, r.Length);
            Assert.AreEqual(7, r.ReadInt32());
            Assert.IsTrue(r.IsAtEnd);
        }

        #endregion

        private struct ProbeStruct
        {
            public int A;
            public float B;
            public ulong C;
        }
    }
}
