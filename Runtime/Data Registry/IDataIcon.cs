using Vapor.GameplayTags;

namespace Vapor
{
    /// <summary>
    /// Data that carries an icon, addressed through the registry rather than held as a direct
    /// reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is a <see cref="GameplayTag"/> rather than a raw <c>uint</c>. It is the same value
    /// either way — a tag is the hash of its name — but the tag knows how to name itself, which is
    /// what lets a picker offer the addressables by name and lets a serialized document record
    /// <c>Addressable.Icons.Sword</c> instead of an opaque number.
    /// </para>
    /// <para>
    /// An implementer should mark its property
    /// <c>[GameplayTagDrawer(true, true, GameplayTagCategories.ADDRESSABLE)]</c> to narrow the picker
    /// to addressable entries. An attribute on an interface member does not reach the implementation,
    /// so this cannot be declared once here.
    /// </para>
    /// </remarks>
    public interface IDataIcon
    {
        public GameplayTag IconAddressableKey { get; set; }
    }
}
