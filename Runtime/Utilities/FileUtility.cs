#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Vapor
{
    public static class FileUtility
    {
#if UNITY_EDITOR
        public static Assembly FindNearestAssembly(Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            path = Path.GetDirectoryName(path);

            // Loop until reaching the root of the project
            while (!path.EmptyOrNull())
            {
                // Search for .asmdef files in the current directory
                string[] asmdefFiles = Directory.GetFiles(path, "*.asmdef");

                if (asmdefFiles.Length > 0)
                {
                    // Load the first found .asmdef file
                    var asmPath = ConvertFullPathToRelative(Path.GetFullPath(asmdefFiles[0]));
                    var assemblyDefinition = AssetDatabase.LoadAssetAtPath<TextAsset>(asmPath);
                    if (assemblyDefinition)
                    {
                        return Assembly.Load(assemblyDefinition.name);
                        // Return the default namespace
                        // string json = assemblyDefinition.text;
                        // JObject jsonObject = JObject.Parse(json);
                        // return jsonObject["rootNamespace"].ToString();
                    }
                    else
                    {
                        Debug.LogError($"Couldn't Parse Path: {asmdefFiles[0]} -> {asmPath}");
                    }
                }

                // Move up to the parent directory
                path = Directory.GetParent(path)?.FullName;
            }

            // No assembly definition found return project assembly.
            return Assembly.Load("Assembly-CSharp");
        }
        
        public static string FindNearestNamespace(Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            path = Path.GetDirectoryName(path);

            // Loop until reaching the root of the project
            while (!path.EmptyOrNull())
            {
                // Search for .asmdef files in the current directory
                string[] asmdefFiles = Directory.GetFiles(path, "*.asmdef");

                if (asmdefFiles.Length > 0)
                {
                    // Load the first found .asmdef file
                    var asmPath = ConvertFullPathToRelative(Path.GetFullPath(asmdefFiles[0]));
                    var assemblyDefinition = AssetDatabase.LoadAssetAtPath<TextAsset>(asmPath);
                    if (assemblyDefinition)
                    {
                        // Return the default namespace
                        string json = assemblyDefinition.text;
                        JObject jsonObject = JObject.Parse(json);
                        return jsonObject["rootNamespace"]?.ToString();
                    }
                    else
                    {
                        Debug.LogError($"Couldn't Parse Path: {asmdefFiles[0]} -> {asmPath}");
                    }
                }

                // Move up to the parent directory
                path = Directory.GetParent(path)?.FullName;
            }

            // No assembly definition found return project assembly.
            return EditorSettings.projectGenerationRootNamespace;
        }
#endif

        public static string ConvertRelativeToFullPath(string relativePath)
        {
            // Check if the relative path starts with "Assets/"
            if (relativePath.StartsWith("Assets/"))
            {
                // Combine with Application.dataPath
                return Path.Combine(Application.dataPath, relativePath["Assets/".Length..]).Replace('\\', '/');
            }
            // Check if the relative path starts with "Packages/"
            else if (relativePath.StartsWith("Packages/"))
            {
                return FileUtil.GetPhysicalPath(relativePath);
            }

            throw new ArgumentException($"Invalid relative path: {relativePath}");
        }

        public static string ConvertFullPathToRelative(string fullPath)
        {
            // Normalize path to use forward slashes
            fullPath = fullPath.Replace("\\", "/");

            // Get the project's Assets path
            string assetsPath = Application.dataPath.Replace('\\', '/');
            string packagesPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages")).Replace('\\', '/');

            // Check if the absolute path starts with the Assets path
            if (fullPath.StartsWith(assetsPath))
            {
                return FileUtil.GetProjectRelativePath(fullPath);
            }
            // Check if the absolute path starts with the Packages path
            else if (fullPath.StartsWith(packagesPath))
            {
                return FileUtil.GetLogicalPath(fullPath);
            }

            throw new ArgumentException($"Invalid full path: {fullPath}");
        }
    }
}
