using System;
using Vapor.Unsafe;

namespace Vapor
{
    /// <summary>
    /// A typed reference to another VSL entry, which resolves itself on first use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this fills.</b> One data entry referring to another has, until now, meant storing a
    /// <c>GameplayTag</c> — a bare <c>uint</c> that happens to be the hash of the other entry's name. That
    /// works, and it says nothing: nothing in the type declares what kind of entry is expected, nothing
    /// checks that the target exists, nothing stops a room tag being stored where a blueprint was meant,
    /// and every consumer writes its own lookup. A ship fit pointing at the hull it is built on is exactly
    /// the case that makes all four of those hurt at once.
    /// </para>
    /// <para>
    /// <b>It stores the name, not the key.</b> The hash is derived, so a document stays readable and
    /// diffable — <c>blueprint: "Blueprint.Scout"</c> rather than a number nobody can look up — and
    /// renaming an entry breaks its referrers loudly at resolve time instead of silently pointing at
    /// nothing. That is the same trade <see cref="GameplayTags.GameplayTagData"/> makes for its own name.
    /// </para>
    /// <para>
    /// <b>Resolution is lazy and cached, and failure is not an exception.</b> Data documents register in
    /// dependency order, but a reference may still be read before its target exists — during a partial
    /// load, in a test, or in an editor window drawing an entry whose target has been deleted. So
    /// <see cref="TryResolve"/> reports, <see cref="Value"/> returns null, and neither throws; a caller
    /// that needs the target to exist says so in its own words.
    /// </para>
    /// </remarks>
    public struct VslRef<T> : IEquatable<VslRef<T>> where T : class, IData
    {
        private string _name;
        private uint _key;

        // Cached across resolves. Not serialized, and deliberately not part of equality: two references to
        // the same entry are the same reference whether or not either has been looked up yet.
        [NonSerialized] private T _cached;

        public VslRef(string name)
        {
            _name = name;
            _key = string.IsNullOrEmpty(name) ? 0u : name.Hash32();
            _cached = null;
        }

        public static VslRef<T> None => default;

        public static implicit operator VslRef<T>(string name) => new(name);

        /// <summary>The entry's dotted name, which is what the document holds.</summary>
        public string Name => _name;

        /// <summary>The hash of <see cref="Name"/>, which is what the registry is keyed by.</summary>
        public uint Key => _key;

        /// <summary>Whether this points at anything at all. Says nothing about whether the target exists.</summary>
        public bool IsSet => _key != 0;

        /// <summary>The entry, or null. Looked up once and remembered.</summary>
        public T Value
        {
            get
            {
                TryResolve(out var value);
                return value;
            }
        }

        /// <summary>Whether the target is registered, handing it back if so.</summary>
        public bool TryResolve(out T value)
        {
            if (_cached != null)
            {
                value = _cached;
                return true;
            }

            if (_key == 0)
            {
                value = null;
                return false;
            }

            if (GlobalDataRegistry.TryGet(_key, out T found))
            {
                _cached = found;
                value = found;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>Whether the target exists right now.</summary>
        public bool Exists => TryResolve(out _);

        /// <summary>Forgets the cached target, so the next resolve looks it up again.</summary>
        /// <remarks>For editor tooling, where an entry can be re-authored under a running domain.</remarks>
        public void Invalidate() => _cached = null;

        public bool Equals(VslRef<T> other) => _key == other._key;

        public override bool Equals(object obj) => obj is VslRef<T> other && Equals(other);

        public override int GetHashCode() => (int)_key;

        public static bool operator ==(VslRef<T> a, VslRef<T> b) => a.Equals(b);

        public static bool operator !=(VslRef<T> a, VslRef<T> b) => !a.Equals(b);

        public override string ToString() =>
            IsSet ? $"VslRef<{typeof(T).Name}>[{_name}]" : $"VslRef<{typeof(T).Name}>[none]";
    }
}

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes a <see cref="VslRef{T}"/> as the target entry's name, and reads it back without resolving.
    /// </summary>
    /// <remarks>
    /// Reading must not resolve. Documents load in dependency order, but nothing guarantees the target's
    /// document has been read by the time this one is — and a formatter that tried would turn an ordering
    /// detail into a load failure. The reference knows its key from the name alone, so the lookup can wait
    /// until somebody actually wants the thing.
    /// </remarks>
    public sealed class VslRefFormatter<T> : VslFormatter<VslRef<T>> where T : class, IData
    {
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in VslRef<T> value, VslContext context) =>
            writer.WriteString(value.IsSet ? value.Name : null);

        public override VslRef<T> Read(ref VslReader reader, VslContext context) => new(reader.ReadString());
    }
}
