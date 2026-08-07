using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;
using Vapor.Inspector;
using Vapor.Unsafe;

namespace Vapor.NetworkObjects
{
    public class NetworkMessages : VaporBehaviour
    {
        public static NetworkMessages Instance { get; private set; }
        
        public const int RPC_MESSAGE_DEFAULT_SIZE = 1024; // 1k
        public const int RPC_MESSAGE_MAXIMUM_SIZE = 1024 * 64; // 64k
        private const byte k_SpawnMessage = 1;
        private const byte k_DestroyMessage = 2;
        internal const byte RPC_MESSAGE = 3;
        private const byte k_SyncBatchMessage = 4;

        /// <summary>
        /// Batches are split below this so a single message stays a sensible size. One entry always
        /// goes out even if it alone exceeds the budget — fragmentation covers that case.
        /// </summary>
        private const int k_SyncBatchByteBudget = 32 * 1024;

        /// <summary>Bit-packed id plus the payload's length prefix, at their widest.</summary>
        private const int k_SyncEntryOverhead = 13;

        /// <summary>
        /// Everything describing an object's lifetime and state shares one delivery channel. Ordering
        /// is only guaranteed within a channel, so splitting spawns from syncs would let a delta
        /// overtake the spawn it depends on.
        /// </summary>
        private const NetworkDelivery k_ObjectDelivery = NetworkDelivery.ReliableFragmentedSequenced;

        private NetworkManager _networkManager;
        private ulong _networkObjectIdCounter;
        public readonly Dictionary<ulong, VaporNetworkObject> NetworkObjects = new();
        private readonly Dictionary<ulong, HashSet<VaporNetworkObject>> _ownerClientIdToNetworkObjectMap = new();
        private readonly HashSet<VaporNetworkObject> _tickableNetworkObjects = new();
        private readonly List<VaporNetworkObject> _networkObjectsToSend = new();

        // Iterating these directly would break the moment a tick handler spawned something or a
        // Serialize marked another object dirty, so each pass runs over a copy.
        private readonly List<VaporNetworkObject> _tickBuffer = new();
        private readonly List<VaporNetworkObject> _sendBuffer = new();

        #region - Interest -

        internal readonly InterestRegistry Interest = new();

        /// <summary>
        /// Subscribes a client to a replication channel, spawning everything already in it that the
        /// client cannot yet see.
        /// </summary>
        public void Subscribe(ulong clientId, InterestGroup group)
        {
            if (!_networkManager.IsServer || !Interest.Subscribe(clientId, group))
            {
                return;
            }

            foreach (var member in Interest.MembersOf(group))
            {
                if (Interest.IsVisibleTo(member, clientId))
                {
                    ShowTo(member, clientId);
                }
            }
        }

        /// <summary>
        /// Unsubscribes a client, despawning everything the channel was the client's only reason to see.
        /// </summary>
        public void Unsubscribe(ulong clientId, InterestGroup group)
        {
            if (!_networkManager.IsServer || !Interest.Unsubscribe(clientId, group))
            {
                return;
            }

            foreach (var member in Interest.MembersOf(group))
            {
                if (!Interest.IsVisibleTo(member, clientId))
                {
                    HideFrom(member, clientId);
                }
            }
        }

        public bool IsSubscribed(ulong clientId, InterestGroup group) => Interest.IsSubscribed(clientId, group);

        /// <summary>True when the client has been sent this object and may receive syncs and rpcs for it.</summary>
        public bool IsObserving(ulong clientId, ulong networkObjectId) => Interest.IsObserving(clientId, networkObjectId);

        internal void OnInterestChanged(VaporNetworkObject networkObject, InterestGroup group, bool joined)
        {
            if (!_networkManager.IsServer)
            {
                return;
            }

            if (joined)
            {
                Interest.Join(networkObject, group);
            }
            else
            {
                Interest.Leave(networkObject, group);
            }

            RefreshObservers(networkObject);
        }

        /// <summary>
        /// Brings every client's view of one object up to date with what it is now relevant to.
        /// </summary>
        private void RefreshObservers(VaporNetworkObject networkObject)
        {
            if (!_networkManager.IsServer || !networkObject.IsSpawned)
            {
                return;
            }

            foreach (var clientId in _networkManager.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId)
                {
                    continue;
                }

                if (Interest.IsVisibleTo(networkObject, clientId))
                {
                    ShowTo(networkObject, clientId);
                }
                else
                {
                    HideFrom(networkObject, clientId);
                }
            }
        }

        private void ShowTo(VaporNetworkObject networkObject, ulong clientId)
        {
            // Claimed before recursing, so a malformed parent cycle terminates instead of recursing
            // forever.
            if (!Interest.MarkObserving(clientId, networkObject.NetworkObjectId))
            {
                return;
            }

            // Parents go out first. A child that arrives before its parent is never linked into the
            // hierarchy, because the link is resolved on arrival and never retried.
            if (!networkObject.IsRoot && !networkObject.ParentIsUnityObject &&
                NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parent))
            {
                ShowTo(parent, clientId);
            }

