using UnityEditor;

namespace VaporEditor.Inspector
{
    internal static class FolderSetupUtility
    {
        public const string EditorNamespace = "VaporEditor.Inspector";
        public const string FolderRelativePath = "Vapor/Editor/Inspector";
        public const string PropertyDrawerRelativePath = FolderRelativePath + "/Property Drawers";
        public const string EditorRelativePath = FolderRelativePath + "/Editors";
        
        [InitializeOnLoadMethod]
        private static void SetupFolders()
        {
            FolderUtility.CreateFolderFromPath($"Assets/{FolderRelativePath}");
            FolderUtility.CreateFolderFromPath($"Assets/{PropertyDrawerRelativePath}");
            FolderUtility.CreateFolderFromPath($"Assets/{EditorRelativePath}");
        }
    }
}
