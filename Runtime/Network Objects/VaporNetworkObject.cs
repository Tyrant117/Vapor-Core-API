using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;

namespace Vapor.NetworkObjects
{
    [Serializable]
    public struct VaporNetworkObjectSaveData
    {
        public string SaveId;
        public string Json;
    }

    public abstract class VaporNetworkObject : INetworkPacket
    {
        protected VaporNetworkObject() { }

        protected VaporNetworkObject(bool spawnedOnlyOnOwner)
        {
            SpawnedOnlyOnOwner = spawnedOnlyOnOwner;
        }
        
        public ulong NetworkObjectId { get; internal set; }

        /// <summary>
        /// Who owns this object, fixed for its lifetime. There is no transfer, by design.
        /// </summary>
        /// <remarks>
        /// Reassigning it would not do what it looks like: the owner map is keyed on this value, an
        /// owner-only object's relevance is derived from it, and every client already told about the
        /// object would have to be re-evaluated. Owning it until despawn is what keeps that consistent,
        /// and it is why a departing client's objects are despawned rather than handed on.
        /// </remarks>
        public ulong OwnerClientId { get; internal set; }
        public ulong ParentNetworkObjectId { get; internal set; }
        public ushort ParentNetworkBehaviourOrderIndex { get; internal set; }
        public bool IsRoot => ParentNetworkObjectId == 0;
        public bool ParentIsUnityObject { get; internal set; }
        public bool IsHost { get; internal set; }
        public bool IsServer { get; internal set; }
        public bool IsClient { get; internal set; }
        public bool IsOwner => OwnerClientId == NetworkManager.Singleton.LocalClientId;
        public bool IsOwnedByServer => OwnerClientId == NetworkManager.ServerClientId;
        public bool IsSpawned => NetworkObjectId != 0;
        public bool SpawnedOnlyOnOwner { get; internal set; }
        public bool IsPlayerObject { get; internal set; }
        public NetworkMessages NetworkMessages { get; internal set; }
        protected internal abstract bool ShouldTick { get; }
        public bool IsDirty { get; private set; }
        public List<VaporNetworkObject> SubObjects { get; private set; }
        public string SaveId { get; internal set; }

        private uint _networkVariableIdCounter;
        private readonly Dictionary<uint, VaporNetworkVariableBase> _networkVariables = new();
        private readonly List<VaporNetworkVariableBase> _dirtyVariables = new();

        public void Spawn(ulong parentNetworkId = 0, ushort parentNetworkBehaviourOrderIndex = 0, bool parentIsUnityObject = false, ulong ownerClientId = 0UL,
            bool onlySpawnForOwner = false, bool isPlayerObject = false) =>
            NetworkMessages.Instance.SpawnNetworkObject(this, parentNetworkId, parentNetworkBehaviourOrderIndex, parentIsUnityObject, ownerClientId, onlySpawnForOwner, isPlayerObject);

        public void Despawn() => NetworkMessages.DestroyNetworkObject(NetworkObjectId);

        internal void InternalInitialize()
        {
        }

        internal void InternalDestroy()
        {
            NetworkObjectId = 0;
            OwnerClientId = 0;
            ParentNetworkObjectId = 0;
            ParentIsUnityObject = false;
            SubObjects = null;
            IsDirty = false;
            _interestGroups = null;

            // Variables can own native memory, which nothing else will release for them.
            foreach (var variable in _networkVariables.Values)
            {
                variable.Dispose();
            }

            _networkVariables.Clear();
            _dirtyVariables.Clear();
            _networkVariableIdCounter = 0;
        }

        /// <summary>
        /// Called before OnSpawn. NetworkVariables should be constructed and initialized here.
        /// </summary>
        protected internal abstract void OnPreSpawn();

        protected internal abstract void OnSpawn();
        protected internal abstract void OnPostSpawn();
        protected internal abstract void OnDespawn();
        protected internal virtual void OnTick(double deltaTime) { }

        public void MarkDirty()
        {
            if (!IsSpawned)
            {
                Debug.LogError("MarkDirty can only be called on spawned objects.");
                return;
            }

            if (!IsServer)
            {
                Debug.LogError("MarkDirty can only be called on the server.");
                return;
            }

            if (IsDirty)
            {
                return;
            }

            IsDirty = true;
            NetworkMessages.QueueForSend(this);
        }

        #region - SubObjects -

        protected internal virtual void SubObjectSpawned(VaporNetworkObject networkSubObject)
        {
            SubObjects ??= new List<VaporNetworkObject>();
            SubObjects.Add(networkSubObject);
        }

