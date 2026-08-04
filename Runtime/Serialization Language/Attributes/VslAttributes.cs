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
