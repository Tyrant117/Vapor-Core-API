using System;
using NUnit.Framework;
using UnityEngine;
using Vapor.Serialization;

namespace Vapor.Tests.Serialization
{
    /// <summary>
    /// Serialize then deserialize must give back what went in, for every supported shape.
    /// </summary>
    public class VslRoundTripTests
    {
        private static T RoundTrip<T>(T value) => Vsl.Deserialize<T>(Vsl.Serialize(value));

        #region Unity types

        [Test]
        public void UnityValueTypesSurviveExactly()
        {
            var original = UnityTypesFixture.Seeded();
            var copy = RoundTrip(original);

            Assert.AreEqual(original.Vector2, copy.Vector2);
            Assert.AreEqual(original.Vector3, copy.Vector3);
            Assert.AreEqual(original.Vector4, copy.Vector4);
            Assert.AreEqual(original.Vector2Int, copy.Vector2Int);
            Assert.AreEqual(original.Vector3Int, copy.Vector3Int);
            Assert.AreEqual(original.Quaternion.x, copy.Quaternion.x, 1e-6f);
            Assert.AreEqual(original.Quaternion.w, copy.Quaternion.w, 1e-6f);
            Assert.AreEqual(original.Matrix, copy.Matrix);
            Assert.AreEqual(original.Color, copy.Color);
            Assert.AreEqual(original.Color8Bit, copy.Color8Bit);
            Assert.AreEqual(original.Color32.r, copy.Color32.r);
            Assert.AreEqual(original.Color32.a, copy.Color32.a);
            Assert.AreEqual(original.Rect, copy.Rect);
            Assert.AreEqual(original.RectInt.width, copy.RectInt.width);
            Assert.AreEqual(original.Bounds, copy.Bounds);
            Assert.AreEqual(original.BoundsInt.position, copy.BoundsInt.position);
            Assert.AreEqual(original.BoundsInt.size, copy.BoundsInt.size);
            Assert.AreEqual(original.LayerMask.value, copy.LayerMask.value);
            Assert.AreEqual(original.RenderingLayerMask.value, copy.RenderingLayerMask.value);
            Assert.AreEqual(original.Hash, copy.Hash);
        }

        [Test]
        public void ColourUsesHexOnlyWhenItIsExact()
        {
            var text = Vsl.Serialize(UnityTypesFixture.Seeded());

            // 0.5 is 127.5/255, so hex would lose it — that one has to fall back to a tuple.
            StringAssert.Contains("color: (0.5, 0.25, 0.125, 1)", text);
            StringAssert.Contains("color8Bit: 0xFF8800FF", text);
            StringAssert.Contains("color32: 0xFF8800FF", text);
        }

        [Test]
        public void AnimationCurveSurvives()
        {
            var copy = RoundTrip(UnityTypesFixture.Seeded());

            Assert.AreEqual(2, copy.Curve.keys.Length);
            Assert.AreEqual(0f, copy.Curve.keys[0].time, 1e-6f);
            Assert.AreEqual(1f, copy.Curve.keys[1].value, 1e-6f);
        }

        [Test]
        public void GradientSurvives()
        {
            var copy = RoundTrip(UnityTypesFixture.Seeded());

            Assert.AreEqual(2, copy.Gradient.colorKeys.Length);
            Assert.AreEqual(2, copy.Gradient.alphaKeys.Length);
            Assert.AreEqual(Color.red, copy.Gradient.colorKeys[0].color);
            Assert.AreEqual(0f, copy.Gradient.alphaKeys[1].alpha, 1e-6f);
        }

        #endregion

        #region Collections

        [Test]
        public void CollectionsSurvive()
        {
            var original = CollectionsFixture.Seeded();
            var copy = RoundTrip(original);

            CollectionAssert.AreEqual(original.Array, copy.Array);
            CollectionAssert.AreEqual(original.List, copy.List);
            CollectionAssert.AreEquivalent(original.Set, copy.Set);
            CollectionAssert.AreEqual(original.Queue, copy.Queue);
            CollectionAssert.AreEqual(original.Linked, copy.Linked);
            Assert.AreEqual(original.Pair, copy.Pair);
            Assert.AreEqual(original.Tuple, copy.Tuple);
            Assert.AreEqual(5, copy.NullableSet);
            Assert.IsNull(copy.NullableUnset);
            Assert.AreEqual("x", copy.Nested[0].Id);
        }

