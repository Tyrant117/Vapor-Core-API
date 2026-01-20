using UnityEngine;

namespace VaporEditor
{
    // File: Assets/Editor/AssemblyDefinitionTools.cs
    using UnityEditor;
    using UnityEngine;
    using System.IO;
    using Vapor; // Make sure this namespace is accessible in your Editor assembly

    public static class AssemblyDefinitionTools
    {
        private const string ASSEMBLY_INFO_FILENAME = "AssemblyInfo.cs";
        private const string MENU_PATH = "Assets/Create/Vapor/Assembly Info (TypeCache)";

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
            return selectedPath.EndsWith(".asmdef");
        }

        /// <summary>
        /// Creates the AssemblyInfo.cs file with the specified content.
        /// </summary>
        [MenuItem(MENU_PATH, false, 0)] // Priority 0 to appear at the top of the "Create" submenu
        private static void CreateAssemblyInfoFile()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string directory = Path.GetDirectoryName(selectedPath);

            string assemblyInfoFilePath = Path.Combine(directory, ASSEMBLY_INFO_FILENAME);

            // Check if the file already exists
            if (File.Exists(assemblyInfoFilePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "File Already Exists",
                        $"An AssemblyInfo.cs file already exists at:\n{assemblyInfoFilePath}\n\nDo you want to overwrite it?",
                        "Overwrite", "Cancel"))
                {
                    return;
                }
            }

            // Content for the AssemblyInfo.cs file
            string fileContent =
                @"using Vapor;

[assembly: TypeCache]";

            try
            {
                File.WriteAllText(assemblyInfoFilePath, fileContent);
                AssetDatabase.Refresh(); // Refresh the AssetDatabase to show the new file in the project window
                Debug.Log($"Successfully created/overwrote {ASSEMBLY_INFO_FILENAME} for assembly definition at: {directory}");

                // Optionally, select the newly created file in the project window
                Object newFile = AssetDatabase.LoadAssetAtPath<TextAsset>(assemblyInfoFilePath);
                if (newFile)
                {
                    Selection.activeObject = newFile;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error creating AssemblyInfo.cs: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create AssemblyInfo.cs: {e.Message}", "OK");
            }
        }
    }
}
