using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using Vapor;
using Vapor.Keys;

namespace VaporEditor
{
    public static class DataRegistryMenu
    {
        [MenuItem("Vapor/Keys/Generate Data Keys", priority = -9100)]
        private static void GenerateDataKeys()
        {
            SetupDataKeys();
            RuntimeEditorUtility.SaveAndRefresh();
        }

        public static void SetupDataKeys()
        {
            GlobalDataRegistry.Initialize();

            var types = GlobalDataRegistry.GetAllTypes().ToList();
            var baseTypes = types
                .Select(t => t.BaseType)
                .Where(t => t != null && t != typeof(object) && typeof(IData).IsAssignableFrom(t))
                .ToList();
            types.AddRange(baseTypes);
            // Filter out types that don't implement IData
            types = types.Distinct().ToList();
            
            HashSet<string> prefixes = new();
            foreach (var type in types)
            {
                // Make generic DataRegistry<type>
                var keyOptions = type.GetCustomAttribute<KeyOptionsAttribute>();
                var genericType = typeof(DataRegistry<>).MakeGenericType(type);

                // Get the GetAll method via reflection
                var getAllMethod = genericType.GetMethod("GetAll");
                var allData = ((IEnumerable<IData>)getAllMethod!.Invoke(null, null)).ToList();
                if(allData.Count == 0)
                {
                    continue;
                }

                foreach (var data in allData)
                {
                    if (data?.Name == null)
                    {
                        continue;
                    }

                    var endIdx = data.Name.IndexOf('.');
                    if (endIdx == -1)
                    {
                        prefixes.Add(data.GetType().Name.Replace(" ", ""));
                    }
                    else
                    {
                        var prefix = data.Name[..endIdx].Replace(" ", "");
                        prefixes.Add(prefix);
                    }
                }

                var keys = allData.Select(d => KeyGenerator.StringToKeyValuePair(d.Name)).ToList();
                var scriptName = type.Name;
                scriptName = scriptName.Replace("Scriptable", "").Replace("Data", "").Replace("Key", "");
                scriptName = scriptName.EndsWith("SO") ? scriptName[..^2] : scriptName;
                scriptName = scriptName.EndsWith("So") ? scriptName[..^2] : scriptName;
                scriptName = scriptName.EndsWith("s") ? scriptName[..^1] : scriptName;
                
                var category = keyOptions?.Category ?? $"{scriptName}s";
                scriptName = $"{scriptName}Keys";
                KeyGenerator.FormatKeyFiles(KeyGenerator.RELATIVE_KEY_PATH, KeyGenerator.NAMESPACE_NAME, scriptName, category, keys);
            }
            
            var categories = new HashSet<string>
            {
                "Socket",
                "AnimationLayer",
                "Event",
                "Input",
                "Cue",
                "Addressable",
                "Montage",
            };
            foreach (var label in prefixes)
            {
                if(label == "default")
                {
                    continue;
                }
                if(label.Contains("/"))
                {
                    continue;
                }

                categories.Add(label);
            }

            string generatedCode = GenerateGameplayTagCategoriesFile(
                "Vapor.GameplayFramework.GameplayTags",
                "GameplayTagDefaultCategories",
                "GameplayTagCategories",
                categories
            );
            
            // // Write All The Prefixes To A File
            // const string className = "VaporTagPrefixes";
            // var sb = new StringBuilder();
            // sb.AppendLine("namespace " + KeyGenerator.NAMESPACE_NAME);
            // sb.AppendLine("{");
            // sb.AppendLine("    public static class " + className);
            // sb.AppendLine("    {");
            //
            // foreach (var label in prefixes)
            // {
            //     if(label == "default")
            //     {
            //         continue;
            //     }
            //     if(label.Contains("/"))
            //     {
            //         continue;
            //     }
            //
            //     var constName = label.Replace(" ", "").Replace("-", "_").Replace(".", "_").Replace("(", "_").Replace(")", "_");
            //     sb.AppendLine($"        public const string {constName} = \"{label}\";");
            // }
            //
            // sb.AppendLine("    }");
            // sb.AppendLine("}");

            string fullPath = "Assets/Vapor Gameplay Framework/Runtime/Gameplay Tags/Utilities/GameplayTagCategories.cs";
            File.WriteAllText(fullPath, generatedCode);
        }

        private static string GenerateGameplayTagCategoriesFile(string namespaceName, string enumName, string className, HashSet<string> categoryNames)
        {
            StringBuilder sb = new StringBuilder();

            // Using directives
            sb.AppendLine("using System;");
            sb.AppendLine();

            // Namespace declaration
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");

            // --- Enum Generation ---
            sb.AppendLine($"    public enum {enumName}");
            sb.AppendLine("    {");
            foreach (var category in categoryNames)
            {
                sb.AppendLine($"        {category},");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // --- Static Class Generation ---
            sb.AppendLine($"    public static class {className}");
            sb.AppendLine("    {");

            // Constant string fields
            foreach (var category in categoryNames)
            {
                // Convert "AnimationLayer" to "ANIMATION_LAYER"
                string constName = string.Concat(category.Select((ch, index) => char.IsUpper(ch) && index > 0 ? $"_{ch}" : ch.ToString())).ToUpper().Replace(".", "_");
                sb.AppendLine($"        public const string {constName} = \"{category}\";");
            }

            sb.AppendLine();

            // GetNameForCategory method
            sb.AppendLine($"        public static string GetNameForCategory({enumName} category)");
            sb.AppendLine("        {");
            sb.AppendLine("            return category switch");
            sb.AppendLine("            {");
            foreach (var category in categoryNames)
            {
                string constName = string.Concat(category.Select((ch, index) => char.IsUpper(ch) && index > 0 ? $"_{ch}" : ch.ToString())).ToUpper().Replace(".", "_");
                sb.AppendLine($"                {enumName}.{category} => {constName},");
            }

            sb.AppendLine($"                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)");
            sb.AppendLine("            };");
            sb.AppendLine("        }");

            // Close static class
            sb.AppendLine("    }");

            // Close namespace
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