        [Test]
        public void StackKeepsItsOrder()
        {
            // Enumerating a stack yields top-first; pushing that order back would invert it.
            var copy = RoundTrip(CollectionsFixture.Seeded());
            Assert.AreEqual(3, copy.Stack.Peek());
            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, copy.Stack);
        }

        [Test]
        public void DictionaryKeyShapeFollowsTheKeyType()
        {
            var text = Vsl.Serialize(CollectionsFixture.Seeded());

            StringAssert.Contains("stringKeyed: { a: 1  b: 2 }", text);
            StringAssert.Contains("enumKeyed: { Fire: 1.5 }", text);
            // A struct key cannot be a member name, so it falls back to pairs.
            StringAssert.Contains("structKeyed: [ ((1, 2), \"here\") ]", text);

            var copy = RoundTrip(CollectionsFixture.Seeded());
            Assert.AreEqual(1, copy.StringKeyed["a"]);
            Assert.AreEqual(1.5f, copy.EnumKeyed[Element.Fire]);
            Assert.AreEqual("here", copy.StructKeyed[new Vector2Int(1, 2)]);
        }

        #endregion

        #region Member policy

        [Test]
        public void OptInSerializesOnlyMarkedMembers()
        {
            var text = Vsl.Serialize(new OptInFixture { Included = 1, Excluded = 2, Property = 3, NotAProperty = 4 });

            StringAssert.Contains("included: 1", text);
            StringAssert.Contains("property: 3", text);
            StringAssert.DoesNotContain("excluded", text);
            StringAssert.DoesNotContain("notAProperty", text);
        }

        [Test]
        public void UnityRulesFollowUnitysOwnPolicy()
        {
            var text = Vsl.Serialize(UnityRulesFixture.Seeded());

            StringAssert.Contains("publicField: 1", text);
            StringAssert.Contains("serializeField: 2", text);
            StringAssert.Contains("forcedIn: 5", text);
            StringAssert.Contains("renamed: 7", text);
            StringAssert.DoesNotContain("plainPrivate", text);
            StringAssert.DoesNotContain("ignored", text);
            StringAssert.DoesNotContain("notSerialized", text);

            var copy = RoundTrip(UnityRulesFixture.Seeded());
            Assert.AreEqual(1, copy.PublicField);
            Assert.AreEqual(2, copy.SerializeField);
            Assert.AreEqual(5, copy.ForcedIn);
            Assert.AreEqual(7, copy.OriginalName);
            Assert.AreEqual(0, copy.Ignored, "[VslIgnore] is not restored");
            Assert.AreEqual(11, copy.PlainPrivate, "an unserialized field keeps its constructed value");
        }

        [Test]
        public void MemberNamesAreNormalisedToCamelCase()
        {
            var text = Vsl.Serialize(GoldenFixture.Seeded());

            StringAssert.Contains("healthFraction:", text);
            StringAssert.Contains("count: 7", text, "the _ prefix is stripped");
            StringAssert.DoesNotContain("_count", text);
            StringAssert.DoesNotContain("HealthFraction", text);
        }

        [Test]
        public void CommentAttributeIsEmitted()
        {
            StringAssert.Contains("# 0-1, drives the HUD bar", Vsl.Serialize(GoldenFixture.Seeded()));
        }

        #endregion

        #region Polymorphism

        [Test]
        public void DerivedTypeIsTaggedAndRebuilt()
        {
            var original = new PolymorphicFixture { Power = new FireballAbility { Cooldown = 2.5f, Damage = 25 } };
            original.Rotation.Add(new FireballAbility { Damage = 1 });
            original.Rotation.Add(new HealingAbility { Amount = 2 });

            var text = Vsl.Serialize(original);
            StringAssert.Contains("!FireballAbility", text);
            StringAssert.Contains("!Heal ", text, "[VslType] overrides the class name");

            var copy = Vsl.Deserialize<PolymorphicFixture>(text);
            Assert.IsInstanceOf<FireballAbility>(copy.Power);
            Assert.AreEqual(25, ((FireballAbility)copy.Power).Damage);
            Assert.AreEqual(2.5f, copy.Power.Cooldown);
            Assert.IsInstanceOf<FireballAbility>(copy.Rotation[0]);
            Assert.IsInstanceOf<HealingAbility>(copy.Rotation[1]);
            Assert.AreEqual(2, ((HealingAbility)copy.Rotation[1]).Amount);
        }

