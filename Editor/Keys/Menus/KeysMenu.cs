using UnityEditor;
using UnityEngine;
using Vapor;
using Vapor.Keys;

namespace VaporEditor.Keys
{
    public static class KeysMenu
    {
        [MenuItem("Assets/Create/Vapor/Keys/Named Key", priority = VaporConfig.AssetMenuPriority, secondaryPriority = 0)]
        private static void CreateNamedKey()
        {
            ScriptableObjectUtility.Create<NamedKeySo>();
        }

        [MenuItem("Assets/Create/Vapor/Keys/Integer Key", priority = VaporConfig.AssetMenuPriority, secondaryPriority = 1)]
        private static void CreateIntegerKey()
        {
            ScriptableObjectUtility.Create<IntegerKeySo>();
        }

        [MenuItem("Assets/Create/Vapor/Keys/Key Collection", priority = VaporConfig.AssetMenuPriority, secondaryPriority = 2)]
        private static void CreateKeyCollection()
        {
            ScriptableObjectUtility.Create<KeyCollectionSo>();
        }
    }
}
