using System;
using System.Globalization;
using Unity.Scripting.LifecycleManagement;
using Vapor.GameplayTags;

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes a gameplay tag as its dotted name — <c>Ability.Fire.Burn</c> — not as its hash.
    /// </summary>
    /// <remarks>
    /// A <see cref="GameplayTag"/> is a uint xxHash32 of its name, so the hash alone is unreadable
    /// and uneditable. Writing the name costs a tree lookup and makes the document legible; reading
    /// it back re-hashes, which means a tag an AI invented resolves correctly even though it was
    /// never registered. A tag whose name cannot be recovered falls back to its raw key so the value
    /// still survives the round trip.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class GameplayTagFormatter : VslFormatter<GameplayTag>, IVslNameKeyFormatter<GameplayTag>
    {
        private const string NoneName = "None";

        public static readonly GameplayTagFormatter Instance = new GameplayTagFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in GameplayTag value, VslContext context)
        {
            if (value.IsNone())
            {
                writer.WriteIdentifier(NoneName);
                return;
            }

            // GetName answers "None" for an unregistered key, which would silently discard the tag.
            // Check the map directly and keep the raw key when the name is genuinely unknown.
            if (GameplayTagTree.TagMap.TryGetValue(value.Key, out var node) && !string.IsNullOrEmpty(node?.Name))
            {
                writer.WriteIdentifier(node.Name);
                return;
            }

            writer.WriteUInt64(value.Key);
        }

        public override GameplayTag Read(ref VslReader reader, VslContext context)
        {
            if (reader.PeekKind() == VslValueKind.Number)
            {
                return new GameplayTag((uint)reader.ReadUInt64());
            }

            if (reader.TryReadNull())
            {
                return GameplayTag.None();
            }

            var name = reader.ReadIdentifier();
            return VslNames.Matches(name, NoneName)
                ? GameplayTag.None()
                : new GameplayTag(name.ToString());
        }

        /// <summary>
        /// The same name <see cref="Write"/> produces, for a dictionary keyed on tags — so an item's
        /// attributes read as <c>{ Attribute.Item.Durability: 100 }</c> rather than as pairs.
        /// </summary>
        /// <remarks>
        /// Not <see cref="GameplayTag.ToString"/>: that answers <c>None</c> for a key the tree does not
        /// know, which would collapse every unregistered tag in the dictionary onto one member and lose
        /// all but the last. An unknown key is written as its number instead, which
        /// <see cref="TryParseName"/> reads straight back.
        /// </remarks>
        public string ToName(in GameplayTag value)
        {
            if (value.IsNone())
            {
                return NoneName;
            }

            return GameplayTagTree.TagMap.TryGetValue(value.Key, out var node) && !string.IsNullOrEmpty(node?.Name)
                ? node.Name
                : value.Key.ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A bare number is a raw key, which is what <see cref="ToName"/> falls back to; anything else is
        /// a name and is re-hashed. The two can never be confused, because a tag name cannot be all
        /// digits — it has to start with a letter to be written bare in the first place.
        /// </remarks>
        public bool TryParseName(ReadOnlySpan<char> text, out GameplayTag value)
        {
            if (text.IsEmpty)
            {
                value = GameplayTag.None();
                return false;
            }

            if (uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var key))
            {
                value = new GameplayTag(key);
                return true;
            }

            value = VslNames.Matches(text, NoneName) ? GameplayTag.None() : new GameplayTag(text.ToString());
            return true;
        }
    }

    [NoAutoStaticsCleanup]
    public sealed class GameplayTagContainerFormatter : VslFormatter<GameplayTagContainer>
    {
        public static readonly GameplayTagContainerFormatter Instance = new GameplayTagContainerFormatter();

        public override void Write(ref VslWriter writer, in GameplayTagContainer value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var tags = value.Tags;
            writer.BeginSequence(tags.Count <= context.Options.InlineSequenceLimit);
            for (var i = 0; i < tags.Count; i++)
            {
                GameplayTagFormatter.Instance.Write(ref writer, tags[i], context);
            }

            writer.EndSequence();
        }

        public override GameplayTagContainer Read(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            var container = new GameplayTagContainer();
            var tags = container.Tags;

            reader.ReadSequenceStart();
            while (reader.TryReadSequenceItem())
            {
                tags.Add(GameplayTagFormatter.Instance.Read(ref reader, context));
            }

            return container;
        }
    }
}
