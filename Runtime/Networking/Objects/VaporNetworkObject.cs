using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// A replicated plain-C# object: an identity, an owner, a parent, a stack of
    /// <see cref="NetworkComponent"/>s, replicated variables, custom state and rpcs — and no
    /// GameObject. Roots (actors, world objects) and sub-objects (abilities, attribute containers)
    /// are the same class; a sub-object simply has a parent.
    /// </summary>
    /// <remarks>
    /// The same object runs identically offline (a <see cref="NetworkWorld"/> with no session), on a
    /// server, or on a client. Authority is a property of the world, not of the object: offline every
    /// peer is the authority; networked, only the server is. Ownership is a replicated attribute the
    /// authority may transfer at any time.
    /// <para>
    /// The generated rpc code and the network variables you declare are the only things a subclass
    /// normally touches; everything under "Internal plumbing" belongs to the world and the replicator.
    /// </para>
    /// </remarks>
    public abstract partial class VaporNetworkObject : IRpcHost, INetworkVariableHost
    {
        private const byte k_FlagVariables = 1;
        private const byte k_FlagCustomState = 2;
        private const byte k_FlagComponents = 4;

        private const byte k_OpAdd = 1;
        private const byte k_OpRemove = 2;
        private const byte k_OpDelta = 3;

        private readonly List<VaporNetworkVariableBase> _variables = new();
        private readonly List<VaporNetworkVariableBase> _dirtyVariables = new();
        private readonly List<NetworkComponent> _components = new();
        private readonly List<NetworkComponent> _dirtyComponents = new();
        private readonly List<(byte op, ushort id, NetworkComponent component)> _componentOps = new();
        private readonly List<VaporNetworkObject> _subObjects = new();
        private readonly List<InterestGroup> _interestGroups = new();
        private Dictionary<Type, List<NetworkComponent>> _componentsByType;
        private ushort _nextComponentId = 1;
        private bool _customStateDirty;
        private bool _spawnCompleted;
        internal bool QueuedDirty;

        #region - Identity and role -

        /// <summary>The world this object is spawned in; null before spawn and after despawn.</summary>
        public NetworkWorld World { get; internal set; }

        /// <summary>Server-assigned, unique for the session. 0 = not spawned.</summary>
        public ulong NetworkObjectId { get; internal set; }

        public ulong OwnerClientId { get; internal set; } = NetworkSession.ServerClientId;

        public VaporNetworkObject Parent { get; internal set; }
        public ulong ParentNetworkObjectId => Parent?.NetworkObjectId ?? 0;
        public bool IsRoot => Parent == null;
        public IReadOnlyList<VaporNetworkObject> SubObjects => _subObjects;

        /// <summary>Only the owner ever receives this object (a player's private state).</summary>
        public bool SpawnedOnlyOnOwner { get; internal set; }

        /// <summary>Marks the object that stands for a connected player.</summary>
        public bool IsPlayerObject { get; internal set; }

        /// <summary>Optional persistence key. Not replicated; the save layer's business.</summary>
        public string SaveId { get; set; }

        public bool IsSpawned => World != null && NetworkObjectId != 0;
        public bool IsOffline => World == null || World.IsOffline;
        public bool IsServer => World != null && World.IsServer;
        public bool IsClient => World != null && World.IsClient;
        public bool IsHost => World != null && World.IsHost;

        /// <summary>True when this peer may mutate replicated state: the server, or anyone offline.</summary>
        public bool IsAuthority => World != null && World.IsAuthority;

        public bool IsOwner => World != null && (World.IsOffline || OwnerClientId == World.LocalClientId);
        public bool IsOwnedByServer => OwnerClientId == NetworkSession.ServerClientId;

        /// <summary>Set when there is state the replicator has not sent yet.</summary>
        public bool IsDirty => _dirtyVariables.Count > 0 || _customStateDirty || _dirtyComponents.Count > 0 || _componentOps.Count > 0;

        public virtual bool WantsNetworkTick => false;

        VaporNetworkObject IRpcHost.RpcObject => this;
        ushort IRpcHost.RpcComponentId => 0;

        #endregion

        #region - Interest -

        /// <summary>The channels this object belongs to. Empty (and not spatial) means globally relevant.</summary>
        public IReadOnlyList<InterestGroup> InterestGroups => _interestGroups;

        /// <summary>
        /// When set, the object is relevant to a client only within that client's spatial interest
        /// (or through a channel it belongs to). Until a spatial provider is installed on the world it
        /// behaves as global, so nothing disappears merely because the grid is not there yet.
        /// </summary>
        public bool UsesSpatialRelevance
        {
            get => _usesSpatialRelevance;
            set
            {
                if (_usesSpatialRelevance == value) return;
                _usesSpatialRelevance = value;
                World?.OnInterestChanged(this);
            }
        }

        private bool _usesSpatialRelevance;

        public bool IsGloballyRelevant => _interestGroups.Count == 0 && !_usesSpatialRelevance;

        public bool IsInInterestGroup(InterestGroup group) => _interestGroups.Contains(group);

        public void AddInterestGroup(InterestGroup group)
        {
            if (group.IsNone || _interestGroups.Contains(group)) return;
            _interestGroups.Add(group);
            World?.OnInterestChanged(this);
        }

        public void RemoveInterestGroup(InterestGroup group)
        {
            if (_interestGroups.Remove(group))
            {
                World?.OnInterestChanged(this);
            }
        }

        public void ClearInterestGroups()
        {
            if (_interestGroups.Count == 0) return;
            _interestGroups.Clear();
            World?.OnInterestChanged(this);
        }

        #endregion

        #region - Lifecycle hooks -

        /// <summary>Before spawn: construct network variables here (or in the constructor). State has not arrived yet on a client.</summary>
        protected virtual void OnPreSpawn() { }

        /// <summary>Spawned. On a client the initial state has been applied.</summary>
        protected virtual void OnSpawn() { }

        /// <summary>After this object and its components have all seen <see cref="OnSpawn"/>.</summary>
        protected virtual void OnPostSpawn() { }

        protected virtual void OnDespawn() { }

        protected virtual void OnOwnershipChanged(ulong previousOwner, ulong currentOwner) { }

        protected virtual void OnNetworkTick(uint tick, double deltaTime) { }

        /// <summary>
        /// A sub-object of this one has finished spawning — on the authority right after
        /// <see cref="NetworkWorld.Spawn"/>, on a client when the spawn arrives. This is how a parent
        /// finds the children it did not construct itself: an actor learns of its attribute containers
        /// and abilities here.
        /// </summary>
        protected virtual void OnSubObjectSpawned(VaporNetworkObject subObject) { }

        /// <summary>A sub-object of this one is despawning; it is still linked and still has its id.</summary>
        protected virtual void OnSubObjectDespawned(VaporNetworkObject subObject) { }

        #endregion

        #region - Custom state -

        /// <summary>
        /// Written once into every spawn message, before the object's state, and read on the receiving
        /// peer right after construction and before <see cref="OnPreSpawn"/>. For anything the object
        /// needs in order to become itself — an actor writes its template key here and clones the
        /// template on the way in.
        /// </summary>
        protected virtual void WriteSpawnData(NetworkWriter writer) { }

        protected virtual void ReadSpawnData(NetworkReader reader) { }

        internal void WriteSpawnDataInternal(NetworkWriter writer) => WriteSpawnData(writer);

        internal void ReadSpawnDataInternal(NetworkReader reader) => ReadSpawnData(reader);

        /// <summary>
        /// State beyond network variables. Called with <c>full</c> for spawn and new observers, and
        /// without after <see cref="MarkDirty"/>. Whatever is written must be read back in the same order.
        /// </summary>
        protected virtual void WriteState(NetworkWriter writer, bool full) { }

        protected virtual void ReadState(NetworkReader reader, bool full) { }

        /// <summary>Flags custom state as changed. Variables and components mark themselves.</summary>
        public void MarkDirty()
        {
            if (!IsSpawned || !IsAuthority || IsOffline) return;
            _customStateDirty = true;
            World.MarkDirty(this);
        }

        #endregion

        #region - Snapshot channel -

        /// <summary>
        /// Opts the object into the unreliable, latest-wins snapshot channel: the server writes a
        /// snapshot for each client at (roughly) <see cref="SnapshotRateHz"/> scaled by the world's
        /// LOD, under a per-client byte budget. Transforms live here.
        /// </summary>
        public virtual bool HasSnapshotChannel => false;

        /// <summary>Authored send rate; the LOD scales it down per client.</summary>
        public float SnapshotRateHz { get; set; } = 30f;

        /// <summary>Weight when snapshots compete for a client's budget. Higher reaches the wire sooner.</summary>
        public virtual float SnapshotPriority => 1f;

        /// <summary>
        /// When true the owning client is the source of truth for the snapshot state: it sends
        /// snapshots to the server, which applies them through <see cref="ReadSnapshot"/> (validating
        /// there) and relays its own view to everyone else. Owner-authoritative movement.
        /// </summary>
        public virtual bool OwnerWritesSnapshots => false;

        /// <summary>
        /// Whether there is anything worth sending to <paramref name="forClientId"/> right now. The
        /// default sends to everyone except the owner of an owner-written object (its truth flows the
        /// other way); an override can add change thresholds, or return true for the owner to push a
        /// correction.
        /// </summary>
        protected virtual bool HasSnapshotToSend(ulong forClientId, bool isOwner) => !(OwnerWritesSnapshots && isOwner);

        /// <summary>Server (or the owner, when owner-authoritative): write the latest state for one peer.</summary>
        protected virtual void WriteSnapshot(NetworkWriter writer, uint tick, ulong forClientId) { }

        /// <summary>Everyone else: apply a snapshot. <paramref name="fromClientId"/> is the server, or the owner on the server.</summary>
        protected virtual void ReadSnapshot(NetworkReader reader, uint tick, ulong fromClientId) { }

        internal bool HasSnapshotToSendInternal(ulong forClientId, bool isOwner) => HasSnapshotToSend(forClientId, isOwner);

        internal void WriteSnapshotInternal(NetworkWriter writer, uint tick, ulong forClientId) => WriteSnapshot(writer, tick, forClientId);

        internal void ReadSnapshotInternal(NetworkReader reader, uint tick, ulong fromClientId) => ReadSnapshot(reader, tick, fromClientId);

        #endregion

        #region - Components -

        public IReadOnlyList<NetworkComponent> Components => _components;

        /// <summary>
        /// Attaches a component. Before spawn anyone may; once spawned in a networked world only the
        /// authority may, and the change replicates.
        /// </summary>
        public T AddComponent<T>(T component) where T : NetworkComponent
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (component.Owner != null) throw new InvalidOperationException($"{component.GetType().Name} is already attached to an object.");
            // Before the spawn completes anyone may attach — that is how a client rebuilds a template's
            // stack in OnPreSpawn; afterwards it is the authority's call and it replicates.
            if (_spawnCompleted && !IsAuthority) throw new InvalidOperationException("Only the authority may add components to a spawned object.");

            AttachComponent(component, _nextComponentId++);
            if (_spawnCompleted)
            {
                component.SpawnInternal();
                if (!IsOffline)
                {
                    _componentOps.Add((k_OpAdd, component.ComponentId, component));
                    World.MarkDirty(this);
                }
            }

            return component;
        }

        public bool RemoveComponent(NetworkComponent component)
        {
            if (component == null || component.Owner != this) return false;
            if (_spawnCompleted && !IsAuthority) throw new InvalidOperationException("Only the authority may remove components from a spawned object.");

            if (_spawnCompleted)
            {
                component.DespawnInternal();
                if (!IsOffline)
                {
                    _componentOps.Add((k_OpRemove, component.ComponentId, null));
                    World.MarkDirty(this);
                }
            }

            _dirtyComponents.Remove(component);
            DetachComponent(component);
            return true;
        }

        public T Get<T>() where T : class
        {
            var list = ListFor(typeof(T));
            return list.Count > 0 ? list[0] as T : null;
        }

        public bool TryGet<T>(out T component) where T : class
        {
            component = Get<T>();
            return component != null;
        }

        public void GetAll<T>(List<T> results) where T : class
        {
            foreach (var c in ListFor(typeof(T)))
            {
                results.Add((T)(object)c);
            }
        }

        public bool Has<T>() where T : class => ListFor(typeof(T)).Count > 0;

        public NetworkComponent GetComponentById(ushort componentId)
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i].ComponentId == componentId) return _components[i];
            }

            return null;
        }

        private List<NetworkComponent> ListFor(Type type)
        {
            _componentsByType ??= new Dictionary<Type, List<NetworkComponent>>();
            if (!_componentsByType.TryGetValue(type, out var list))
            {
                list = new List<NetworkComponent>();
                foreach (var c in _components)
                {
                    if (type.IsInstanceOfType(c)) list.Add(c);
                }

                _componentsByType.Add(type, list);
            }

            return list;
        }

        private void AttachComponent(NetworkComponent component, ushort id)
        {
            _components.Add(component);
            _componentsByType?.Clear();
            component.AttachInternal(this, id);
            if (id >= _nextComponentId) _nextComponentId = (ushort)(id + 1);
        }

        private void DetachComponent(NetworkComponent component)
        {
            _components.Remove(component);
            _componentsByType?.Clear();
            component.DetachInternal();
        }

        internal void OnComponentDirty(NetworkComponent component)
        {
            if (!IsSpawned || !IsAuthority || IsOffline) return;
            if (!_dirtyComponents.Contains(component)) _dirtyComponents.Add(component);
            World.MarkDirty(this);
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
            if (!IsSpawned || !IsAuthority || IsOffline) return;
            _dirtyVariables.Add(variable);
            World.MarkDirty(this);
        }

        bool INetworkVariableHost.CanWriteVariables => CanWriteVariablesInternal;

        internal bool CanWriteVariablesInternal => World == null || World.IsAuthority;

        #endregion

        #region - Rpc support -

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool BeginSendRpc(uint hash, out NetworkWriter writer)
        {
            if (!IsSpawned)
            {
                Debug.LogError($"Rpc [{hash:X8}] called on {GetType().Name} before it was spawned.");
                writer = null;
                return false;
            }

            return World.BeginRpc(this, hash, out writer);
        }

        /// <summary>
        /// Sends the finished rpc to every remote target and returns true when this peer is itself in
        /// the target set, in which case the generated send path falls through and runs the body here.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool EndSendRpc(NetworkWriter writer, RpcTarget target, Delivery delivery) =>
            World.EndRpc(this, writer, target, delivery);

        #endregion

        #region - Internal plumbing: spawn -

        internal void SpawnInternal(NetworkWorld world, ulong id, ulong owner, VaporNetworkObject parent, bool spawnedOnlyOnOwner, bool isPlayerObject)
        {
            World = world;
            NetworkObjectId = id;
            OwnerClientId = owner;
            SpawnedOnlyOnOwner = spawnedOnlyOnOwner;
            IsPlayerObject = isPlayerObject;
            Parent = parent;
            parent?._subObjects.Add(this);
            OnPreSpawn();
        }

        internal void CompleteSpawn()
        {
            _spawnCompleted = true;
            OnSpawn();
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].SpawnInternal();
            }

            OnPostSpawn();

            // The parent hears about it last, once the child is whole.
            Parent?.OnSubObjectSpawned(this);
        }

        internal void DespawnInternal()
        {
            // The parent hears about it first, while the child is still whole and still linked.
            Parent?.OnSubObjectDespawned(this);

            for (int i = _components.Count - 1; i >= 0; i--)
            {
                _components[i].DespawnInternal();
            }

            OnDespawn();
            _spawnCompleted = false;
            Parent?._subObjects.Remove(this);
            Parent = null;
            World = null;
            NetworkObjectId = 0;
            _dirtyVariables.Clear();
            _dirtyComponents.Clear();
            _componentOps.Clear();
            _customStateDirty = false;
            QueuedDirty = false;
        }

        internal void SetOwnerInternal(ulong newOwner)
        {
            ulong previous = OwnerClientId;
            if (previous == newOwner) return;
            OwnerClientId = newOwner;
            OnOwnershipChanged(previous, newOwner);
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OwnershipChangedInternal(previous, newOwner);
            }
        }

        internal void TickInternal(uint tick, double dt)
        {
            if (WantsNetworkTick) OnNetworkTick(tick, dt);
            for (int i = 0; i < _components.Count; i++)
            {
                var c = _components[i];
                if (c.WantsNetworkTick) c.TickInternal(tick, dt);
            }
        }

        #endregion

        #region - Internal plumbing: state -

        internal void ClearDirty()
        {
            foreach (var v in _dirtyVariables) v.ClearDirty();
            _dirtyVariables.Clear();
            foreach (var c in _dirtyComponents) c.ClearDirty();
            _dirtyComponents.Clear();
            _componentOps.Clear();
            _customStateDirty = false;
            QueuedDirty = false;
        }

        /// <summary>
        /// Writes the object's state as the replicator would: everything when <c>full</c>, otherwise what
        /// changed since <see cref="ClearDirty"/>. Public so tools and tests can measure what an object costs
        /// on the wire; the replicator is the only caller that should act on the bytes.
        /// </summary>
        public void WriteObjectState(NetworkWriter writer, bool full)
        {
            byte flags = 0;
            bool anyVariables = full ? _variables.Count > 0 : _dirtyVariables.Count > 0;
            bool anyComponents = full ? _components.Count > 0 : _componentOps.Count > 0 || _dirtyComponents.Count > 0;
            if (anyVariables) flags |= k_FlagVariables;
            if (full || _customStateDirty) flags |= k_FlagCustomState;
            if (anyComponents) flags |= k_FlagComponents;
            writer.WriteByte(flags);

            if (anyVariables)
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

            if (anyComponents)
            {
                if (full)
                {
                    writer.WriteVarUInt32((uint)_components.Count);
                    foreach (var c in _components)
                    {
                        WriteComponentEntry(writer, k_OpAdd, c);
                    }
                }
                else
                {
                    int countAt = writer.ReserveUInt16();
                    int count = 0;
                    foreach (var (op, id, component) in _componentOps)
                    {
                        if (op == k_OpAdd)
                        {
                            WriteComponentEntry(writer, k_OpAdd, component);
                        }
                        else
                        {
                            writer.WriteByte(k_OpRemove);
                            writer.WriteVarUInt32(id);
                        }

                        count++;
                    }

                    foreach (var c in _dirtyComponents)
                    {
                        if (c.Owner != this || WasAddedThisTick(c)) continue;
                        writer.WriteByte(k_OpDelta);
                        writer.WriteVarUInt32(c.ComponentId);
                        int at = writer.ReserveUInt16();
                        int start = writer.Position;
                        c.Write(writer, full: false);
                        writer.PatchUInt16(at, checked((ushort)(writer.Position - start)));
                        count++;
                    }

                    writer.PatchUInt16(countAt, checked((ushort)count));
                }
            }
        }

        private bool WasAddedThisTick(NetworkComponent component)
        {
            foreach (var (op, _, c) in _componentOps)
            {
                if (op == k_OpAdd && ReferenceEquals(c, component)) return true;
            }

            return false;
        }

        private static void WriteComponentEntry(NetworkWriter writer, byte op, NetworkComponent component)
        {
            writer.WriteByte(op);
            writer.WriteVarUInt32(component.ComponentId);
            writer.WriteVarUInt32(NetworkTypeRegistry.TagOf(component.GetType()));
            int at = writer.ReserveUInt16();
            int start = writer.Position;
            component.WriteSpawnDataInternal(writer);
            component.Write(writer, full: true);
            writer.PatchUInt16(at, checked((ushort)(writer.Position - start)));
        }

        internal void ReadObjectState(NetworkReader reader, bool full)
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
                reader.Seek(end);
            }

            if ((flags & k_FlagComponents) != 0)
            {
                int count = full ? (int)reader.ReadVarUInt32() : reader.ReadUInt16();
                for (int i = 0; i < count; i++)
                {
                    byte op = reader.ReadByte();
                    ushort id = checked((ushort)reader.ReadVarUInt32());
                    switch (op)
                    {
                        case k_OpAdd:
                            ReadComponentAdd(reader, id);
                            break;

                        case k_OpRemove:
                        {
                            var existing = GetComponentById(id);
                            if (existing != null)
                            {
                                existing.DespawnInternal();
                                DetachComponent(existing);
                            }
                            break;
                        }

                        case k_OpDelta:
                        {
                            int length = reader.ReadUInt16();
                            int end = reader.Position + length;
                            var existing = GetComponentById(id);
                            if (existing != null)
                            {
                                existing.Read(reader, full: false);
                            }

                            reader.Seek(end);
                            break;
                        }

                        default:
                            throw new NetworkSerializationException($"Unknown component op {op}.");
                    }
                }
            }
        }

        private void ReadComponentAdd(NetworkReader reader, ushort id)
        {
            uint typeTag = reader.ReadVarUInt32();
            int length = reader.ReadUInt16();
            int end = reader.Position + length;

            // Full packets upsert: an object cloned from a template already carries its components,
            // and the wire entry is then a state update rather than a new attachment.
            var existing = GetComponentById(id);
            if (existing != null && NetworkTypeRegistry.TagOf(existing.GetType()) == typeTag)
            {
                existing.ReadSpawnDataInternal(reader);
                existing.Read(reader, full: true);
                reader.Seek(end);
                return;
            }

            if (existing != null)
            {
                existing.DespawnInternal();
                DetachComponent(existing);
            }

            var created = NetworkTypeRegistry.Create<NetworkComponent>(typeTag);
            if (created == null)
            {
                Debug.LogWarning($"{GetType().Name} received a component with unknown type tag [{typeTag:X8}]; skipping it.");
                reader.Seek(end);
                return;
            }

            AttachComponent(created, id);
            created.ReadSpawnDataInternal(reader);
            created.Read(reader, full: true);
            reader.Seek(end);
            if (_spawnCompleted)
            {
                created.SpawnInternal();
            }
        }

        #endregion

        public override string ToString() => $"{GetType().Name}[{NetworkObjectId}]";
    }
}
