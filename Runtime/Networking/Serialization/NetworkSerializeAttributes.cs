using System;

namespace Vapor.Networking
{
    /// <summary>
    /// Asks the network generator to emit a formatter for this type. Member selection follows the same
    /// rules as VSL — public fields and <c>[SerializeField]</c> privates, plus anything marked
    /// <see cref="NetworkSerializeAttribute"/> or <c>[VslSerialize]</c>, minus anything marked
    /// <see cref="NetworkIgnoreAttribute"/> or <c>[VslIgnore]</c> — so a type describes its members
    /// once and both the text and the wire formats agree on them.
    /// </summary>
    /// <remarks>
    /// The type (and every containing type) must be <c>partial</c>: the generator nests the formatter
    /// inside the type so it can reach private members. Classes need an accessible parameterless
    /// constructor; structs always qualify.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class NetworkSerializableAttribute : Attribute
    {
    }

    /// <summary>
    /// Opts a single member into the wire format, whatever its accessibility. On its own — with no
    /// <see cref="NetworkSerializableAttribute"/> on the type — it also asks the generator for a
    /// formatter that carries only the opted-in members.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    public sealed class NetworkSerializeAttribute : Attribute
    {
    }

    /// <summary>Keeps a member that would otherwise qualify off the wire.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
    public sealed class NetworkIgnoreAttribute : Attribute
    {
    }
}
