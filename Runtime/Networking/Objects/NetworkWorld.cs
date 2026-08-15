using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// The set of live <see cref="VaporNetworkObject"/>s in one process, their ids, and the plumbing
    /// between them and the wire. With a <see cref="NetworkSession"/> it replicates; without one it is
    /// an offline world in which every object is its own authority and every rpc runs locally.
    /// </summary>
    /// <remarks>
    /// Authority is decided here once: the server, or anyone offline. Objects ask their world rather
    /// than a global, so a test can stand up a server world and a client world side by side and a
    /// host can run both in one process. <see cref="Current"/> exists for the few places — reference
    /// resolution in rpc arguments — that have no world in hand; set it, don't rely on it.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class NetworkWorld : IDisposable
    {
        private static bool s_FormattersRegistered;

        private readonly Dictionary<ulong, VaporNetworkObject> _objects = new();
        private readonly List<VaporNetworkObject> _objectList = new();
        private readonly List<VaporNetworkObject> _roots = new();
        private readonly List<VaporNetworkObject> _dirty = new();
        private readonly List<VaporNetworkObject> _tickScratch = new();
        private readonly NetworkWriter _offlineRpcWriter = new(256);
        private ulong _nextObjectId = 1;

        /// <summary>The world rpc arguments resolve against when no world is passed explicitly.</summary>
        public static NetworkWorld Current { get; set; }

        public NetworkWorld(NetworkSession session = null)
        {
            EnsureFormatters();
            Session = session;
            if (session != null)
            {
                Replicator = new Replicator(this, session);
            }

            Current ??= this;
        }

        #region - Role -

        public NetworkSession Session { get; }

        internal Replicator Replicator { get; }

        /// <summary>Interest management (subscriptions, spatial provider). Null offline.</summary>
        public InterestManager Interest => Replicator?.Interest;

        public bool IsOffline => Session == null;
        public bool IsServer => Session == null || Session.IsServer;
        public bool IsClient => Session == null || Session.IsClient;
        public bool IsHost => Session != null && Session.IsHost;

        /// <summary>The server, or anyone offline. The one place this is decided.</summary>
        public bool IsAuthority => Session == null || Session.IsServer;

        public ulong LocalClientId => Session == null ? NetworkSession.ServerClientId : Session.LocalClientId;

        #endregion

        #region - Objects -

        public IReadOnlyList<VaporNetworkObject> Objects => _objectList;
        public IReadOnlyList<VaporNetworkObject> Roots => _roots;
        public int Count => _objectList.Count;

        public bool TryGet(ulong networkObjectId, out VaporNetworkObject networkObject) => _objects.TryGetValue(networkObjectId, out networkObject);

        public bool TryGet<T>(ulong networkObjectId, out T networkObject) where T : VaporNetworkObject
        {
            if (_objects.TryGetValue(networkObjectId, out var found) && found is T typed)
            {
                networkObject = typed;
                return true;
            }

            networkObject = null;
            return false;
        }

        public VaporNetworkObject Get(ulong networkObjectId) => _objects.TryGetValue(networkObjectId, out var found) ? found : null;

        public event Action<VaporNetworkObject> Spawned;
        public event Action<VaporNetworkObject> Despawned;
        public event Action<VaporNetworkObject, ulong, ulong> OwnershipChanged;

        /// <summary>
        /// Server: a client is about to have its owned objects despawned. Transfer ownership of anything
        /// that should outlive the player here.
        /// </summary>
        public event Action<ulong> ClientLeaving;

        /// <summary>Server: whether a departing client's owned objects are despawned automatically. On by default.</summary>
        public bool DespawnOwnedObjectsOnDisconnect { get; set; } = true;

        #endregion

        #region - Spawn / despawn / ownership (authority) -

        /// <summary>
        /// Spawns an object. Authority only. On a networked server the spawn replicates to every
        /// client the object is relevant to; offline it simply becomes live.
        /// </summary>
        public void Spawn(VaporNetworkObject networkObject, ulong ownerClientId = NetworkSession.ServerClientId, VaporNetworkObject parent = null,
            bool spawnedOnlyOnOwner = false, bool isPlayerObject = false)
        {
            if (networkObject == null) throw new ArgumentNullException(nameof(networkObject));
            if (!IsAuthority) throw new InvalidOperationException("Only the authority (server or offline) may spawn objects.");
            if (networkObject.IsSpawned) throw new InvalidOperationException($"{networkObject} is already spawned.");
            if (parent != null && !parent.IsSpawned) throw new InvalidOperationException("The parent must be spawned first.");
            if (parent != null && parent.World != this) throw new InvalidOperationException("The parent belongs to another world.");

            ulong id = _nextObjectId++;
            networkObject.SpawnInternal(this, id, ownerClientId, parent, spawnedOnlyOnOwner, isPlayerObject);
            Register(networkObject);
            networkObject.CompleteSpawn();
            Replicator?.OnSpawned(networkObject);
            networkObject.ClearDirty();
            Spawned?.Invoke(networkObject);
        }

        /// <summary>Despawns an object and its sub-objects. Authority only.</summary>
        public void Despawn(VaporNetworkObject networkObject)
        {
            if (networkObject == null) throw new ArgumentNullException(nameof(networkObject));
            if (!IsAuthority) throw new InvalidOperationException("Only the authority (server or offline) may despawn objects.");
            if (!networkObject.IsSpawned || networkObject.World != this) return;

            DespawnRecursive(networkObject);
        }

        private void DespawnRecursive(VaporNetworkObject networkObject)
        {
            // Children first, from a copy: a child's OnDespawn may despawn siblings.
            var subs = networkObject.SubObjects;
            if (subs.Count > 0)
            {
                foreach (var sub in new List<VaporNetworkObject>(subs))
                {
                    if (sub.IsSpawned) DespawnRecursive(sub);
                }
            }

            ulong id = networkObject.NetworkObjectId;
            Replicator?.OnDespawning(networkObject);
            networkObject.DespawnInternal();
            Unregister(id, networkObject);
            Despawned?.Invoke(networkObject);
        }

        /// <summary>Transfers ownership. Authority only. Replicates to every client that knows the object.</summary>
        public void ChangeOwnership(VaporNetworkObject networkObject, ulong newOwnerClientId)
        {
            if (networkObject == null) throw new ArgumentNullException(nameof(networkObject));
            if (!IsAuthority) throw new InvalidOperationException("Only the authority may change ownership.");
            if (!networkObject.IsSpawned || networkObject.World != this) throw new InvalidOperationException($"{networkObject} is not spawned in this world.");

            ulong previous = networkObject.OwnerClientId;
            if (previous == newOwnerClientId) return;
            networkObject.SetOwnerInternal(newOwnerClientId);
            Replicator?.OnOwnershipChanged(networkObject, previous);
            OwnershipChanged?.Invoke(networkObject, previous, newOwnerClientId);
        }

        /// <summary>Despawns every object owned by a client (roots first, which takes their sub-objects with them).</summary>
        public void DespawnOwnedBy(ulong clientId)
        {
            if (!IsAuthority) return;
            var owned = new List<VaporNetworkObject>();
            foreach (var root in _roots)
            {
                if (root.OwnerClientId == clientId) owned.Add(root);
            }

            foreach (var root in owned)
            {
                if (root.IsSpawned) Despawn(root);
            }

            // Sub-objects owned by the client under roots that are not: despawn individually.
            owned.Clear();
            foreach (var obj in _objectList)
            {
                if (obj.OwnerClientId == clientId && obj.IsSpawned) owned.Add(obj);
            }

            foreach (var obj in owned)
            {
                if (obj.IsSpawned) Despawn(obj);
            }
        }

        internal void HandleClientLeaving(ulong clientId)
        {
            ClientLeaving?.Invoke(clientId);
            if (DespawnOwnedObjectsOnDisconnect)
            {
                DespawnOwnedBy(clientId);
            }
        }

        #endregion

        #region - Remote (client) side, driven by the replicator -

        internal VaporNetworkObject SpawnRemote(uint typeTag, ulong id, ulong owner, ulong parentId, bool spawnedOnlyOnOwner, bool isPlayerObject, NetworkReader reader)
        {
            if (_objects.ContainsKey(id))
            {
                Debug.LogWarning($"Received a spawn for object {id}, which already exists; ignoring.");
                return null;
            }

            VaporNetworkObject parent = null;
            if (parentId != 0 && !_objects.TryGetValue(parentId, out parent))
            {
                Debug.LogWarning($"Received a spawn for object {id} under unknown parent {parentId}; ignoring.");
                return null;
            }

            var networkObject = NetworkTypeRegistry.Create<VaporNetworkObject>(typeTag);
            if (networkObject == null)
            {
                Debug.LogWarning($"Received a spawn with unknown type tag [{typeTag:X8}]; ignoring. The peers are likely running different builds.");
                return null;
            }

            networkObject.ReadSpawnDataInternal(reader);
            networkObject.SpawnInternal(this, id, owner, parent, spawnedOnlyOnOwner, isPlayerObject);
            networkObject.ReadObjectState(reader, full: true);
            Register(networkObject);
            networkObject.CompleteSpawn();
            Spawned?.Invoke(networkObject);
            return networkObject;
        }

        internal void DespawnRemote(ulong id)
        {
            if (!_objects.TryGetValue(id, out var networkObject)) return;
            // Children first, mirroring the authority.
            var subs = networkObject.SubObjects;
            if (subs.Count > 0)
            {
                foreach (var sub in new List<VaporNetworkObject>(subs))
                {
                    if (sub.IsSpawned) DespawnRemote(sub.NetworkObjectId);
                }
            }

            networkObject.DespawnInternal();
            Unregister(id, networkObject);
            Despawned?.Invoke(networkObject);
        }

        internal void SetOwnerRemote(ulong id, ulong newOwner)
        {
            if (!_objects.TryGetValue(id, out var networkObject)) return;
            ulong previous = networkObject.OwnerClientId;
            if (previous == newOwner) return;
            networkObject.SetOwnerInternal(newOwner);
            OwnershipChanged?.Invoke(networkObject, previous, newOwner);
        }

        internal void ApplyRemoteState(ulong id, NetworkReader reader)
        {
            if (!_objects.TryGetValue(id, out var networkObject)) return;
            networkObject.ReadObjectState(reader, full: false);
        }

        #endregion

        #region - Registry -

        private void Register(VaporNetworkObject networkObject)
        {
            _objects.Add(networkObject.NetworkObjectId, networkObject);
            _objectList.Add(networkObject);
            if (networkObject.IsRoot) _roots.Add(networkObject);
        }

        private void Unregister(ulong id, VaporNetworkObject networkObject)
        {
            _objects.Remove(id);
            _objectList.Remove(networkObject);
            _roots.Remove(networkObject);
            _dirty.Remove(networkObject);
        }

        #endregion

        #region - Dirty tracking -

        internal void MarkDirty(VaporNetworkObject networkObject)
        {
            if (networkObject.QueuedDirty || !networkObject.IsSpawned) return;
            networkObject.QueuedDirty = true;
            _dirty.Add(networkObject);
        }

        internal List<VaporNetworkObject> DirtyObjects => _dirty;

        internal void OnInterestChanged(VaporNetworkObject networkObject) => Replicator?.OnInterestChanged(networkObject);

        #endregion

        #region - Rpc plumbing -

        internal bool BeginRpc(IRpcHost host, uint hash, out NetworkWriter writer)
        {
            if (Replicator != null)
            {
                return Replicator.BeginRpc(host, hash, out writer);
            }

            // Offline: nothing is sent; the generated code still needs a writer to serialize into.
            _offlineRpcWriter.Reset();
            writer = _offlineRpcWriter;
            return true;
        }

        internal bool EndRpc(IRpcHost host, NetworkWriter writer, RpcTarget target, Delivery delivery)
        {
            if (Replicator != null)
            {
                return Replicator.EndRpc(host, writer, target, delivery);
            }

            return true;   // offline: every rpc runs locally
        }

        internal void DispatchRpc(ulong objectId, ushort componentId, uint hash, NetworkReader reader)
        {
            if (!_objects.TryGetValue(objectId, out var networkObject))
            {
                // Despawned in flight; not an error.
                return;
            }

            IRpcHost host = componentId == 0 ? networkObject : networkObject.GetComponentById(componentId);
            if (host == null)
            {
                Debug.LogWarning($"Rpc [{hash:X8}] addressed to component {componentId} of {networkObject}, which has no such component.");
                return;
            }

            if (!RpcRegistry.TryGet(hash, out var handler, out var name))
            {
                Debug.LogWarning($"Received unknown rpc [{hash:X8}] for {networkObject}. The peers are likely running different builds.");
                return;
            }

            try
            {
                handler(host, reader);
            }
            catch (InvalidCastException)
            {
                Debug.LogError($"Rpc {name} was addressed to a {host.GetType().Name}, which does not declare it.");
            }
        }

        #endregion

        #region - Tick -

        /// <summary>
        /// Advances every object that wants a tick, then lets the replicator send what changed. Wire
        /// this to <see cref="NetworkSession.Tick"/> (see <see cref="BindToSession"/>) or call it from a
        /// test.
        /// </summary>
        public void NetworkTick(uint tick, double deltaTime)
        {
            _tickScratch.Clear();
            _tickScratch.AddRange(_objectList);
            foreach (var networkObject in _tickScratch)
            {
                if (networkObject.IsSpawned) networkObject.TickInternal(tick, deltaTime);
            }

            Replicator?.EndOfTick(tick);
        }

        /// <summary>Subscribes the world to its session's tick and connection events.</summary>
        public void BindToSession()
        {
            if (Session == null) return;
            Session.Tick += NetworkTick;
            Replicator?.Bind();
        }

        public void UnbindFromSession()
        {
            if (Session == null) return;
            Session.Tick -= NetworkTick;
            Replicator?.Unbind();
        }

        #endregion

        #region - Shutdown -

        /// <summary>Despawns everything (authority) or drops everything (client) and detaches from the session.</summary>
        public void Clear()
        {
            if (IsAuthority)
            {
                foreach (var root in new List<VaporNetworkObject>(_roots))
                {
                    if (root.IsSpawned) Despawn(root);
                }

                foreach (var obj in new List<VaporNetworkObject>(_objectList))
                {
                    if (obj.IsSpawned) Despawn(obj);
                }
            }
            else
            {
                foreach (var obj in new List<VaporNetworkObject>(_objectList))
                {
                    if (obj.IsSpawned) DespawnRemote(obj.NetworkObjectId);
                }
            }
        }

        public void Dispose()
        {
            UnbindFromSession();
            Clear();
            Replicator?.Dispose();
            if (ReferenceEquals(Current, this)) Current = null;
        }

        #endregion

        private static void EnsureFormatters()
        {
            if (s_FormattersRegistered) return;
            s_FormattersRegistered = true;
            NetworkFormatters.Register(new VaporNetworkObjectReference.Formatter());
            NetworkFormatters.Register(new InterestGroupFormatter());
        }

        private sealed class InterestGroupFormatter : NetworkFormatter<InterestGroup>
        {
            public override void Write(NetworkWriter writer, in InterestGroup value) => writer.WriteUInt32(value.Key);
            public override InterestGroup Read(NetworkReader reader) => new(reader.ReadUInt32());
        }
    }
}
