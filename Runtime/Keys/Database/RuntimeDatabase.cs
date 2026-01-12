using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Vapor.Inspector;
using Vapor.Unsafe;
using Object = UnityEngine.Object;

namespace Vapor.Keys
{
    public static class RuntimeDatabaseUtility
    {
        public static void InitializeRuntimeDatabase(Type ofType, IList<Object> keyValuePairs)
        {
            // Assuming T is known at runtime and is of type 'YourType'
            Type runtimeDatabaseGenericType = typeof(RuntimeDatabase<>);
            Type runtimeDatabaseType = runtimeDatabaseGenericType.MakeGenericType(ofType);

            // Find the method you want to call
            MethodInfo initKeyDatabaseMethod = runtimeDatabaseType.GetMethod("InitDatabase", BindingFlags.Public | BindingFlags.Static);

            if (initKeyDatabaseMethod != null)
            {
                // Make the method call
                initKeyDatabaseMethod.Invoke(null, new object[] { keyValuePairs });
            }
            else
            {
                Debug.LogError("[RuntimeDatabaseUtility] Method not found.");
            }
        }

        public static void PostInitializeRuntimeDatabase(Type ofType)
        {
            Type runtimeDatabaseGenericType = typeof(RuntimeDatabase<>);
            Type runtimeDatabaseType = runtimeDatabaseGenericType.MakeGenericType(ofType);

            // Find the method you want to call
            MethodInfo initKeyDatabaseMethod = runtimeDatabaseType.GetMethod("PostInitDatabase", BindingFlags.Public | BindingFlags.Static);

            if (initKeyDatabaseMethod != null)
            {
                // Make the method call
                initKeyDatabaseMethod.Invoke(null, null);
            }
            else
            {
                Debug.LogError("[RuntimeDatabaseUtility] Method not found.");
            }
        }
    }

    public class RuntimeDatabase<T> where T : Object
    {
        private static bool s_Initialized;
        private static Dictionary<uint, T> s_Db;
        public static T Get(uint id) => s_Db[id];
        public static bool TryGet(uint id, out T value) => s_Db.TryGetValue(id, out value);
        public static bool SafeGet(uint id, out T value)
        {
            if (s_Initialized)
            {
                return s_Db.TryGetValue(id, out value);
            }

            value = null;
            return false;
        }
        
        public static IEnumerable<T> All() => s_Db.Values;
        public static int Count => s_Db.Count;

        public static void InitDatabase(IList<Object> keyValuePairs)
        {
            s_Db ??= new Dictionary<uint, T>();
            s_Initialized = true;
            s_Db.Clear();

            if (typeof(T).GetInterfaces().Any(t => t == typeof(IKey)))
            {
                var converted = keyValuePairs.OfType<IKey>();
                foreach (var data in converted)
                {
                    if (data is not T tData)
                    {
                        continue;
                    }
                    if (!s_Db.TryAdd(data.Key, tData))
                    {
                        Debug.LogError(
                            $"{TooltipMarkup.ClassMethod(nameof(RuntimeDatabase<T>), nameof(InitDatabase))} - {TooltipMarkup.Class(typeof(T).Name)} - Could not add key for {data.DisplayName}");
                    }
                }
            }
            else
            {
                foreach (var data in keyValuePairs)
                {
                    if (data is not T tData)
                    {
                        continue;
                    }
                    s_Db.Add(data.name.Hash32(), tData);
                }
            }
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(RuntimeDatabase<T>), nameof(InitDatabase))} - {TooltipMarkup.Class(typeof(T).Name)} - Loaded {s_Db.Count} Items");
        }

        public static void PostInitDatabase()
        {
            //Debug.Log($"{TooltipMarkup.ClassMethod(nameof(RuntimeDatabase<T>), nameof(PostInitDatabase))} - {TooltipMarkup.Class(typeof(T).Name)}");
            if(!s_Initialized)
            {
                return;
            }

            if (s_Db == null)
            {
                return;
            }
            
            foreach (var item in s_Db.Values)
            {
                if (item is IDatabaseInitialize dbInit)
                {
                    dbInit.InitializedInDatabase();
                }
            }

            foreach (var item in s_Db.Values)
            {
                if (item is IDatabaseInitialize dbInit)
                {
                    dbInit.PostInitializedInDatabase();
                }
            }
        }

        public static void InitKeyDatabase<TKey>(KeyDatabaseSo<TKey> db) where TKey : ScriptableObject, IKey, T
        {
            Debug.Log($"RuntimeDatabase of type:{typeof(T).Name}. Init!");
            s_Db.Clear();
            foreach (var data in db.Data)
            {
                s_Db.Add(data.Key, data);
            }
        }

        public static void InitValueDatabase<TValue>(TypeDatabaseSo<TValue> db) where TValue : Object, T
        {
            Debug.Log($"RuntimeDatabase of type:{typeof(T).Name}. Init!");
            s_Db.Clear();

            foreach (var data in db.Data)
            {
                s_Db.Add(data.name.Hash32(), data);
            }
        }
    }
}
