using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Vapor.Keys;

namespace VaporEditor.Keys
{
    public class ScriptableObjectAddressableHandler : AssetPostprocessor
    {
        private const string k_Extension = ".asset";

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            foreach (string str in importedAssets)
            {
                //Debug.Log("Reimported Asset: " + str);
                if(IsScriptableObjectAsset(str))
                {
                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(str);
                    var atr = so.GetType().GetCustomAttribute<DatabaseKeyValuePairAttribute>();
                    if(atr != null)
                    {
                        AddToAddressables(str, atr.AddressableLabel);
                    }
                }
            }

            for (int i = 0; i < movedAssets.Length; i++)
            {
                //Debug.Log("Moved Asset: " + movedAssets[i] + " from: " + movedFromAssetPaths[i]);
                if (IsScriptableObjectAsset(movedAssets[i]))
                {
                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(movedAssets[i]);
                    var atr = so.GetType().GetCustomAttribute<DatabaseKeyValuePairAttribute>();
                    if (atr != null)
                    {
                        AddToAddressables(movedAssets[i], atr.AddressableLabel);
                    }
                }
            }
        }

        // Helper method to check if the asset is a ScriptableObject
        private static bool IsScriptableObjectAsset(string assetPath)
        {
            string extension = Path.GetExtension(assetPath);
            return extension == k_Extension;  // ScriptableObject assets usually have ".asset" extension
        }

        public static void AddToAddressables(string assetPath, string withLabel = null)
        {
            // Get the Addressable Asset Settings object (it manages all addressables)
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings || !settings.DefaultGroup)
            {
                Debug.LogError("A default addressable group must be created before auto-marking an object addressable.");
                return;
            }

            // Find or create an Addressable Group (we'll use the default group if none exists)
            AddressableAssetGroup group = settings.DefaultGroup;

            // Add the asset to the Addressable Group
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            Debug.Log(guid);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
            Debug.Log(entry);

            // Optional: Set an address name (e.g., same name as the asset)
            entry.address = Path.GetFileNameWithoutExtension(assetPath);
            if (withLabel != null)
            {
                entry.SetLabel(withLabel, true, true);
            }

            // Save changes to addressables
            AssetDatabase.SaveAssets();
        }
    }
}