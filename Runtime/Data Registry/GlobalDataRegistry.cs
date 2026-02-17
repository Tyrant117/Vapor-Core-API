using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Vapor.Inspector;
using Vapor.Unsafe;

namespace Vapor
{
    public static class GlobalDataRegistry
    {
        private static readonly Dictionary<uint, IData> s_RegistryMap = new(2048);
        
        public static event Action OnRegistriesBuilt;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        public static void EditorInitialize()
        {
            Initialize();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Initialize()
        {
            s_RegistryMap.Clear();
            var assetTypes = VaporTypeCache.GetTypesDerivedFrom<IScriptableData>().GetTypesWithAttribute<IsAddressableAttribute>();
            SortedDictionary<int, List<IScriptableData>> assetsByOrder = new();
            List<AsyncOperationHandle<IList<ScriptableObject>>> handles = new();
            foreach (var assetType in assetTypes)
            {
                var atr = assetType.GetCustomAttribute<IsAddressableAttribute>();
                var assets = AddressableAssetUtility.LoadAll<ScriptableObject>(null, new object[] { atr.AddressableLabel }, out var handle);
                int order;
                if (assets.Count > 0)
                {
                    order = ((IScriptableData)assets[0]).GetOrder();
                    handles.Add(handle);
                }
                else
                {
                    handle.Release();
                    continue;
                }

                if (!assetsByOrder.ContainsKey(order))
                {
                    assetsByOrder.Add(order, new List<IScriptableData>());
                }

                // foreach (var asset in assets.OfType<IScriptableData>())
                // {
                //     Debug.Log($"Loaded Asset {asset.Name} | {asset.Key}");
                // }

                assetsByOrder[order].AddRangeUnique(assets.OfType<IScriptableData>());
            }

            // var types = AppDomain.CurrentDomain.GetAssemblies()
            //     .SelectMany(a => a.GetTypes())
            //     .Where(t => typeof(IDataRegistry).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            //     .ToArray();
            var types = VaporTypeCache.GetTypesDerivedFrom<IDataRegistry>().Where(t => !t.IsInterface && !t.IsAbstract);
            SortedDictionary<int, List<IDataRegistry>> registriesByOrder = new();
            foreach (var type in types)
            {
                if (Activator.CreateInstance(type) is not IDataRegistry reg)
                {
                    continue;
                }

                var order = reg.GetOrder();

                if (!registriesByOrder.ContainsKey(order))
                {
                    registriesByOrder.Add(order, new List<IDataRegistry>());
                }

                registriesByOrder[order].Add(reg);
            }
            
            var orders = new List<int>(registriesByOrder.Keys.Count + assetsByOrder.Keys.Count);
            orders.AddRange(registriesByOrder.Keys);
            orders.AddRangeUnique(assetsByOrder.Keys);
            orders.Sort();
            foreach (var order in orders)
            {
                if (registriesByOrder.TryGetValue(order, out var registries))
                {
                    foreach (var reg in registries)
                    {
                        reg.BuildRegistry();
                    }
                }

                if (assetsByOrder.TryGetValue(order, out var assets))
                {
                    foreach (var asset in assets)
                    {
                        asset.Register();
                    }
                }
            }

            foreach (var handle in handles)
            {
                handle.Release();
            }
            handles.Clear();
            assetsByOrder.Clear();
            registriesByOrder.Clear();
            
            OnRegistriesBuilt?.Invoke();
        }
        
        public static void Register(IData data)
        {
            if (s_RegistryMap.TryGetValue(data.Key, out var existing))
            {
                throw new Exception(
                    $"GlobalDataRegistry: Duplicate Key {data.Name} | {data.Key}. Existing={existing.Name}| {existing.Key}. " +
                    $"Existing={existing.GetType().Name}, New={data.GetType().Name}");
            }

            s_RegistryMap[data.Key] = data;
        }

        public static IData Get(uint id) => s_RegistryMap.GetValueOrDefault(id);
        public static IData Get(string id) => Get(id.Hash32());

        public static bool TryGet(uint id, out IData value) => s_RegistryMap.TryGetValue(id, out value);
        public static bool TryGet(string id, out IData value) => TryGet(id.Hash32(), out value);

        public static IEnumerable<IData> GetAll() => s_RegistryMap.Values;
        
        public static TData Get<TData>(uint id) where TData : class, IData => s_RegistryMap.GetValueOrDefault(id) as TData;
        public static TData Get<TData>(string id) where TData : class, IData => Get<TData>(id.Hash32());

        public static bool TryGet<TData>(uint id, out TData value) where TData : IData
        {
            if (s_RegistryMap.TryGetValue(id, out var data) && data is TData typedData)
            {
                value = typedData;
                return true;
            }

            value = default;
            return false;
        }
        public static bool TryGet<TData>(string id, out TData value) where TData : IData => TryGet(id.Hash32(), out value);

        public static IEnumerable<TData> GetAll<TData>() where TData : class => s_RegistryMap.Values.OfType<TData>();

        public static IEnumerable<Type> GetAllTypes() => s_RegistryMap.Values.Select(x => x.GetType()).Distinct();
    }
}