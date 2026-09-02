using System.Collections.Generic;
using NUnit.Framework;
using Vapor.GameplayTags;
using Vapor.Serialization;

namespace Vapor.Tests.Serialization
{
    /// <summary>
    /// Covers the gameplay-tag formatters, which Core registers into VSL rather than VSL shipping.
    /// </summary>
    /// <remarks>
    /// These live here, not beside the rest of the VSL round-trip tests, because
    /// <see cref="GameplayTag"/> is a Core type: the serialization package knows nothing about it,
    /// and Core installs <see cref="GameplayTagFormatter"/> through
    /// <see cref="VslFormatterProviderAttribute"/>. That registration is exactly what these tests
    /// exercise — a tag that fell back to the reflection formatter would still round-trip, and would
    /// write an unreadable object where a dotted name belongs.
    /// </remarks>
    public class VslGameplayTagTests
    {
        [VslSerializable]
        public partial class TagFixture
        {
            public GameplayTag Tag;
            public Dictionary<GameplayTag, double> TagKeyed;
        }

        [Test]
        public void TagIsWrittenAsItsDottedName()
        {
            GameplayTagTree.InsertTag("Ability.Fire.Burn");

            var text = Vsl.Serialize(new TagFixture { Tag = new GameplayTag("Ability.Fire.Burn") });

            StringAssert.Contains("tag: Ability.Fire.Burn", text);
            Assert.AreEqual(new GameplayTag("Ability.Fire.Burn"), Vsl.Deserialize<TagFixture>(text).Tag);
        }

        [Test]
        public void TagKeyedDictionaryIsWrittenAsNamedMembers()
        {
            // Registered first: an unknown key has no name to be written as, and falls back to its number
            // the same way a tag value does.
            GameplayTagTree.InsertTag("Attribute.Item.Durability");

            var fixture = new TagFixture
            {
                TagKeyed = new Dictionary<GameplayTag, double> { [new GameplayTag("Attribute.Item.Durability")] = 100d },
            };

            var text = Vsl.Serialize(fixture);

            StringAssert.Contains("tagKeyed: { Attribute.Item.Durability: 100 }", text);

            var copy = Vsl.Deserialize<TagFixture>(text);
            Assert.AreEqual(100d, copy.TagKeyed[new GameplayTag("Attribute.Item.Durability")]);
        }

        [Test]
        public void TagKeyedDictionaryKeepsAnUnregisteredKey()
        {
            var tag = new GameplayTag("Attribute.Item.NeverRegistered.aa9f13");
            var fixture = new TagFixture
            {
                TagKeyed = new Dictionary<GameplayTag, double> { [tag] = 3d },
            };

            // The name cannot be recovered, so the key is written as its number rather than as "None" -
            // which every unresolved tag in the table would otherwise collapse onto.
            var copy = Vsl.Deserialize<TagFixture>(Vsl.Serialize(fixture));

            Assert.AreEqual(1, copy.TagKeyed.Count);
            Assert.AreEqual(3d, copy.TagKeyed[tag]);
        }
    }
}
