using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Vapor.Inspector;
using Vapor.Unsafe;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Vapor
{
    [NoAutoStaticsCleanup]
    public static class GlobalDataRegistry
    {
        private static readonly Dictionary<uint, IData> s_RegistryMap = new(2048);

        public static event Action OnRegistriesBuilt;

        /// <summary>
        /// Two names that hashed to the same key, so the second was dropped.
        /// </summary>
        /// <remarks>
        /// Recorded as well as logged. Keys are a 32-bit hash of the name, and by the birthday bound a
        /// project with a few thousand entries has a real chance of one collision — at which point a
        /// quest or recipe silently does not exist, behind a single console line nobody reads. Keeping
        /// the list lets an editor validator fail the build on it instead.
        /// </remarks>
        public readonly struct KeyCollision
        {
            public readonly uint Key;
            public readonly string ExistingName;
            public readonly string DroppedName;
            public readonly string ExistingType;
            public readonly string DroppedType;

            /// <summary>True when the same name was registered twice, rather than two names colliding.</summary>
            public bool IsDuplicateName => ExistingName == DroppedName;

            public KeyCollision(uint key, string existingName, string droppedName, string existingType, string droppedType)
            {
                Key = key;
                ExistingName = existingName;
                DroppedName = droppedName;
                ExistingType = existingType;
                DroppedType = droppedType;
            }
        }

        private static readonly List<KeyCollision> s_Collisions = new();

        /// <summary>Collisions seen while building the current registry. Cleared on each rebuild.</summary>
        public static IReadOnlyList<KeyCollision> Collisions => s_Collisions;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        public static void EditorInitialize()
        {
            // Defer out of the InitializeOnLoad critical path. Addressables may not be ready
            // during a domain reload, and the synchronous loads in Initialize() would otherwise
            // stall the editor on every recompile.
            EditorApplication.delayCall += Initialize;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        public static void Initialize()
        {
            s_RegistryMap.Clear();
            s_Collisions.Clear();
            var assetTypes = VaporTypeCache.GetTypesDerivedFrom<IScriptableData>().GetTypesWithAttribute<IsAddressableAttribute>().Where(t => !t.IsInterface && !t.IsAbstract);
            SortedDictionary<int, List<IScriptableData>> assetsByOrder = new();
            List<AsyncOperationHandle<ScriptableObject>> handles = new();
            foreach (var assetType in assetTypes)
            {
                var atr = assetType.GetCustomAttribute<IsAddressableAttribute>();
                var assets = AddressableAssetUtility.LoadAll<ScriptableObject>(null, new object[] { atr.AddressableLabel }, out var handle);
                if (assets == null)
                {
                    continue;
                }
                
                // LoadAll returns null (handled above) when nothing is found, so assets is always
                // non-empty here. Order is treated as type-level: every asset of this type is
                // expected to return the same GetOrder(), so it is read from the first.
                Debug.Log(((IScriptableData)assets[0]).Name);
                int order = ((IScriptableData)assets[0]).GetOrder();
                handles.AddRange(handle);

                if (!assetsByOrder.ContainsKey(order))
                {
                    assetsByOrder.Add(order, new List<IScriptableData>());
                }

                foreach (var asset in assets)
                {
                    if(asset is not IScriptableData data)
                    {
                        continue;
                    }

                    if (assetsByOrder[order].Contains(data))
                    {
                        continue;
                    }

                    assetsByOrder[order].Add(data);
                }
            }

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

            // The third source: data authored as text rather than as code or as assets. Grouped by
            // the same order scale so a VSL document can be sequenced against the code registries -
            // gameplay tags at -500 before attributes at -300 - instead of always landing last.
            SortedDictionary<int, List<IData>> documentsByOrder = new();
            List<AsyncOperationHandle<TextAsset>> documentHandles = new();
            foreach (var (_, order, entries) in VslDataStore.ReadAll(documentHandles))
            {
                if (!documentsByOrder.TryGetValue(order, out var bucket))
                {
                    bucket = new List<IData>();
                    documentsByOrder.Add(order, bucket);
                }

                bucket.AddRange(entries);
            }

            var orders = new List<int>(registriesByOrder.Keys.Count + assetsByOrder.Keys.Count + documentsByOrder.Keys.Count);
            orders.AddRange(registriesByOrder.Keys);
            orders.AddRangeUnique(assetsByOrder.Keys);
            orders.AddRangeUnique(documentsByOrder.Keys);
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

                if (documentsByOrder.TryGetValue(order, out var documents))
                {
                    foreach (var data in documents)
                    {
                        Register(data);
                    }
                }
            }

            foreach (var handle in handles)
            {
                handle.Release();
            }
            handles.Clear();

            foreach (var handle in documentHandles)
            {
                handle.Release();
            }
            documentHandles.Clear();

            assetsByOrder.Clear();
            registriesByOrder.Clear();
            documentsByOrder.Clear();

            OnRegistriesBuilt?.Invoke();
        }
        
        /// <summary>
        /// Raised for each data object as it is registered.
        /// </summary>
        /// <remarks>
        /// Where <see cref="OnRegistriesBuilt"/> says a whole build finished, this says one object
        /// arrived. It exists so a per-type <see cref="DataRegistry{TData}"/> can pick up something
        /// registered after its build — code-built data, a test fixture — instead of answering lookups
        /// for it with a miss until the next full rebuild.
        /// </remarks>
        public static event Action<IData> OnDataRegistered;

        public static void Register(IData data)
        {
            if (data == null)
            {
                Debug.LogError("GlobalDataRegistry: Attempted to register a null IData.");
                return;
            }

            if (s_RegistryMap.TryGetValue(data.Key, out var existing))
            {
                bool sameName = existing.Name == data.Name;
                Debug.LogError($"GlobalDataRegistry: {(sameName ? "Duplicate Key" : "Hash collision")} {data.Name} | {data.Key}." +
                               $" Existing={existing.GetType().Name} ({existing.Name}), New={data.GetType().Name} ({data.Name})");
                s_Collisions.Add(new KeyCollision(data.Key, existing.Name, data.Name, existing.GetType().Name, data.GetType().Name));
                return;
            }

            s_RegistryMap[data.Key] = data;
            OnDataRegistered?.Invoke(data);
        }

        public static IData Get(uint id) => s_RegistryMap.GetValueOrDefault(id);
        public static IData Get(string id) => string.IsNullOrEmpty(id) ? null : Get(id.Hash32());

        public static bool TryGet(uint id, out IData value) => s_RegistryMap.TryGetValue(id, out value);
        public static bool TryGet(string id, out IData value)
        {
            if (string.IsNullOrEmpty(id))
            {
                value = default;
                return false;
            }
            return TryGet(id.Hash32(), out value);
        }

        public static IEnumerable<IData> GetAll() => s_RegistryMap.Values;
        
        public static TData Get<TData>(uint id) where TData : class, IData => s_RegistryMap.GetValueOrDefault(id) as TData;
        public static TData Get<TData>(string id) where TData : class, IData => string.IsNullOrEmpty(id) ? null : Get<TData>(id.Hash32());

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
        public static bool TryGet<TData>(string id, out TData value) where TData : IData
        {
            if (string.IsNullOrEmpty(id))
            {
                value = default;
                return false;
            }
            return TryGet(id.Hash32(), out value);
        }

        public static IEnumerable<TData> GetAll<TData>() where TData : class => s_RegistryMap.Values.OfType<TData>();

        public static IEnumerable<Type> GetAllTypes() => s_RegistryMap.Values.Select(x => x.GetType()).Distinct();
    }
}
