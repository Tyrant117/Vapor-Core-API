using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.Keys
{
    public static class KeyUtility
    {
#if UNITY_EDITOR
        private static bool s_Cached;
        [InitializeOnLoadMethod]
        private static void InitEditor()
        {
            if (s_Cached)
            {
                return;
            }
            s_CachedFieldInfos.Clear();
            s_CachedCategories.Clear();
            s_CachedTypeNames.Clear();

            foreach (var keysType in TypeCache.GetTypesDerivedFrom<IKeysProvider>())
            {
                s_CachedFieldInfos.TryAdd(keysType, GetFieldInfo(keysType));
                var category = GetKeyCategory(keysType);
                if (!s_CachedCategories.TryGetValue(category, out var typeSet))
                {
                    typeSet = new HashSet<Type>();
                    s_CachedCategories[category] = typeSet;
                }
                if (!s_CachedTypeNames.TryGetValue(keysType.Name, out var nameSet))
                {
                    nameSet = new HashSet<Type>();
                    s_CachedTypeNames[keysType.Name] = nameSet;
                }
                typeSet.Add(keysType);
                nameSet.Add(keysType);
            }
            s_Cached = true;
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(KeyUtility), nameof(InitEditor))} - Cached Keys");
        }
        #endif

        private static bool s_RuntimeCached;
        private static void InitRuntime()
        {
            if (s_RuntimeCached)
            {
                return;
            }
            s_CachedFieldInfos.Clear();
            s_CachedCategories.Clear();
            s_CachedTypeNames.Clear();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (typeof(IKeysProvider).IsAssignableFrom(type) && !type.IsInterface)
                    {
                        s_CachedFieldInfos.TryAdd(type, GetFieldInfo(type));
                        var category = GetKeyCategory(type);
                        if (!s_CachedCategories.TryGetValue(category, out var typeSet))
                        {
                            typeSet = new HashSet<Type>();
                            s_CachedCategories[category] = typeSet;
                        }
                        if (!s_CachedTypeNames.TryGetValue(type.Name, out var nameSet))
                        {
                            nameSet = new HashSet<Type>();
                            s_CachedTypeNames[type.Name] = nameSet;
                        }
                        typeSet.Add(type);
                        nameSet.Add(type);
                    }
                }
            }
            s_RuntimeCached = true;
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(KeyUtility), nameof(InitRuntime))} - Cached Keys");
        }

        public const string ASSEMBLY_NAME = "VaporKeyDefinitions";        
        private static readonly Dictionary<Type, FieldInfo> s_CachedFieldInfos = new();
        private static readonly Dictionary<string, HashSet<Type>> s_CachedCategories = new();
        private static readonly Dictionary<string, HashSet<Type>> s_CachedTypeNames = new();
        
        public static List<DropdownModel> GetAllKeysFromTypeName(string typeName)
        {
#if UNITY_EDITOR
            InitEditor();
#else
            InitRuntime();
#endif
            if (s_CachedTypeNames.TryGetValue(typeName, out var name))
            {
                List<DropdownModel> result = new();
                foreach (var t in name)
                {
                    if(s_CachedFieldInfos.TryGetValue(t, out var fi))
                    {
                        result.AddRange(GetReflectedKeys(fi));
                    }                    
                }
                return result;
            }

            return new List<DropdownModel>() { new ("None", KeyDropdownValue.None, "None") };
        }

        public static List<DropdownModel> GetAllKeysFromCategory(string category)
        {
#if UNITY_EDITOR
            InitEditor();
#else
            InitRuntime();
#endif
            if (s_CachedCategories.TryGetValue(category, out var cachedCategory))
            {
                List<DropdownModel> result = new();
                foreach (var t in cachedCategory)
                {
                    if (s_CachedFieldInfos.TryGetValue(t, out var fi))
                    {
                        result.AddRange(GetReflectedKeys(fi));
                    }
                }
                return result;
            }

            return new List<DropdownModel>() { new ("None", KeyDropdownValue.None, "None") };
        }

        public static List<DropdownModel> GetAllKeysFromType(Type type)
        {
            return ((List<(string, KeyDropdownValue)>)type.GetField(KeyGenerator.KEYS_FIELD_NAME, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? new List<(string, KeyDropdownValue)>()
                {
                    ("None", KeyDropdownValue.None),
                })
                .Select(t => new DropdownModel(t.Item1,t.Item2, t.Item1)).ToList();
        }

        private static List<DropdownModel> GetReflectedKeys(FieldInfo fieldInfo)
        {
            return ((List<(string, KeyDropdownValue)>)fieldInfo?.GetValue(null) ?? new List<(string, KeyDropdownValue)>
                {
                    ("None", KeyDropdownValue.None),
                })
                .Select(t => new DropdownModel(t.Item1, t.Item2, t.Item1)).ToList();
        }

        private static string GetKeyCategory(Type type)
        {
            return (string)type.GetField(KeyGenerator.KEYS_CATEGORY_NAME, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        private static FieldInfo GetFieldInfo(Type type)
        {
            return type.GetField(KeyGenerator.KEYS_FIELD_NAME, BindingFlags.Public | BindingFlags.Static);
        }

#if UNITY_EDITOR
        public static void GenerateKeysOfType<T>() where T : KeySo
        {
            var scriptName = typeof(T).Name;
            scriptName = scriptName.Replace("Scriptable", "");
            scriptName = scriptName.Replace("SO", "");
            scriptName = scriptName.Replace("So", "");
            scriptName = scriptName.Replace("Key", "");
            KeyGenerator.GenerateKeys(typeof(T), $"{scriptName}Keys");
        }
#endif
    }
}
