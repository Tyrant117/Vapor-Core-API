using System;

namespace Vapor.Serialization
{
    /// <summary>
    /// Non-generic view of a formatter, for paths that only have a <see cref="Type"/> — reflection,
    /// polymorphic slots, and dictionary values.
    /// </summary>
    public interface IVslFormatter
    {
        Type ValueType { get; }

        /// <summary>
        /// True when this type renders as a single token or a one-line tuple. Containers use it to
        /// decide whether their contents can be written inline.
        /// </summary>
        bool IsScalar { get; }

        void WriteObject(ref VslWriter writer, object value, VslContext context);

        object ReadObject(ref VslReader reader, VslContext context);
    }

    /// <summary>
    /// Reads and writes a single type. The typed path is the fast one: no boxing, and the formatter
    /// is resolved once per <c>T</c> rather than looked up per value.
    /// </summary>
    public interface IVslFormatter<T> : IVslFormatter
    {
        void Write(ref VslWriter writer, in T value, VslContext context);

        T Read(ref VslReader reader, VslContext context);
    }

    /// <summary>
    /// Implemented by a formatter whose type also reads as a member name, so a dictionary keyed on it
    /// is written as an object — <c>{ Attribute.Item.Durability: 100 }</c> — rather than as positional
    /// pairs.
    /// </summary>
    /// <remarks>
    /// The extension point behind <see cref="DictionaryFormatter{TKey,TValue}"/>'s object form. Strings,
    /// enums and the integral types are recognized by the dictionary formatter itself; anything else
    /// with a legible name — a <c>GameplayTag</c>, and whatever id type comes after it — says so here
    /// rather than being named in a switch that formatter would have to keep growing.
    /// <para>
    /// A name has to round-trip: whatever <see cref="ToName"/> produces, <see cref="TryParseName"/> has
    /// to read back as the same key. A key whose name cannot be recovered is written as something that
    /// can — its raw id — rather than as a name that would resolve to something else.
    /// </para>
    /// </remarks>
    public interface IVslNameKeyFormatter<T>
    {
        /// <summary>The member name this key is written as.</summary>
        string ToName(in T value);

        /// <summary>Reads back what <see cref="ToName"/> wrote. False when the text names no valid key.</summary>
        bool TryParseName(ReadOnlySpan<char> text, out T value);
    }

    /// <summary>
    /// Base class for formatters. Supplies the non-generic bridge so implementations only write the
    /// typed pair.
    /// </summary>
    public abstract class VslFormatter<T> : IVslFormatter<T>
    {
        public Type ValueType => typeof(T);

        public virtual bool IsScalar => false;

        public abstract void Write(ref VslWriter writer, in T value, VslContext context);

        public abstract T Read(ref VslReader reader, VslContext context);

        public void WriteObject(ref VslWriter writer, object value, VslContext context) =>
            Write(ref writer, value == null ? default : (T)value, context);

        public object ReadObject(ref VslReader reader, VslContext context) => Read(ref reader, context);
    }
}
