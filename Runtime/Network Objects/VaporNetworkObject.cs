using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;
using Vapor.Inspector;
using Vapor.Unsafe;

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
        private static readonly HashSet<Type> s_SerializeShortcutTypes = new()
        {
            typeof(bool),
            typeof(byte),
            typeof(sbyte),
            typeof(char),
            typeof(decimal),
            typeof(double),
            typeof(float),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(short),
            typeof(ushort),
            typeof(string),
            typeof(Vector2),
            typeof(Vector3),
            typeof(Vector4),
            typeof(Quaternion),
            typeof(Color),
            typeof(Pose),
        };
        
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
        private readonly Dictionary<uint, Action<FastBufferReader>> _rpcs = new();
        private readonly Dictionary<uint, ValueTuple<SendTo, NetworkDelivery>> _rpcAttributes = new();
        private readonly Dictionary<uint, VaporNetworkVariableBase> _networkVariables = new();
        private readonly List<VaporNetworkVariableBase> _dirtyVariables = new();

        // private static readonly uint s_AddSubObjectRpcHash = nameof(AddSubObjectRpc).Hash32();
        // private static readonly uint s_RemoveSubObjectRpcHash = nameof(RemoveSubObjectRpc).Hash32();

        public void Spawn(ulong parentNetworkId = 0, ushort parentNetworkBehaviourOrderIndex = 0, bool parentIsUnityObject = false, ulong ownerClientId = 0UL, 
            bool onlySpawnForOwner = false, bool isPlayerObject = false) =>
            NetworkMessages.Instance.SpawnNetworkObject(this, parentNetworkId, parentNetworkBehaviourOrderIndex, parentIsUnityObject, ownerClientId, onlySpawnForOwner, isPlayerObject);

        public void Despawn() => NetworkMessages.DestroyNetworkObject(NetworkObjectId);

        internal void InternalInitialize()
        {
            // _rpcs[s_AddSubObjectRpcHash] = AddSubObjectRpc;
            // _rpcAttributes[s_AddSubObjectRpcHash] = (SendTo.Everyone, NetworkDelivery.ReliableSequenced);

            // _rpcs[s_RemoveSubObjectRpcHash] = RemoveSubObjectRpc;
            // _rpcAttributes[s_RemoveSubObjectRpcHash] = (SendTo.Everyone, NetworkDelivery.ReliableSequenced);
        }

        internal void InternalDestroy()
        {
            NetworkObjectId = 0;
            OwnerClientId = 0;
            ParentNetworkObjectId = 0;
            ParentIsUnityObject = false;
            SubObjects = null;
            IsDirty = false;

            _rpcs.Clear();
            _rpcAttributes.Clear();
            _networkVariables.Clear();
            _dirtyVariables.Clear();
            _networkVariableIdCounter = 0;
        }

        protected void RegisterRpcMethod(Action<FastBufferReader> rpcMethod)
        {
            var methodHash = rpcMethod.Method.Name.Hash32();
            if (!_rpcs.TryAdd(methodHash, rpcMethod))
            {
                return;
            }

            var atr = (VaporRpcAttribute)rpcMethod.Method.GetCustomAttributes(typeof(VaporRpcAttribute), false)[0];
            _rpcAttributes[methodHash] = (atr.SendTo, atr.NetworkDelivery);
        }

        /// <summary>
        /// Called before OnSpawn. Rpcs and NetworkVariables should be registered and initialized here.
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

        // public void AddSubObject(VaporNetworkObject subobject)
        // {
        //     if (!IsServer)
        //     {
        //         Debug.LogError($"{TooltipMarkup.ClassMethod(nameof(VaporNetworkObject), nameof(AddSubObject))} can only be called on the server.");
        //         return;
        //     }
        //
        //     Children ??= new List<ulong>();
        //     Children.Add(subobject.NetworkObjectId);
        //     Send(s_AddSubObjectRpcHash, subobject.NetworkObjectId);
        // }

        // private void AddSubObjectRpc(FastBufferReader reader)
        // {
        //     Receive(reader, out ulong subObjectNetworkId);
        //     Children ??= new List<ulong>();
        //     Children.Add(subObjectNetworkId);
        // }

        // public void RemoveSubObject(VaporNetworkObject subobject)
        // {
        //     if (!IsServer)
        //     {
        //         Debug.LogError($"{TooltipMarkup.ClassMethod(nameof(VaporNetworkObject), nameof(AddSubObject))} can only be called on the server.");
        //         return;
        //     }
        //
        //     Children.Remove(subobject.NetworkObjectId);
        //     Send(s_RemoveSubObjectRpcHash, subobject.NetworkObjectId);
        // }

        // private void RemoveSubObjectRpc(FastBufferReader reader)
        // {
        //     Receive(reader, out ulong subObjectNetworkId);
        //     Children.Remove(subObjectNetworkId);
        // }

        protected internal virtual void SubObjectSpawned(VaporNetworkObject networkSubObject)
        {
            SubObjects ??= new List<VaporNetworkObject>();
            SubObjects.Add(networkSubObject);
        }

        protected internal virtual void SubObjectDespawned(VaporNetworkObject networkSubObject)
        {
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
            if (_rpcs.TryGetValue(methodHash, out var rpcCallback))
            {
                rpcCallback.Invoke(reader);
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

        #region - Send / Receive Rpcs -

        protected void Send(Action<FastBufferReader> rpcMethod)
        {
            var methodHash = rpcMethod.Method.Name.Hash32();
            Send(methodHash);
        }

        protected void Send<T0>(Action<FastBufferReader> rpcMethod, T0 arg1)
        {
            var methodHash = rpcMethod.Method.Name.Hash32();
            Send(methodHash, arg1);
        }

        protected void Send<T0, T1>(Action<FastBufferReader> rpcMethod, T0 arg1, T1 arg2)
        {
            var methodHash = rpcMethod.Method.Name.Hash32();
            Send(methodHash, arg1, arg2);
        }

        protected void Send<T0, T1, T2>(Action<FastBufferReader> rpcMethod, T0 arg1, T1 arg2, T2 arg3)
        {
            var methodHash = rpcMethod.Method.Name.Hash32();
            Send(methodHash, arg1, arg2, arg3);
        }

        protected void Send<T0, T1, T2, T3>(Action<FastBufferReader> rpcMethod, T0 arg1, T1 arg2, T2 arg3, T3 arg4)
        {
            var methodHash = rpcMethod.Method.Name.Hash32();
            Send(methodHash, arg1, arg2, arg3, arg4);
        }

        protected void Send(Action<FastBufferReader> rpcMethod, INetworkPacket packet, bool fullPacket = false)
        {
            var methodHash = rpcMethod.Method.Name.Hash32();
            Send(methodHash, packet, fullPacket);
        }

        protected void Send(uint methodHash)
        {
            var rpcAttributes = _rpcAttributes[methodHash];

            using var writer = new FastBufferWriter(NetworkMessages.RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, NetworkMessages.RPC_MESSAGE_MAXIMUM_SIZE);
            // Write opcode first (4 bytes header)
            writer.WriteValueSafe(NetworkMessages.RPC_MESSAGE);
            // Write our message source
            writer.WriteValueSafe(NetworkObjectId);
            // Write our rpc hash
            writer.WriteValueSafe(methodHash);
            NetworkMessages.Send(this, writer, SpawnedOnlyOnOwner ? SendTo.Owner : rpcAttributes.Item1, rpcAttributes.Item2);
        }

        protected void Send<T0>(uint methodHash, T0 arg1)
        {
            var rpcAttributes = _rpcAttributes[methodHash];

            using var writer = new FastBufferWriter(NetworkMessages.RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, NetworkMessages.RPC_MESSAGE_MAXIMUM_SIZE);
            // Write opcode first (4 bytes header)
            writer.WriteValueSafe(NetworkMessages.RPC_MESSAGE);
            // Write our message source
            writer.WriteValueSafe(NetworkObjectId);
            // Write our rpc hash
            writer.WriteValueSafe(methodHash);

            WriteArgument(writer, arg1);
            
            NetworkMessages.Send(this, writer, SpawnedOnlyOnOwner ? SendTo.Owner : rpcAttributes.Item1, rpcAttributes.Item2);
        }

        protected void Send<T0, T1>(uint methodHash, T0 arg1, T1 arg2)
        {
            var rpcAttributes = _rpcAttributes[methodHash];

            using var writer = new FastBufferWriter(NetworkMessages.RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, NetworkMessages.RPC_MESSAGE_MAXIMUM_SIZE);
            // Write opcode first (4 bytes header)
            writer.WriteValueSafe(NetworkMessages.RPC_MESSAGE);
            // Write our message source
            writer.WriteValueSafe(NetworkObjectId);
            // Write our rpc hash
            writer.WriteValueSafe(methodHash);
            
            WriteArgument(writer, arg1);
            WriteArgument(writer, arg2);
            
            NetworkMessages.Send(this, writer, SpawnedOnlyOnOwner ? SendTo.Owner : rpcAttributes.Item1, rpcAttributes.Item2);
        }

        protected void Send<T0, T1, T2>(uint methodHash, T0 arg1, T1 arg2, T2 arg3)
        {
            var rpcAttributes = _rpcAttributes[methodHash];

            using var writer = new FastBufferWriter(NetworkMessages.RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, NetworkMessages.RPC_MESSAGE_MAXIMUM_SIZE);
            // Write opcode first (4 bytes header)
            writer.WriteValueSafe(NetworkMessages.RPC_MESSAGE);
            // Write our message source
            writer.WriteValueSafe(NetworkObjectId);
            // Write our rpc hash
            writer.WriteValueSafe(methodHash);
            
            WriteArgument(writer, arg1);
            WriteArgument(writer, arg2);
            WriteArgument(writer, arg3);
            
            NetworkMessages.Send(this, writer, SpawnedOnlyOnOwner ? SendTo.Owner : rpcAttributes.Item1, rpcAttributes.Item2);
        }

        protected void Send<T0, T1, T2, T3>(uint methodHash, T0 arg1, T1 arg2, T2 arg3, T3 arg4)
        {
            var rpcAttributes = _rpcAttributes[methodHash];

            using var writer = new FastBufferWriter(NetworkMessages.RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, NetworkMessages.RPC_MESSAGE_MAXIMUM_SIZE);
            // Write opcode first (4 bytes header)
            writer.WriteValueSafe(NetworkMessages.RPC_MESSAGE);
            // Write our message source
            writer.WriteValueSafe(NetworkObjectId);
            // Write our rpc hash
            writer.WriteValueSafe(methodHash);
            
            WriteArgument(writer, arg1);
            WriteArgument(writer, arg2);
            WriteArgument(writer, arg3);
            WriteArgument(writer, arg4);
            
            NetworkMessages.Send(this, writer, SpawnedOnlyOnOwner ? SendTo.Owner : rpcAttributes.Item1, rpcAttributes.Item2);
        }

        protected void Send(uint methodHash, INetworkPacket packet, bool fullPacket = false)
        {
            var rpcAttributes = _rpcAttributes[methodHash];

            using var writer = new FastBufferWriter(NetworkMessages.RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, NetworkMessages.RPC_MESSAGE_MAXIMUM_SIZE);
            // Write opcode first (4 bytes header)
            writer.WriteValueSafe(NetworkMessages.RPC_MESSAGE);
            // Write our message source
            writer.WriteValueSafe(NetworkObjectId);
            // Write our rpc hash
            writer.WriteValueSafe(methodHash);
            var bytes = PacketHandler.CreatePacket(packet, fullPacket);
            writer.WriteValueSafe(bytes);
            NetworkMessages.Send(this, writer, SpawnedOnlyOnOwner ? SendTo.Owner : rpcAttributes.Item1, rpcAttributes.Item2);
        }

        private static void WriteArgument<T>(FastBufferWriter writer, T arg)
        {
            if (s_SerializeShortcutTypes.Contains(typeof(T)))
            {
                NetworkVariableSerialization<T>.Write(writer, ref arg);
                return;
            }
            
            // Write If Null
            if (arg == null)
            {
                writer.WriteValueSafe(true);
                return;
            }
            writer.WriteValueSafe(false);

            if (arg is VaporNetworkObject vaporNetworkObject)
            {
                writer.WriteValueSafe(vaporNetworkObject.NetworkObjectId);
                return;
            }
            
            if (arg is NetworkBehaviour networkBehaviour)
            {
                writer.WriteValueSafe((NetworkBehaviourReference)networkBehaviour);
                return;
            }

            if (arg is INetworkPacket networkPacket)
            {
                PacketHandler.CreatePacket(writer, networkPacket);
                return;
            }

            if (arg is INetworkSerializable networkSerializable)
            {
                writer.WriteNetworkSerializable(networkSerializable);
                return;
            }
            
            NetworkVariableSerialization<T>.Write(writer, ref arg);
        }

        protected void Receive<T0>(FastBufferReader reader, out T0 value)
        {
            value = ReadArgument<T0>(reader);
        }

        protected void Receive<T0, T1>(FastBufferReader reader, out T0 value1, out T1 value2)
        {
            value1 = ReadArgument<T0>(reader);
            value2 = ReadArgument<T1>(reader);
        }

        protected void Receive<T0, T1, T2>(FastBufferReader reader, out T0 value1, out T1 value2, out T2 value3)
        {
            value1 = ReadArgument<T0>(reader);
            value2 = ReadArgument<T1>(reader);
            value3 = ReadArgument<T2>(reader);
        }

        protected void Receive<T0, T1, T2, T3>(FastBufferReader reader, out T0 value1, out T1 value2, out T2 value3, out T3 value4)
        {
            value1 = ReadArgument<T0>(reader);
            value2 = ReadArgument<T1>(reader);
            value3 = ReadArgument<T2>(reader);
            value4 = ReadArgument<T3>(reader);
        }

        protected void ReceivePacket(FastBufferReader reader, ref INetworkPacket packet)
        {
            PacketHandler.FillPacket(ref packet, reader);
        }

        protected void ReceivePacket<T>(FastBufferReader reader, out T packet) where T : INetworkPacket, new()
        {
            reader.ReadValueSafe(out NativeArray<byte> bytes, Allocator.Temp);
            packet = PacketHandler.FromPacket<T>(bytes);
        }

        private static T ReadArgument<T>(FastBufferReader reader)
        {
            // Shortcut for primitives
            if (s_SerializeShortcutTypes.Contains(typeof(T)))
            {
                T primitive = default;
                NetworkVariableSerialization<T>.Read(reader, ref primitive);
                return primitive;
            }
            
            reader.ReadValueSafe(out bool isNull);
            if (isNull)
            {
                return default;
            }

            if (typeof(T).IsAssignableFrom(typeof(VaporNetworkObject)))
            {
                reader.ReadValueSafe(out ulong networkObjectId);
                NetworkMessages.Instance.NetworkObjects.TryGetValue(networkObjectId, out var networkObject);
                return (T)(object)networkObject;
            }
            
            if (typeof(T).IsAssignableFrom(typeof(NetworkBehaviour)))
            {
                reader.ReadValueSafe(out NetworkBehaviourReference networkBehaviourId);
                networkBehaviourId.TryGet(out NetworkBehaviour networkBehaviour);
                return (T)(object)networkBehaviour;
            }

            if (typeof(T).IsAssignableFrom(typeof(INetworkPacket)))
            {
                return PacketHandler.FromPacket<T>(reader);
            }

            if (typeof(T).IsAssignableFrom(typeof(INetworkSerializable)))
            {
                var networkSerializable = (INetworkSerializable)Activator.CreateInstance<T>();
                reader.ReadNetworkSerializableInPlace(ref networkSerializable);
                return (T)networkSerializable;
            }

            T value = default;
            NetworkVariableSerialization<T>.Read(reader, ref value);
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