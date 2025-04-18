using System.Collections.Generic;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.Keys
{
    public class KeyCollectionSo : VaporScriptableObject
    {
        public enum KeyType
        {
            String,
            Integer,
        }

        [System.Serializable]
        public class InternalKey
        {
            [HideInInspector]
            public bool IsDeprecated;

            [HorizontalGroup("H", "Key", "50")]
            [InlineToggleButton("ToggleDeprecated", "@IsDeprecated", "d_VisibilityOff", "d_VisibilityOn", tooltip: "If <lw>Shut</lw>, this key will be ignored by KeyGenerator.GenerateKeys().")]
            public KeyType KeyType;
            [HorizontalGroup("H")]
            public string Name;
            [HorizontalGroup("H")]
            [ShowIf("@UseInteger")]
            public ushort Key;


#pragma warning disable IDE0051 // Remove unused private members
            private void ToggleDeprecated() { IsDeprecated = !IsDeprecated; }
#pragma warning restore IDE0051 // Remove unused private members

            public bool UseInteger => KeyType == KeyType.Integer;

            public KeyGenerator.KeyValuePair ToKvp()
            {
                return UseInteger ? new KeyGenerator.KeyValuePair(Name, Key, string.Empty) : KeyGenerator.StringToKeyValuePair(Name);
            }
        }

        public string Category;
        public bool IncludeNone;
        public List<InternalKey> Keys;

        [Button]
        public void GenerateKeys()
        {
#if UNITY_EDITOR
            List<KeyGenerator.KeyValuePair> kvps = new();
            if (IncludeNone)
            {
                kvps.Add(new KeyGenerator.KeyValuePair("None", KeyDropdownValue.None, string.Empty));
            }
            foreach (var key in Keys)
            {
                if (key.IsDeprecated)
                {
                    continue;
                }

                kvps.Add(key.ToKvp());
            }

            if (kvps.Count > 0)
            {
                string namespaceName = FileUtility.FindNearestNamespace(this);
                namespaceName = namespaceName == null ? KeyGenerator.NamespaceName : $"{namespaceName}.{KeyGenerator.NamespaceName}";

                var path = FileUtility.ConvertFullPathToRelative(KeyGenerator.FindNearestDirectory(this));
                KeyGenerator.FormatKeyFiles(path, namespaceName, name, Category, kvps);

                RuntimeEditorUtility.SaveAndRefresh();
            }
#endif
        }
    }
}
