using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Vapor.Inspector;
using Vapor.Unsafe;

namespace Vapor
{
    [NoAutoStaticsCleanup]
    public static class DataRegistry<TData> where TData : class, IData
    {
        private static readonly Dictionary<uint, TData> s_RegistryMap = new(256);

        static DataRegistry()
        {
            // subscribe once
            GlobalDataRegistry.OnRegistriesBuilt -= Rebuild;
            GlobalDataRegistry.OnRegistriesBuilt += Rebuild;
            GlobalDataRegistry.OnDataRegistered -= Add;
            GlobalDataRegistry.OnDataRegistered += Add;
            GlobalDataRegistry.OnDataUnregistered -= Remove;
            GlobalDataRegistry.OnDataUnregistered += Remove;
            Rebuild();
        }

        /// <summary>
        /// Rescans the whole global map. Only correct as a response to that map being rebuilt.
        /// </summary>
        /// <remarks>
        /// Costs one pass over every registered entry, per closed generic — so a project with fifty of
        /// these pays fifty passes. That is affordable once on a code load and nowhere else, which is
        /// why saving a document goes through <see cref="Add"/> and <see cref="Remove"/> instead.
        /// </remarks>
        private static void Rebuild()
        {
            s_RegistryMap.Clear();
            foreach (var data in GlobalDataRegistry.GetAll<TData>())
            {
                s_RegistryMap[data.Key] = data;
            }

            if (VslSaveDiagnostics.Verbose)
            {
                Debug.Log($"{TooltipMarkup.ClassMethod(nameof(DataRegistry<TData>), nameof(Rebuild))} - {TooltipMarkup.Class(typeof(TData).Name)} - Loaded {s_RegistryMap.Count} Items");
            }
        }

        /// <summary>
        /// Takes a single registration as it happens, so data registered after this map was built is
        /// findable straight away rather than at the next rebuild.
        /// </summary>
        private static void Add(IData data)
        {
            if (data is TData typed)
            {
                s_RegistryMap[typed.Key] = typed;
            }
        }

        /// <summary>
        /// Drops a single entry as it leaves the global registry.
        /// </summary>
        /// <remarks>
        /// The half that lets a document be re-ingested without a rebuild: an entry deleted or renamed
        /// in the window leaves here at the same moment it leaves the global map, so a stale key is
        /// never answerable from this side.
        /// </remarks>
        private static void Remove(IData data)
        {
            if (data is TData typed)
            {
                s_RegistryMap.Remove(typed.Key);
            }
        }

        public static TData Get(uint id) => s_RegistryMap.GetValueOrDefault(id);

        public static TData Get(string id) => string.IsNullOrEmpty(id) ? null : Get(id.Hash32());

        public static bool TryGet(uint id, out TData value) => s_RegistryMap.TryGetValue(id, out value);

        public static bool TryGet(string id, out TData value)
        {
            if (string.IsNullOrEmpty(id))
            {
                value = default;
                return false;
            }
            return TryGet(id.Hash32(), out value);
        }

        public static IEnumerable<TData> GetAll() => s_RegistryMap.Values;
    }
}
