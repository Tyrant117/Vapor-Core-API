using System.Collections.Generic;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.Keys
{
    public abstract class KeyDatabaseSo<T> : ScriptableObject where T : ScriptableObject, IKey
    {
        public List<T> Data = new();

        [Button]
        private void FindAll()
        {
#if UNITY_EDITOR
            List<string> guids = new();
            Data.Clear();
            guids.Clear();
            string typeFilter = typeof(T).Name;
            guids.AddRange(UnityEditor.AssetDatabase.FindAssets($"t:{typeFilter}"));
            for (int i = 0; i < guids.Count; i++)
            {
                var refVal = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i])) as T;
                if (!refVal.IsDeprecated && refVal.ValidKey())
                {
                    Data.Add(refVal);
                }
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
            KeyGenerator.GenerateKeys<T>($"{scriptName}Keys", true);
#endif
        }
    }
}
