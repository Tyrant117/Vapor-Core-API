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

        /// <summary>
        /// The keys each authored document put in the map, so one document can be swapped out without
        /// disturbing the rest.
        /// </summary>
        /// <remarks>
        /// This is what makes a save cheap. Re-ingesting one document means dropping exactly the keys
        /// it contributed last time and adding the ones it contributes now; without the record there is
        /// no way to tell a key that has been deleted from one that was never in this file, and the only
        /// safe answer is to rebuild everything.
        /// </remarks>
        private static readonly Dictionary<Type, List<uint>> s_DocumentKeys = new();

        /// <summary>Raised when a full rebuild finishes. Not raised for a single document changing.</summary>
        /// <remarks>
        /// Anything that only needs to know something moved should use <see cref="OnRegistryChanged"/>
        /// instead. A per-type <see cref="DataRegistry{TData}"/> stays on this one because its whole-map
        /// rescan is only correct — and only affordable — as a response to the map being rebuilt.
        /// </remarks>
        public static event Action OnRegistriesBuilt;

        /// <summary>
        /// Raised after any change to the map, whether a full rebuild or one document being replaced.
        /// </summary>
        /// <remarks>
        /// The signal for a lazily built cache to drop what it has. Invalidating is O(1) and rebuilding
        /// happens on the next read, which is what lets a save leave the dropdown cache and the tag tree
        /// alone until something actually asks for them.
        /// </remarks>
        public static event Action OnRegistryChanged;

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

            /// <summary>
            /// The document that was being ingested when this was seen, or null for anything else.
            /// </summary>
            /// <remarks>
            /// Recorded so replacing one document can retire the collisions it caused. Without it a
            /// collision fixed by an edit would stay in the list until the next full rebuild, and a
            /// validator would fail a build on a problem that no longer exists.
            /// </remarks>
            public readonly Type Source;

            /// <summary>True when the same name was registered twice, rather than two names colliding.</summary>
            public bool IsDuplicateName => ExistingName == DroppedName;

            public KeyCollision(uint key, string existingName, string droppedName, string existingType, string droppedType)
                : this(key, existingName, droppedName, existingType, droppedType, null)
            {
            }

            public KeyCollision(uint key, string existingName, string droppedName, string existingType, string droppedType, Type source)
            {
                Key = key;
                ExistingName = existingName;
                DroppedName = droppedName;
                ExistingType = existingType;
                DroppedType = droppedType;
                Source = source;
            }
        }

        private static readonly List<KeyCollision> s_Collisions = new();

        /// <summary>Collisions seen while building the current registry. Cleared on each rebuild.</summary>
        public static IReadOnlyList<KeyCollision> Collisions => s_Collisions;

        /// <summary>The document whose entries are currently being ingested, for collision reporting.</summary>
        private static Type s_IngestingDocument;

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
            s_DocumentKeys.Clear();

            SortedDictionary<int, List<IScriptableData>> assetsByOrder = new();
            List<AsyncOperationHandle<ScriptableObject>> handles = new();

            using (VslSaveDiagnostics.Measure("assets"))
            {
                var assetTypes = VaporTypeCache.GetTypesDerivedFrom<IScriptableData>().GetTypesWithAttribute<IsAddressableAttribute>().Where(t => !t.IsInterface && !t.IsAbstract);
                var seenAssets = new HashSet<IScriptableData>();
                foreach (var assetType in assetTypes)
                {
                    var atr = assetType.GetCustomAttribute<IsAddressableAttribute>();
                    var assets = AddressableAssetUtility.LoadAll<ScriptableObject>(new object[] { atr.AddressableLabel }, out var handle);
                    if (assets == null)
                    {
                        continue;
                    }

                    // LoadAll returns null (handled above) when nothing is found, so assets is always
                    // non-empty here. Order is treated as type-level: every asset of this type is
                    // expected to return the same GetOrder(), so it is read from the first.
                    int order = ((IScriptableData)assets[0]).GetOrder();
                    handles.AddRange(handle);

                    if (!assetsByOrder.TryGetValue(order, out var bucket))
                    {
                        bucket = new List<IScriptableData>();
                        assetsByOrder.Add(order, bucket);
                    }

                    foreach (var asset in assets)
                    {
                        // Deduplicated through a set rather than by scanning the bucket: one asset can be
                        // reached under more than one label, and the linear scan was quadratic in the
                        // number of assets of a type.
                        if (asset is IScriptableData data && seenAssets.Add(data))
                        {
                            bucket.Add(data);
                        }
                    }
                }
            }

            SortedDictionary<int, List<IDataRegistry>> registriesByOrder = new();
            using (VslSaveDiagnostics.Measure("code registries"))
            {
                var types = VaporTypeCache.GetTypesDerivedFrom<IDataRegistry>().Where(t => !t.IsInterface && !t.IsAbstract);
                foreach (var type in types)
                {
                    if (Activator.CreateInstance(type) is not IDataRegistry reg)
                    {
                        continue;
                    }

                    var order = reg.GetOrder();

                    if (!registriesByOrder.TryGetValue(order, out var bucket))
                    {
                        bucket = new List<IDataRegistry>();
                        registriesByOrder.Add(order, bucket);
                    }

                    bucket.Add(reg);
                }
            }

            // The third source: data authored as text rather than as code or as assets. Grouped by
            // the same order scale so a VSL document can be sequenced against the code registries -
            // gameplay tags at -500 before attributes at -300 - instead of always landing last.
            SortedDictionary<int, List<(Type Owner, List<IData> Entries)>> documentsByOrder = new();
            List<AsyncOperationHandle<TextAsset>> documentHandles = new();
            using (VslSaveDiagnostics.Measure("read documents"))
            {
                foreach (var (owner, order, entries) in VslDataStore.ReadAll(documentHandles))
                {
                    if (!documentsByOrder.TryGetValue(order, out var bucket))
                    {
                        bucket = new List<(Type, List<IData>)>();
                        documentsByOrder.Add(order, bucket);
                    }

                    bucket.Add((owner, entries));
                }
            }

            using (VslSaveDiagnostics.Measure("register"))
            {
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
                        foreach (var (owner, entries) in documents)
                        {
                            var keys = TrackDocument(owner);
                            s_IngestingDocument = owner;
                            foreach (var data in entries)
                            {
                                if (Register(data) && keys != null)
                                {
                                    keys.Add(data.Key);
                                }
                            }

                            s_IngestingDocument = null;
                        }
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

            using (VslSaveDiagnostics.Measure("notify"))
            {
                OnRegistriesBuilt?.Invoke();
                OnRegistryChanged?.Invoke();
            }
        }

        /// <summary>The key list a document writes into, created on first use.</summary>
        private static List<uint> TrackDocument(Type owner)
        {
            if (owner == null)
            {
                return null;
            }

            if (!s_DocumentKeys.TryGetValue(owner, out var keys))
            {
                keys = new List<uint>();
                s_DocumentKeys.Add(owner, keys);
            }

            return keys;
        }

        /// <summary>
        /// Swaps one authored document's entries for a new set, leaving every other source alone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What a save calls instead of <see cref="Initialize"/>. Saving a <c>.vsl</c> cannot change a
        /// code registry or an addressable asset, so reloading those — and re-reading every other
        /// document to do it — is work with a guaranteed empty result. The typed registries stay exact
        /// because they follow <see cref="OnDataRegistered"/> and <see cref="OnDataUnregistered"/>
        /// rather than rescanning.
        /// </para>
        /// <para>
        /// Build order does not apply here: nothing is being built, one document's contribution is being
        /// exchanged in place. A change that needs the orders honoured again — a new code registry, an
        /// asset added — is a full rebuild, which is what the Rebuild Registry button is for.
        /// </para>
        /// </remarks>
        public static void ReplaceDocument(Type owner, IReadOnlyList<IData> entries)
        {
            if (owner == null)
            {
                Debug.LogError($"{nameof(GlobalDataRegistry)}: a document has to name the type that owns it to be replaced.");
                return;
            }

            using (VslSaveDiagnostics.Measure("registry"))
            {
                var keys = TrackDocument(owner);
                var incoming = entries ?? Array.Empty<IData>();

                // Worked out as a difference rather than as a wholesale swap. Editing one field of one
                // entry leaves every other key exactly where it was, and firing register/unregister for
                // all of them anyway would cost one dictionary operation per entry per closed
                // DataRegistry<TData> - which is the multiplicative cost the full rebuild was made of.
                var held = new HashSet<uint>(keys);
                var arriving = new HashSet<uint>();
                foreach (var data in incoming)
                {
                    if (data != null)
                    {
                        arriving.Add(data.Key);
                    }
                }

                // Departures first, so a save that moves a key from one entry to another - which is what
                // two renames crossing over look like - is not read as a collision with itself.
                foreach (var key in keys)
                {
                    if (!arriving.Contains(key) && s_RegistryMap.Remove(key, out var removed))
                    {
                        OnDataUnregistered?.Invoke(removed);
                    }
                }

                keys.Clear();

                // The collisions this document caused go with it. Anything still wrong will be reported
                // again on the way back in, so what survives is only what is still true.
                s_Collisions.RemoveAll(c => c.Source == owner);

                s_IngestingDocument = owner;
                var claimed = new HashSet<uint>();
                foreach (var data in incoming)
                {
                    if (data == null)
                    {
                        Debug.LogError($"{nameof(GlobalDataRegistry)}: {owner.Name} holds a null entry, which was skipped.");
                        continue;
                    }

                    // Two entries of one document under one key is a duplicate however the key got
                    // there, so it is caught before the cheaper paths can quietly absorb it.
                    if (!claimed.Add(data.Key) || !held.Contains(data.Key))
                    {
                        // Either a duplicate within the document, or a key this document did not have
                        // last time - both of which Register is already the right judge of.
                        if (Register(data))
                        {
                            keys.Add(data.Key);
                        }

                        continue;
                    }

                    // A key this document already owned. The same object means nothing about it moved
                    // and nobody needs telling; a different object under the same name - the first save
                    // after a window opened its own copy - means the map has to be re-pointed, or the
                    // typed registries go on answering with the copy that was just replaced.
                    if (s_RegistryMap.TryGetValue(data.Key, out var existing) && !ReferenceEquals(existing, data))
                    {
                        s_RegistryMap[data.Key] = data;
                        keys.Add(data.Key);
                        OnDataRegistered?.Invoke(data);
                        continue;
                    }

                    if (existing == null)
                    {
                        // Recorded as ours but not actually in the map. Nothing should do that, so treat
                        // it as new rather than trusting the record.
                        if (Register(data))
                        {
                            keys.Add(data.Key);
                        }

                        continue;
                    }

                    keys.Add(data.Key);
                }

                s_IngestingDocument = null;
            }

            using (VslSaveDiagnostics.Measure("notify"))
            {
                OnRegistryChanged?.Invoke();
            }
        }

        /// <summary>Every key currently contributed by an authored document.</summary>
        /// <remarks>
        /// The complement is what a window calls "external": registered by code or by an asset, and so
        /// not editable here. Answering from the map means the windows no longer re-read the whole data
        /// folder to work it out.
        /// </remarks>
        public static IEnumerable<uint> GetDocumentKeys()
        {
            foreach (var keys in s_DocumentKeys.Values)
            {
                foreach (var key in keys)
                {
                    yield return key;
                }
            }
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

        /// <summary>
        /// Raised for each data object as it leaves the registry.
        /// </summary>
        /// <remarks>
        /// The counterpart to <see cref="OnDataRegistered"/>, and the half that was missing. Without it
        /// a typed registry could only learn about a deletion by rebuilding from scratch, which is why
        /// every save used to.
        /// </remarks>
        public static event Action<IData> OnDataUnregistered;

        /// <summary>Adds data to the registry. Returns false when the key was already taken.</summary>
        public static bool Register(IData data)
        {
            if (data == null)
            {
                Debug.LogError("GlobalDataRegistry: Attempted to register a null IData.");
                return false;
            }

            if (s_RegistryMap.TryGetValue(data.Key, out var existing))
            {
                bool sameName = existing.Name == data.Name;
                Debug.LogError($"GlobalDataRegistry: {(sameName ? "Duplicate Key" : "Hash collision")} {data.Name} | {data.Key}." +
                               $" Existing={existing.GetType().Name} ({existing.Name}), New={data.GetType().Name} ({data.Name})");
                s_Collisions.Add(new KeyCollision(data.Key, existing.Name, data.Name, existing.GetType().Name, data.GetType().Name, s_IngestingDocument));
                return false;
            }

            s_RegistryMap[data.Key] = data;
            OnDataRegistered?.Invoke(data);
            return true;
        }

        /// <summary>Removes data from the registry by key. Returns false when nothing was there.</summary>
        public static bool Unregister(uint key)
        {
            if (!s_RegistryMap.Remove(key, out var removed))
            {
                return false;
            }

            OnDataUnregistered?.Invoke(removed);
            return true;
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

        /// <summary>How many entries the registry holds. Cheaper than counting <see cref="GetAll"/>.</summary>
        public static int Count => s_RegistryMap.Count;

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
