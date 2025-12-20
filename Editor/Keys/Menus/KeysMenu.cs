using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vapor.Keys;
using System.IO;
using System.Linq;
using UnityEditor.AddressableAssets;
using System.Text;
using UnityEditor.AddressableAssets.Settings;

namespace VaporEditor.Keys
{
    public static class KeysMenu
    {
        [MenuItem("Vapor/Keys/Generate Addressable Keys", priority = -9001)]
        private static void GenerateAddressableKeys()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings)
            {
                Debug.LogError("Addressable Asset Settings not found!");
                return;
            }

            var labels = settings.GetLabels();
            var entries = new List<AddressableAssetEntry>(1024);
            settings.GetAllAssets(entries, false);

            GenerateAddressableLabels(labels);
            GenerateAddressableNames(entries.Select(e => e.address));
            AssetDatabase.Refresh();
        }

        private static void GenerateAddressableLabels(List<string> labels)
        {
            var className = "AddressableLabels";
            var nameSpace = "VaporKeyDefinitions";

            var sb = new StringBuilder();
            sb.AppendLine("namespace " + nameSpace);
            sb.AppendLine("{");
            sb.AppendLine("    public static class " + className);
            sb.AppendLine("    {");

            foreach (var label in labels)
            {
                if(label == "default")
                {
                    continue;
                }
                if(label.Contains("/"))
                {
                    continue;
                }

                var constName = label.Replace(" ", "").Replace("-", "_").Replace(".", "_").Replace("(", "_").Replace(")", "_");
                sb.AppendLine($"        public const string {constName} = \"{label}\";");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            var path = "Assets/Vapor/Keys/Definitions";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            File.WriteAllText(Path.Combine(path, className + ".cs"), sb.ToString());
        }
        
        private static void GenerateAddressableNames(IEnumerable<string> labels)
        {
            var className = "AddressableNames";
            var nameSpace = "VaporKeyDefinitions";

            var sb = new StringBuilder();
            sb.AppendLine("namespace " + nameSpace);
            sb.AppendLine("{");
            sb.AppendLine("    public static class " + className);
            sb.AppendLine("    {");

            foreach (var label in labels)
            {
                if(label == "default")
                {
                    continue;
                }
                if(label.Contains("/"))
                {
                    continue;
                }

                var constName = label.Replace(" ", "").Replace("-", "_").Replace(".", "_").Replace("(", "_").Replace(")", "_");
                sb.AppendLine($"        public const string {constName} = \"{label}\";");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            var path = "Assets/Vapor/Keys/Definitions";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            File.WriteAllText(Path.Combine(path, className + ".cs"), sb.ToString());
        }

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
