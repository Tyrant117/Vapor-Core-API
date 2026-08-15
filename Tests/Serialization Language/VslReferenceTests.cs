using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vapor.Serialization;
using Object = UnityEngine.Object;

namespace Vapor.Tests.Serialization
{
    /// <summary>
    /// <c>UnityEngine.Object</c> members serialize as references and resolve back to the same
    /// instance — by <c>EntityId</c> within a session, and by asset locator across one.
    /// </summary>
    public class VslReferenceTests
    {
        private readonly List<Object> _created = new List<Object>();

        private ScriptableObject NewAsset(string name)
        {
            var asset = ScriptableObject.CreateInstance<ScriptableObject>();
            asset.name = name;
            _created.Add(asset);
            return asset;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _created)
            {
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }
            }

            _created.Clear();

            // The test framework puts this back itself, but a leaked "ignore everything" would
            // quietly blind every test that ran after it, so it is reset by hand as well.
            LogAssert.ignoreFailingMessages = false;
        }

        #region Session references

        [Test]
        public void ReferenceResolvesBackToTheSameInstanceWithinASession()
        {
            var asset = NewAsset("Sword");
            var fixture = new ObjectReferenceFixture { Asset = asset };
            fixture.Assets.Add(asset);

            var text = Vsl.Serialize(fixture);
            StringAssert.Contains(EntityId.ToULong(asset.GetEntityId()).ToString(), text);

            var copy = Vsl.Deserialize<ObjectReferenceFixture>(text);
            Assert.AreSame(asset, copy.Asset);
            Assert.AreSame(asset, copy.Assets[0]);
        }

        [Test]
        public void InMemoryObjectHasNoDurableLocator()
        {
            // Created at runtime, so it is not an asset: an id is all there is to record.
            var text = Vsl.Serialize(new ObjectReferenceFixture { Asset = NewAsset("Transient") });

            StringAssert.DoesNotContain("resource", text);
            StringAssert.DoesNotContain("addressable", text);
        }

        [Test]
        public void NullReferenceWritesAtNull()
        {
            var text = Vsl.Serialize(new ObjectReferenceFixture());

            StringAssert.Contains("asset: @null", text);
            Assert.IsNull(Vsl.Deserialize<ObjectReferenceFixture>(text).Asset);
        }

        [Test]
        public void DestroyedObjectWritesAtNullRatherThanADanglingId()
        {
            var asset = ScriptableObject.CreateInstance<ScriptableObject>();
            var fixture = new ObjectReferenceFixture { Asset = asset };
            Object.DestroyImmediate(asset);

            StringAssert.Contains("asset: @null", Vsl.Serialize(fixture));
        }

        [Test]
        public void UnresolvableReferenceBecomesNull()
        {
            Assert.IsNull(Vsl.Deserialize<ObjectReferenceFixture>("{ asset: @999999999999 }").Asset);
        }

        [Test]
        public void UnresolvableReferenceThrowsInStrictMode()
        {
            var context = new VslContext(VslOptions.Validating);
            Assert.Throws<VslException>(() =>
                Vsl.Deserialize<ObjectReferenceFixture>("{ asset: @999999999999 }", context));
        }

        #endregion

        #region Reference forms

        [Test]
        public void ParsesEveryReferenceForm()
        {
            AssertParses("@null", VslObjectReference.Null);
            AssertParses("@1234", new VslObjectReference(1234UL));
            AssertParses("@(1234, resource, \"UI/Icons/Sword\")",
                new VslObjectReference(1234UL, VslAssetSource.Resource, "UI/Icons/Sword"));
            AssertParses("@(resource, \"UI/Icons/Sword\")",
                new VslObjectReference(VslAssetSource.Resource, "UI/Icons/Sword"));
            AssertParses("@(addressable, \"Boss_Fireking\")",
                new VslObjectReference(VslAssetSource.Addressable, "Boss_Fireking"));

            // Unbracketed, short source names, and a bare string all read too.
            AssertParses("@resource \"UI/Icons/Sword\"",
                new VslObjectReference(VslAssetSource.Resource, "UI/Icons/Sword"));
            AssertParses("@(res, \"UI/Icons/Sword\")",
                new VslObjectReference(VslAssetSource.Resource, "UI/Icons/Sword"));
            AssertParses("@(addr, \"Boss_Fireking\")",
                new VslObjectReference(VslAssetSource.Addressable, "Boss_Fireking"));
            AssertParses("\"UI/Icons/Sword\"",
                new VslObjectReference(VslAssetSource.Resource, "UI/Icons/Sword"));
        }

        [Test]
        public void WritesTheCanonicalFormForEachReference()
        {
            Assert.AreEqual("@null", Write(VslObjectReference.Null));
            Assert.AreEqual("@1234", Write(new VslObjectReference(1234UL)));
            Assert.AreEqual("@(1234, resource, \"UI/Icons/Sword\")",
                Write(new VslObjectReference(1234UL, VslAssetSource.Resource, "UI/Icons/Sword")));
            Assert.AreEqual("@(addressable, \"Boss_Fireking\")",
                Write(new VslObjectReference(VslAssetSource.Addressable, "Boss_Fireking")));
        }

        [Test]
        public void ReferenceWithLocatorRoundTripsThroughAnObject()
        {
            // A reference carrying a locator must survive being skipped, nested, and re-read.
            const string document = @"{
                asset: @(1234, resource, ""UI/Icons/Sword"")
                missing: @(addressable, ""Boss_Fireking"")
                assets: [ @(res, ""A"")  @(addr, ""B"")  @null ]
            }";

            // Resolving these keys is meant to fail, and Addressables answers a key it does not
            // hold by logging an InvalidKeyException of its own — which the test framework counts
            // as a failure — before the loader here ever gets to swallow it. What that log says,
            // and whether it appears at all, belongs to the Addressables version and to whatever
            // catalog the project happens to be built with, so the noise is dropped wholesale
            // rather than pinned with LogAssert.Expect to a message this codebase does not own and
            // cannot promise will be emitted.
            LogAssert.ignoreFailingMessages = true;

            var fixture = Vsl.Deserialize<ObjectReferenceFixture>(document);

            // None of these resolve — nothing is at those paths — but parsing must not throw, and
            // the unresolved reference becomes null rather than derailing the whole document. This
            // is all the test asks of its environment: were something ever addressed
            // "Boss_Fireking", the reference would resolve and this would fail.
            Assert.IsNull(fixture.Asset);
            Assert.IsNull(fixture.Missing);
            Assert.AreEqual(3, fixture.Assets.Count);
        }

        [Test]
        public void KeyIsEmptyWithoutASource()
        {
            // A source of None means there is no key, whatever string was handed in.
            var reference = new VslObjectReference(1234UL, VslAssetSource.None, "ignored");

            Assert.IsFalse(reference.HasAssetKey);
            Assert.AreEqual("@1234", reference.ToString());
        }

        private static void AssertParses(string source, VslObjectReference expected)
        {
            var reader = new VslReader(source.AsSpan(), VslContext.Default);
            var found = reader.TryReadReference(out var actual);

            Assert.AreEqual(expected, actual, $"parsing '{source}'");
            Assert.AreEqual(!expected.IsNull, found, $"'{source}' should report {(expected.IsNull ? "absent" : "present")}");
        }

        private static string Write(VslObjectReference reference)
        {
            var options = VslOptions.Default.Clone();
            options.EmitHeader = false;

            var writer = new VslWriter(new VslContext(options));
            try
            {
                writer.WriteReference(reference);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        #endregion

        #region Resource paths

        [TestCase("Assets/Game/Resources/UI/Icons/Sword.png", ExpectedResult = "UI/Icons/Sword")]
        [TestCase("Assets/Resources/Player.prefab", ExpectedResult = "Player")]
        [TestCase("Assets/A/Resources/B/Resources/C.asset", ExpectedResult = "C")]
        [TestCase("Assets/Game/Prefabs/Player.prefab", ExpectedResult = null)]
        [TestCase("Assets/ResourcesLike/Player.prefab", ExpectedResult = null)]
        [TestCase("", ExpectedResult = null)]
        [TestCase(null, ExpectedResult = null)]
        public string DerivesResourcePathFromAssetPath(string assetPath) =>
            VslAssetLocator.ToResourcePath(assetPath);

        #endregion

        #region Reference table

        [Test]
        public void ReferenceTableRebindsDeterministically()
        {
            // The supported way to key references on something of your own instead of EntityId.
            var original = NewAsset("Original");
            var replacement = NewAsset("Replacement");

            var writeTable = new VslReferenceTable();
            writeTable.Register(1, original);

            var text = Vsl.Serialize(
                new ObjectReferenceFixture { Asset = original },
                new VslContext(VslOptions.Default) { References = writeTable });

            StringAssert.Contains("asset: @1", text);

            var readTable = new VslReferenceTable();
            readTable.Register(1, replacement);

            var copy = Vsl.Deserialize<ObjectReferenceFixture>(
                text,
                new VslContext(VslOptions.Default) { References = readTable });

            Assert.AreSame(replacement, copy.Asset, "the id resolved through the table, not the EntityId");
        }

        [Test]
        public void ReferenceTableAutoAssignsIds()
        {
            var table = new VslReferenceTable();
            var first = table.Register(NewAsset("A"));
            var second = table.Register(NewAsset("B"));

            Assert.AreEqual(1UL, first);
            Assert.AreEqual(2UL, second);
            Assert.AreEqual(2, table.Count);
        }

        #endregion
    }
}
