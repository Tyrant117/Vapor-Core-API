using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Networking
{
    /// <summary>
    /// A replicated field owned by a <see cref="VaporNetworkObject"/> or a <see cref="NetworkComponent"/>.
    /// The authority (server, or anyone offline) writes; every peer reads and gets
    /// <see cref="OnValueChanged"/>.
    /// </summary>
    /// <remarks>
    /// Variables are constructed with their host in <c>OnPreSpawn</c> (or a constructor) and register
    /// themselves in declaration order; the index in that order is what goes on the wire, so both
    /// sides must construct the same variables in the same order — the same rule the previous
    /// implementation and NGO both had.
    /// </remarks>
    public abstract class VaporNetworkVariableBase
    {
        internal INetworkVariableHost Host;
        internal int Index = -1;

        public bool IsDirty { get; internal set; }

        protected VaporNetworkVariableBase(INetworkVariableHost host)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            host.RegisterVariable(this);
        }

        /// <summary>The whole value, for spawn and for full resyncs.</summary>
        internal abstract void WriteFull(NetworkWriter writer);

        /// <summary>What a peer needs to catch up. Scalars write the whole value; collections may write a delta.</summary>
        internal abstract void WriteDelta(NetworkWriter writer);

        internal abstract void ReadFull(NetworkReader reader);

        internal abstract void ReadDelta(NetworkReader reader);

        internal void ClearDirty() => IsDirty = false;

        protected void MarkDirty()
        {
            if (IsDirty) return;
            IsDirty = true;
            Host.OnVariableDirty(this);
        }

        /// <summary>True when this peer may write the variable: the authority, or anyone offline.</summary>
        public bool CanWrite => Host.CanWriteVariables;
    }

    /// <summary>What a variable needs from whatever hosts it. Implemented by objects and components.</summary>
    public interface INetworkVariableHost
    {
        void RegisterVariable(VaporNetworkVariableBase variable);
        void OnVariableDirty(VaporNetworkVariableBase variable);
        bool CanWriteVariables { get; }
    }

    /// <summary>A replicated scalar. Equality decides whether a write is a change; unchanged writes send nothing.</summary>
    [NoAutoStaticsCleanup]
    public sealed class VaporNetworkVariable<T> : VaporNetworkVariableBase
    {
        private static readonly IEqualityComparer<T> s_Comparer = EqualityComparer<T>.Default;

        private T _value;

        /// <summary>Raised on every peer when the value changes: (previous, current). On the authority it fires synchronously on write.</summary>
        public event Action<T, T> OnValueChanged;

        public VaporNetworkVariable(INetworkVariableHost host, T initialValue = default) : base(host)
        {
            _value = initialValue;
        }

        public T Value
        {
            get => _value;
            set
            {
                if (!CanWrite)
                {
                    throw new InvalidOperationException($"Only the authority may write a network variable ({typeof(T).Name}).");
                }

                if (s_Comparer.Equals(_value, value))
                {
                    return;
                }

                T previous = _value;
                _value = value;
                MarkDirty();
                OnValueChanged?.Invoke(previous, value);
            }
        }

        /// <summary>Sets the value without a permission check or a dirty mark — for local prediction and tests.</summary>
        public void SetLocal(T value)
        {
            if (s_Comparer.Equals(_value, value)) return;
            T previous = _value;
            _value = value;
            OnValueChanged?.Invoke(previous, value);
        }

        internal override void WriteFull(NetworkWriter writer) => NetworkFormatters.Get<T>().Write(writer, _value);

        internal override void WriteDelta(NetworkWriter writer) => WriteFull(writer);

        internal override void ReadFull(NetworkReader reader)
        {
            T incoming = NetworkFormatters.Get<T>().Read(reader);
            if (s_Comparer.Equals(_value, incoming)) return;
            T previous = _value;
            _value = incoming;
            OnValueChanged?.Invoke(previous, incoming);
        }

        internal override void ReadDelta(NetworkReader reader) => ReadFull(reader);

        public static implicit operator T(VaporNetworkVariable<T> variable) => variable._value;

        public override string ToString() => _value?.ToString() ?? "null";
    }
}