        [Test]
        public void SameTagOnUnrelatedHierarchiesResolvesPerSlot()
        {
            var original = new TagCollisionFixture
            {
                Power = new HealingAbility { Amount = 10 },
                Effect = new HealingEffect { PerTick = 3 },
            };

            var text = Vsl.Serialize(original);
            Assert.AreEqual(2, CountOccurrences(text, "!Heal "), "both slots write the same short tag");

            var copy = Vsl.Deserialize<TagCollisionFixture>(text);
            Assert.IsInstanceOf<HealingAbility>(copy.Power);
            Assert.IsInstanceOf<HealingEffect>(copy.Effect);
            Assert.AreEqual(10, ((HealingAbility)copy.Power).Amount);
            Assert.AreEqual(3, ((HealingEffect)copy.Effect).PerTick);
        }

        private static int CountOccurrences(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        #endregion

        #region Nulls, populate, leniency

        [Test]
        public void NullsSurvive()
        {
            var copy = RoundTrip(new PolymorphicFixture { Power = null, Rotation = null });
            Assert.IsNull(copy.Power);
            Assert.IsNull(copy.Rotation);
        }

        [Test]
        public void PopulateOnlyTouchesMembersThatArePresent()
        {
            var target = new GoldenFixture { Label = "Original", HealthFraction = 0.9f };
            Vsl.Populate(target, "{ label: \"Renamed\" }");

            Assert.AreEqual("Renamed", target.Label);
            Assert.AreEqual(0.9f, target.HealthFraction, "an absent member is left alone");
        }

        [Test]
        public void AcceptsHandWrittenInput()
        {
            const string messy = @"{
                ""label"": ""Hand written"",
                HEALTHFRACTION: 0.5,
                _count: 3,
                unknownMember: { whatever: [1 2 3] },
                mode: ice,
                position: (1, 2),
            }";

            var fixture = Vsl.Deserialize<GoldenFixture>(messy);

            Assert.AreEqual("Hand written", fixture.Label);
            Assert.AreEqual(0.5f, fixture.HealthFraction, "matched case-insensitively");
            Assert.AreEqual(3, fixture.Count, "matched through the _ prefix");
            Assert.AreEqual(Element.Ice, fixture.Mode, "enum matched case-insensitively");
            Assert.AreEqual(new Vector3(1f, 2f, 0f), fixture.Position, "short tuple filled the gap");
        }

        [Test]
        public void StrictModeRejectsAnUnknownMember()
        {
            var context = new VslContext(VslOptions.Validating);
            Assert.Throws<VslException>(() => Vsl.Deserialize<GoldenFixture>("{ nope: 1 }", context));
        }

        [Test]
        public void OutputIsByteStableAcrossRoundTrips()
        {
            // The property golden-file tests depend on.
            var text = Vsl.Serialize(GoldenFixture.Seeded());
            Assert.AreEqual(text, Vsl.Serialize(Vsl.Deserialize<GoldenFixture>(text)));
        }

        [Test]
        public void DirectReferenceCycleIsReportedNotOverflowed()
        {
            var node = new CycleNode { Id = "a" };
            node.Next = node;

            Assert.Throws<VslException>(() => Vsl.Serialize(node));
        }

        [Test]
        public void CycleThroughACollectionIsAlsoReported()
        {
            // The guard lives in the writer rather than in the object formatter, so a cycle that
            // only ever passes through a List is caught too.
            var node = new CycleNode { Id = "a" };
            node.Children.Add(node);

            Assert.Throws<VslException>(() => Vsl.Serialize(node));
        }

        #endregion
    }
}
