using System;

namespace Vapor.Networking
{
    /// <summary>
    /// The untyped face of a formatter, for the few paths — reflection fallback, polymorphic packets,
    /// tooling — that only have a <see cref="Type"/> in hand.
    /// </summary>
    public interface INetworkFormatter
    {
        Type ValueType { get; }
        void WriteBoxed(NetworkWriter writer, object value);
        object ReadBoxed(NetworkReader reader);
    }

    /// <summary>
    /// Turns a <typeparamref name="T"/> into bytes and back. Formatters are stateless singletons looked
    /// up through <see cref="NetworkFormatters"/>; generated code, RPC arguments, network variables and
    /// packet fields all go through this one contract.
    /// </summary>
    public interface INetworkFormatter<T> : INetworkFormatter
    {
        void Write(NetworkWriter writer, in T value);
        T Read(NetworkReader reader);
    }

    /// <summary>
    /// Base for hand-written and generated formatters: implements the boxed face once so implementors
    /// only write the typed pair.
    /// </summary>
    public abstract class NetworkFormatter<T> : INetworkFormatter<T>
    {
        public Type ValueType => typeof(T);

        public abstract void Write(NetworkWriter writer, in T value);

        public abstract T Read(NetworkReader reader);

        void INetworkFormatter.WriteBoxed(NetworkWriter writer, object value) => Write(writer, (T)value);

        object INetworkFormatter.ReadBoxed(NetworkReader reader) => Read(reader);
    }

    /// <summary>
    /// A formatter built from two delegates — the shortest way to register a formatter for a type you
    /// don't own, or to adapt an existing pair of methods.
    /// </summary>
    public sealed class DelegateNetworkFormatter<T> : NetworkFormatter<T>
    {
        public delegate void WriteDelegate(NetworkWriter writer, in T value);
        public delegate T ReadDelegate(NetworkReader reader);

        private readonly WriteDelegate _write;
        private readonly ReadDelegate _read;

        public DelegateNetworkFormatter(WriteDelegate write, ReadDelegate read)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _read = read ?? throw new ArgumentNullException(nameof(read));
        }

        public override void Write(NetworkWriter writer, in T value) => _write(writer, value);

        public override T Read(NetworkReader reader) => _read(reader);
    }
}
