using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// A behaviour that rides on a <see cref="VaporNetworkObject"/>: it has replicated variables and
    /// rpcs of its own, but no spawn of its own — it is created with its object (or added to it later)
    /// and addressed as (object id, component id). NGO's NetworkBehaviour-on-NetworkObject shape, in
    /// plain C#.
    /// </summary>
    /// <remarks>
    /// Components exist before spawn: a template can carry a stack of them, and both peers construct
    /// the same stack when they clone the same template. Ids are assigned by the authority as
    /// components are added and are stable for the component's lifetime on that object.
    /// </remarks>
    public abstract partial class NetworkComponent : IRpcHost, INetworkVariableHost
    {
        private const byte k_FlagVariables = 1;
        private const byte k_FlagCustomState = 2;

        private readonly List<VaporNetworkVariableBase> _variables = new();
        private readonly List<VaporNetworkVariableBase> _dirtyVariables = new();
        private bool _customStateDirty;
        private bool _spawned;

        public VaporNetworkObject Owner { get; internal set; }

        /// <summary>1-based, unique within the owner. 0 is the owner itself.</summary>
        public ushort ComponentId { get; internal set; }

        public bool IsAttached => Owner != null;
        public bool IsSpawned => _spawned && Owner is { IsSpawned: true };
        public NetworkWorld World => Owner?.World;
        public bool IsServer => Owner is { IsServer: true };
        public bool IsClient => Owner is { IsClient: true };
        public bool IsOwner => Owner is { IsOwner: true };
        public bool IsAuthority => Owner is { IsAuthority: true };
        public bool IsOffline => Owner == null || Owner.IsOffline;
        public ulong OwnerClientId => Owner?.OwnerClientId ?? NetworkSession.ServerClientId;

        /// <summary>Set when this component has un-replicated changes. Cleared once the delta is written.</summary>
        public bool IsDirty => _dirtyVariables.Count > 0 || _customStateDirty;

        /// <summary>Opt in to <see cref="OnNetworkTick"/>.</summary>
        public virtual bool WantsNetworkTick => false;

        VaporNetworkObject IRpcHost.RpcObject => Owner;
        ushort IRpcHost.RpcComponentId => ComponentId;

        #region - Lifecycle hooks -

        /// <summary>Added to an object (before or after its spawn). Construct network variables here or in the constructor.</summary>
        protected virtual void OnAttach() { }

        /// <summary>The owning object is spawned (or this was added to an already spawned object).</summary>
        protected virtual void OnSpawn() { }

        protected virtual void OnDespawn() { }

        /// <summary>Removed from its object.</summary>
        protected virtual void OnDetach() { }

        protected virtual void OnOwnershipChanged(ulong previousOwner, ulong currentOwner) { }

        protected virtual void OnNetworkTick(uint tick, double deltaTime) { }

        #endregion

        #region - Custom state -

        /// <summary>
        /// Written once into every full entry for this component (spawn, add, resync), before its
        /// state, and read right after construction on the receiving peer — for whatever the component
        /// needs to become itself (an actor component ships its authored config when it did not come
        /// from a template both peers hold).
        /// </summary>
        protected virtual void WriteSpawnData(NetworkWriter writer) { }

        protected virtual void ReadSpawnData(NetworkReader reader) { }

        internal void WriteSpawnDataInternal(NetworkWriter writer) => WriteSpawnData(writer);
        internal void ReadSpawnDataInternal(NetworkReader reader) => ReadSpawnData(reader);

        /// <summary>
        /// State beyond network variables. Called with <c>full</c> for spawn and new observers, and
        /// without after <see cref="MarkDirty"/>. Whatever is written here must be read back in the same
        /// order by <see cref="ReadState"/>.
        /// </summary>
        protected virtual void WriteState(NetworkWriter writer, bool full) { }

        protected virtual void ReadState(NetworkReader reader, bool full) { }

        /// <summary>Flags custom state as changed. Variables mark themselves.</summary>
        protected void MarkDirty()
        {
            if (Owner == null) return;
            _customStateDirty = true;
            Owner.OnComponentDirty(this);
        }

        #endregion

        #region - Variables -

        void INetworkVariableHost.RegisterVariable(VaporNetworkVariableBase variable)
        {
            variable.Index = _variables.Count;
            _variables.Add(variable);
        }

        void INetworkVariableHost.OnVariableDirty(VaporNetworkVariableBase variable)
        {
            if (Owner == null) return;
            _dirtyVariables.Add(variable);
            Owner.OnComponentDirty(this);
        }

        bool INetworkVariableHost.CanWriteVariables => Owner == null || Owner.CanWriteVariablesInternal;

        #endregion

        #region - Rpc support -

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool BeginSendRpc(uint hash, out NetworkWriter writer)
        {
            if (Owner == null || !Owner.IsSpawned)
            {
                Debug.LogError($"Rpc [{hash:X8}] called on {GetType().Name} before its object was spawned.");
                writer = null;
                return false;
            }

            return Owner.World.BeginRpc(this, hash, out writer);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool EndSendRpc(NetworkWriter writer, RpcTarget target, Delivery delivery) =>
            Owner.World.EndRpc(this, writer, target, delivery);

        #endregion

        #region - Internal plumbing -

        internal void AttachInternal(VaporNetworkObject owner, ushort componentId)
        {
            Owner = owner;
            ComponentId = componentId;
            OnAttach();
        }

        internal void SpawnInternal()
        {
            if (_spawned) return;
            _spawned = true;
            OnSpawn();
        }

        internal void DespawnInternal()
        {
            if (!_spawned) return;
            _spawned = false;
            OnDespawn();
        }

        internal void DetachInternal()
        {
            OnDetach();
            Owner = null;
            ComponentId = 0;
            _dirtyVariables.Clear();
            _customStateDirty = false;
        }

        internal void OwnershipChangedInternal(ulong previous, ulong current) => OnOwnershipChanged(previous, current);

        internal void TickInternal(uint tick, double dt) => OnNetworkTick(tick, dt);

        internal void ClearDirty()
        {
            foreach (var v in _dirtyVariables) v.ClearDirty();
            _dirtyVariables.Clear();
            _customStateDirty = false;
        }

        internal void Write(NetworkWriter writer, bool full)
        {
            byte flags = 0;
            if (full ? _variables.Count > 0 : _dirtyVariables.Count > 0) flags |= k_FlagVariables;
            if (full || _customStateDirty) flags |= k_FlagCustomState;
            writer.WriteByte(flags);

            if ((flags & k_FlagVariables) != 0)
            {
                if (full)
                {
                    writer.WriteVarUInt32((uint)_variables.Count);
                    for (int i = 0; i < _variables.Count; i++)
                    {
                        writer.WriteVarUInt32((uint)i);
                        _variables[i].WriteFull(writer);
                    }
                }
                else
                {
                    writer.WriteVarUInt32((uint)_dirtyVariables.Count);
                    foreach (var v in _dirtyVariables)
                    {
                        writer.WriteVarUInt32((uint)v.Index);
                        v.WriteDelta(writer);
                    }
                }
            }

            if ((flags & k_FlagCustomState) != 0)
            {
                int at = writer.ReserveUInt16();
                int start = writer.Position;
                WriteState(writer, full);
                writer.PatchUInt16(at, checked((ushort)(writer.Position - start)));
            }
        }

        internal void Read(NetworkReader reader, bool full)
        {
            byte flags = reader.ReadByte();
            if ((flags & k_FlagVariables) != 0)
            {
                uint count = reader.ReadVarUInt32();
                for (uint i = 0; i < count; i++)
                {
                    int index = (int)reader.ReadVarUInt32();
                    if (index < 0 || index >= _variables.Count)
                    {
                        throw new NetworkSerializationException($"{GetType().Name} received variable index {index} but has {_variables.Count} variables. Both peers must construct the same variables in the same order.");
                    }

                    if (full) _variables[index].ReadFull(reader);
                    else _variables[index].ReadDelta(reader);
                }
            }

            if ((flags & k_FlagCustomState) != 0)
            {
                int length = reader.ReadUInt16();
                int end = reader.Position + length;
                ReadState(reader, full);
                // Positioned from the prefix, so a misread here cannot take the rest of the packet with it.
                reader.Seek(end);
            }
        }

        #endregion
    }
}