        protected internal virtual void SubObjectDespawned(VaporNetworkObject networkSubObject)
        {
            SubObjects ??= new List<VaporNetworkObject>();
            SubObjects.Remove(networkSubObject);
        }

        #endregion

        #region - Interest -

        private HashSet<InterestGroup> _interestGroups;

        /// <summary>
        /// True while this object replicates to every connected client. Joining any interest group
        /// narrows it to the clients subscribed to that group.
        /// </summary>
        public bool IsGloballyRelevant => _interestGroups == null || _interestGroups.Count == 0;

        public IReadOnlyCollection<InterestGroup> InterestGroups =>
            _interestGroups ?? (IReadOnlyCollection<InterestGroup>)Array.Empty<InterestGroup>();

        public bool IsInInterestGroup(InterestGroup group) => _interestGroups != null && _interestGroups.Contains(group);

        /// <summary>
        /// Joins a replication channel. The first group an object joins stops it being global, so
        /// clients subscribed to nothing relevant will see it despawn.
        /// </summary>
        public bool AddInterestGroup(InterestGroup group)
        {
            if (group.IsNone || !CanChangeInterest())
            {
                return false;
            }

            _interestGroups ??= new HashSet<InterestGroup>();
            if (!_interestGroups.Add(group))
            {
                return false;
            }

            NotifyInterestChanged(group, true);
            return true;
        }

        public bool RemoveInterestGroup(InterestGroup group)
        {
            if (group.IsNone || !CanChangeInterest() || _interestGroups == null || !_interestGroups.Remove(group))
            {
                return false;
            }

            NotifyInterestChanged(group, false);
            return true;
        }

        /// <summary>Returns the object to global relevance.</summary>
        public void ClearInterestGroups()
        {
            if (_interestGroups == null || _interestGroups.Count == 0 || !CanChangeInterest())
            {
                return;
            }

            var left = new List<InterestGroup>(_interestGroups);
            _interestGroups.Clear();
            foreach (var group in left)
            {
                NotifyInterestChanged(group, false);
            }
        }

        private void NotifyInterestChanged(InterestGroup group, bool joined)
        {
            if (IsSpawned && NetworkMessages)
            {
                NetworkMessages.OnInterestChanged(this, group, joined);
            }
        }

        /// <summary>
        /// Interest drives who receives this object, so only the authority may change it. Before spawn
        /// there is no authority to test and nothing has replicated yet, which is what lets OnPreSpawn
        /// place an object in its groups.
        /// </summary>
        private bool CanChangeInterest()
        {
            if (!IsSpawned || !NetworkMessages || IsServer)
            {
                return true;
            }

            Debug.LogError($"Interest groups can only be changed on the server. {GetType().Name} [{NetworkObjectId}]");
            return false;
        }

        #endregion

        #region - Network Variables -

        internal void RegisterNetworkVariable(VaporNetworkVariableBase networkVariable)
        {
            _networkVariables.Add(networkVariable.NetworkVariableId, networkVariable);
        }

        internal void MarkNetworkVariableDirty(VaporNetworkVariableBase dirtyVariable)
        {
            if (!IsSpawned)
            {
                Debug.LogError("MarkDirty can only be called on spawned objects.");
                return;
            }

            if (!IsServer)
            {
                Debug.LogError("MarkDirty can only be called on the server.");
                return;
            }

            _dirtyVariables.Add(dirtyVariable);

            if (IsDirty)
            {
                return;
            }

            IsDirty = true;
            NetworkMessages.QueueForSend(this);
        }

        internal uint GetNextNetworkVariableId()
        {
            _networkVariableIdCounter++;
            return _networkVariableIdCounter;
        }

        #endregion

        internal void OnMessageReceived(FastBufferReader reader)
        {
            reader.ReadValueSafe(out uint methodHash);
            if (!s_RpcHandlers.TryGetValue(methodHash, out var entry))
            {
                Debug.LogWarning($"Received an unknown rpc [{methodHash:X8}] on {GetType().Name} [{NetworkObjectId}]. The peers are likely running different builds.");
                return;
            }

            entry.Handler.Invoke(this, reader);
        }

        public virtual void Serialize(FastBufferWriter writer, bool fullPacket)
        {
            writer.WriteValueSafe(fullPacket);
            if (fullPacket)
            {
                writer.WriteValueSafe(_networkVariables.Count);
                foreach (var variable in _networkVariables.Values)
                {
                    variable.WriteFull(writer);
                }
            }
            else
            {
                writer.WriteValueSafe(_dirtyVariables.Count);
                foreach (var variable in _dirtyVariables)
                {
                    variable.Write(writer);
                }

                // Only the delta consumes the dirty state. A full packet is a snapshot for one joining
                // client, and the changes it happens to contain are still owed to everyone else.
                _dirtyVariables.Clear();
                IsDirty = false;
            }
        }

