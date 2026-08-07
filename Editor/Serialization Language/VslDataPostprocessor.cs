using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
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

            ScriptableObjectAddressableHandler.AddToAddressableAssets(assetPath, VslDataStore.AddressableLabel);
        }

        private static bool IsDataDocument(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) &&
            assetPath.EndsWith(Vsl.FileExtension, StringComparison.OrdinalIgnoreCase) &&
            assetPath.StartsWith(VslDataStore.RelativeFolder + "/", StringComparison.OrdinalIgnoreCase);
    }
}
