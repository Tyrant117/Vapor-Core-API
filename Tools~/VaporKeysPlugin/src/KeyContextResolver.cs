namespace VaporKeysPlugin
{
    /// <summary>How a matched literal should be completed / validated.</summary>
    public enum KeyInsertMode
    {
        /// <summary>The literal is (or should be) a string; insert <c>"Display Name"</c>.</summary>
        String,

        /// <summary>The literal is (or should be) a uint; insert the hash literal and show the name as an inlay.</summary>
        Uint,
    }

    /// <summary>The resolved key set + insertion mode for a literal at a given call site. Null <see cref="Set"/> means "not a key context".</summary>
    public readonly struct KeyContext(KeySet set, KeyInsertMode mode)
    {
        public readonly KeySet Set = set;
        public readonly KeyInsertMode Mode = mode;

        public bool IsKey => Set != null;
        public static readonly KeyContext None = new KeyContext(null, KeyInsertMode.String);
    }

    /// <summary>
    /// The Vapor-specific brain of the plugin, kept free of SDK types so it is unit-testable and stable across
    /// SDK versions. The SDK glue (reference factory / completion / inlay providers) extracts a few plain facts
    /// from the syntax tree — described by <see cref="ISyntaxFacts"/> — and asks this resolver what to do.
    /// </summary>
    public static class KeyContextResolver
    {
        /// <summary>
        /// The minimal, SDK-agnostic view of a literal's surroundings that the resolver needs. The SDK layer
        /// implements this by walking the PSI tree (see IdeIntegration.cs). Keeping it an interface means the
        /// mapping rules below never change when SDK type names do.
        /// </summary>
        public interface ISyntaxFacts
        {
            /// <summary>True if the literal is a string literal, false if it is an integer literal.</summary>
            bool IsStringLiteral { get; }

            /// <summary>The simple type name the literal is being converted/assigned to, e.g. "GameplayTag", "KeyDropdownValue", "uint", "String". Null if unknown.</summary>
            string TargetTypeSimpleName { get; }

            /// <summary>If the literal is an argument to <c>DataRegistry&lt;T&gt;.Get/TryGet</c>, the simple name of T (e.g. "AttributeData"); otherwise null.</summary>
            string DataRegistryTypeArgumentSimpleName { get; }

            /// <summary>The <c>category</c> from a <c>[DataKey("category")]</c> on the matching parameter/member, if present; otherwise null.</summary>
            string DataKeyCategory { get; }

            /// <summary>The <c>dataType</c> simple name from a <c>[DataKey(typeof(X))]</c> on the matching parameter/member, if present; otherwise null.</summary>
            string DataKeyTypeSimpleName { get; }
        }

        /// <summary>
        /// Decides which key set (if any) applies to the literal and how it should be inserted. Precedence:
        /// explicit <c>[DataKey]</c> ▸ <c>DataRegistry&lt;T&gt;</c> argument ▸ target type (GameplayTag / KeyDropdownValue).
        /// </summary>
        public static KeyContext Resolve(ISyntaxFacts facts, ManifestStore store)
        {
            if (facts == null || store == null)
            {
                return KeyContext.None;
            }

            var mode = facts.IsStringLiteral ? KeyInsertMode.String : KeyInsertMode.Uint;

            // 1. Explicit [DataKey] wins — it is the only signal for plain uint/string params like TryGetAttribute.
            // [DataKey(typeof(AttributeData))]: that type's manifest. A type with no manifest row (e.g. the
            // runtime Attribute class rather than its AttributeData definition) falls back to the keys whose
            // dotted names sit under the type name as a prefix.
            if (!string.IsNullOrEmpty(facts.DataKeyTypeSimpleName))
            {
                var set = store.GetByDataTypeSimpleName(facts.DataKeyTypeSimpleName);
                if (set.Entries.Count == 0)
                {
                    set = store.GetByNamePrefix(facts.DataKeyTypeSimpleName);
                }
                return set.Entries.Count > 0 ? new KeyContext(set, mode) : KeyContext.None;
            }

            // [DataKey("...")]: an exact category ("Attributes") wins; otherwise the string is a name prefix
            // ("Attribute" → Attribute.Armor.Melee, …), which is how call sites like TryGetAttribute use it.
            if (!string.IsNullOrEmpty(facts.DataKeyCategory))
            {
                var set = store.GetByCategory(facts.DataKeyCategory);
                if (set.Entries.Count == 0)
                {
                    set = store.GetByNamePrefix(facts.DataKeyCategory);
                }
                return set.Entries.Count > 0 ? new KeyContext(set, mode) : KeyContext.None;
            }

            // 2. DataRegistry<T>.Get/TryGet argument → keys for T.
            if (!string.IsNullOrEmpty(facts.DataRegistryTypeArgumentSimpleName))
            {
                var set = store.GetByDataTypeSimpleName(facts.DataRegistryTypeArgumentSimpleName);
                return set.Entries.Count > 0 ? new KeyContext(set, mode) : KeyContext.None;
            }

            // 3. Target type is a distinctive Vapor key type.
            switch (facts.TargetTypeSimpleName)
            {
                case "GameplayTag":
                    return new KeyContext(store.GetTags(), mode);
                case "KeyDropdownValue":
                    // Bare KeyDropdownValue has no type context on its own; tags are the safest broad set. (v2: narrow by surrounding type.)
                    return new KeyContext(store.GetTags(), mode);
                default:
                    return KeyContext.None;
            }
        }
    }
}
