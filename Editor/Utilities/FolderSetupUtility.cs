using UnityEditor;
using VaporEditor.Inspector;

namespace VaporEditor
{
    internal static class FolderSetupUtility
    {
        public const string TASK_RELATIVE_PATH = "Vapor/Editor/Tasks";
        public const string FOLDER_KEYS_RELATIVE_PATH = "Vapor/Keys";
        public const string DEFINITIONS_RELATIVE_PATH = FOLDER_KEYS_RELATIVE_PATH + "/Definitions";
        public const string CONFIG_RELATIVE_PATH = FOLDER_KEYS_RELATIVE_PATH + "/Config";
        

        public const string KEY_ROOT_NAMESPACE = "Vapor.KeyDefinitions";
        public const string INTERNAL_ASSEMBLY_REFERENCE_NAME = "vapor.core.runtime";

        [InitializeOnLoadMethod]
        private static void SetupFolders()
        {
            FolderUtility.CreateFolderFromPath($"Assets/{TASK_RELATIVE_PATH}");
            FolderUtility.CreateFolderFromPath($"Assets/{FOLDER_KEYS_RELATIVE_PATH}");
            FolderUtility.CreateFolderFromPath($"Assets/{DEFINITIONS_RELATIVE_PATH}");
            FolderUtility.CreateFolderFromPath($"Assets/{CONFIG_RELATIVE_PATH}");

            FolderUtility.CreateAssemblyDefinition($"Assets/{FOLDER_KEYS_RELATIVE_PATH}", KEY_ROOT_NAMESPACE, KEY_ROOT_NAMESPACE, new[] { INTERNAL_ASSEMBLY_REFERENCE_NAME }, false);
        }
    }
}
