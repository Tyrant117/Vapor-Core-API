using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;
using Vapor.Inspector;

namespace Vapor.NetworkObjects
{
    [System.Serializable]
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

            __rpc_exec = true;
            try
            {
                entry.Handler.Invoke(this, reader);
            }
            finally
            {
                __rpc_exec = false;
            }
        }

        public virtual void Serialize(FastBufferWriter writer, bool fullPacket)
        {
            writer.WriteValueSafe(fullPacket);
            if (fullPacket)
            {
                writer.WriteValueSafe(_networkVariables.Count);
                foreach (var variable in _networkVariables.Values)
                {
                    variable.Write(writer);
                }
            }
            else
            {
                writer.WriteValueSafe(_dirtyVariables.Count);
                foreach (var variable in _dirtyVariables)
                {
                    variable.Write(writer);
                }
            }

            _dirtyVariables.Clear();
            IsDirty = false;
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

        // Everything in this region is the support surface for the VaporRpc IL post-processor
        // (Vapor Core/Editor/CodeGen). User code never calls these members directly; the weaver emits
        // calls to them inside [VaporRpc] method bodies and their generated receive handlers.
        // They are public only because woven code in other assemblies must reach them.

        [EditorBrowsable(EditorBrowsableState.Never)]
        public delegate void __RpcReceiveHandler(VaporNetworkObject target, FastBufferReader reader);

        private readonly struct RpcTableEntry
        {
            public readonly __RpcReceiveHandler Handler;
            public readonly string Name;

            public RpcTableEntry(__RpcReceiveHandler handler, string name)
            {
                Handler = handler;
                Name = name;
            }
        }

        // Keyed by the weave-time hash of the declaring type + method signature, so the table is global
        // rather than per-instance and inherited rpcs resolve without walking type hierarchies.
        private static readonly Dictionary<uint, RpcTableEntry> s_RpcHandlers = new();

        // Set by OnMessageReceived around handler invocation; a woven [VaporRpc] method that observes it
        // true clears it and runs its body instead of serializing a send.
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool __rpc_exec;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void __registerRpc(uint hash, __RpcReceiveHandler handler, string name)
        {
            if (s_RpcHandlers.TryGetValue(hash, out var existing) && existing.Name != name)
            {
                Debug.LogError($"VaporRpc hash collision: {name} and {existing.Name} both hash to [{hash:X8}]. The rpc {name} will not be callable.");
                return;
            }

            s_RpcHandlers[hash] = new RpcTableEntry(handler, name);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool __beginSendRpc(uint hash, out FastBufferWriter writer)
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
        /// itself in the target set, in which case the woven method falls through and runs its body.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool __endSendRpc(FastBufferWriter writer, SendTo sendTo, NetworkDelivery networkDelivery)
        {
            using (writer)
            {
                return NetworkMessages.SendRpc(this, writer, sendTo, networkDelivery);
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void __writeValue<T>(FastBufferWriter writer, T value)
        {
            NetworkVariableSerialization<T>.Write(writer, ref value);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static T __readValue<T>(FastBufferReader reader)
        {
            T value = default;
            NetworkVariableSerialization<T>.Read(reader, ref value);
            return value;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void __writeNetworkObject(FastBufferWriter writer, VaporNetworkObject value)
        {
            if (value == null)
            {
                writer.WriteValueSafe(true);
                return;
            }

            writer.WriteValueSafe(false);
            writer.WriteValueSafe(value.NetworkObjectId);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static VaporNetworkObject __readNetworkObject(FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool isNull);
            if (isNull)
            {
                return null;
            }

            reader.ReadValueSafe(out ulong networkObjectId);
            NetworkMessages.Instance.NetworkObjects.TryGetValue(networkObjectId, out var value);
            return value;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void __writeNetworkBehaviour(FastBufferWriter writer, NetworkBehaviour value)
        {
            if (!value)
            {
                writer.WriteValueSafe(true);
                return;
            }

            writer.WriteValueSafe(false);
            writer.WriteValueSafe((NetworkBehaviourReference)value);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static NetworkBehaviour __readNetworkBehaviour(FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool isNull);
            if (isNull)
            {
                return null;
            }

            reader.ReadValueSafe(out NetworkBehaviourReference reference);
            reference.TryGet(out NetworkBehaviour value);
            return value;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void __writePacket(FastBufferWriter writer, INetworkPacket value)
        {
            if (value == null)
            {
                writer.WriteValueSafe(true);
                return;
            }

            writer.WriteValueSafe(false);
            PacketHandler.CreatePacket(writer, value);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static INetworkPacket __readPacket(FastBufferReader reader)
        {
            reader.ReadValueSafe(out bool isNull);
            return isNull ? null : PacketHandler.FromPacket(reader);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void __writeSerializable<T>(FastBufferWriter writer, T value) where T : INetworkSerializable, new()
        {
            bool isNull = (object)value == null;
            writer.WriteValueSafe(isNull);
            if (!isNull)
            {
                writer.WriteNetworkSerializable(value);
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static T __readSerializable<T>(FastBufferReader reader) where T : INetworkSerializable, new()
        {
            reader.ReadValueSafe(out bool isNull);
            if (isNull)
            {
                return default;
            }

            reader.ReadNetworkSerializable(out T value);
            return value;
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