        public virtual void Deserialize(FastBufferReader reader, out bool fullPacket)
        {
            reader.ReadValueSafe(out fullPacket);
            reader.ReadValueSafe(out int variableCount);
            for (int i = 0; i < variableCount; i++)
            {
                ByteUnpacker.ReadValueBitPacked(reader, out uint networkVariableId);
                if (_networkVariables.TryGetValue(networkVariableId, out var variable))
                {
                    variable.Read(reader);
                }
            }

            IsDirty = false;
        }

        #region - Rpcs (Codegen Support) -

        // The support surface for the VaporRpc source generator (Tools~/VaporRpcGenerator). You never
        // call these yourself: the generator writes the send path and the receive handler for every
        // [VaporRpc] method into the partial class that declares it, and those call in here.
        //
        // Everything is protected rather than public because generated code always lives inside a
        // subclass. Argument serialization is the exception — see RpcSerialization, which generated
        // code reaches from other assemblies and so has to be public.

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected delegate void RpcReceiveHandler(VaporNetworkObject target, FastBufferReader reader);

        private readonly struct RpcTableEntry
        {
            public readonly RpcReceiveHandler Handler;
            public readonly string Name;

            public RpcTableEntry(RpcReceiveHandler handler, string name)
            {
                Handler = handler;
                Name = name;
            }
        }

        // Keyed by the compile-time hash of the declaring type + method signature, so the table is
        // global rather than per-instance and inherited rpcs resolve without walking type hierarchies.
        private static readonly Dictionary<uint, RpcTableEntry> s_RpcHandlers = new();

        /// <summary>
        /// Called once per declaring type at subsystem registration, from the generated
        /// <c>RegisterVaporRpcs</c>.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected static void RegisterRpc(uint hash, RpcReceiveHandler handler, string name)
        {
            if (s_RpcHandlers.TryGetValue(hash, out var existing) && existing.Name != name)
            {
                Debug.LogError($"VaporRpc hash collision: {name} and {existing.Name} both hash to [{hash:X8}]. The rpc {name} will not be callable.");
                return;
            }

            s_RpcHandlers[hash] = new RpcTableEntry(handler, name);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool BeginSendRpc(uint hash, out FastBufferWriter writer)
        {
            if (!IsSpawned || !NetworkMessages)
            {
                Debug.LogError($"Rpc [{hash:X8}] called on {GetType().Name} before it was spawned.");
                writer = default;
                return false;
            }

            writer = new FastBufferWriter(NetworkMessages.RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, NetworkMessages.RPC_MESSAGE_MAXIMUM_SIZE);
            writer.WriteValueSafe(NetworkMessages.RPC_MESSAGE);
            writer.WriteValueSafe(NetworkObjectId);
            writer.WriteValueSafe(hash);
            return true;
        }

        /// <summary>
        /// Sends the finished rpc buffer to every remote target and returns true when the local peer is
        /// itself in the target set, in which case the generated send path falls through and runs the
        /// rpc's body here too.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected bool EndSendRpc(FastBufferWriter writer, SendTo sendTo, NetworkDelivery networkDelivery)
        {
            using (writer)
            {
                return NetworkMessages.SendRpc(this, writer, sendTo, networkDelivery);
            }
        }

        #endregion

        #region - Save / Load -

        public VaporNetworkObjectSaveData ToSaveData()
        {
            return new VaporNetworkObjectSaveData
            {
                SaveId = SaveId,
                Json = ToJson()
            };
        }

        protected abstract string ToJson();

        public void FromSaveData(VaporNetworkObjectSaveData data)
        {
            if (data.SaveId.EmptyOrNull())
            {
                return;
            }
            
            if (data.SaveId != SaveId)
            {
                throw new Exception($"Trying to load a save with the wrong save id. Expected {SaveId} but got {data.SaveId}");
            }
            
            FromJson(data.Json);
        }

        protected abstract void FromJson(string json);

        #endregion
    }

    public static class VaporNetworkObjectQuery
    {
        public static T WithSaveId<T>(this T obj, string saveId) where T : VaporNetworkObject
        {
            obj.SaveId = saveId;
            return obj;
        }

