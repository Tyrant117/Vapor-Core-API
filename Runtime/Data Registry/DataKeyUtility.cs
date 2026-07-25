using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vapor.GameplayTags;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor
{
    /// <summary>
    /// Registry-backed source for inspector key/tag dropdowns. Replaces the old reflection-over-generated-classes
    /// <c>KeyUtility</c>: builds <see cref="DropdownModel"/>s directly from every <see cref="IData"/> registered in
    /// <see cref="GlobalDataRegistry"/>. Because <see cref="GameplayTag"/> is the lowest-level identifier over the
    /// whole <c>uint = Hash32(name)</c> space, every registered data name is a valid tag: the general picker
    /// enumerates all data while category / type-name filters narrow it.
    /// </summary>
    public static class DataKeyUtility
    {
        private static bool s_Cached;
        private static bool s_Subscribed;

        private static readonly List<DropdownModel> s_AllDropdownModels = new();
        private static readonly Dictionary<string, List<DropdownModel>> s_CachedCategories = new();
        private static readonly Dictionary<string, List<DropdownModel>> s_CachedTypeNames = new();

        private static void Invalidate() => s_Cached = false;

        private static void EnsureInitialized()
        {
            if (!s_Subscribed)
            {
                // Rebuilding the registry (e.g. after regenerating data keys) must refresh the dropdown cache.
                GlobalDataRegistry.OnRegistriesBuilt += Invalidate;
                s_Subscribed = true;
            }

            if (s_Cached)
            {
                return;
            }

            s_AllDropdownModels.Clear();
            s_CachedCategories.Clear();
            s_CachedTypeNames.Clear();

            // Inspectors draw before play mode, so make sure the registry has been built at least once.
            if (!GlobalDataRegistry.GetAll().Any())
            {
                GlobalDataRegistry.Initialize();
            }

            // Pre-seed category / type-name buckets from the known IData types (mirrors how KeyUtility.Init used
            // VaporTypeCache over IKeysProvider) so category lookups resolve even for types with no instances yet.
            foreach (var type in VaporTypeCache.GetTypesDerivedFrom<IData>())
            {
                if (type.IsInterface || type.IsAbstract)
                {
                    continue;
                }

                var (scriptName, category) = KeyGenerator.DeriveScriptAndCategory(type, type.GetCustomAttribute<KeyOptionsAttribute>());
                s_CachedCategories.TryAdd(category, new List<DropdownModel>());
                s_CachedTypeNames.TryAdd(scriptName, new List<DropdownModel>());
            }

            foreach (var data in GlobalDataRegistry.GetAll())
            {
                if (data.Key == 0)
                {
                    continue;
                }

                var type = data.GetType();
                var (scriptName, category) = KeyGenerator.DeriveScriptAndCategory(type, type.GetCustomAttribute<KeyOptionsAttribute>());

                var tooltip = (data as GameplayTagData)?.EditorTooltip;
                if (string.IsNullOrEmpty(tooltip))
                {
                    tooltip = data.Name;
                }

                // Value carries a GameplayTag (the lowest-level primitive) so consumers cast (GameplayTag)model.Value.
                var model = new DropdownModel(data.Name, (GameplayTag)data.Key, tooltip);
                s_AllDropdownModels.Add(model);

                if (!s_CachedCategories.TryGetValue(category, out var categoryList))
                {
                    categoryList = new List<DropdownModel>();
                    s_CachedCategories[category] = categoryList;
                }
                categoryList.Add(model);

                if (!s_CachedTypeNames.TryGetValue(scriptName, out var typeList))
                {
                    typeList = new List<DropdownModel>();
                    s_CachedTypeNames[scriptName] = typeList;
                }
                typeList.Add(model);
            }

            s_Cached = true;
        }

        /// <summary>Every registered data entry as a dropdown model. This is the full tag universe.</summary>
        public static List<DropdownModel> GetAllDropdownModels()
        {
            EnsureInitialized();
            return s_AllDropdownModels;
        }

        /// <summary>Dropdown models for a single category (e.g. "Attributes", "AnimationMontages", "GameplayTags").</summary>
        public static List<DropdownModel> GetAllKeysFromCategory(string category)
        {
            EnsureInitialized();
            if (s_CachedCategories.TryGetValue(category, out var list) && list.Count > 0)
            {
                return list;
            }

            return new List<DropdownModel> { new("None", GameplayTag.None(), "None") };
        }

        /// <summary>Dropdown models for a single generated-script name (e.g. "AttributeKeys").</summary>
        public static List<DropdownModel> GetAllKeysFromTypeName(string typeName)
        {
            EnsureInitialized();
            if (s_CachedTypeNames.TryGetValue(typeName, out var list) && list.Count > 0)
            {
                return list;
            }

            return new List<DropdownModel> { new("None", GameplayTag.None(), "None") };
        }
    }
}
