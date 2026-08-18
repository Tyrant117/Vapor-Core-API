using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Vapor.GameplayTags;
using Vapor.Inspector;
using VaporEditor.Inspector;

namespace Vapor.Tests.Inspector
{
    /// <summary>
    /// The dictionary half of <see cref="InspectorTreeProperty"/>: what the rows are, and what editing
    /// one does to the dictionary underneath.
    /// </summary>
    /// <remarks>
    /// Asserted against the dictionary rather than against the rows. A structural edit rebuilds the rows
    /// a frame later — the rebuild is deferred so it cannot tear down the widget that caused it — while
    /// the dictionary is written immediately, so it is the dictionary that says what happened.
    /// </remarks>
    public class DictionaryPropertyTests
    {
        private class Fixture
        {
            public Dictionary<GameplayTag, double> Attributes = new();
        }

        private static readonly GameplayTag Durability = new("Attribute.Item.Durability");
        private static readonly GameplayTag Value = new("Attribute.Item.Value");
        private static readonly GameplayTag Volume = new("Attribute.Item.Volume");

        private static InspectorTreeProperty PropertyOf(Fixture fixture)
        {
            var tree = new InspectorTreeObject(fixture, typeof(Fixture));
            return tree.Fields.First(field => field.PropertyName == nameof(Fixture.Attributes));
        }

        private static Fixture Seeded() => new()
        {
            Attributes = new Dictionary<GameplayTag, double>
            {
                [Durability] = 100d,
                [Value] = 25d,
            },
        };

        [Test]
        public void ADictionaryIsNotWalkedAsAnObject()
        {
            var property = PropertyOf(Seeded());

            // Without this it reflects over Comparer, Count, Keys and Values - the members of the
            // dictionary itself - instead of over what it holds.
            Assert.IsTrue(property.IsDictionary);
            Assert.IsEmpty(property.Fields);
            Assert.AreEqual(typeof(GameplayTag), property.DictionaryHelper.KeyType);
            Assert.AreEqual(typeof(double), property.DictionaryHelper.ValueType);
        }

        [Test]
        public void EveryEntryBecomesAKeyAndAValue()
        {
            var property = PropertyOf(Seeded());

            Assert.AreEqual(2, property.DictionarySize);

            var first = property.DictionaryData[0];
            Assert.IsTrue(first.Key.IsDictionaryEntry);
            Assert.IsTrue(first.Key.IsEntryKey);
            Assert.IsFalse(first.Value.IsEntryKey);
            Assert.AreEqual(Durability, first.Key.GetValue());
            Assert.AreEqual(100d, first.Value.GetValue());
        }

        [Test]
        public void AnEntryHasNoLabelOfItsOwn()
        {
            var property = PropertyOf(Seeded());

            // The row's two columns are what say which half is which.
            Assert.IsEmpty(property.DictionaryData[0].Key.DisplayName);
            Assert.IsTrue(property.DictionaryData[0].Key.HasAttribute<HideLabelAttribute>());
        }

        [Test]
        public void EditingAValueWritesThroughToTheDictionary()
        {
            var fixture = Seeded();
            var property = PropertyOf(fixture);

            property.DictionaryData[0].Value.SetValue(55d);

            Assert.AreEqual(55d, fixture.Attributes[Durability]);
            Assert.AreEqual(2, fixture.Attributes.Count);
        }

        [Test]
        public void RenamingAKeyKeepsTheEntryWhereItWas()
        {
            var fixture = Seeded();
            var property = PropertyOf(fixture);

            property.DictionaryData[0].Key.SetValue(Volume);

            Assert.IsFalse(fixture.Attributes.ContainsKey(Durability));
            Assert.AreEqual(100d, fixture.Attributes[Volume]);

            // Order is the point: a remove-then-add would drop the renamed entry wherever its new hash
            // landed, and the row would jump out from under whoever was editing it.
            CollectionAssert.AreEqual(new[] { Volume, Value }, fixture.Attributes.Keys);
            CollectionAssert.AreEqual(new[] { 100d, 25d }, fixture.Attributes.Values);
        }

        [Test]
        public void RenamingOntoATakenKeyIsRefused()
        {
            var fixture = Seeded();
            var property = PropertyOf(fixture);

            // Merging the two would destroy whichever entry lost, so nothing happens at all.
            property.DictionaryData[0].Key.SetValue(Value);

            Assert.AreEqual(2, fixture.Attributes.Count);
            Assert.AreEqual(100d, fixture.Attributes[Durability]);
            Assert.AreEqual(25d, fixture.Attributes[Value]);
        }

        [Test]
        public void AddingAnEntryAppendsItUnderTheDefaultKey()
        {
            var fixture = Seeded();
            var property = PropertyOf(fixture);

            property.AddEntry();

            Assert.AreEqual(3, fixture.Attributes.Count);
            Assert.IsTrue(fixture.Attributes.ContainsKey(GameplayTag.None()));
            CollectionAssert.AreEqual(new[] { Durability, Value, GameplayTag.None() }, fixture.Attributes.Keys);
        }

        [Test]
        public void AddingASecondUnnamedEntryIsRefused()
        {
            var fixture = Seeded();
            var property = PropertyOf(fixture);

            property.AddEntry();
            property.AddEntry();

            // There is no second default tag to invent, so the empty row has to be named before another
            // can be added.
            Assert.AreEqual(3, fixture.Attributes.Count);
        }

        [Test]
        public void RemovingAnEntryLeavesTheRestInOrder()
        {
            var fixture = Seeded();
            fixture.Attributes[Volume] = 2d;
            var property = PropertyOf(fixture);

            property.RemoveEntryAt(1);

            Assert.AreEqual(2, fixture.Attributes.Count);
            CollectionAssert.AreEqual(new[] { Durability, Volume }, fixture.Attributes.Keys);
            CollectionAssert.AreEqual(new[] { 100d, 2d }, fixture.Attributes.Values);
        }

        [Test]
        public void ANullDictionaryIsCreatedRatherThanDrawnEmpty()
        {
            var fixture = new Fixture { Attributes = null };

            var property = PropertyOf(fixture);

            Assert.IsNotNull(fixture.Attributes);
            Assert.AreEqual(0, property.DictionarySize);
        }

        [Test]
        public void AnIntegerKeyedDictionaryCountsThroughItsDefaults()
        {
            var counts = new CountFixture();
            var tree = new InspectorTreeObject(counts, typeof(CountFixture));
            var property = tree.Fields.First(field => field.PropertyName == nameof(CountFixture.Counts));

            property.AddEntry();
            property.AddEntry();

            // A key that can be counted through gets the next free value rather than being refused.
            Assert.AreEqual(2, counts.Counts.Count);
            CollectionAssert.AreEqual(new[] { 0u, 1u }, counts.Counts.Keys);
        }

        private class CountFixture
        {
            public Dictionary<uint, int> Counts = new();
        }
    }
}
