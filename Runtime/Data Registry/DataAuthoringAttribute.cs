using System;

namespace Vapor
{
    /// <summary>
    /// Marks an <see cref="IData"/> type as authored in the <c>Vapor Data/Data Types</c> window and
    /// persisted to a VSL document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opt-in rather than opt-out. Plenty of <see cref="IData"/> implementations are not authored by
    /// hand at all — <see cref="AddressableData"/> is generated from the Addressables labels, and
    /// others only ever exist as runtime instances — so listing every implementation would fill the
    /// window with types that have nothing to edit.
    /// </para>
    /// <para>
    /// Not inherited: a subclass that should have its own document declares its own attribute, and
    /// one that should live in its base type's document simply does not.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DataAuthoringAttribute : Attribute
    {
        /// <summary>
        /// The group this type sits under in the window's type rail. Defaults to the category
        /// <c>KeyGenerator.DeriveScriptAndCategory</c> derives from the type name, which is the same
        /// grouping the generated key classes use.
        /// </summary>
        public string Category { get; set; }

        /// <summary>The name shown in the type rail. Defaults to the type name.</summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Dotted prefix a newly added entry is named under — <c>"Ability.Fire"</c> spawns
        /// <c>Ability.Fire.New</c>. Defaults to the type name.
        /// </summary>
        /// <remarks>
        /// A data name is a dotted path whose first segments decide which key class it is generated
        /// into and which branch of the tag tree it joins, so an entry created in the wrong branch has
        /// to be renamed before it is any use. Declaring the branch once here is what makes adding an
        /// entry a single click.
        /// </remarks>
        public string NamePrefix { get; set; }

        /// <summary>
        /// The name of this type's document, without the extension. Defaults to the type name, so
        /// <c>GameplayTagData</c> is stored at <c>Assets/Vapor/Data/GameplayTagData.vsl</c>.
        /// </summary>
        /// <remarks>
        /// One document per type. A family shares its root's file — every entry carries its own
        /// <c>!tag</c>, so a subclass sits alongside its base type rather than in a file of its own.
        /// </remarks>
        public string FileName { get; set; }

        /// <summary>
        /// Hex colour this type is marked with in the editor, with or without the leading <c>#</c>.
        /// Derived from the type name when not set.
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// Build order for this type's document, on the same scale as
        /// <see cref="IDataRegistry.GetOrder"/>. Lower builds first, so a type whose data other data
        /// refers to — gameplay tags at -500, attributes at -300 — registers before its dependents.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// An editor view type to draw the right-hand pane with, in place of the reflected inspector.
        /// Must implement <c>VaporEditor.DataRegistry.IDataAuthoringView</c>.
        /// </summary>
        /// <remarks>
        /// Typed as <see cref="Type"/> rather than the interface because the interface lives in the
        /// editor assembly, which runtime code cannot reference. The window validates it.
        /// </remarks>
        public Type View { get; set; }
    }
}