            // The snapshot has to be taken after anything already owed to the existing observers has
            // gone out. Otherwise this client's snapshot would contain a change that is still queued as
            // a delta, and the delta would then be applied on top of it a second time.
            FlushPendingSync(networkObject);
            SendSpawnMessage(networkObject, clientId);
        }

        private void HideFrom(VaporNetworkObject networkObject, ulong clientId)
        {
            if (!Interest.ClearObserving(clientId, networkObject.NetworkObjectId))
            {
                return;
            }

            // The receiver despawns exactly what it is told to, so anything hanging off this object
            // has to go with it or it is left pointing at a parent that no longer exists.
            if (networkObject.SubObjects != null)
            {
                for (int i = 0; i < networkObject.SubObjects.Count; i++)
                {
                    HideFrom(networkObject.SubObjects[i], clientId);
                }
            }

            SendDestroyMessage(networkObject.NetworkObjectId, clientId);
        }

        #endregion

        private void Awake()
        {
            Instance = this;
            Interest.ParentLookup = id => NetworkObjects.TryGetValue(id, out var parent) ? parent : null;
            TryGetComponent(out _networkManager);
            _networkManager.OnServerStarted += OnServerStarted;
            _networkManager.OnClientStarted += OnClientStarted;
            _networkManager.OnConnectionEvent += OnConnection;
            if (_networkManager.IsServer)
            {
                OnServerStarted();
            }

            if (_networkManager.IsClient)
            {
                OnClientStarted();
            }
        }

        private void OnConnection(NetworkManager arg1, ConnectionEventData arg2)
        {
            switch (arg2.EventType)
            {
                case ConnectionEvent.ClientConnected:
                    if (_networkManager.IsServer && arg2.ClientId != NetworkManager.ServerClientId)
                    {
                        SynchronizeClient(arg2.ClientId);
                    }

                    break;
                case ConnectionEvent.ClientDisconnected:
                    if (_networkManager.IsServer && arg2.ClientId != NetworkManager.ServerClientId)
                    {
                        DespawnObjectsOwnedBy(arg2.ClientId);
                        Interest.DropClient(arg2.ClientId);
                    }

                    break;
                case ConnectionEvent.PeerConnected:
                case ConnectionEvent.PeerDisconnected:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Despawns everything a departing client owned.
        /// </summary>
        /// <remarks>
        /// Ownership is fixed for an object's lifetime, so there is nobody to hand these to — see
        /// <see cref="VaporNetworkObject.OwnerClientId"/>. Anything worth keeping has to be persisted
        /// from <c>OnDespawn</c>, which runs before the object is torn down.
        /// <para>
        /// Children are despawned by the cascade whoever owns them: a sub-object cannot outlive the
        /// thing it hangs from.
        /// </para>
        /// </remarks>
        private void DespawnObjectsOwnedBy(ulong clientId)
        {
            if (!_ownerClientIdToNetworkObjectMap.TryGetValue(clientId, out var owned) || owned.Count == 0)
            {
                _ownerClientIdToNetworkObjectMap.Remove(clientId);
                return;
            }

            // Copied: each despawn removes itself from this very set, and cascades into children that
            // may be in it too.
            var doomed = ListPool<VaporNetworkObject>.Get();
            doomed.AddRange(owned);
            foreach (var networkObject in doomed)
            {
                DestroyNetworkObject(networkObject.NetworkObjectId);
            }

            ListPool<VaporNetworkObject>.Release(doomed);
            _ownerClientIdToNetworkObjectMap.Remove(clientId);
        }

        /// <summary>
        /// Sends a joining client everything it is already relevant to.
        /// </summary>
        /// <remarks>
        /// Parents go out before their children. A child whose parent has not arrived cannot be linked
        /// into the hierarchy on the receiving side, and the link is never retried — relying on
        /// dictionary order to get this right holds only until the first despawn frees a slot.
        /// <para>
        /// Owner-only objects are included when the joiner owns them, which matters for anything
        /// restored from a save before its client arrived.
        /// </para>
        /// </remarks>
        private void SynchronizeClient(ulong clientId)
        {
            var ordered = ListPool<VaporNetworkObject>.Get();
            var visited = HashSetPool<ulong>.Get();

            foreach (var networkObject in NetworkObjects.Values)
            {
                AppendWithParentsFirst(networkObject, ordered, visited);
            }

            foreach (var networkObject in ordered)
            {
                if (Interest.IsVisibleTo(networkObject, clientId))
                {
                    ShowTo(networkObject, clientId);
                }
            }

            HashSetPool<ulong>.Release(visited);
            ListPool<VaporNetworkObject>.Release(ordered);
        }

        private void AppendWithParentsFirst(VaporNetworkObject networkObject, List<VaporNetworkObject> ordered, HashSet<ulong> visited)
        {
            // Marked before recursing, so a corrupted parent cycle terminates instead of overflowing.
            if (!visited.Add(networkObject.NetworkObjectId))
            {
                return;
            }

            if (!networkObject.IsRoot && !networkObject.ParentIsUnityObject &&
                NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parent))
            {
                AppendWithParentsFirst(parent, ordered, visited);
            }

            ordered.Add(networkObject);
        }

        private void OnServerStarted()
        {
            _networkManager.CustomMessagingManager.OnUnnamedMessage += ReceiveMessage;
            _networkManager.NetworkTickSystem.Tick += OnTick;
        }

        private void OnClientStarted()
        {
            if (_networkManager.IsHost)
            {
                return;
            }

            _networkManager.CustomMessagingManager.OnUnnamedMessage += ReceiveMessage;
            _networkManager.NetworkTickSystem.Tick += OnTick;
        }

        private void OnDestroy()
        {
            
            if (_networkManager)
            {
                if (_networkManager.CustomMessagingManager != null)
                {
                    _networkManager.CustomMessagingManager.OnUnnamedMessage -= ReceiveMessage;
                }

                if (_networkManager.NetworkTickSystem != null)
                {
                    _networkManager.NetworkTickSystem.Tick -= OnTick;
                }

                _networkManager.OnServerStarted -= OnServerStarted;
                _networkManager.OnClientStarted -= OnClientStarted;
                _networkManager.OnConnectionEvent -= OnConnection;
            }

            bool networkManagerAlive = _networkManager && _networkManager.SpawnManager != null;
            foreach (var networkObject in NetworkObjects.Values)
            {
                if (!networkObject.IsRoot)
                {
                    if (networkObject.ParentIsUnityObject)
                    {
                        if (networkManagerAlive && _networkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var spawnedObject))
                        {
                            var networkBehaviour = spawnedObject.GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex);
                            if (networkBehaviour is ISubObjectOwner spawnListener)
                            {
                                spawnListener.OnSubObjectDespawned(networkObject);
                            }
                        }
                    }
                    else if (NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentNetworkObject))
                    {
                        parentNetworkObject.SubObjectDespawned(networkObject);
                    }
                }

                networkObject.OnDespawn();
                networkObject.InternalDestroy();
            }

            NetworkObjects.Clear();
        }

        private void OnTick()
        {
            var deltaTime = _networkManager.NetworkTickSystem.LocalTime.FixedDeltaTime;

            // A tick handler is free to spawn or despawn, which would otherwise invalidate the
            // enumerator mid-pass.
            _tickBuffer.Clear();
            _tickBuffer.AddRange(_tickableNetworkObjects);
            foreach (var networkObject in _tickBuffer)
            {
                networkObject.OnTick(deltaTime);
            }

            _tickBuffer.Clear();

            if (!_networkManager.IsServer || _networkObjectsToSend.Count == 0)
            {
                return;
            }

            // Drained before sending: serializing one object can mark another dirty, and that has to
            // land in the next tick's queue rather than in the one being walked.
            _sendBuffer.Clear();
            _sendBuffer.AddRange(_networkObjectsToSend);
            _networkObjectsToSend.Clear();

            SendSyncBatches(_sendBuffer);
            _sendBuffer.Clear();
        }

        /// <summary>
        /// Serializes each dirty object once, then sends every client one message holding just the
        /// objects it can see.
        /// </summary>
        /// <remarks>
        /// Serializing once is not an optimisation, it is a requirement: <c>Serialize</c> is what
        /// consumes the dirty state, so running it per client would hand the second client an empty
        /// delta. The payloads are identical across clients — only the selection differs — so each is
        /// copied out once and then composed into per-client batches.
        /// </remarks>
        private void SendSyncBatches(List<VaporNetworkObject> dirty)
        {
            var customMessagingManager = _networkManager.CustomMessagingManager;
            if (customMessagingManager == null || dirty.Count == 0)
            {
                return;
            }

            var entries = ListPool<SyncEntry>.Get();

            foreach (var networkObject in dirty)
            {
                if (!networkObject.IsSpawned)
                {
                    continue;
                }

                using var scratch = new FastBufferWriter(RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, RPC_MESSAGE_MAXIMUM_SIZE);
                networkObject.Serialize(scratch, false);

                // Copied out rather than kept as a view: AsNativeArray aliases the writer's buffer, and
                // the writer has to be disposed before the next object is serialized.
                var payload = new NativeArray<byte>(scratch.Length, Allocator.Temp);
                NativeArray<byte>.Copy(scratch.AsNativeArray(), payload, scratch.Length);
                entries.Add(new SyncEntry(networkObject.NetworkObjectId, payload));
            }

            if (entries.Count > 0)
            {
                foreach (var clientId in _networkManager.ConnectedClientsIds)
                {
                    if (clientId != NetworkManager.ServerClientId)
                    {
                        SendSyncBatch(clientId, entries, customMessagingManager);
                    }
                }
            }

            foreach (var entry in entries)
            {
                entry.Payload.Dispose();
            }

            ListPool<SyncEntry>.Release(entries);
        }

        private void SendSyncBatch(ulong clientId, List<SyncEntry> entries, CustomMessagingManager customMessagingManager)
        {
            var mine = ListPool<int>.Get();
            var sizes = ListPool<int>.Get();
            for (int i = 0; i < entries.Count; i++)
            {
                if (Interest.IsObserving(clientId, entries[i].NetworkObjectId))
                {
                    mine.Add(i);
                    sizes.Add(entries[i].Payload.Length);
                }
            }

            if (mine.Count > 0)
            {
                var chunkCounts = ListPool<int>.Get();
                SyncBatchCodec.Chunk(sizes, k_SyncBatchByteBudget, k_SyncEntryOverhead, chunkCounts);

                int index = 0;
                foreach (var chunkCount in chunkCounts)
                {
                    using var writer = new FastBufferWriter(RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, RPC_MESSAGE_MAXIMUM_SIZE);
                    writer.WriteValueSafe(k_SyncBatchMessage);
                    SyncBatchCodec.WriteCount(writer, chunkCount);

                    for (int i = 0; i < chunkCount; i++, index++)
                    {
                        var entry = entries[mine[index]];
                        SyncBatchCodec.WriteEntry(writer, entry.NetworkObjectId, entry.Payload);
                    }

                    customMessagingManager.SendUnnamedMessage(clientId, writer, k_ObjectDelivery);
                }

                ListPool<int>.Release(chunkCounts);
            }

            ListPool<int>.Release(sizes);
            ListPool<int>.Release(mine);
        }

        /// <summary>
        /// Sends whatever this object still owes its current observers, so a snapshot taken afterwards
        /// cannot be overtaken by a delta describing changes it already contains.
        /// </summary>
        private void FlushPendingSync(VaporNetworkObject networkObject)
        {
            if (!networkObject.IsDirty || !networkObject.IsSpawned || !_networkManager.IsServer)
            {
                return;
            }

            var single = ListPool<VaporNetworkObject>.Get();
            single.Add(networkObject);
            SendSyncBatches(single);
            ListPool<VaporNetworkObject>.Release(single);

            _networkObjectsToSend.Remove(networkObject);
            _sendBuffer.Remove(networkObject);
        }

        private readonly struct SyncEntry
        {
            public readonly ulong NetworkObjectId;
            public readonly NativeArray<byte> Payload;

            public SyncEntry(ulong networkObjectId, NativeArray<byte> payload)
            {
                NetworkObjectId = networkObjectId;
                Payload = payload;
            }
        }

        /// <summary>
        /// Fills <paramref name="targetIds"/> with every client that has been sent this object, which
        /// is the only set a sync or an rpc may address.
        /// </summary>
        private void AddObservers(List<ulong> targetIds, VaporNetworkObject networkObject,
            ulong excludedClientId = ulong.MaxValue, ulong secondExcludedClientId = ulong.MaxValue)
        {
            foreach (var clientId in _networkManager.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId || clientId == excludedClientId || clientId == secondExcludedClientId)
                {
                    continue;
                }

                if (IsObserving(clientId, networkObject.NetworkObjectId))
                {
                    targetIds.Add(clientId);
                }
            }
        }

        public T CreateNetworkObject<T>(ulong parentNetworkId = 0, ushort parentNetworkBehaviourOrderIndex = 0, bool parentIsUnityObject = false, ulong ownerClientId = 0UL, bool onlySpawnForOwner = false, bool isPlayerObject = false) where T : VaporNetworkObject, new()
        {
            Debug.Assert(_networkManager.IsServer, "Only the server can create network objects");
            _networkObjectIdCounter++;
            var networkObject = new T
            {
                NetworkObjectId = _networkObjectIdCounter,
                IsServer = _networkManager.IsServer,
                IsClient = _networkManager.IsClient,
                IsHost = _networkManager.IsHost,
                OwnerClientId = ownerClientId,
                ParentNetworkObjectId = parentNetworkId,
                ParentNetworkBehaviourOrderIndex = parentNetworkBehaviourOrderIndex,
                ParentIsUnityObject = parentIsUnityObject,
                SpawnedOnlyOnOwner = onlySpawnForOwner,
                IsPlayerObject = isPlayerObject,
                NetworkMessages = this,
            };
            NetworkObjects.Add(networkObject.NetworkObjectId, networkObject);
            AddNetworkObjectToOwnerMap(networkObject);
            if (networkObject.ShouldTick)
            {
                _tickableNetworkObjects.Add(networkObject);
            }

            networkObject.InternalInitialize();
            networkObject.OnPreSpawn();
            if (!networkObject.IsRoot)
            {
                if (networkObject.ParentIsUnityObject)
                {
                    var networkBehaviour = _networkManager.SpawnManager.SpawnedObjects[networkObject.ParentNetworkObjectId]
                        .GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex);
                    if (networkBehaviour is ISubObjectOwner subObjectOwner)
                    {
                        // subObjectOwner.AddSubObject(networkObject);
                        subObjectOwner.OnSubObjectSpawned(networkObject);
                    }
                            
                }
                else if (NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentNetworkObject))
                {
                    // parentNetworkObject.AddSubObject(networkObject);
                    parentNetworkObject.SubObjectSpawned(networkObject);
                }
            }
            networkObject.OnSpawn();
            networkObject.OnPostSpawn();

            PublishSpawn(networkObject);
            return networkObject;
        }

        internal void SpawnNetworkObject(VaporNetworkObject networkObject, ulong parentNetworkId = 0, ushort parentNetworkBehaviourOrderIndex = 0, bool parentIsUnityObject = false,
            ulong ownerClientId = 0UL, bool onlySpawnForOwner = false, bool isPlayerObject = false)
        {
            Debug.Assert(_networkManager.IsServer, "Only the server can create network objects");
            _networkObjectIdCounter++;
            networkObject.NetworkObjectId = _networkObjectIdCounter;
            networkObject.IsServer = _networkManager.IsServer;
            networkObject.IsClient = _networkManager.IsClient;
            networkObject.IsHost = _networkManager.IsHost;
            networkObject.OwnerClientId = ownerClientId;
            networkObject.ParentNetworkObjectId = parentNetworkId;
            networkObject.ParentNetworkBehaviourOrderIndex = parentNetworkBehaviourOrderIndex;
            networkObject.ParentIsUnityObject = parentIsUnityObject;
            networkObject.SpawnedOnlyOnOwner = onlySpawnForOwner;
            networkObject.IsPlayerObject = isPlayerObject;
            networkObject.NetworkMessages = this;

            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(NetworkMessages), nameof(SpawnNetworkObject))} Spawning: {networkObject.GetType().Name} [{networkObject.NetworkObjectId}]");

            NetworkObjects.Add(networkObject.NetworkObjectId, networkObject);
            AddNetworkObjectToOwnerMap(networkObject);
            if (networkObject.ShouldTick)
            {
                _tickableNetworkObjects.Add(networkObject);
            }

            networkObject.InternalInitialize();
            networkObject.OnPreSpawn();
            if (!networkObject.IsRoot)
            {
                if (networkObject.ParentIsUnityObject)
                {
                    var networkBehaviour = _networkManager.SpawnManager.SpawnedObjects[networkObject.ParentNetworkObjectId]
                        .GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex);
                    if (networkBehaviour is ISubObjectOwner subObjectOwner)
                    {
                        // subObjectOwner.AddSubObject(networkObject);
                        subObjectOwner.OnSubObjectSpawned(networkObject);
                    }
                }
                else if (NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentNetworkObject))
                {
                    // parentNetworkObject.AddSubObject(networkObject);
                    parentNetworkObject.SubObjectSpawned(networkObject);
                }
            }
            networkObject.OnSpawn();
            networkObject.OnPostSpawn();

            PublishSpawn(networkObject);
        }

        /// <summary>
        /// Sends one client the snapshot that lets it construct this object. Always addressed to a
        /// single client — who receives it is decided by interest, in <see cref="ShowTo"/>.
        /// </summary>
        private void SendSpawnMessage(VaporNetworkObject networkObject, ulong targetClientId)
        {
            var customMessagingManager = _networkManager.CustomMessagingManager;
            if (customMessagingManager == null)
            {
                return;
            }

            using var writer = new FastBufferWriter(RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, RPC_MESSAGE_MAXIMUM_SIZE);

            // Write opcode first (1 byte header)
            writer.WriteValueSafe(k_SpawnMessage);
            // Write our spawn type
            writer.WriteValueSafe(PacketRegistry.GetOpCode(networkObject.GetType()));
            // Write our message type
            writer.WriteValueSafe(networkObject.NetworkObjectId);
            // Write our owner id
            writer.WriteValueSafe(networkObject.OwnerClientId);
            // Write our parent id
            writer.WriteValueSafe(networkObject.ParentNetworkObjectId);
            writer.WriteValueSafe(networkObject.ParentNetworkBehaviourOrderIndex);
            writer.WriteValueSafe(networkObject.ParentIsUnityObject);
            // Tell if only on owner
            writer.WriteValueSafe(networkObject.SpawnedOnlyOnOwner);
            // Tell if player object
            writer.WriteValueSafe(networkObject.IsPlayerObject);
            // Create a Full Spawn Packet, This Calls Serialize on the NetworkObject
            PacketHandler.CreatePacket(writer, networkObject, true);

            customMessagingManager.SendUnnamedMessage(targetClientId, writer, k_ObjectDelivery);
        }

        private void SendDestroyMessage(ulong networkObjectId, ulong targetClientId)
        {
            var customMessagingManager = _networkManager.CustomMessagingManager;
            if (customMessagingManager == null)
            {
                return;
            }

            using var writer = new FastBufferWriter(RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, RPC_MESSAGE_MAXIMUM_SIZE);
            writer.WriteValueSafe(k_DestroyMessage);
            writer.WriteValueSafe(networkObjectId);

            customMessagingManager.SendUnnamedMessage(targetClientId, writer, k_ObjectDelivery);
        }

        /// <summary>
        /// Puts a freshly spawned object into its interest channels and sends it to whoever is already
        /// listening.
        /// </summary>
        private void PublishSpawn(VaporNetworkObject networkObject)
        {
            Interest.Register(networkObject);
            RefreshObservers(networkObject);
        }

        private void AddNetworkObjectToOwnerMap(VaporNetworkObject networkObject)
        {
            if (!_ownerClientIdToNetworkObjectMap.TryGetValue(networkObject.OwnerClientId, out var ownerNetworkObjects))
            {
                ownerNetworkObjects = new HashSet<VaporNetworkObject>();
                _ownerClientIdToNetworkObjectMap.Add(networkObject.OwnerClientId, ownerNetworkObjects);
            }

            ownerNetworkObjects.Add(networkObject);
        }

        private void RemoveNetworkObjectFromOwnerMap(VaporNetworkObject networkObject)
        {
            if(_ownerClientIdToNetworkObjectMap.TryGetValue(networkObject.OwnerClientId, out var ownerNetworkObjects))
            {
                ownerNetworkObjects.Remove(networkObject);
            }
        }

        public void DestroyNetworkObject(ulong networkId)
        {
            if (!_networkManager)
            {
                return;
            }
            
            Debug.Assert(_networkManager.IsServer, "Only the server can destroy network objects");
            if (!NetworkObjects.TryGetValue(networkId, out var networkObject))
            {
                return;
            }

            // Children first, and the parent link is severed before the parent leaves the table.
            // Removing the parent up front instead would make every child's lookup miss, and the
            // despawn callbacks would silently never fire.
            DespawnChildren(networkObject);
            NotifyParentDespawned(networkObject);

            NetworkObjects.Remove(networkId);
            RemoveNetworkObjectFromOwnerMap(networkObject);

            foreach (var clientId in _networkManager.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId && IsObserving(clientId, networkId))
                {
                    SendDestroyMessage(networkId, clientId);
                }
            }

            Interest.Unregister(networkObject);

            networkObject.OnDespawn();
            networkObject.InternalDestroy();
            _tickableNetworkObjects.Remove(networkObject);
            _networkObjectsToSend.Remove(networkObject);
        }

        private void NotifyParentDespawned(VaporNetworkObject networkObject)
        {
            if (networkObject.IsRoot)
            {
                return;
            }

            if (networkObject.ParentIsUnityObject)
            {
                if (_networkManager.SpawnManager != null &&
                    _networkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var spawnedObject) &&
                    spawnedObject.GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex) is ISubObjectOwner subObjectOwner)
                {
                    subObjectOwner.OnSubObjectDespawned(networkObject);
                }
            }
            else if (NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentNetworkObject))
            {
                parentNetworkObject.SubObjectDespawned(networkObject);
            }
        }

        /// <summary>
        /// Sends an rpc buffer to every remote target resolved from <paramref name="sendTo"/> and returns
        /// true when the local peer is itself a target and should invoke the rpc body directly. Remote
        /// sends always exclude the local client, so a host never receives its own rpc back through
        /// Netcode's unnamed-message loopback.
        /// </summary>
        internal bool SendRpc(VaporNetworkObject networkObject, FastBufferWriter writer, SendTo sendTo, NetworkDelivery networkDelivery)
        {
            var customMessagingManager = _networkManager.CustomMessagingManager;
            if (customMessagingManager == null)
            {
                return false;
            }

            var localClientId = _networkManager.LocalClientId;
            var ownerClientId = networkObject.OwnerClientId;

            if (!_networkManager.IsServer)
            {
                // Pure clients can only reach the server over unnamed messages; anything wider is a
                // server-authority send and a client asking for it is a programming error.
                switch (sendTo)
                {
                    case SendTo.Server:
                    case SendTo.Authority:
                        customMessagingManager.SendUnnamedMessage(NetworkManager.ServerClientId, writer, networkDelivery);
                        return false;
                    case SendTo.Owner:
                        if (ownerClientId == localClientId)
                        {
                            return true;
                        }

                        if (ownerClientId == NetworkManager.ServerClientId)
                        {
                            customMessagingManager.SendUnnamedMessage(NetworkManager.ServerClientId, writer, networkDelivery);
                            return false;
                        }

                        Debug.LogError($"A client cannot send an rpc to another client. {networkObject.GetType().Name} [{networkObject.NetworkObjectId}] is owned by {ownerClientId}.");
                        return false;
                    case SendTo.Me:
                        return true;
                    default:
                        Debug.LogError($"A client cannot send an rpc with {nameof(SendTo)}.{sendTo}. Only the server can target other clients.");
                        return false;
                }
            }

            bool invokeLocally;
            var targetIds = ListPool<ulong>.Get();
            switch (sendTo)
            {
                case SendTo.Owner:
                    invokeLocally = ownerClientId == localClientId;
                    if (!invokeLocally && IsObserving(ownerClientId, networkObject.NetworkObjectId))
                    {
                        targetIds.Add(ownerClientId);
                    }

                    break;
                case SendTo.NotOwner:
                    invokeLocally = ownerClientId != localClientId;
                    AddObservers(targetIds, networkObject, localClientId, ownerClientId);
                    break;
                case SendTo.Server:
                case SendTo.Authority:
                case SendTo.Me:
                    invokeLocally = true;
                    break;
                case SendTo.NotServer:
                case SendTo.NotAuthority:
                case SendTo.NotMe:
                    invokeLocally = false;
                    AddObservers(targetIds, networkObject, localClientId);
                    break;
                case SendTo.Everyone:
                    invokeLocally = true;
                    AddObservers(targetIds, networkObject, localClientId);
                    break;
                case SendTo.ClientsAndHost:
                    invokeLocally = _networkManager.IsHost;
                    AddObservers(targetIds, networkObject, localClientId);
                    break;
                case SendTo.SpecifiedInParams:
                default:
                    ListPool<ulong>.Release(targetIds);
                    throw new ArgumentOutOfRangeException(nameof(sendTo), sendTo, null);
            }

            // An owner-only object must never reach anyone but its owner, whatever the attribute asked
            // for. Only the remote target set is clamped — whether this peer runs the body is the
            // author's intent, and rewriting SendTo wholesale used to silently drop the server's own
            // execution of a Me or Everyone rpc.
            if (networkObject.SpawnedOnlyOnOwner)
            {
                targetIds.RemoveAll(id => id != ownerClientId);
            }

            if (targetIds.Count > 0)
            {
                customMessagingManager.SendUnnamedMessage(targetIds, writer, networkDelivery);
            }

            ListPool<ulong>.Release(targetIds);
            return invokeLocally;
        }

        public void SendToTarget(FastBufferWriter writer, ulong targetClientId, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced)
        {
            _networkManager.CustomMessagingManager?.SendUnnamedMessage(targetClientId, writer, networkDelivery);
        }

        public void SendToGroup(FastBufferWriter writer, IReadOnlyList<ulong> clientIds, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced)
        {
            _networkManager.CustomMessagingManager?.SendUnnamedMessage(clientIds, writer, networkDelivery);
        }

        private void ReceiveMessage(ulong clientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte opCode);
            switch (opCode)
            {
                case k_SpawnMessage:
                {
                    // Create Network Object From Type Code
                    reader.ReadValueSafe(out uint typeCode);
                    if (!PacketRegistry.TryCreate(typeCode, out var created))
                    {
                        // Same shape as the unknown-rpc case: a type this build has never heard of means
                        // the peers disagree, and there is no way to read the rest of this packet.
                        Debug.LogWarning($"Received a spawn for unknown type code [{typeCode:X8}]. The peers are likely running different builds.");
                        reader.Seek(reader.Length);
                        return;
                    }

                    if (created is not VaporNetworkObject networkObject)
                    {
                        Debug.LogError($"Spawn opcode [{typeCode:X8}] is {created.GetType().Name}, which is not a {nameof(VaporNetworkObject)}.");
                        reader.Seek(reader.Length);
                        return;
                    }

                    // Set Network Id
                    reader.ReadValueSafe(out ulong networkId);
                    reader.ReadValueSafe(out ulong ownerId);
                    reader.ReadValueSafe(out ulong parentNetworkId);
                    reader.ReadValueSafe(out ushort parentNetworkBehaviourOrderIndex);
                    reader.ReadValueSafe(out bool parentIsUnityObject);
                    reader.ReadValueSafe(out bool spawnOnlyOnOwner);
                    reader.ReadValueSafe(out bool isPlayerObject);
                    networkObject.NetworkObjectId = networkId;
                    networkObject.IsServer = _networkManager.IsServer;
                    networkObject.IsClient = _networkManager.IsClient;
                    networkObject.IsHost = _networkManager.IsHost;
                    networkObject.OwnerClientId = ownerId;
                    networkObject.ParentNetworkObjectId = parentNetworkId;
                    networkObject.ParentNetworkBehaviourOrderIndex = parentNetworkBehaviourOrderIndex;
                    networkObject.ParentIsUnityObject = parentIsUnityObject;
                    networkObject.SpawnedOnlyOnOwner = spawnOnlyOnOwner;
                    networkObject.IsPlayerObject = isPlayerObject;
                    networkObject.NetworkMessages = this;
                    
                    if (!NetworkObjects.TryAdd(networkId, networkObject))
                    {
                        // Already spawned here. Throwing on the duplicate would take the whole message
                        // pump down over what is only a redundant send.
                        Debug.LogWarning($"Received a duplicate spawn for {networkObject.GetType().Name} [{networkId}]; ignoring it.");
                        return;
                    }

                    AddNetworkObjectToOwnerMap(networkObject);
                    if (networkObject.ShouldTick)
                    {
                        _tickableNetworkObjects.Add(networkObject);
                    }
                    
                    networkObject.InternalInitialize();
                    networkObject.OnPreSpawn();
                    
                    // Deserialize Full Packet, straight out of the message rather than through a copy.
                    PacketHandler.FromPacketInto(reader, networkObject);
                    if (!networkObject.IsRoot)
                    {
                        if (networkObject.ParentIsUnityObject)
                        {
                            var networkBehaviour = _networkManager.SpawnManager.SpawnedObjects[networkObject.ParentNetworkObjectId]
                                .GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex);
                            if (networkBehaviour is ISubObjectOwner subObjectOwner)
                            {
                                subObjectOwner.OnSubObjectSpawned(networkObject);
                            }
                            
                        }
                        else if (NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentNetworkObject))
                        {
                            parentNetworkObject.SubObjectSpawned(networkObject);
                        }
                    }

                    networkObject.OnSpawn();
                    networkObject.OnPostSpawn();
                    break;
                }
                case k_DestroyMessage:
                {
                    reader.ReadValueSafe(out ulong networkId);
                    if (NetworkObjects.Remove(networkId, out var networkObject))
                    {
                        RemoveNetworkObjectFromOwnerMap(networkObject);

                        if (!networkObject.IsRoot)
                        {
                            if (networkObject.ParentIsUnityObject)
                            {
                                var networkBehaviour = _networkManager.SpawnManager.SpawnedObjects[networkObject.ParentNetworkObjectId]
                                    .GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex);
                                if (networkBehaviour is ISubObjectOwner subObjectOwner)
                                {
                                    subObjectOwner.OnSubObjectDespawned(networkObject);
                                }
                            }
                            else if (NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentNetworkObject))
                            {
                                parentNetworkObject.SubObjectDespawned(networkObject);
                            }
                        }

                        networkObject.OnDespawn();
                        networkObject.InternalDestroy();
                    }

                    _tickableNetworkObjects.Remove(networkObject);
                    break;
                }
                case k_SyncBatchMessage:
                {
                    int entryCount = SyncBatchCodec.ReadCount(reader);
                    for (int i = 0; i < entryCount; i++)
                    {
                        var networkId = SyncBatchCodec.ReadEntry(reader, out var payload, Allocator.Temp);

                        // An object this client does not have costs it one entry, not the rest of the
                        // message — the length prefix is what makes that recoverable. It happens
                        // legitimately whenever a despawn and a sync cross on the wire.
                        if (NetworkObjects.TryGetValue(networkId, out var networkObject))
                        {
                            using var payloadReader = new FastBufferReader(payload, Allocator.Temp);
                            networkObject.Deserialize(payloadReader, out _);
                        }

                        payload.Dispose();
                    }

                    break;
                }
                case RPC_MESSAGE:
                {
                    reader.ReadValueSafe(out ulong networkId);
                    if (!NetworkObjects.TryGetValue(networkId, out var networkObject))
                    {
                        reader.Seek(reader.Length);
                        reader.Dispose();
                        return;
                    }
                    
                    networkObject.OnMessageReceived(reader);
                    break;
                }
                default:
                {
                    reader.Seek(reader.Length);
                    reader.Dispose();
                    return;
                }
            }
        }

        public bool TryGetReference<T>(ulong networkId, out T reference) where T : VaporNetworkObject
        {
            if (NetworkObjects.TryGetValue(networkId, out var netObj) && netObj is T typedNetObj)
            {
                reference = typedNetObj;
                return true;
            }

            reference = null;
            return false;
        }

        internal void QueueForSend(VaporNetworkObject vaporNetworkObject)
        {
            _networkObjectsToSend.Add(vaporNetworkObject);
        }

        // public void DespawnChildren(ulong ownerClientId, ulong networkObjectId)
        // {
        //     if (_ownerClientIdToNetworkObjectMap.TryGetValue(ownerClientId, out var networkObjects))
        //     {
        //         var children = ListPool<ulong>.Get();
        //         foreach (var networkObject in networkObjects)
        //         {
        //             if (networkObject.ParentNetworkObjectId == networkObjectId)
        //             {
        //                 children.Add(networkObject.NetworkObjectId);
        //             }
        //         }
        //
        //         foreach (var childNetworkObjectId in children)
        //         {
        //             DestroyNetworkObject(childNetworkObjectId);
        //         }
        //
        //         ListPool<ulong>.Release(children);
        //     }
        // }

        private void DespawnChildren(VaporNetworkObject networkObject)
        {
            if (networkObject.SubObjects == null || networkObject.SubObjects.Count == 0)
            {
                return;
            }

            // Copied first: each child unregisters itself from this very list on its way out.
            var children = ListPool<VaporNetworkObject>.Get();
            children.AddRange(networkObject.SubObjects);
            foreach (var child in children)
            {
                DestroyNetworkObject(child.NetworkObjectId);
            }

            ListPool<VaporNetworkObject>.Release(children);
        }
    }
}
