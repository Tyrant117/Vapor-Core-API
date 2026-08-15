using NUnit.Framework;
using UnityEngine;
using Vapor.Networking;
using Vapor.Unsafe;

namespace Vapor.Tests.Networking
{
    [TestFixture]
    public class NetworkQuantizationTests
    {
        private static NetworkReader ReaderOver(NetworkWriter writer)
        {
            var segment = writer.WrittenSegment;
            return new NetworkReader(segment.Array, segment.Offset, segment.Count);
        }

        [TestCase(0f, 8)]
        [TestCase(-100f, 8)]
        [TestCase(100f, 8)]
        [TestCase(37.123f, 12)]
        [TestCase(-0.5f, 16)]
        public void RangedFloatsStayWithinTheirDocumentedError(float value, int bits)
        {
            var w = new NetworkWriter();
            NetworkQuantization.WriteRangedFloat(w, value, -100f, 100f, bits);
            w.FlushBits();
            Assert.AreEqual((bits + 7) / 8, w.Length);

            float back = NetworkQuantization.ReadRangedFloat(ReaderOver(w), -100f, 100f, bits);
            Assert.AreEqual(value, back, NetworkQuantization.RangedFloatError(-100f, 100f, bits) + 1e-4f);
        }

        [Test]
        public void RangedFloatsClampOutOfRangeValues()
        {
            var w = new NetworkWriter();
            NetworkQuantization.WriteRangedFloat(w, 500f, 0f, 1f, 8);
            w.FlushBits();
            Assert.AreEqual(1f, NetworkQuantization.ReadRangedFloat(ReaderOver(w), 0f, 1f, 8), 1e-5f);
        }

        [TestCase(0f)]
        [TestCase(12.34567f)]
        [TestCase(-987.654f)]
        [TestCase(0.004f)]
        public void FixedPointRoundsToThePrecision(float value)
        {
            var w = new NetworkWriter();
            NetworkQuantization.WriteFixedPoint(w, value, 0.01f);
            float back = NetworkQuantization.ReadFixedPoint(ReaderOver(w), 0.01f);
            Assert.AreEqual(value, back, 0.005f + 1e-5f);
        }

        [Test]
        public void FixedPointOnCentimetresCostsTwoBytesUnderEightyMetresAndThreeUnderTenKilometres()
        {
            var w = new NetworkWriter();
            NetworkQuantization.WriteFixedPoint(w, 50f, 0.01f);
            Assert.AreEqual(2, w.Length);   // 5000 steps -> 10000 zig-zag -> 14 bits -> 2 bytes
            w.Reset();
            NetworkQuantization.WriteFixedPoint(w, -163.84f, 0.01f);
            Assert.AreEqual(3, w.Length);   // 16384 steps -> 32767 zig-zag -> 15 bits -> 3 bytes
            w.Reset();
            NetworkQuantization.WriteFixedPoint(w, 9999f, 0.01f);
            Assert.AreEqual(3, w.Length);   // 999900 steps -> ~2^21 -> still 3 bytes
        }

        [Test]
        public void FixedPointBitsRoundTripWithinRange()
        {
            var w = new NetworkWriter();
            NetworkQuantization.WriteFixedPointBits(w, 12.34f, 0.01f, 16);   // ±327.68
            NetworkQuantization.WriteFixedPointBits(w, -300f, 0.01f, 16);
            w.FlushBits();
            Assert.AreEqual(4, w.Length);
            var r = ReaderOver(w);
            Assert.AreEqual(12.34f, NetworkQuantization.ReadFixedPointBits(r, 0.01f, 16), 0.0051f);
            Assert.AreEqual(-300f, NetworkQuantization.ReadFixedPointBits(r, 0.01f, 16), 0.0051f);
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(90f, 0f, 0f)]
        [TestCase(0f, 180f, 0f)]
        [TestCase(45f, 45f, 45f)]
        [TestCase(-30f, 120f, 275f)]
        [TestCase(179.9f, -89.9f, 0.1f)]
        public void SmallestThreeKeepsRotationsWithinATenthOfADegree(float x, float y, float z)
        {
            var q = Quaternion.Euler(x, y, z);
            var w = new NetworkWriter();
            NetworkQuantization.WriteSmallestThree(w, q, 10);
            w.FlushBits();
            Assert.AreEqual(4, w.Length);   // 2 + 3 * 10 = 32 bits

            var back = NetworkQuantization.ReadSmallestThree(ReaderOver(w), 10);
            Assert.LessOrEqual(Quaternion.Angle(q, back), 0.2f);
        }

        [Test]
        public void SmallestThreeHandlesNegativeLargestComponent()
        {
            var q = new Quaternion(0.1f, 0.2f, 0.3f, -0.927f).normalized;   // -q of a rotation
            var w = new NetworkWriter();
            NetworkQuantization.WriteSmallestThree(w, q);
            w.FlushBits();
            var back = NetworkQuantization.ReadSmallestThree(ReaderOver(w));
            Assert.LessOrEqual(Quaternion.Angle(q, back), 0.2f);
        }

        [Test]
        public void UnitVectorsRoundTripWithinACoupleOfDegrees()
        {
            var direction = new Vector3(0.3f, -0.5f, 0.81f).normalized;
            var w = new NetworkWriter();
            NetworkQuantization.WriteUnitVector(w, direction, 8);
            w.FlushBits();
            var back = NetworkQuantization.ReadUnitVector(ReaderOver(w), 8);
            Assert.LessOrEqual(Vector3.Angle(direction, back), 2f);
        }

        [Test]
        public void XxHash32MatchesTheUnsafeImplementation()
        {
            foreach (var text in new[] { "", "a", "Actors.Rpg.Player", "Ability.Fire.Burn", "Vapor.Networking.Replicator", new string('x', 1000), "ünïcödé 日本語" })
            {
                Assert.AreEqual(XxHash.Hash32(text), XxHash32.Hash(text), $"'{text}'");
            }
        }
    }
}
