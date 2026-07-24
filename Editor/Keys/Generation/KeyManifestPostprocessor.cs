using System;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Keys
{
    /// <summary>
    /// Keeps the key manifests (*.keys.tsv) in sync with the data assets automatically, so IDE autocomplete
    /// stays correct as soon as content changes — without running the menu command and, crucially, without a
    /// script recompile (only text files are written).
    /// <para>
    /// The compiled .cs key classes are NOT regenerated here; run <c>Vapor/Keys/Generate Data Keys</c> when
    /// you need the <c>const uint</c> keys refreshed. Toggle this behaviour via
    /// <c>Vapor/Keys/Auto-Generate Key Manifests</c>.
    /// </para>
    /// </summary>
    public class KeyManifestPostprocessor : AssetPostprocessor
    {
        private const string k_MenuPath = "Vapor/Keys/Auto-Generate Key Manifests";
        private const string k_PrefKey = "Vapor.Keys.AutoGenerateManifests";

        private static bool s_Queued;

        private static bool Enabled => EditorPrefs.GetBool(k_PrefKey, true);

        [MenuItem(k_MenuPath, priority = -9099)]
        private static void ToggleEnabled() => EditorPrefs.SetBool(k_PrefKey, !Enabled);

        [MenuItem(k_MenuPath, isValidateFunction: true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(k_MenuPath, Enabled);
            return true;
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!Enabled || s_Queued)
            {
                return;
            }

            if (!TouchesDataAsset(importedAssets) && !TouchesDataAsset(deletedAssets) && !TouchesDataAsset(movedAssets))
            {
                return;
            }

            // Coalesce a burst of imports into a single regeneration that runs after import completes,
            // so we never call AssetDatabase-heavy work from inside the import callback.
            s_Queued = true;
            EditorApplication.delayCall += Regenerate;
        }

        private static void Regenerate()
        {
            EditorApplication.delayCall -= Regenerate;
            s_Queued = false;
            try
            {
                KeyManifestGenerator.GenerateAll();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Vapor Keys] Failed to auto-generate key manifests: {e.Message}");
            }
        }

        private static bool TouchesDataAsset(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path == null)
                {
                    continue;
                }

                // React only to ScriptableObject data assets; ignore our own generated manifests so writing
                // them can never re-trigger this postprocessor.
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    && path.Replace('\\', '/').IndexOf(KeyManifestGenerator.GeneratedFolderRelative, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
