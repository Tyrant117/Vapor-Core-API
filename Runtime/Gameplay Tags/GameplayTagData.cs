using UnityEngine.Localization;
using Vapor.Serialization;
using Vapor.Unsafe;

namespace Vapor.GameplayTags
{
    /// <summary>
    /// One gameplay tag: a dotted name and the <c>uint</c> hash every other system identifies it by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authored in the <c>Vapor Data/Data Types</c> window and persisted to
    /// <c>Assets/Vapor/Data/GameplayTagData.vsl</c>. The code registries that build tags in
    /// <c>BuildRegistry()</c> still work unchanged — the constructor and the <c>With*</c> builders are
    /// untouched — so both sources of tags can coexist while they are migrated.
    /// </para>
    /// <para>
    /// <c>partial</c>, and with an accessible parameterless constructor, so the VSL source generator
    /// can emit a real formatter for it rather than falling back to reflection.
    /// </para>
    /// </remarks>
    [DataAuthoring(Order = TagOrder)]
    public partial class GameplayTagData : ILocalizedData, IDataIcon
    {
        /// <summary>
        /// Matches <see cref="IGameplayTagRegistry"/>'s order. Tags are the lowest-level identifier in
        /// the project, so everything else registers after them.
        /// </summary>
        private const int TagOrder = -500;

        private string _name;
        private uint _key;

        [VslSerialize]
        [VslComment("Dotted tag name. The uint key is the hash of this, so renaming a tag changes its identity.")]
        public string Name
        {
            get => _name;
            private set
            {
                _name = value;
                _key = string.IsNullOrEmpty(value) ? 0u : value.Hash32();
            }
        }

        public uint Key => _key;

        [VslSerialize]
        [VslComment("Shown in tag picker tooltips. Editor only.")]
        public string EditorTooltip { get; private set; }
        
        [VslSerialize]
        [VslComment("Localized name label, as (table, entry).")]
        public LocalizedString LocalizedName { get; set; }
        
        [VslSerialize]
        [VslComment("Localized description label, as (table, entry).")]
        public LocalizedString LocalizedDescription { get; set; }
        
        [VslSerialize]
        [VslComment("Addressable entry holding this tag's icon, by name.")]
        [GameplayTagDrawer(true, true, GameplayTagCategories.ADDRESSABLE)]
        public GameplayTag IconAddressableKey { get; set; }

        /// <summary>
        /// For the serializer. Deserialization sets <see cref="Name"/>, which is what derives
        /// <see cref="Key"/>.
        /// </summary>
        internal GameplayTagData()
        {
        }

        public GameplayTagData(string name)
        {
            Name = name;
        }

        public GameplayTagData WithEditorTooltip(string editorTooltip)
        {
            EditorTooltip = editorTooltip;
            return this;
        }
    }
}
