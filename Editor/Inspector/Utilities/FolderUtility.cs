using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static class FolderUtility
    {
        /// <summary>
        /// Create a folder from a unity assetPath.
        /// </summary>
        /// <param name="path">The path to create the folder at. Should always start with "Assets/"</param>
        public static void CreateFolderFromPath(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            Debug.Log($"Creating Folder at {path}");
            var split = path.Split('/');
            if (split.Length == 0)
            {
                Debug.LogError($"Path is not formatted with '/': {path}");
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();
                var lastFolder = split[0];
                for (var i = 1; i < split.Length; i++)
                {
                    var subFolderName = split[i];
                    var nextFolder = $"{lastFolder}/{subFolderName}";                  
                    if (Directory.Exists(nextFolder))
                    {
                        lastFolder = nextFolder;
                        continue;
                    }

                    AssetDatabase.CreateFolder(lastFolder, subFolderName);
                    lastFolder = nextFolder;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        // ReSharper disable once UnusedMember.Global
        public static void CreateAssemblyDefinition(string folderPath, string name, string rootNamespace, string[] references, bool editorOnly)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                CreateFolderFromPath(folderPath);
            }

            AssetDatabase.StartAssetEditing();
            var changed = false;
            
            // ReSharper disable once IdentifierTypo
            var asmdef = AssetDatabase.LoadAssetAtPath<TextAsset>($"{folderPath}/{name}.asmdef");
            if (!asmdef)
            {
                var indexOfSlash = folderPath.IndexOf('/');
                var fullPath = Application.dataPath + $"{folderPath[indexOfSlash..]}/{name}.asmdef";
                if (!Directory.Exists(folderPath))
                {
                    Debug.LogError($"Directory Does Not Exist: {folderPath}");
                    return;
                }
                Debug.Log(fullPath);
                StreamWriter w = new(fullPath);
                StringBuilder sb = new();
                sb.Append("{\n");
                sb.Append($"\t\"name\": \"{name}\",\n");
                sb.Append($"\t\"rootNamespace\": \"{rootNamespace}\",\n");
                
                sb.Append("\t\"references\": [\n");
                for (var i = 0; i < references.Length; i++)
                {
                    sb.Append(i == references.Length - 1 ? $"\t\t\"{references[i]}\"\n" : $"\t\t\"{references[i]}\",\n");
                }
                sb.Append("\t],\n");
                
                if (editorOnly)
                {
                    sb.Append("\t\"includePlatforms\": [\"Editor\"],\n");
                }
                else
                {
                    sb.Append("\t\"includePlatforms\": [],\n");
                }

                sb.Append("\t\"excludePlatforms\": [],\n");
                sb.Append("\t\"allowUnsafeCode\": false,\n");
                sb.Append("\t\"overrideReferences\": false,\n");
                sb.Append("\t\"precompiledReferences\": [],\n");
                sb.Append("\t\"autoReferenced\": true,\n");
                sb.Append("\t\"defineConstraints\": [],\n");
                sb.Append("\t\"versionDefines\": [],\n");
                sb.Append("\t\"noEngineReferences\": false\n");
                sb.Append("}");
                w.Write(sb.ToString());
                w.Close();
                changed = true;
            }

            AssetDatabase.StopAssetEditing();
            if (!changed) return;

            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }
    }
}