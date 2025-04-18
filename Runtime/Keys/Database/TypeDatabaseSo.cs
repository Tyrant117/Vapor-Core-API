using System.Collections.Generic;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.Keys
{
    public abstract class TypeDatabaseSo<T> : ScriptableObject where T : Object
    {
        public string KeyName = $"{typeof(T).Name}Keys";
        public string KeyCategory = $"{typeof(T).Name}Keys";
        public string LabelFilter;
        public List<T> Data = new();

        [Button]
        private void FindAll()
        {
#if UNITY_EDITOR
            List<string> guids = new();
            Data.Clear();
            Data.Clear();
            guids.Clear();
            string typeFilter = typeof(T).Name;
            guids.AddRange(UnityEditor.AssetDatabase.FindAssets($"t:{typeFilter} l:{LabelFilter}"));
            for (int i = 0; i < guids.Count; i++)
            {
                var refVal = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i])) as T;
                Data.Add(refVal);
            }
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#endif
        }

        [Button]
        private void GenerateKeys()
        {
#if UNITY_EDITOR
            var scriptName = typeof(T).Name;
            scriptName = scriptName.Replace("Scriptable", "");
            scriptName = scriptName.Replace("SO", "");
            scriptName = scriptName.Replace("So", "");
            scriptName = scriptName.Replace("Key", "");

            List<KeyGenerator.KeyValuePair> kvpList = new();
            List<string> guids = new();
            string typeFilter = typeof(T).Name;
            guids.AddRange(UnityEditor.AssetDatabase.FindAssets($"t:{typeFilter} l:{LabelFilter}"));
            for (int i = 0; i < guids.Count; i++)
            {
                var refVal = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]));
                var key = refVal.name.GetStableHashU16();
                var guid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(refVal));
                kvpList.Add(new KeyGenerator.KeyValuePair(refVal.name, key, guid));
            }

            KeyGenerator.FormatKeyFiles(KeyGenerator.RelativeKeyPath, KeyGenerator.NamespaceName, KeyName, KeyCategory, kvpList);
#endif
        }
    }
}