        public static string GetFullSavePath(this VaporNetworkObject networkObject)
        {
            if (networkObject.SaveId.EmptyOrNull())
            {
                return null;
            }

            var rootBehaviour = networkObject.GetRootNetworkBehaviour();
            var sourceBehaviourTypeName = rootBehaviour ? rootBehaviour.GetType().Name : null;

            var pool = ListPool<string>.Get();
            pool.Add(networkObject.SaveId);
            var currentParentId = networkObject.ParentNetworkObjectId;
            
            // Create The Save Path
            if(!networkObject.ParentIsUnityObject)
            {
                while (currentParentId != 0)
                {
                    if (NetworkMessages.Instance.NetworkObjects.TryGetValue(currentParentId, out var parentObject))
                    {
                        // If the parent doesn't have a save ID, we stop traversing this branch,
                        // as it breaks the continuous save path.
                        if (parentObject.SaveId.EmptyOrNull())
                        {
                            break;
                        }

                        pool.Add(parentObject.SaveId);
                    }

                    if (parentObject is { ParentIsUnityObject: false })
                    {
                        currentParentId = parentObject.ParentNetworkObjectId;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            pool.Reverse();
            var joined = string.Join('_', pool);

            if (sourceBehaviourTypeName != null)
            {
                joined = $"{sourceBehaviourTypeName}_{joined}";
            }
            
            ListPool<string>.Release(pool);
            return joined;
        }

        public static NetworkBehaviour GetParentNetworkBehaviour(this VaporNetworkObject networkObject)
        {
            if (networkObject.ParentNetworkObjectId == 0)
            {
                return null;
            }

            if (networkObject.ParentIsUnityObject)
            {
                return NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentObject) 
                    ? parentObject.GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex) 
                    : null;
            }

            return null;
        }

        public static NetworkBehaviour GetRootNetworkBehaviour(this VaporNetworkObject networkObject)
        {
            if (networkObject.ParentNetworkObjectId == 0)
            {
                return null;
            }
            
            if (networkObject.ParentIsUnityObject)
            {
                return networkObject.GetParentNetworkBehaviour();
            }
            
            var currentParentId = networkObject.ParentNetworkObjectId;
            while (currentParentId != 0)
            {
                if (NetworkMessages.Instance.NetworkObjects.TryGetValue(currentParentId, out var parentObject))
                {
                    if (parentObject.ParentIsUnityObject)
                    {
                        return parentObject.GetParentNetworkBehaviour();
                    }

                    currentParentId = parentObject.ParentNetworkObjectId;
                }
                else
                {
                    return null;
                }
            }
            
            return null;
        }

        public static T Ancestor<T>(this VaporNetworkObject networkObject) where T : class
        {
            if (networkObject.ParentNetworkObjectId == 0)
            {
                return null;
            }
            
            if (networkObject.ParentIsUnityObject)
            {
                if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentObject))
                {
                    return null;
                }

                parentObject.TryGetComponent(out T ancestor);
                return ancestor;
            }

            var currentParentId = networkObject.ParentNetworkObjectId;
            while (currentParentId != 0)
            {
                if (NetworkMessages.Instance.NetworkObjects.TryGetValue(currentParentId, out var parentObject) && parentObject is T ancestor)
                {
                    return ancestor;
                }

                currentParentId = parentObject?.ParentNetworkObjectId ?? 0;
            }
            return null;
        }

        public static T[] Ancestors<T>(this VaporNetworkObject networkObject) where T : class
        {
            var pool = ListPool<T>.Get();
            var currentParentId = networkObject.ParentNetworkObjectId;
            while (currentParentId != 0)
            {
                if (NetworkMessages.Instance.NetworkObjects.TryGetValue(currentParentId, out var parentObject) && parentObject is T ancestor)
                {
                    pool.Add(ancestor);
                }

                currentParentId = parentObject?.ParentNetworkObjectId ?? 0;
            }
            
            var result = new T[pool.Count];
            pool.CopyTo(result);
            ListPool<T>.Release(pool);
            return result;
        }

        public static T[] Q<T>(this VaporNetworkObject networkObject) where T : VaporNetworkObject
        {
            if (networkObject.SubObjects == null)
            {
                return Array.Empty<T>();
            }

            var pool = ListPool<T>.Get();
            RecursiveSearchChildren(networkObject);
            var result = new T[pool.Count];
            pool.CopyTo(result);
            ListPool<T>.Release(pool);
            return result;

            void RecursiveSearchChildren(VaporNetworkObject parent)
            {
                if (parent.SubObjects == null) return;

                foreach (var childObject in parent.SubObjects)
                {
                    if (childObject is T match)
                    {
                        pool.Add(match);
                    }

                    RecursiveSearchChildren(childObject);
                }
            }
        }
    }
}