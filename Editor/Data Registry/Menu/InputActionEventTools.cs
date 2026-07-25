using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Vapor;
using Vapor.GameplayTags;
using Object = UnityEngine.Object;

namespace VaporEditor
{
    public static class InputActionEventTools
    {
        private const string MENU_PATH = "Assets/Create/Vapor/Input Action Events";

        /// <summary>
        /// Validates if the context menu item should be enabled.
        /// It's enabled only if an .asmdef file is selected.
        /// </summary>
        [MenuItem(MENU_PATH, true)]
        private static bool ValidateCreateAssemblyInfo()
        {
            // Check if exactly one object is selected
            if (Selection.objects == null || Selection.objects.Length != 1)
            {
                return false;
            }

            // Check if the selected object is an Assembly Definition Asset
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return selectedPath.EndsWith(".inputactions");
        }

        /// <summary>
        /// Creates the AssemblyInfo.cs file with the specified content.
        /// </summary>
        [MenuItem(MENU_PATH, false, 0)] // Priority 0 to appear at the top of the "Create" submenu
        private static void CreateAssemblyInfoFile()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string directory = Path.GetDirectoryName(selectedPath);
            string fileName = Selection.activeObject.name + "InputActionRegistry.cs";

            string cSharpFilePath = Path.Combine(directory ?? throw new InvalidOperationException(), fileName);

            // Check if the file already exists
            if (File.Exists(cSharpFilePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "File Already Exists",
                        $"An {fileName} file already exists at:\n{cSharpFilePath}\n\nDo you want to overwrite it?",
                        "Overwrite", "Cancel"))
                {
                    return;
                }
            }

            InputActionAsset inputActionAsset = (InputActionAsset)Selection.activeObject;

            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("using Vapor;");
            stringBuilder.AppendLine("using Vapor.GameplayTags;");
            stringBuilder.AppendLine("using Vapor.Unsafe;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"public class {inputActionAsset.name}InputActionRegistry : IGameplayTagRegistry");
            stringBuilder.AppendLine("{");
            
            foreach (var actionMap in inputActionAsset.actionMaps)
            {
                foreach (var action in actionMap.actions)
                {
                    // internal static uint CurrentHealthTag { get; } = "Vapor.Resource.Health.Current".Hash32();
                    string tagName = InputActionEvents.FormatInputActionTag(GameplayTagCategories.INPUT, action);
                    string varName = tagName.Replace(".", "");
                    stringBuilder.AppendLine($"    internal static uint {varName} {{ get; }} = \"{tagName}\".Hash32();");
                }
            }

            stringBuilder.AppendLine("    public void BuildRegistry()");
            stringBuilder.AppendLine("    {");

            foreach (var actionMap in inputActionAsset.actionMaps)
            {
                foreach (var action in actionMap.actions)
                {
                    string tagName = InputActionEvents.FormatInputActionTag(GameplayTagCategories.INPUT, action);
                    string varName = tagName.Replace(".", "_").ToLowerInvariant();
                    stringBuilder.AppendLine($"        var {varName} = new GameplayTagData(\"{tagName}\");");
                    stringBuilder.AppendLine($"        GlobalDataRegistry.Register({varName});");
                }
            }

            stringBuilder.AppendLine("    }");
            stringBuilder.AppendLine("}");

            string fileContent = stringBuilder.ToString();

            try
            {
                File.WriteAllText(cSharpFilePath, fileContent);
                AssetDatabase.Refresh(); // Refresh the AssetDatabase to show the new file in the project window
                Debug.Log($"Successfully created/overwrote {fileName} for assembly definition at: {directory}");

                // Optionally, select the newly created file in the project window
                Object newFile = AssetDatabase.LoadAssetAtPath<TextAsset>(cSharpFilePath);
                if (newFile)
                {
                    Selection.activeObject = newFile;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error creating AssemblyInfo.cs: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create AssemblyInfo.cs: {e.Message}", "OK");
            }
        }
    }
}
