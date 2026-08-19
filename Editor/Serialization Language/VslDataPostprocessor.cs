using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Vapor;
using Vapor.Serialization;

namespace VaporEditor.Serialization
{
    /// <summary>
    /// Keeps every data document under <see cref="VslDataStore.RelativeFolder"/> marked addressable
    /// with the label the registry loads by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done from a postprocessor rather than inside <see cref="VslImporter.OnImportAsset"/> because
    /// writing to the Addressables settings mutates the asset database, and doing that from inside an
    /// import is re-entrant and unreliable.
    /// </para>
    /// <para>
    /// This is what stops the editor and player load paths from drifting: the editor reads these
    /// files off disk, a build reads them through Addressables, and a document can only exist in the
    /// folder if it also carries the label.
    /// </para>
    /// </remarks>
    public class VslDataPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths, bool didDomainReload)
        {
            foreach (var path in importedAssets)
            {
                TryMark(path);
            }

            foreach (var path in movedAssets)
            {
                TryMark(path);
            }
        }

        private static void TryMark(string assetPath)
        {
            if (!IsDataDocument(assetPath))
            {
                return;
            }

            // Checked rather than created, matching VslEditorAssetLocator: a project that does not use
            // Addressables must not sprout a settings asset just because a document was saved.
            if (!AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                return;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            // Preserve an entry's existing group. Reimporting a document must not move deliberately
            // remote or otherwise specially configured content into the default group just to add a
            // registry label.
            var entry = settings.FindAssetEntry(guid);

            // Already published: nothing to do. A document is reimported whenever its bytes change, and
            // re-applying a label the entry already carries would dirty the addressables settings asset
            // on every one of those - a settings write per save, for no change at all.
            if (entry != null && entry.labels != null && entry.labels.Contains(VslDataStore.AddressableLabel))
            {
                return;
            }

            if (entry == null)
            {
                if (settings.DefaultGroup == null)
                {
                    Debug.LogError("A default addressable group must exist before a VSL data document can be published.");
                    return;
                }

                entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
                entry.address = Path.GetFileNameWithoutExtension(assetPath);
            }

            entry.SetLabel(VslDataStore.AddressableLabel, true, true, false);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, false);
        }

        private static bool IsDataDocument(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) &&
            assetPath.EndsWith(Vsl.FileExtension, StringComparison.OrdinalIgnoreCase) &&
            assetPath.StartsWith(VslDataStore.RelativeFolder + "/", StringComparison.OrdinalIgnoreCase);
    }
}
