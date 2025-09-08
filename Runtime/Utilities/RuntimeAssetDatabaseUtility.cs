using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using Vapor.Keys;
using Object = UnityEngine.Object;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Vapor
{
    /// <summary>
    /// A static class to access the AssetDatabase functionality. Should be used carefully as it will only function in the editor not build.
    /// </summary>
    public static class RuntimeAssetDatabaseUtility
    {
        public static List<T> FindAssetsByType<T>() where T : Object
        {
#if UNITY_EDITOR
            var guids = AssetDatabase.FindAssets($"t:{typeof(T)}");
            return guids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).Where(asset => asset != null).ToList();
#else
            return null;
#endif
        }

        public static List<Object> FindAssetsByType(Type type)
        {
#if UNITY_EDITOR
            var guids = AssetDatabase.FindAssets($"t:{type}");
            return guids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Object>).Where(asset => asset != null).ToList();
#else
            return null;
#endif
        }

        public static T FindAssetByKey<T>(uint key) where T : NamedKeySo
        {

#if UNITY_EDITOR
            var guids = AssetDatabase.FindAssets($"t:{typeof(T)}");
            return guids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).FirstOrDefault(asset => asset && asset.Key == key);
#else
            return RuntimeDatabase<T>.Get(key);
#endif
        }
    }
}
