using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Vapor.Networking;

namespace Vapor.Tests.Networking
{
    /// <summary>
    /// The formatter registry: what resolves without registration, what a registration changes, and
    /// what happens for a type nothing can serve.
    /// </summary>
    [TestFixture]
    public class NetworkFormatterTests
    {
        private static T RoundTrip<T>(T value)
        {
            var w = new NetworkWriter();
            NetworkFormatters.Write(w, value);
            var segment = w.WrittenSegment;
            return NetworkFormatters.Read<T>(new NetworkReader(segment.Array, segment.Offset, segment.Count));
        }

        #region - Built-ins -

        [Test]
        public void PrimitivesResolveAndRoundTrip()
        {
            Assert.AreEqual(42, RoundTrip(42));
            Assert.AreEqual(-3L, RoundTrip(-3L));
            Assert.AreEqual(1.5, RoundTrip(1.5));
            Assert.AreEqual("hi", RoundTrip("hi"));
            Assert.AreEqual(true, RoundTrip(true));
            Assert.AreEqual((byte)7, RoundTrip((byte)7));
            Assert.AreEqual(12.34m, RoundTrip(12.34m));
        }

        [Test]
        public void SystemTypesRoundTrip()
        {
            var guid = Guid.NewGuid();
            Assert.AreEqual(guid, RoundTrip(guid));
            var now = new DateTime(2026, 8, 15, 12, 30, 0, DateTimeKind.Utc);
            Assert.AreEqual(now, RoundTrip(now));
            Assert.AreEqual(DateTimeKind.Utc, RoundTrip(now).Kind);
            Assert.AreEqual(TimeSpan.FromMinutes(90), RoundTrip(TimeSpan.FromMinutes(90)));
        }

