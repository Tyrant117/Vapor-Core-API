using System;

namespace Vapor.Serialization
{
    /// <summary>
    /// Includes a field or property in VSL serialization.
    /// </summary>
    /// <remarks>
    /// VSL is opt-in: without this attribute a member is only serialized when its declaring type is
    /// marked <see cref="VslSerializableAttribute"/> and the member satisfies Unity's own rules.
    /// Properties must expose both a getter and a setter.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class VslSerializeAttribute : Attribute
    {
    }

    /// <summary>
    /// Serializes every member of this type using Unity's own rules: public fields, plus non-public
    /// fields carrying <see cref="UnityEngine.SerializeField"/>.
    /// </summary>
    /// <remarks>
    /// Named to mirror <see cref="SerializableAttribute"/>, which in Unity already means "apply
    /// Unity's serialization rules to this type". Individual members can still opt out with
    /// <see cref="VslIgnoreAttribute"/> or opt in with <see cref="VslSerializeAttribute"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
    public sealed class VslSerializableAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a <see cref="VslSerializeAttribute"/> member as a polymorphic slot: it holds a subclass or
    /// an implementation, and the document records which one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the VSL counterpart of <see cref="UnityEngine.SerializeReference"/>, which cannot be
    /// applied to a property — Unity only serializes fields. It changes nothing about how the value is
    /// written: a member whose declared type is not sealed already gets a <c>!Tag</c> and is rebuilt
    /// through it. What it adds is the declaration at the call site, and with it the inspector's type
    /// picker, which otherwise has no way to know a slot is meant to be filled with a chosen type
    /// rather than drawn as a fixed object.
    /// </para>
    /// <para>
    /// Pair it with <see cref="VslSerializeAttribute"/>. It does not imply it: the source generator
    /// keys off <c>[VslSerialize]</c> alone, so a member carrying only this one would be serialized by
    /// the reflection path and skipped by the generated formatter — the same type behaving two
    /// different ways depending on whether it happens to be <c>partial</c>. Declaring both keeps the
    /// two paths honest, and leaving one off is reported rather than silently ignored.
    /// </para>
    /// <para>
    /// Options for the picker itself — flattening, whether null is offered, whether the selector is
    /// drawn at all — come from <c>[SerializeReferenceDrawer]</c>, which already works on properties.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class VslSerializeReferenceAttribute : Attribute
    {
    }

    /// <summary>
    /// Excludes a field or property from VSL serialization, overriding any type-level policy.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class VslIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// Overrides the name a member is written under, decoupling the text format from the C# name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class VslNameAttribute : Attribute
    {
        public string Name { get; }

        public VslNameAttribute(string name) => Name = name;
    }

    /// <summary>
    /// Registers the short <c>!tag</c> written for this type in a polymorphic slot.
    /// </summary>
    /// <remarks>
    /// Without this attribute a type is tagged by its short name, falling back to its full name when
    /// that is ambiguous. Declaring a tag explicitly keeps the text stable across C# renames.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class VslTypeAttribute : Attribute
    {
        public string Tag { get; }

        public VslTypeAttribute(string tag) => Tag = tag;
    }

    /// <summary>
    /// The named subsets a member can belong to. A write or read filtered to a profile touches only
    /// the members whose profiles intersect it; a member with no <see cref="VslProfileAttribute"/> is
    /// in every profile.
    /// </summary>
    /// <remarks>
    /// The two named ones are what the actor model needs — a prefab-like <b>Template</b> document
    /// authored in the editor and a <b>Save</b> document of a live instance, from the same class —
    /// but the mechanism is generic: the remaining bits are yours to name.
    /// </remarks>
    [Flags]
    public enum VslProfiles : uint
    {
        None = 0,
        /// <summary>Authoring: what a designer or an AI writes into a template document.</summary>
        Template = 1u << 0,
        /// <summary>Persistence: what a live instance carries into a save document.</summary>
        Save = 1u << 1,
        Profile3 = 1u << 2,
        Profile4 = 1u << 3,
        Profile5 = 1u << 4,
        Profile6 = 1u << 5,
        Profile7 = 1u << 6,
        Profile8 = 1u << 7,
        All = uint.MaxValue,
    }

    /// <summary>
    /// Restricts a member to the given profiles. Absent, a member belongs to every profile.
    /// </summary>
    /// <example>
    /// <code>
    /// [VslSerialize] public float MaxHealth;                            // template and save
    /// [VslSerialize, VslProfile(VslProfiles.Save)] public float Health; // save only: absent from the template document
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class VslProfileAttribute : Attribute
    {
        public VslProfiles Profiles { get; }

        public VslProfileAttribute(VslProfiles profiles) => Profiles = profiles;
    }

    /// <summary>
    /// Asks the source generator to emit <c>Clone()</c>, <c>CopyFrom(T)</c> and
    /// <see cref="IVslCloneable.VslCloneObject"/> for this type: a deep copy over exactly the members
    /// VSL serializes, without a text round trip. What <c>ActorFactory</c> uses to stamp a live
    /// instance out of a template.
    /// </summary>
    /// <remarks>
    /// The type (and every containing type) must be <c>partial</c>. In a hierarchy, mark every level
    /// that adds serialized members: each level copies the members it declares and chains to its base,
    /// so private members stay private and a base-typed reference clones to the runtime type.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class VslCloneableAttribute : Attribute
    {
    }

    /// <summary>
    /// Emits a <c>#</c> comment above this member on write.
    /// </summary>
    /// <remarks>
    /// This is how a serialized document carries its own schema. Because the primary author of VSL is
    /// an AI, an exported file annotated with these comments doubles as the prompt describing how to
    /// write more of them.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public sealed class VslCommentAttribute : Attribute
    {
        public string Comment { get; }

        public VslCommentAttribute(string comment) => Comment = comment;
    }
}
