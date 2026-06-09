using System.Collections.Generic;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.NetworkObjects
{
    public static class NetworkDataRegistry<TData> where TData : VaporNetworkObject
    {
        
        private static readonly Dictionary<NetworkRegistryIdentifier, TData> s_RegistryMap = new Dictionary<NetworkRegistryIdentifier, TData>(256);
        
        static NetworkDataRegistry()
        {
            // subscribe once
            GlobalDataRegistry.OnRegistriesBuilt -= Rebuild;
            GlobalDataRegistry.OnRegistriesBuilt += Rebuild;
            Rebuild();
        }

        private static void Rebuild()
        {
            s_RegistryMap.Clear();
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(NetworkDataRegistry<TData>), nameof(Rebuild))} - {TooltipMarkup.Class(typeof(TData).Name)} - Loaded {s_RegistryMap.Count} Items");
        }

        public static void Register(NetworkRegistryIdentifier id, TData networkObject)
        {
            s_RegistryMap[id] = networkObject;
        }

        public static void Unregister(NetworkRegistryIdentifier id)
        {
            s_RegistryMap.Remove(id);
        }
        
        public static bool Contains(NetworkRegistryIdentifier id) => s_RegistryMap.ContainsKey(id);
        public static bool TryGet(NetworkRegistryIdentifier id, out TData value) => s_RegistryMap.TryGetValue(id, out value);

        public static IEnumerable<TData> GetAll() => s_RegistryMap.Values;

        public static IEnumerable<TData> GetAll(ulong ownerClientId, ulong parentNetworkObjectId)
        {
            foreach (var data in s_RegistryMap.Values)
            {
                if (data.OwnerClientId != ownerClientId)
                {
                    continue;
                }
                
                if (data.ParentNetworkObjectId != parentNetworkObjectId)
                {
                    continue;
                }
                
                yield return data;
            }
        }
    }
}