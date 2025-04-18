using UnityEngine;

namespace Vapor
{
    public static class RuntimeEditorUtility
    {
        //[System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void DirtyAndSave(Object obj)
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(obj);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(obj);
#endif
        }

        //[System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void Ping(Object obj)
        {
#if UNITY_EDITOR
            UnityEditor.EditorGUIUtility.PingObject(obj);
            UnityEditor.Selection.SetActiveObjectWithContext(obj, null);
#endif
        }

        //[System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void SaveAndRefresh()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        
        public static T FindNearestAssetOfType<T>(Object obj)
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(obj);
            path = System.IO.Path.GetDirectoryName(path);

            // Loop until reaching the root of the project
            while (!path.EmptyOrNull())
            {
                // Search for .asmdef files in the current directory
                string[] files = System.IO.Directory.GetFiles(path);
                if (files.Length > 0)
                {
                    foreach (var file in files)
                    {
                        var asmPath = ConvertFullPathToRelative(System.IO.Path.GetFullPath(file));
                        var assetAtPath = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(asmPath);
                        if (assetAtPath is T atPath)
                        {
                            return atPath;
                        }
                    }
                }

                // Move up to the parent directory
                path = System.IO.Directory.GetParent(path)?.FullName;
            }
#endif

            // No assembly definition found
            return default;
        }
        
        public static string ConvertFullPathToRelative(string fullPath)
        {
#if UNITY_EDITOR
            // Normalize path to use forward slashes
            fullPath = fullPath.Replace("\\", "/");

            // Get the project's Assets path
            string assetsPath = Application.dataPath.Replace('\\', '/');
            string packagesPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Packages")).Replace('\\', '/');

            // Check if the absolute path starts with the Assets path
            if (fullPath.StartsWith(assetsPath))
            {
                return UnityEditor.FileUtil.GetProjectRelativePath(fullPath);
            }
            // Check if the absolute path starts with the Packages path
            else if (fullPath.StartsWith(packagesPath))
            {
                return UnityEditor.FileUtil.GetLogicalPath(fullPath);
            }
#endif
            throw new System.ArgumentException($"Invalid full path: {fullPath}");
        }
    }
}
