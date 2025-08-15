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

        public const string ASSEMBLY_NAME = "VaporKeyDefinitions";        
        private static readonly Dictionary<Type, FieldInfo> s_CachedFieldInfos = new();
        private static readonly Dictionary<string, HashSet<Type>> s_CachedCategories = new();
        private static readonly Dictionary<string, HashSet<Type>> s_CachedTypeNames = new();

        public static List<DropdownModel> GetAllKeysFromTypeName(string typeName)
        {
#if UNITY_EDITOR
            InitEditor();
            if (s_CachedTypeNames.ContainsKey(typeName))
            {
                List<DropdownModel> result = new();
                foreach (var t in s_CachedTypeNames[typeName])
                {
                    if(s_CachedFieldInfos.TryGetValue(t, out var fi))
                    {
                        result.AddRange(GetReflectedKeys(fi));
                    }                    
                }
                return result;
            }
            else
            {
                return new List<DropdownModel>() { new ("None", KeyDropdownValue.None, "None") };
            }
#else
            return new List<DropdownModel>() { new ("None", KeyDropdownValue.None) };
#endif
        }

        public static List<DropdownModel> GetAllKeysFromCategory(string category)
        {
#if UNITY_EDITOR
            InitEditor();
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
            else
            {
                return new List<DropdownModel>() { new ("None", KeyDropdownValue.None, "None") };
            }
#else
            return new List<DropdownModel>() { new ("None", KeyDropdownValue.None) };
#endif
        }

        public static List<DropdownModel> GetAllKeysFromType(Type type)
        {
#if UNITY_EDITOR
            return ((List<(string, KeyDropdownValue)>)type.GetField(KeyGenerator.KEYS_FIELD_NAME, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? new List<(string, KeyDropdownValue)>()
                {
                    ("None", KeyDropdownValue.None),
                })
                .Select(t => new DropdownModel(t.Item1,t.Item2, t.Item1)).ToList();
#else
            return new List<DropdownModel>() { new ("None", KeyDropdownValue.None) };
#endif
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

        public static void GenerateKeysOfType<T>() where T : KeySo
        {
#if UNITY_EDITOR
            var scriptName = typeof(T).Name;
            scriptName = scriptName.Replace("Scriptable", "");
            scriptName = scriptName.Replace("SO", "");
            scriptName = scriptName.Replace("So", "");
            scriptName = scriptName.Replace("Key", "");
            KeyGenerator.GenerateKeys(typeof(T), $"{scriptName}Keys");
#endif
        }
    }
}
