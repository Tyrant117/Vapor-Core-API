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
