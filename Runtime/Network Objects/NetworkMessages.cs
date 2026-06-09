using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;
using Vapor.Inspector;

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
        private const byte k_SyncMessage = 4;

        private NetworkManager _networkManager;
        private ulong _networkObjectIdCounter;
        public readonly Dictionary<ulong, VaporNetworkObject> NetworkObjects = new();
        private readonly Dictionary<ulong, HashSet<VaporNetworkObject>> _ownerClientIdToNetworkObjectMap = new();
        private readonly HashSet<VaporNetworkObject> _tickableNetworkObjects = new();
        private readonly List<VaporNetworkObject> _networkObjectsToSend = new();

        private void Awake()
        {
            Instance = this;
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
                    if (_networkManager.IsServer)
                    {
                        // Synchronize Network Objects
                        var clientId = arg2.ClientId;
                        foreach (var networkObject in NetworkObjects.Values)
                        {
                            if (networkObject.SpawnedOnlyOnOwner)
                            {
                                continue;
                            }

                            // Spawn
                            SendSpawnMessage(networkObject, clientId);
                        }
                    }

                    break;
                case ConnectionEvent.PeerConnected:
                case ConnectionEvent.ClientDisconnected:
                case ConnectionEvent.PeerDisconnected:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
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
            foreach (var networkObject in _tickableNetworkObjects)
            {
                networkObject.OnTick(deltaTime);
            }

            if (_networkManager.IsServer && _networkObjectsToSend.Count > 0)
            {
                var targetIds = ListPool<ulong>.Get();
                var customMessagingManager = _networkManager.CustomMessagingManager;
                foreach (var clientId in _networkManager.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId)
                    {
                        continue;
                    }

                    targetIds.Add(clientId);
                }

                foreach (var networkObject in _networkObjectsToSend)
                {
                    if (!networkObject.IsSpawned)
                    {
                        continue;
                    }

                    using var writer = new FastBufferWriter(RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, RPC_MESSAGE_MAXIMUM_SIZE);
                    // Write opcode first (4 bytes header)
                    writer.WriteValueSafe(k_SyncMessage);
                    // Write our message source
                    writer.WriteValueSafe(networkObject.NetworkObjectId);
                    // Write our data
                    networkObject.Serialize(writer, false);
                    if (networkObject.SpawnedOnlyOnOwner)
                    {
                        customMessagingManager.SendUnnamedMessage(networkObject.OwnerClientId, writer);
                    }
                    else
                    {
                        customMessagingManager.SendUnnamedMessage(targetIds.AsReadOnly(), writer);
                    }
                }

                _networkObjectsToSend.Clear();
                ListPool<ulong>.Release(targetIds);
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

            SendSpawnMessage(networkObject);
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

            SendSpawnMessage(networkObject);
        }

        private void SendSpawnMessage(VaporNetworkObject networkObject, ulong targetClientId = 0)
        {
            using var writer = new FastBufferWriter(RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, RPC_MESSAGE_MAXIMUM_SIZE);
            var customMessagingManager = _networkManager.CustomMessagingManager;

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
            var packet = PacketHandler.CreatePacket(networkObject, true);
            writer.WriteValueSafe(packet);
            if (!networkObject.SpawnedOnlyOnOwner)
            {
                var targetIds = ListPool<ulong>.Get();
                if (targetClientId != 0)
                {
                    targetIds.Add(targetClientId);
                }
                else
                {
                    foreach (var clientId in _networkManager.ConnectedClientsIds)
                    {
                        if (clientId == NetworkManager.ServerClientId)
                        {
                            continue;
                        }

                        targetIds.Add(clientId);
                    }
                }

                if (targetIds.Count != 0)
                {
                    customMessagingManager.SendUnnamedMessage(targetIds.AsReadOnly(), writer);
                }

                ListPool<ulong>.Release(targetIds);
            }
            else if (!networkObject.IsOwnedByServer)
            {
                customMessagingManager.SendUnnamedMessage(networkObject.OwnerClientId, writer);
            }
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
            if (NetworkObjects.Remove(networkId, out var networkObject))
            {
                DespawnChildren(networkObject);
                RemoveNetworkObjectFromOwnerMap(networkObject);

                var customMessagingManager = _networkManager.CustomMessagingManager;
                if (customMessagingManager == null)
                {
                    return;
                }
                
                using var writer = new FastBufferWriter(RPC_MESSAGE_DEFAULT_SIZE, Allocator.Temp, RPC_MESSAGE_MAXIMUM_SIZE);
                // Write opcode first (1 byte header)
                writer.WriteValueSafe(k_DestroyMessage);
                // Write our message type
                writer.WriteValueSafe(networkObject.NetworkObjectId);

                if (!networkObject.SpawnedOnlyOnOwner)
                {
                    var targetIds = ListPool<ulong>.Get();
                    foreach (var clientId in _networkManager.ConnectedClientsIds)
                    {
                        if (clientId == NetworkManager.ServerClientId)
                        {
                            continue;
                        }

                        targetIds.Add(clientId);
                    }

                    customMessagingManager.SendUnnamedMessage(targetIds.AsReadOnly(), writer);
                    ListPool<ulong>.Release(targetIds);
                }
                else if (!networkObject.IsOwnedByServer)
                {
                    customMessagingManager.SendUnnamedMessage(networkObject.OwnerClientId, writer);
                }
                
                if (!networkObject.IsRoot)
                {
                    if (networkObject.ParentIsUnityObject)
                    {
                        var networkBehaviour = _networkManager.SpawnManager.SpawnedObjects[networkObject.ParentNetworkObjectId]
                            .GetNetworkBehaviourAtOrderIndex(networkObject.ParentNetworkBehaviourOrderIndex);
                        if (networkBehaviour is ISubObjectOwner subObjectOwner)
                        {
                            // subObjectOwner.RemoveSubObject(networkObject);
                            subObjectOwner.OnSubObjectDespawned(networkObject);
                        }
                    }
                    else if (NetworkObjects.TryGetValue(networkObject.ParentNetworkObjectId, out var parentNetworkObject))
                    {
                        // parentNetworkObject.RemoveSubObject(networkObject);
                        parentNetworkObject.SubObjectDespawned(networkObject);
                    }
                }

                networkObject.OnDespawn();
                networkObject.InternalDestroy();
            }

            _tickableNetworkObjects.Remove(networkObject);
        }

        public void Send(VaporNetworkObject networkObject, FastBufferWriter writer, SendTo sendTo, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced)
        {
            // Allocate a temporary writer with some capacity
            var customMessagingManager = _networkManager.CustomMessagingManager;
            if (customMessagingManager == null)
            {
                return;
            }
            
            if (networkObject.SpawnedOnlyOnOwner)
            {
                customMessagingManager.SendUnnamedMessage(networkObject.OwnerClientId, writer);
                return;
            }

            var targetIds = ListPool<ulong>.Get();
            switch (sendTo)
            {
                case SendTo.Owner:
                    customMessagingManager.SendUnnamedMessage(networkObject.OwnerClientId, writer);
                    break;
                case SendTo.NotOwner:
                    foreach (var clientId in _networkManager.ConnectedClientsIds)
                    {
                        if (clientId == networkObject.OwnerClientId)
                        {
                            continue;
                        }

                        targetIds.Add(clientId);
                    }
                    customMessagingManager.SendUnnamedMessage(targetIds, writer, networkDelivery);
                    break;
                case SendTo.Server:
                    customMessagingManager.SendUnnamedMessage(NetworkManager.ServerClientId, writer);
                    break;
                case SendTo.NotServer:
                    foreach (var clientId in _networkManager.ConnectedClientsIds)
                    {
                        if (clientId == NetworkManager.ServerClientId)
                        {
                            continue;
                        }

                        targetIds.Add(clientId);
                    }
                    customMessagingManager.SendUnnamedMessage(targetIds, writer, networkDelivery);
                    break;
                case SendTo.Me:
                    customMessagingManager.SendUnnamedMessage(_networkManager.LocalClientId, writer);
                    break;
                case SendTo.NotMe:
                    foreach (var clientId in _networkManager.ConnectedClientsIds)
                    {
                        if (clientId == _networkManager.LocalClientId)
                        {
                            continue;
                        }

                        targetIds.Add(clientId);
                    }
                    customMessagingManager.SendUnnamedMessage(targetIds, writer, networkDelivery);
                    break;
                case SendTo.Everyone:
                    customMessagingManager.SendUnnamedMessage(_networkManager.ConnectedClientsIds, writer, networkDelivery);
                    break;
                case SendTo.ClientsAndHost:
                    foreach (var clientId in _networkManager.ConnectedClientsIds)
                    {
                        if (clientId == NetworkManager.ServerClientId && !_networkManager.IsHost)
                        {
                            continue;
                        }

                        targetIds.Add(clientId);
                    }
                    customMessagingManager.SendUnnamedMessage(targetIds, writer, networkDelivery);
                    break;
                case SendTo.Authority:
                    customMessagingManager.SendUnnamedMessage(NetworkManager.ServerClientId, writer);
                    break;
                case SendTo.NotAuthority:
                    foreach (var clientId in _networkManager.ConnectedClientsIds)
                    {
                        if (clientId == NetworkManager.ServerClientId)
                        {
                            continue;
                        }

                        targetIds.Add(clientId);
                    }
                    customMessagingManager.SendUnnamedMessage(targetIds, writer, networkDelivery);
                    break;
                case SendTo.SpecifiedInParams:
                default:
                    throw new ArgumentOutOfRangeException(nameof(sendTo), sendTo, null);
            }
            ListPool<ulong>.Release(targetIds);
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
                    var type = PacketRegistry.GetType(typeCode);
                    var networkObject = (VaporNetworkObject)Activator.CreateInstance(type, true);

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
                    
                    NetworkObjects.Add(networkId, networkObject);
                    AddNetworkObjectToOwnerMap(networkObject);
                    if (networkObject.ShouldTick)
                    {
                        _tickableNetworkObjects.Add(networkObject);
                    }
                    
                    networkObject.InternalInitialize();
                    networkObject.OnPreSpawn();
                    
                    // Deserialize Full Packet
                    reader.ReadValueSafe(out NativeArray<byte> packet, Allocator.Temp);
                    PacketHandler.FromPacket(ref networkObject, packet);
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
                case k_SyncMessage:
                {
                    reader.ReadValueSafe(out ulong networkId);
                    if (!NetworkObjects.TryGetValue(networkId, out var networkObject))
                    {
                        reader.Seek(reader.Length);
                        reader.Dispose();
                        return;
                    }

                    networkObject.Deserialize(reader, out _);
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
            if (networkObject.SubObjects == null)
            {
                return;
            }

            foreach (var childNetworkObjectId in networkObject.SubObjects)
            {
                DestroyNetworkObject(childNetworkObjectId.NetworkObjectId);
            }
        }
    }
}
