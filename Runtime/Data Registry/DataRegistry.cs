using System.Collections.Generic;
using UnityEngine;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor
{
    public static class DataRegistry<TData> where TData : class, IData
    {
        private static readonly Dictionary<uint, TData> s_RegistryMap = new Dictionary<uint, TData>(256);

        static DataRegistry()
        {
            // subscribe once
            GlobalDataRegistry.OnRegistriesBuilt -= Rebuild;
            GlobalDataRegistry.OnRegistriesBuilt += Rebuild;
            Rebuild();
        }

        private static void Rebuild()
        {
            s_RegistryMap.Clear();
            foreach (var data in GlobalDataRegistry.GetAll<TData>())
            {
                s_RegistryMap[data.Key] = data;
            }
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(DataRegistry<TData>), nameof(Rebuild))} - {TooltipMarkup.Class(typeof(TData).Name)} - Loaded {s_RegistryMap.Count} Items");
        }
        
        public static TData Get(uint id) => s_RegistryMap.GetValueOrDefault(id);

        public static bool TryGet(uint id, out TData value) => s_RegistryMap.TryGetValue(id, out value);

        public static IEnumerable<TData> GetAll() => s_RegistryMap.Values;
    }
}
