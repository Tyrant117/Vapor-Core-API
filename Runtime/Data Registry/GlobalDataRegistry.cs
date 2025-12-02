using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Vapor
{
    public static class GlobalDataRegistry
    {
        private static readonly Dictionary<uint, IData> s_RegistryMap = new(2048);
        private static readonly List<IDataRegistry> s_Registries = new();
        
        public static event Action OnRegistriesBuilt;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Initialize()
        {
            s_RegistryMap.Clear();
            s_Registries.Clear();

            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IDataRegistry).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToArray();

            foreach (var type in types)
            {
                var reg = Activator.CreateInstance(type) as IDataRegistry;
                AddRegistry(reg);
            }

            OrderRegistries();
            OnRegistriesBuilt?.Invoke();
        }

        public static void AddRegistry(IDataRegistry registry)
        {
            s_Registries.Add(registry);
        }
        
        public static void OrderRegistries()
        {
            foreach (var registry in s_Registries.OrderBy(r => r.Order))
            {
                registry.BuildRegistry();
            }
        }
        
        public static void Register(IData data)
        {
            if (s_RegistryMap.TryGetValue(data.Key, out var existing))
            {
                throw new Exception(
                    $"GlobalDataRegistry: Duplicate key {data.Key}. " +
                    $"Existing={existing.GetType().Name}, New={data.GetType().Name}");
            }

            s_RegistryMap[data.Key] = data;
        }

        public static IData Get(uint id)
        {
            return s_RegistryMap.GetValueOrDefault(id);
        }
        
        public static bool TryGet(uint id, out IData value)
        {
            return s_RegistryMap.TryGetValue(id, out value);
        }

        public static IEnumerable<IData> GetAll() => s_RegistryMap.Values;
        
        public static TData Get<TData>(uint id) where TData : class => s_RegistryMap.GetValueOrDefault(id) as TData;

        public static bool TryGet<TData>(uint id, out TData value) where TData : class
        {
            if (s_RegistryMap.TryGetValue(id, out var data) && data is TData typedData)
            {
                value = typedData;
                return true;
            }

            value = null;
            return false;
        }

        public static IEnumerable<TData> GetAll<TData>() where TData : class => s_RegistryMap.Values.OfType<TData>();

        public static IEnumerable<Type> GetAllTypes() => s_RegistryMap.Values.Select(x => x.GetType()).Distinct();
    }
}