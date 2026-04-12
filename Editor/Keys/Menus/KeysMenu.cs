using System;
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
            // Make an Addressable Data Registry;
            GenerateAddressableDataRegistry(labels);
            return;
            
            
            var path = "Assets/Vapor/Keys/Definitions";
            var className = "AddressableNames";
            var nameSpace = "VaporKeyDefinitions";

            KeyGenerator.FormatKeyFiles(path, nameSpace, className, "Addressables", labels.Select(l =>
            {
                if(l == "default")
                {
                    return default;
                }
                
                if(l.Contains("/"))
                {
                    return default;
                }
                
                return KeyGenerator.StringToKeyValuePair(l.Replace(" ", "").Replace("-", "_").Replace(".", "_").Replace("(", "_").Replace(")", "_"), l);
            }).ToList());
            return;

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

            
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            File.WriteAllText(Path.Combine(path, className + ".cs"), sb.ToString());
        }

        private static void GenerateAddressableDataRegistry(IEnumerable<string> labels)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("using Vapor;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"public class AddressableDataRegistry : IDataRegistry");
            stringBuilder.AppendLine("{");
            
            stringBuilder.AppendLine("    public int GetOrder() => 0;");

            stringBuilder.AppendLine("    public void BuildRegistry()");
            stringBuilder.AppendLine("    {");

            foreach (var label in labels)
            {
                if(label == "default")
                {
                    continue;
                }
                
                var dataName = label.Replace(" ", "").Replace("-", "").Replace("_", ".").Replace("(", ".").Replace(")", "").Replace("/", "");
                if (dataName.EndsWith("."))
                {
                    dataName = dataName[..^1];
                }
                var varName = label.Replace(" ", "").Replace("-", "_").Replace(".", "_").Replace("(", "_").Replace(")", "").Replace("/", "_");
                stringBuilder.AppendLine($"        var {varName} = new AddressableData(\"Addressable.{dataName}\", \"{label}\");");
                stringBuilder.AppendLine($"        GlobalDataRegistry.Register({varName});");
            }

            stringBuilder.AppendLine("    }");
            stringBuilder.AppendLine("}");

            string fileContent = stringBuilder.ToString();

            var directory = "Assets/Vapor/Keys/Definitions";
            var fileName = "AddressableDataRegistry.cs";
            var fullPath = Path.Combine(directory, fileName);
            try
            {
                File.WriteAllText(fullPath, fileContent);
                AssetDatabase.Refresh(); // Refresh the AssetDatabase to show the new file in the project window
                Debug.Log($"Successfully created/overwrote {fileName} for assembly definition at: {directory}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error creating AssemblyInfo.cs: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create AddressableDataRegistry.cs: {e.Message}", "OK");
            }
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