        [Test]
        public void UnityTypesRoundTrip()
        {
            Assert.AreEqual(new Vector3(1, 2, 3), RoundTrip(new Vector3(1, 2, 3)));
            Assert.AreEqual(new Vector2Int(-4, 5), RoundTrip(new Vector2Int(-4, 5)));
            Assert.AreEqual(new Vector3Int(1, -2, 3), RoundTrip(new Vector3Int(1, -2, 3)));
            Assert.AreEqual(Color.cyan, RoundTrip(Color.cyan));
            Assert.AreEqual(new Color32(1, 2, 3, 4), RoundTrip(new Color32(1, 2, 3, 4)));
            Assert.AreEqual(new Rect(1, 2, 3, 4), RoundTrip(new Rect(1, 2, 3, 4)));
            Assert.AreEqual(new Bounds(Vector3.one, Vector3.one * 2), RoundTrip(new Bounds(Vector3.one, Vector3.one * 2)));
            var pose = new Pose(new Vector3(1, 2, 3), Quaternion.Euler(0, 90, 0));
            Assert.AreEqual(pose, RoundTrip(pose));
            Assert.AreEqual(Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one * 2), RoundTrip(Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one * 2)));
        }

        #endregion

        #region - Enums -

        private enum ByteEnum : byte { A = 1, B = 200 }
        private enum IntEnum { Negative = -3, Zero = 0, Big = 1 << 20 }
        private enum LongEnum : long { Huge = 1L << 40, Low = -1L << 40 }
        [Flags] private enum FlagsEnum : ushort { None = 0, X = 1, Y = 2, Z = 4 }

        [Test]
        public void EnumsOfEveryUnderlyingWidthRoundTrip()
        {
            Assert.AreEqual(ByteEnum.B, RoundTrip(ByteEnum.B));
            Assert.AreEqual(IntEnum.Negative, RoundTrip(IntEnum.Negative));
            Assert.AreEqual(IntEnum.Big, RoundTrip(IntEnum.Big));
            Assert.AreEqual(LongEnum.Huge, RoundTrip(LongEnum.Huge));
            Assert.AreEqual(LongEnum.Low, RoundTrip(LongEnum.Low));
            Assert.AreEqual(FlagsEnum.X | FlagsEnum.Z, RoundTrip(FlagsEnum.X | FlagsEnum.Z));
        }

        [Test]
        public void SmallEnumValuesCostOneByte()
        {
            var w = new NetworkWriter();
            NetworkFormatters.Write(w, IntEnum.Zero);
            NetworkFormatters.Write(w, ByteEnum.A);
            Assert.AreEqual(2, w.Length);
        }

        #endregion

        #region - Collections -

        [Test]
        public void ArraysListsSetsAndDictionariesRoundTrip()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, RoundTrip(new[] { 1, 2, 3 }));
            CollectionAssert.AreEqual(new List<string> { "a", null, "c" }, RoundTrip(new List<string> { "a", null, "c" }));
            CollectionAssert.AreEquivalent(new HashSet<int> { 5, 6 }, RoundTrip(new HashSet<int> { 5, 6 }));
            var dict = new Dictionary<string, Vector3> { ["a"] = Vector3.up, ["b"] = Vector3.down };
            CollectionAssert.AreEquivalent(dict, RoundTrip(dict));
            Assert.AreEqual(new KeyValuePair<int, string>(1, "x"), RoundTrip(new KeyValuePair<int, string>(1, "x")));
        }

        [Test]
        public void CollectionCountsAreValidatedBeforeAllocation()
        {
            var w = new NetworkWriter();
            w.WriteVarUInt32(1_000_001); // nullable prefix: one million elements, no element bytes
            var reader = new NetworkReader(w.ToArray());
            Assert.Throws<NetworkSerializationException>(() => reader.Read<int[]>());

            reader.SetSource(w.Buffer, 0, w.Length);
            Assert.Throws<NetworkSerializationException>(() => reader.Read<List<int>>());

            reader.SetSource(w.Buffer, 0, w.Length);
            Assert.Throws<NetworkSerializationException>(() => reader.Read<Dictionary<int, int>>());
        }

        [Test]
        public void NullCollectionsRoundTripAsNull()
        {
            Assert.IsNull(RoundTrip<int[]>(null));
            Assert.IsNull(RoundTrip<List<int>>(null));
            Assert.IsNull(RoundTrip<Dictionary<int, int>>(null));
            Assert.IsNull(RoundTrip<byte[]>(null));
        }

        [Test]
        public void EmptyCollectionsAreNotNull()
        {
            Assert.IsNotNull(RoundTrip(Array.Empty<int>()));
            Assert.AreEqual(0, RoundTrip(new List<int>()).Count);
        }

        [Test]
        public void NestedCollectionsResolveRecursively()
        {
            var value = new List<int[]> { new[] { 1 }, new[] { 2, 3 } };
            var back = RoundTrip(value);
            Assert.AreEqual(2, back.Count);
            CollectionAssert.AreEqual(new[] { 2, 3 }, back[1]);
        }

        [Test]
        public void NullablesRoundTrip()
        {
            Assert.AreEqual(5, RoundTrip<int?>(5));
            Assert.IsNull(RoundTrip<int?>(null));
        }

        #endregion

        #region - Registration -

        private struct Custom { public int X; public string Name; }
        private struct Unregistered { public int Y; }

        [Test]
        public void ARegisteredFormatterIsUsedAndPropagatesToCollections()
        {
            NetworkFormatters.Register<Custom>(
                (NetworkWriter w, in Custom v) => { w.WriteVarInt32(v.X); w.WriteString(v.Name); },
                r => new Custom { X = r.ReadVarInt32(), Name = r.ReadString() });

            var back = RoundTrip(new Custom { X = 9, Name = "nine" });
            Assert.AreEqual(9, back.X);
            Assert.AreEqual("nine", back.Name);

            var list = RoundTrip(new List<Custom> { new() { X = 1, Name = "one" } });
            Assert.AreEqual("one", list[0].Name);
            Assert.IsTrue(NetworkFormatters.CanSerialize(typeof(Custom[])));
        }

        [Test]
        public void AMissingFormatterThrowsAHelpfulException()
        {
            Assert.IsFalse(NetworkFormatters.CanSerialize(typeof(Unregistered)));
            Assert.IsFalse(NetworkFormatters.TryGet<Unregistered>(out _));
            var ex = Assert.Throws<NetworkFormatterMissingException>(() => NetworkFormatters.Get<Unregistered>());
            Assert.AreEqual(typeof(Unregistered), ex.ValueType);
            StringAssert.Contains("NetworkSerializable", ex.Message);
        }

        [Test]
        public void ReRegistrationReplacesTheFormatter()
        {
            NetworkFormatters.Register<Custom>((NetworkWriter w, in Custom v) => w.WriteInt32(v.X), r => new Custom { X = r.ReadInt32() });
            var w = new NetworkWriter();
            NetworkFormatters.Write(w, new Custom { X = 1, Name = "ignored" });
            Assert.AreEqual(4, w.Length);

            NetworkFormatters.Register<Custom>((NetworkWriter w2, in Custom v) => w2.WriteInt64(v.X), r => new Custom { X = (int)r.ReadInt64() });
            w.Reset();
            NetworkFormatters.Write(w, new Custom { X = 1 });
            Assert.AreEqual(8, w.Length);
        }

        [Test]
        public void TheUntypedFaceRoundTripsBoxedValues()
        {
            var formatter = NetworkFormatters.Get(typeof(Vector2));
            var w = new NetworkWriter();
            formatter.WriteBoxed(w, new Vector2(3, 4));
            var segment = w.WrittenSegment;
            Assert.AreEqual(new Vector2(3, 4), formatter.ReadBoxed(new NetworkReader(segment.Array, segment.Offset, segment.Count)));
        }

        #endregion
    }
}
