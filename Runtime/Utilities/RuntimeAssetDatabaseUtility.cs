using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
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
#if UNITY_EDITOR
        public static List<T> FindAssetsByType<T>() where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T)}");
            return guids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).Where(asset => asset != null).ToList();
        }

        public static List<Object> FindAssetsByType(Type type)
        {
            var guids = AssetDatabase.FindAssets($"t:{type}");
            return guids.Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Object>).Where(asset => asset != null).ToList();
        }
#endif
    }
}
