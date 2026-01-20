using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Vapor.Keys
{
    public static class DatabaseBootstrapper
    {
        private static HashSet<Type> s_TypeInitDataStoreCounter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Init()
        {
            s_TypeInitDataStoreCounter ??= new HashSet<Type>();
            s_TypeInitDataStoreCounter.Clear();
            if (Application.isEditor)
            {
#if UNITY_EDITOR
                var types = TypeCache.GetTypesWithAttribute(typeof(DatabaseKeyValuePairAttribute)).OrderBy(t1 => t1.GetCustomAttribute<DatabaseKeyValuePairAttribute>().Order).ToArray();
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
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();

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
                            Debug.Log($"Loading Addressables for {type.Name} with label {atr.AddressableLabel}");
                            if (atr.AddressableLabel == null)
                            {
                                Debug.LogError($"Loading Addressables for failed {type.Name} label is null");
                                continue;
                            }

                            if (!AddressableAssetUtility.CheckAddressableKeyValidity(atr.AddressableLabel))
                            {
                                Debug.LogWarning($"Loading Addressables for failed {type.Name} label has no valid objects");
                                continue;
                            }
                            
                            var assets = new List<Object>();

                            var addressableAssets = AddressableAssetUtility.LoadAll<Object>(Debug.Log, new object[] { atr.AddressableLabel }, out _);
                            if (addressableAssets != null)
                            {
                                assets.AddRange(addressableAssets);
                            }
                            var moreAssets = Resources.LoadAll(string.Empty, type);
                            if(moreAssets != null)
                            {
                                assets.AddRange(moreAssets);
                            }

                            RuntimeDatabaseUtility.InitializeRuntimeDatabase(type, assets);
                        }
                        else
                        {
                            var assets = Resources.LoadAll(string.Empty, type);
                            RuntimeDatabaseUtility.InitializeRuntimeDatabase(type, assets.ToList());
                        }
                    }

                    validTypes.Sort((t1, t2) => t1.GetCustomAttribute<DatabaseKeyValuePairAttribute>().Order.CompareTo(t2.GetCustomAttribute<DatabaseKeyValuePairAttribute>().Order));
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
