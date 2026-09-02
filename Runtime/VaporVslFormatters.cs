using Vapor.GameplayTags;
using Vapor.Serialization;

namespace Vapor
{
    /// <summary>
    /// Installs the formatters for Core's own types into VSL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The serialization package ships on its own and references nothing in Vapor, which is the whole
    /// reason it can. So it cannot know what a <see cref="GameplayTag"/> is, or that a
    /// <see cref="VslRef{T}"/> resolves through <see cref="GlobalDataRegistry"/>. Core says so here,
    /// and the <c>[assembly: VslFormatterProvider]</c> in <c>AssemblyInfo.cs</c> is what makes
    /// <see cref="VslFormatterRegistry"/> call it.
    /// </para>
    /// <para>
    /// Called from the registry's type initializer, so this runs before the first lookup can be
    /// answered. That ordering is the point: a tag registered any later would already have resolved
    /// to the reflection formatter and been cached there, and would write a nested object where its
    /// dotted name belongs.
    /// </para>
    /// </remarks>
    public static class VaporVslFormatters
    {
        public static void RegisterFormatters()
        {
            VslFormatterRegistry.Register(GameplayTagFormatter.Instance);
            VslFormatterRegistry.Register(GameplayTagContainerFormatter.Instance);

            // Open generic: one formatter per construction, closed the same way the built-in
            // collection formatters are.
            VslFormatterRegistry.RegisterGeneric(typeof(VslRef<>), typeof(VslRefFormatter<>));
        }
    }
}
