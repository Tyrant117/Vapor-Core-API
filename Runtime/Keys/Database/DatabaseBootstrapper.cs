using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Vapor.Keys
{
    public static class DatabaseBootstrapper
    {
        private static HashSet<Type> s_TypeInitDataStoreCounter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Init()
        {
            s_TypeInitDataStoreCounter ??= new();
            s_TypeInitDataStoreCounter.Clear();
            if (Application.isEditor)
            {
#if UNITY_EDITOR
                var types = UnityEditor.TypeCache.GetTypesWithAttribute(typeof(DatabaseKeyValuePairAttribute));
                foreach (var type in types)
                {
                    var assets = RuntimeAssetDatabaseUtility.FindAssetsByType(type);
                    RuntimeDatabaseUtility.InitializeRuntimeDatabase(type, assets);
                }

                foreach (var type in types)
                {
                    RuntimeDatabaseUtility.PostInitializeRuntimeDatabase(type);
                }
#endif
            }
            else
            {
                // Get all loaded assemblies in the current application domain
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                // Iterate through each assembly
                foreach (Assembly assembly in assemblies)
                {
                    // Get all types in the assembly
                    var types = assembly.GetTypes();
                    List<Type> validTypes = new(types.Length);

                    // Iterate through each type
                    foreach (Type type in types)
                    {
                        // Check if the type has the DatabaseKeyValuePair attribute
                        if (!type.IsDefined(typeof(DatabaseKeyValuePairAttribute), false))
                        {
                            continue;
                        }

                        validTypes.Add(type);
                        var atr = type.GetCustomAttribute<DatabaseKeyValuePairAttribute>();
                        if (atr.UseAddressables)
                        {
                            var assets = AddressableAssetUtility.LoadAll<UnityEngine.Object>(x => Debug.Log(x), atr.AddressableLabel);
                            RuntimeDatabaseUtility.InitializeRuntimeDatabase(type, assets.ToList());
                        }
                        else
                        {
                            var assets = Resources.LoadAll(string.Empty, type);
                            RuntimeDatabaseUtility.InitializeRuntimeDatabase(type, assets.ToList());
                        }
                    }

                    foreach (var type in validTypes)
                    {
                        RuntimeDatabaseUtility.PostInitializeRuntimeDatabase(type);
                    }
                }
            }
        }

        public static bool HasInitDataStore(Type type)
        {
            return s_TypeInitDataStoreCounter.Contains(type);
        }

        public static void InitDataStore(Type type)
        {
            s_TypeInitDataStoreCounter.Add(type);
        }
    }
}
