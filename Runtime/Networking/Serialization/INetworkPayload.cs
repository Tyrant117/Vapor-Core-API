using System;

namespace Vapor.Networking
{
    /// <summary>
    /// A type that writes itself: the hand-written alternative to a generated formatter, for classes
    /// whose wire format is not just their fields — nested polymorphic parts, conditional sections,
    /// or a layout that has to stay stable. Anything implementing this is serializable everywhere a
    /// formatter is (rpc arguments, network variables, state), through the fallback resolver
    /// <see cref="PayloadFormatter{T}"/> registers with <see cref="NetworkFormatters"/>.
    /// </summary>
    /// <remarks>
    /// The receiver constructs the value with its parameterless constructor (a struct's default) and
    /// then calls <see cref="Read"/>, so a class needs one; it may be non-public. Behind an interface
    /// or an abstract base, register a <see cref="PolymorphicFormatter{TBase}"/> for the base — the
    /// concrete type's payload formatter does the rest.
    /// </remarks>
    public interface INetworkPayload
    {
        void Write(NetworkWriter writer);
        void Read(NetworkReader reader);
    }

    /// <summary>The formatter behind every <see cref="INetworkPayload"/>: construct, then read.</summary>
    public sealed class PayloadFormatter<T> : NetworkFormatter<T> where T : INetworkPayload
    {
        private readonly bool _isClass = !typeof(T).IsValueType;

        public override void Write(NetworkWriter writer, in T value)
        {
            if (_isClass)
            {
                // A class travels behind a null marker; a struct is always present.
                writer.WriteBool(value != null);
                if (value == null)
                {
                    return;
                }
            }

            value.Write(writer);
        }

        public override T Read(NetworkReader reader)
        {
            if (_isClass && !reader.ReadBool())
            {
                return default;
            }

            var value = (T)Activator.CreateInstance(typeof(T), nonPublic: true);
            value.Read(reader);
            return value;
        }

        /// <summary>Resolves a payload formatter for any <see cref="INetworkPayload"/> type. Installed as a formatter fallback.</summary>
        public static INetworkFormatter Resolve(Type type)
        {
            if (type == null || type.IsAbstract || type.IsInterface || !typeof(INetworkPayload).IsAssignableFrom(type))
            {
                return null;
            }

            return (INetworkFormatter)Activator.CreateInstance(typeof(PayloadFormatter<>).MakeGenericType(type));
        }
    }
}
