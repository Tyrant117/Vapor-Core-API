using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// Turns a world's spawns, despawns, ownership changes, dirty state and rpcs into records on the
    /// session, batched per client and per delivery class, and applies the same records on the way
    /// in. Relevance is spawn scope: a client that is not meant to see an object never receives it,
    /// and receives a despawn the moment it stops being meant to.
    /// </summary>
    /// <remarks>
    /// Ordering rules, which are the whole reason this class exists:
    /// <list type="bullet">
    /// <item>All state-carrying traffic — spawn, despawn, ownership, sync, reliable rpcs — rides one
    /// reliable-sequenced stream per client, so nothing can overtake the spawn it depends on.</item>
    /// <item>Spawns, despawns, ownership changes and rpcs are recorded the moment they happen; dirty
    /// state is written once per tick and fanned out to every client that knows the object.</item>
    /// <item>A client's known set is the authority on what may be addressed to it.</item>
    /// </list>
    /// Backpressure is ordinary: a batch the transport cannot take stays queued and goes with the next
    /// tick, in order.
    /// </remarks>
    internal sealed class Replicator : IDisposable
    {
        internal enum MessageType : byte { Spawn = 1, Despawn = 2, Ownership = 3, Sync = 4, Rpc = 5, Snapshot = 6, ClientSnapshot = 7 }

        private const int k_RecordHeader = 3;   // u16 length + u8 type
        private const int k_MaxRecordPayload = ushort.MaxValue - 1;

        private sealed class OutboundBatch
        {
            public readonly Delivery Delivery;
            public readonly NetworkWriter Writer = new(1024, 4 << 20);
            public readonly List<int> RecordEnds = new();

            public OutboundBatch(Delivery delivery) => Delivery = delivery;
        }

        private sealed class Peer
        {
            public ulong ClientId;
            public readonly HashSet<ulong> Known = new();
            public readonly HashSet<InterestGroup> Subscriptions = new();
            /// <summary>Objects this client is kept aware of whatever the channels and the grid say.</summary>
            public readonly HashSet<ulong> Pinned = new();
            /// <summary>Snapshot scheduling: how many sends each object is "owed" for this client.</summary>
            public readonly Dictionary<ulong, float> SnapshotDue = new();
            public readonly OutboundBatch Reliable = new(Delivery.ReliableFragmentedSequenced);
            public readonly OutboundBatch Unreliable = new(Delivery.Unreliable);
            public readonly OutboundBatch UnreliableSequenced = new(Delivery.UnreliableSequenced);

            public OutboundBatch For(Delivery delivery) => delivery switch
            {
                Delivery.Unreliable => Unreliable,
                Delivery.UnreliableSequenced => UnreliableSequenced,
                _ => Reliable,
            };
        }

        private readonly NetworkWorld _world;
        private readonly NetworkSession _session;
        private readonly Dictionary<ulong, Peer> _peers = new();
        private readonly List<Peer> _peerList = new();
        private readonly Peer _serverPeer = new() { ClientId = NetworkSession.ServerClientId };   // client side: the server
        private readonly NetworkWriter _stateScratch = new(4096, 4 << 20);
        private readonly List<NetworkWriter> _rpcWriters = new();
        private readonly NetworkReader _recordReader = new();
        private readonly List<Peer> _peerScratch = new();
        private readonly List<(VaporNetworkObject obj, float due)> _snapshotCandidates = new();
        private readonly List<ulong> _spatialCandidateIds = new();
        private readonly List<ulong> _knownIdScratch = new();
        private readonly Dictionary<ulong, float> _ownerSnapshotDue = new();
        private int _rpcDepth;
        private bool _bound;

        public Replicator(NetworkWorld world, NetworkSession session)
        {
            _world = world;
            _session = session;
            Interest = new InterestManager(this);
        }

        public InterestManager Interest { get; }

        private bool IsServer => _session.IsServer;

        #region - Session binding -

        public void Bind()
        {
            if (_bound) return;
            _bound = true;
            _session.ClientConnected += OnClientConnected;
            _session.ClientDisconnected += OnClientDisconnected;
            _session.Data += OnData;

            // Clients that connected before the world bound still need their initial spawns.
            if (IsServer)
            {
                foreach (var clientId in _session.ConnectedClientIds)
                {
                    if (!_peers.ContainsKey(clientId)) OnClientConnected(clientId);
                }
            }
        }

        public void Unbind()
        {
            if (!_bound) return;
            _bound = false;
            _session.ClientConnected -= OnClientConnected;
            _session.ClientDisconnected -= OnClientDisconnected;
            _session.Data -= OnData;
        }

        public void Dispose() => Unbind();

        /// <summary>
        /// A host's local player is not a peer. It already holds the objects the server holds — there
        /// is nothing to spawn for it, nothing to snapshot to it, and no interest to compute: it sees
        /// the world the server sees.
        /// </summary>
        private bool IsLocalPlayer(ulong clientId) => _session.IsHost && clientId == _session.LocalPlayerClientId;

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer || IsLocalPlayer(clientId) || _peers.ContainsKey(clientId)) return;
            var peer = new Peer { ClientId = clientId };
            _peers.Add(clientId, peer);
            _peerList.Add(peer);
            RelevancePass(peer);
        }

        private void OnClientDisconnected(ulong clientId, SessionDisconnectReason reason)
        {
            if (!IsServer) return;

            // The local player has no peer to tear down, but it is still a player leaving — and that
            // is the only moment anything gets to save it.
            if (IsLocalPlayer(clientId))
            {
                _world.HandleClientLeaving(clientId);
                return;
            }

            if (!_peers.TryGetValue(clientId, out var peer)) return;
            _world.HandleClientLeaving(clientId);
            _peers.Remove(clientId);
            _peerList.Remove(peer);
        }

        #endregion

        #region - Relevance -

        public bool Subscribe(ulong clientId, InterestGroup group)
        {
            if (!IsServer || group.IsNone || !_peers.TryGetValue(clientId, out var peer)) return false;
            if (!peer.Subscriptions.Add(group)) return false;
            RelevancePass(peer);
            return true;
        }

        public bool Unsubscribe(ulong clientId, InterestGroup group)
        {
            if (!IsServer || !_peers.TryGetValue(clientId, out var peer)) return false;
            if (!peer.Subscriptions.Remove(group)) return false;
            RelevancePass(peer);
            return true;
        }

        public bool IsSubscribed(ulong clientId, InterestGroup group) =>
            _peers.TryGetValue(clientId, out var peer) && peer.Subscriptions.Contains(group);

        /// <summary>
        /// Keeps one object relevant to one client for as long as the pin lasts, whatever the channels
        /// and the spatial grid would say. For something the client is being made to look at from
        /// outside its own interest — a view target, a cutscene subject.
        /// </summary>
        public bool Pin(ulong clientId, ulong networkObjectId)
        {
            if (!IsServer || !_peers.TryGetValue(clientId, out var peer)) return false;
            if (!peer.Pinned.Add(networkObjectId)) return false;
            RelevancePass(peer);
            return true;
        }

        /// <summary>Drops a pin. The object goes back to whatever the channels and the grid say about it, which may mean leaving.</summary>
        public bool Unpin(ulong clientId, ulong networkObjectId)
        {
            if (!IsServer || !_peers.TryGetValue(clientId, out var peer)) return false;
            if (!peer.Pinned.Remove(networkObjectId)) return false;
            RelevancePass(peer);
            return true;
        }

        public bool IsPinned(ulong clientId, ulong networkObjectId) =>
            _peers.TryGetValue(clientId, out var peer) && peer.Pinned.Contains(networkObjectId);

        public bool IsObserving(ulong clientId, ulong networkObjectId) =>
            _peers.TryGetValue(clientId, out var peer) && peer.Known.Contains(networkObjectId);

        /// <summary>
        /// An owner-only object concerns its owner alone. Otherwise a spatial object needs the client
        /// within range or subscribed to one of its channels; a non-spatial object with channels needs
        /// a subscription; an object with neither is global.
        /// </summary>
        private bool IsRelevantTo(VaporNetworkObject networkObject, Peer peer)
        {
            if (networkObject.SpawnedOnlyOnOwner)
            {
                return networkObject.OwnerClientId == peer.ClientId;
            }

            // A pin outranks the channels and the grid, but not the owner-only rule above: something the
            // client is being made to look at has to arrive even from outside its interest.
            if (peer.Pinned.Contains(networkObject.NetworkObjectId))
            {
                return true;
            }

            bool inChannel = false;
            var groups = networkObject.InterestGroups;
            foreach (var group in groups)
            {
                if (!peer.Subscriptions.Contains(group))
                {
                    continue;
                }

                inChannel = true;
                break;
            }

            if (!networkObject.UsesSpatialRelevance)
            {
                return groups.Count == 0 || inChannel;
            }

            var spatial = Interest.Spatial;
            if (spatial == null)
            {
                return true;   // no provider yet: behave as global
            }

            return inChannel || spatial.IsRelevant(networkObject, peer.ClientId, peer.Known.Contains(networkObject.NetworkObjectId));

        }

        /// <summary>Visible = relevant, and every ancestor known to the client already.</summary>
        private bool IsVisibleTo(VaporNetworkObject networkObject, Peer peer)
        {
            if (!IsRelevantTo(networkObject, peer)) return false;
            var parent = networkObject.Parent;
            return parent == null || peer.Known.Contains(parent.NetworkObjectId);
        }

        /// <summary>Brings one client's known set in line with what it should see, root by root.</summary>
        private void RelevancePass(Peer peer)
        {
            var roots = _world.Roots;
            foreach (var root in roots)
            {
                VisitForRelevance(peer, root);
            }
        }

        private void VisitForRelevance(Peer peer, VaporNetworkObject networkObject)
        {
            bool relevant = IsRelevantTo(networkObject, peer);
            bool known = peer.Known.Contains(networkObject.NetworkObjectId);

            if (relevant && !known)
            {
                if (!SendSpawn(peer, networkObject))
                {
                    return;
                }
            }
            else if (!relevant && known)
            {
                SendDespawn(peer, networkObject);
                return;   // descendants went with it
            }

            if (!relevant)
            {
                return;
            }

            var subs = networkObject.SubObjects;
            foreach (var sub in subs)
            {
                VisitForRelevance(peer, sub);
            }
        }

        public void RefreshRelevance(VaporNetworkObject networkObject)
        {
            if (!IsServer || networkObject == null || !networkObject.IsSpawned) return;
            _peerScratch.Clear();
            _peerScratch.AddRange(_peerList);
            foreach (var peer in _peerScratch)
            {
                bool parentKnown = networkObject.Parent == null || peer.Known.Contains(networkObject.Parent.NetworkObjectId);
                if (!parentKnown) continue;
                VisitForRelevance(peer, networkObject);
            }
        }

        public void RefreshSpatialRelevance()
        {
            if (!IsServer) return;
            _peerScratch.Clear();
            _peerScratch.AddRange(_peerList);
            var candidates = Interest.Spatial as ISpatialRelevanceCandidates;
            foreach (var peer in _peerScratch)
            {
                if (candidates == null)
                {
                    RelevancePass(peer);
                }
                else
                {
                    RefreshSpatialRelevance(peer, candidates);
                }
            }
        }

        private void RefreshSpatialRelevance(Peer peer, ISpatialRelevanceCandidates candidates)
        {
            // First retire spatial objects that left the candidate area. Iterate a snapshot because
            // despawning a parent also removes all of its descendants from Known.
            _knownIdScratch.Clear();
            _knownIdScratch.AddRange(peer.Known);
            foreach (var id in _knownIdScratch)
            {
                if (!peer.Known.Contains(id) || !_world.TryGet(id, out var networkObject)
                    || !networkObject.UsesSpatialRelevance || IsRelevantTo(networkObject, peer))
                {
                    continue;
                }

                SendDespawn(peer, networkObject);
            }

            // Then discover only objects in nearby cells. The final point query applies subscriptions,
            // owner-only scope, and the smaller non-hysteresis radius for newly observed objects.
            _spatialCandidateIds.Clear();
            candidates.CollectPotentiallyRelevant(peer.ClientId, _spatialCandidateIds);
            foreach (var id in _spatialCandidateIds)
            {
                if (!_world.TryGet(id, out var networkObject) || !networkObject.UsesSpatialRelevance)
                {
                    continue;
                }

                var parent = networkObject.Parent;
                if (parent != null && !peer.Known.Contains(parent.NetworkObjectId))
                {
                    continue;
                }

                VisitForRelevance(peer, networkObject);
            }
        }

        #endregion

        #region - World events (authority) -

        public void OnSpawned(VaporNetworkObject networkObject)
        {
            if (!IsServer) return;
            _peerScratch.Clear();
            _peerScratch.AddRange(_peerList);
            foreach (var peer in _peerScratch)
            {
                if (IsVisibleTo(networkObject, peer))
                {
                    // The object and whatever it already carries: sub-objects spawned inside its own
                    // OnSpawn arrived here before it did, were not visible then (their parent was not
                    // known), and would otherwise wait for the next relevance pass.
                    VisitForRelevance(peer, networkObject);
                }
            }
        }

        public void OnDespawning(VaporNetworkObject networkObject)
        {
            if (!IsServer) return;
            ulong id = networkObject.NetworkObjectId;
            foreach (var peer in _peerList)
            {
                peer.SnapshotDue.Remove(id);

                // A pin on a despawned object would outlive it and then apply to whatever reuses the id.
                peer.Pinned.Remove(id);
                if (peer.Known.Remove(id))
                {
                    _stateScratch.Reset();
                    _stateScratch.WriteVarUInt64(id);
                    AppendRecord(peer.Reliable, MessageType.Despawn, _stateScratch.WrittenSpan);
                }
            }
        }

        public void OnOwnershipChanged(VaporNetworkObject networkObject, ulong previousOwner)
        {
            if (!IsServer) return;
            ulong id = networkObject.NetworkObjectId;
            _stateScratch.Reset();
            _stateScratch.WriteVarUInt64(id);
            _stateScratch.WriteVarUInt64(networkObject.OwnerClientId);
            foreach (var peer in _peerList)
            {
                if (peer.Known.Contains(id))
                {
                    AppendRecord(peer.Reliable, MessageType.Ownership, _stateScratch.WrittenSpan);
                }
            }

            // Owner-only objects change hands: the old owner loses it, the new one gains it.
            if (networkObject.SpawnedOnlyOnOwner)
            {
                RefreshRelevance(networkObject);
            }
        }

        public void OnInterestChanged(VaporNetworkObject networkObject) => RefreshRelevance(networkObject);

        #endregion

        #region - Spawn / despawn records -

        private bool SendSpawn(Peer peer, VaporNetworkObject networkObject)
        {
            try
            {
                _stateScratch.Reset();
                _stateScratch.WriteVarUInt64(networkObject.NetworkObjectId);
                _stateScratch.WriteVarUInt32(NetworkTypeRegistry.TagOf(networkObject.GetType()));
                _stateScratch.WriteVarUInt64(networkObject.OwnerClientId);
                byte flags = 0;
                if (networkObject.SpawnedOnlyOnOwner) flags |= 1;
                if (networkObject.IsPlayerObject) flags |= 2;
                _stateScratch.WriteByte(flags);
                _stateScratch.WriteVarUInt64(networkObject.ParentNetworkObjectId);
                networkObject.WriteSpawnDataInternal(_stateScratch);
                networkObject.WriteObjectState(_stateScratch, full: true);
            }
            catch (NetworkSerializationException e)
            {
                Debug.LogError($"Could not serialize the spawn for {networkObject}: {e.Message}");
                _session.DisconnectClient(peer.ClientId, SessionDisconnectReason.TransportError);
                return false;
            }

            if (!AppendRecord(peer.Reliable, MessageType.Spawn, _stateScratch.WrittenSpan))
            {
                // This peer can never become consistent without the spawn. Fail it explicitly instead
                // of claiming it knows the object and silently dropping all future state for it.
                _session.DisconnectClient(peer.ClientId, SessionDisconnectReason.TransportError);
                return false;
            }

            peer.Known.Add(networkObject.NetworkObjectId);
            return true;
        }

        private void SendDespawn(Peer peer, VaporNetworkObject networkObject)
        {
            ForgetRecursive(peer, networkObject);
            _stateScratch.Reset();
            _stateScratch.WriteVarUInt64(networkObject.NetworkObjectId);
            AppendRecord(peer.Reliable, MessageType.Despawn, _stateScratch.WrittenSpan);
        }

        private static void ForgetRecursive(Peer peer, VaporNetworkObject networkObject)
        {
            peer.Known.Remove(networkObject.NetworkObjectId);
            peer.SnapshotDue.Remove(networkObject.NetworkObjectId);
            var subs = networkObject.SubObjects;
            foreach (var sub in subs)
            {
                ForgetRecursive(peer, sub);
            }
        }

        #endregion

        #region - Tick -

        public void EndOfTick(uint tick)
        {
            double dt = _session.TickInterval;
            if (IsServer)
            {
                DirtyPass();
                foreach (var peer in _peerList)
                {
                    SnapshotPass(peer, tick, dt);
                    Flush(peer);
                }
            }
            else if (_session.IsConnected)
            {
                OwnerSnapshotPass(tick, dt);
                Flush(_serverPeer);
            }
        }

        #endregion

        #region - Snapshot channel -

        /// <summary>
        /// Server: for one client, accrue each known snapshot object's send debt at its rate × LOD ×
        /// priority, then pay the largest debts first until the byte budget is spent. Objects the
        /// client itself is authoritative for (owner-written) are skipped — their truth flows the
        /// other way.
        /// </summary>
        private void SnapshotPass(Peer peer, uint tick, double dt)
        {
            var due = peer.SnapshotDue;
            var lod = Interest.Lod;
            _snapshotCandidates.Clear();

            foreach (var id in peer.Known)
            {
                if (!_world.TryGet(id, out var networkObject) || !networkObject.HasSnapshotChannel) continue;
                if (!networkObject.HasSnapshotToSendInternal(peer.ClientId, networkObject.OwnerClientId == peer.ClientId)) continue;

                float scale = lod?.RateScale(networkObject, peer.ClientId) ?? 1f;
                if (scale <= 0f) continue;

                due.TryGetValue(id, out float owed);
                owed += (float)(networkObject.SnapshotRateHz * scale * dt) * Math.Max(0.01f, networkObject.SnapshotPriority);
                if (owed > 4f) owed = 4f;   // a starved object catches up, it does not burst
                due[id] = owed;
                if (owed >= 1f)
                {
                    _snapshotCandidates.Add((networkObject, owed));
                }
            }

            if (_snapshotCandidates.Count == 0) return;
            _snapshotCandidates.Sort(static (a, b) => b.due.CompareTo(a.due));

            int budget = Interest.SnapshotBudgetBytesPerTick > 0 ? Interest.SnapshotBudgetBytesPerTick : _session.MaxPayload(Delivery.UnreliableSequenced) * 2;
            int spent = 0;
            foreach (var (networkObject, owed) in _snapshotCandidates)
            {
                _stateScratch.Reset();
                _stateScratch.WriteVarUInt64(networkObject.NetworkObjectId);
                _stateScratch.WriteUInt32(tick);
                networkObject.WriteSnapshotInternal(_stateScratch, tick, peer.ClientId);
                int size = _stateScratch.Length + k_RecordHeader;
                if (spent + size > budget && spent > 0)
                {
                    break;   // the rest keep their debt and go first next tick
                }

                AppendRecord(peer.UnreliableSequenced, MessageType.Snapshot, _stateScratch.WrittenSpan);
                spent += size;
                due[networkObject.NetworkObjectId] = owed - 1f;
            }
        }

        /// <summary>Client: owner-authoritative objects push their snapshots to the server at their authored rate.</summary>
        private void OwnerSnapshotPass(uint tick, double dt)
        {
            ulong me = _session.LocalClientId;
            var objects = _world.Objects;
            foreach (var networkObject in objects)
            {
                if (!networkObject.HasSnapshotChannel || !networkObject.OwnerWritesSnapshots || networkObject.OwnerClientId != me)
                {
                    continue;
                }

                _ownerSnapshotDue.TryGetValue(networkObject.NetworkObjectId, out float owed);
                owed += (float)(networkObject.SnapshotRateHz * dt);
                if (owed > 2f) owed = 2f;
                if (owed >= 1f && networkObject.HasSnapshotToSendInternal(NetworkSession.ServerClientId, isOwner: false))
                {
                    owed -= 1f;
                    _stateScratch.Reset();
                    _stateScratch.WriteVarUInt64(networkObject.NetworkObjectId);
                    _stateScratch.WriteUInt32(tick);
                    networkObject.WriteSnapshotInternal(_stateScratch, tick, NetworkSession.ServerClientId);
                    AppendRecord(_serverPeer.UnreliableSequenced, MessageType.ClientSnapshot, _stateScratch.WrittenSpan);
                }

                _ownerSnapshotDue[networkObject.NetworkObjectId] = owed;
            }
        }

        private void DirtyPass()
        {
            var dirty = _world.DirtyObjects;
            if (dirty.Count == 0) return;

            foreach (var networkObject in dirty)
            {
                if (!networkObject.IsSpawned)
                {
                    continue;
                }

                if (networkObject.IsDirty)
                {
                    ulong id = networkObject.NetworkObjectId;
                    _stateScratch.Reset();
                    _stateScratch.WriteVarUInt64(id);
                    networkObject.WriteObjectState(_stateScratch, full: false);
                    var span = _stateScratch.WrittenSpan;
                    foreach (var peer in _peerList)
                    {
                        if (peer.Known.Contains(id))
                        {
                            AppendRecord(peer.Reliable, MessageType.Sync, span);
                        }
                    }
                }

                networkObject.ClearDirty();
            }

            dirty.Clear();
        }

        #endregion

        #region - Rpc -

        public bool BeginRpc(IRpcHost host, uint hash, out NetworkWriter writer)
        {
            if (_rpcDepth >= _rpcWriters.Count)
            {
                _rpcWriters.Add(new NetworkWriter(512));
            }

            writer = _rpcWriters[_rpcDepth++];
            writer.Reset();
            writer.ReserveByte();   // target, patched in EndRpc
            writer.WriteVarUInt64(host.RpcObject.NetworkObjectId);
            writer.WriteVarUInt32(host.RpcComponentId);
            writer.WriteUInt32(hash);
            return true;
        }

        public bool EndRpc(IRpcHost host, NetworkWriter writer, RpcTarget target, Delivery delivery)
        {
            _rpcDepth--;
            writer.PatchByte(0, (byte)target);
            var payload = writer.WrittenSpan;
            var networkObject = host.RpcObject;
            bool ownerIsServer = networkObject.IsOwnedByServer;

            if (IsServer)
            {
                return RouteFromServer(networkObject, target, delivery, payload, excludeClient: NetworkSession.InvalidClientId, ownerIsServer);
            }

            // Client: everything but Me and a self-addressed Owner goes to the server, which proxies.
            bool isOwner = networkObject.IsOwner;
            bool runLocally;
            bool send;
            switch (target)
            {
                case RpcTarget.Server:   runLocally = false;    send = true; break;
                case RpcTarget.Owner:    runLocally = isOwner;  send = !isOwner; break;
                case RpcTarget.NotOwner: runLocally = !isOwner; send = true; break;
                case RpcTarget.NotServer: runLocally = true;    send = true; break;
                case RpcTarget.Everyone: runLocally = true;     send = true; break;
                case RpcTarget.Me:       runLocally = true;     send = false; break;
                case RpcTarget.NotMe:    runLocally = false;    send = true; break;
                default:                 runLocally = false;    send = false; break;
            }

            if (send && _session.IsConnected)
            {
                AppendRecord(_serverPeer.For(delivery), MessageType.Rpc, payload);
            }

            return runLocally;
        }

        /// <summary>Server-side fan-out for an rpc originating here (<paramref name="excludeClient"/> invalid) or proxied for a client.</summary>
        private bool RouteFromServer(VaporNetworkObject networkObject, RpcTarget target, Delivery delivery, ReadOnlySpan<byte> payload, ulong excludeClient, bool ownerIsServer)
        {
            ulong id = networkObject.NetworkObjectId;
            ulong owner = networkObject.OwnerClientId;

            switch (target)
            {
                case RpcTarget.Server:
                    return true;

                case RpcTarget.Me:
                    return excludeClient == NetworkSession.InvalidClientId;   // "me" is the server only when the server sent it

                case RpcTarget.Owner:
                    // A host owning it as its player is as local as the server owning it.
                    if (ownerIsServer || IsLocalPlayer(owner)) return true;
                    if (owner != excludeClient && _peers.TryGetValue(owner, out var ownerPeer) && ownerPeer.Known.Contains(id))
                    {
                        AppendRecord(ownerPeer.For(delivery), MessageType.Rpc, payload);
                    }
                    return false;

                case RpcTarget.NotOwner:
                    // A host wears both hats, and only one of them can be the owner: whichever is not
                    // is a recipient, so this always runs there.
                    var runLocally = _session.IsHost || !ownerIsServer;
                    foreach (var peer in _peerList)
                    {
                        if (peer.ClientId == owner || peer.ClientId == excludeClient || !peer.Known.Contains(id)) continue;
                        AppendRecord(peer.For(delivery), MessageType.Rpc, payload);
                    }
                    return runLocally;

                case RpcTarget.NotServer:
                    foreach (var peer in _peerList)
                    {
                        if (peer.ClientId == excludeClient || !peer.Known.Contains(id)) continue;
                        AppendRecord(peer.For(delivery), MessageType.Rpc, payload);
                    }
                    // Not the server — but a host's player is a client, and it is in this process.
                    return _session.IsHost;

                case RpcTarget.Everyone:
                case RpcTarget.NotMe:
                    foreach (var peer in _peerList)
                    {
                        if (peer.ClientId == excludeClient || !peer.Known.Contains(id)) continue;
                        AppendRecord(peer.For(delivery), MessageType.Rpc, payload);
                    }
                    // Everyone includes the server; NotMe from the server excludes it, but NotMe proxied for a client includes it.
                    return target == RpcTarget.Everyone || excludeClient != NetworkSession.InvalidClientId;

                default:
                    return false;
            }
        }

        #endregion

        #region - Records and batching -

        private bool AppendRecord(OutboundBatch batch, MessageType type, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > k_MaxRecordPayload)
            {
                Debug.LogError($"A {type} record of {payload.Length} bytes exceeds the {k_MaxRecordPayload}-byte record limit and was dropped.");
                return false;
            }

            int recordSize = payload.Length + k_RecordHeader;
            int maxPayload = _session.MaxPayload(batch.Delivery);
            if (recordSize > maxPayload)
            {
                Debug.LogError($"A {recordSize}-byte {type} record exceeds the {maxPayload}-byte payload limit for {batch.Delivery} and was dropped.");
                return false;
            }

            var w = batch.Writer;
            if (!batch.Delivery.IsReliable() && recordSize > w.MaxCapacity - w.Length)
            {
                // Unreliable data has no ordering guarantee to preserve. Drop stale queued records
                // rather than growing until the writer throws on a later tick.
                w.Reset();
                batch.RecordEnds.Clear();
            }

            w.WriteUInt16((ushort)(payload.Length + 1));
            w.WriteByte((byte)type);
            w.WriteBytes(payload);
            batch.RecordEnds.Add(w.Length);
            return true;
        }

        private void Flush(Peer peer)
        {
            Flush(peer, peer.Reliable);
            Flush(peer, peer.UnreliableSequenced);
            Flush(peer, peer.Unreliable);
        }

        private void Flush(Peer peer, OutboundBatch batch)
        {
            var w = batch.Writer;
            if (w.Length == 0)
            {
                batch.RecordEnds.Clear();
                return;
            }

            int maxPayload = _session.MaxPayload(batch.Delivery);
            var ends = batch.RecordEnds;
            int sentUpTo = 0;
            int i = 0;
            while (i < ends.Count)
            {
                int packetStart = sentUpTo;
                int j = i;
                int packetEnd = packetStart;
                while (j < ends.Count && ends[j] - packetStart <= maxPayload)
                {
                    packetEnd = ends[j];
                    j++;
                }

                if (j == i)
                {
                    // One record larger than the transport allows for this delivery: nothing to do but drop it.
                    Debug.LogError($"A {ends[i] - packetStart}-byte record exceeds the {maxPayload}-byte payload limit for {batch.Delivery} and was dropped.");
                    sentUpTo = ends[i];
                    i++;
                    continue;
                }

                var result = IsServer
                    ? _session.Send(peer.ClientId, batch.Delivery, new ReadOnlySpan<byte>(w.Buffer, packetStart, packetEnd - packetStart))
                    : _session.SendToServer(batch.Delivery, new ReadOnlySpan<byte>(w.Buffer, packetStart, packetEnd - packetStart));

                if (result == SendResult.Ok)
                {
                    sentUpTo = packetEnd;
                    i = j;
                    continue;
                }

                if (result == SendResult.QueueFull)
                {
                    if (batch.Delivery.IsReliable())
                    {
                        break;   // reliable records must stay queued, in order
                    }

                    // Unreliable and latest-wins traffic is allowed to disappear. Retaining it would
                    // replay stale snapshots and grow the per-peer buffer under sustained pressure.
                    sentUpTo = w.Length;
                    i = ends.Count;
                    break;
                }

                // Disconnected / not started: the peer is gone; nothing more will ever be sent.
                sentUpTo = w.Length;
                i = ends.Count;
            }

            if (sentUpTo > 0)
            {
                w.DiscardPrefix(sentUpTo);
                ends.RemoveRange(0, i);
                for (int k = 0; k < ends.Count; k++)
                {
                    ends[k] -= sentUpTo;
                }
            }
        }

        #endregion

        #region - Receive -

        private void OnData(ulong senderClientId, Delivery delivery, NetworkReader reader)
        {
            var buffer = reader.Buffer;
            int baseOffset = reader.Offset;
            while (reader.Remaining >= k_RecordHeader)
            {
                int length = reader.ReadUInt16();
                if (length < 1 || length > reader.Remaining)
                {
                    Debug.LogWarning($"Malformed replication record from client {senderClientId}; dropping the rest of the packet.");
                    return;
                }

                var type = (MessageType)reader.ReadByte();
                int payloadStart = baseOffset + reader.Position;
                int payloadLength = length - 1;
                _recordReader.SetSource(buffer, payloadStart, payloadLength);

                try
                {
                    Dispatch(senderClientId, delivery, type, _recordReader);
                }
                catch (NetworkSerializationException e)
                {
                    Debug.LogWarning($"Failed to read a {type} record from client {senderClientId}: {e.Message}");
                }

                reader.Seek(reader.Position + payloadLength);
            }
        }

        private void Dispatch(ulong senderClientId, Delivery delivery, MessageType type, NetworkReader record)
        {
            if (IsServer)
            {
                switch (type)
                {
                    case MessageType.Rpc:
                        HandleClientRpc(senderClientId, delivery, record);
                        break;

                    case MessageType.ClientSnapshot:
                    {
                        ulong id = record.ReadVarUInt64();
                        uint tick = record.ReadUInt32();
                        // Only the owner of an owner-authoritative object has any say over it.
                        if (_world.TryGet(id, out var networkObject) && networkObject.HasSnapshotChannel
                            && networkObject.OwnerWritesSnapshots && networkObject.OwnerClientId == senderClientId)
                        {
                            networkObject.ReadSnapshotInternal(record, tick, senderClientId);
                        }
                        break;
                    }
                }
                // Anything else from a client is not a thing; ignore it.
                return;
            }

            switch (type)
            {
                case MessageType.Spawn:
                {
                    ulong id = record.ReadVarUInt64();
                    uint typeTag = record.ReadVarUInt32();
                    ulong owner = record.ReadVarUInt64();
                    byte flags = record.ReadByte();
                    ulong parentId = record.ReadVarUInt64();
                    _world.SpawnRemote(typeTag, id, owner, parentId, (flags & 1) != 0, (flags & 2) != 0, record);
                    break;
                }

                case MessageType.Despawn:
                    _world.DespawnRemote(record.ReadVarUInt64());
                    break;

                case MessageType.Ownership:
                {
                    ulong id = record.ReadVarUInt64();
                    ulong owner = record.ReadVarUInt64();
                    _world.SetOwnerRemote(id, owner);
                    break;
                }

                case MessageType.Sync:
                {
                    ulong id = record.ReadVarUInt64();
                    _world.ApplyRemoteState(id, record);
                    break;
                }

                case MessageType.Rpc:
                {
                    _ = record.ReadByte();   // target: informational on the receiving side
                    ulong id = record.ReadVarUInt64();
                    ushort componentId = record.ReadVarUInt16();
                    uint hash = record.ReadUInt32();
                    _world.DispatchRpc(id, componentId, hash, record);
                    break;
                }

                case MessageType.Snapshot:
                {
                    ulong id = record.ReadVarUInt64();
                    uint tick = record.ReadUInt32();
                    if (_world.TryGet(id, out var networkObject) && networkObject.HasSnapshotChannel)
                    {
                        networkObject.ReadSnapshotInternal(record, tick, NetworkSession.ServerClientId);
                    }
                    break;
                }
            }
        }

        private void HandleClientRpc(ulong senderClientId, Delivery delivery, NetworkReader record)
        {
            if (!_peers.TryGetValue(senderClientId, out var sender)) return;

            int recordStart = record.Position;
            var target = (RpcTarget)record.ReadByte();
            ulong id = record.ReadVarUInt64();
            ushort componentId = record.ReadVarUInt16();
            uint hash = record.ReadUInt32();

            if (!sender.Known.Contains(id) || !_world.TryGet(id, out var networkObject))
            {
                return;   // the client cannot see this object; nothing it says about it counts
            }

            bool runLocally = target == RpcTarget.Server ||
                              RouteFromServer(networkObject, target, delivery,
                                  new ReadOnlySpan<byte>(record.Buffer, record.Offset + recordStart, record.Length - recordStart),
                                  excludeClient: senderClientId, ownerIsServer: networkObject.IsOwnedByServer);

            if (runLocally)
            {
                _world.DispatchRpc(id, componentId, hash, record);
            }
        }

        #endregion
    }
}
