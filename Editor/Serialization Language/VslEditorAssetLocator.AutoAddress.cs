using System;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using Object = UnityEngine.Object;

namespace VaporEditor.Serialization
{
    /// <summary>
    /// Publishes an asset that a VSL document referenced but that nothing would ship.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, picking an ordinary project asset in a document produces a reference that works
    /// perfectly in the editor and is <c>null</c> in a build: the locator finds no durable key, so only
    /// the session-scoped id is written, and a player has no session to resolve it against. The failure
    /// appears at the worst possible moment and points nowhere near the cause.
    /// </para>
    /// <para>
    /// So an asset that is referenced becomes an addressable one. Everything VSL adopts lands in its own
    /// group, separate from entries a human made deliberately — which is what makes the set auditable,
    /// and removable in one action if the convention is ever abandoned.
    /// </para>
    /// <para>
    /// Assets already under <c>Resources/</c> or already addressable never reach here; the locator
    /// prefers what the project already publishes them by.
    /// </para>
    /// </remarks>
    public sealed partial class VslEditorAssetLocator
    {
        /// <summary>Group every auto-published asset is filed under.</summary>
        public const string AutoGroupName = "VslAssets";

        /// <summary>Label applied to them, so they can be found and released as a set.</summary>
        public const string AutoLabel = "VslAsset";

        /// <summary>
        /// Whether a referenced asset is published automatically.
        /// </summary>
        /// <remarks>
        /// Writing to a project's Addressables settings as a side effect of serialisation is a real
        /// consequence, and a project that manages its groups by hand is entitled to refuse it. Turning
        /// this off restores the old behaviour exactly: the reference stays session-only and does not
        /// survive a build.
        /// </remarks>
        public static bool AutoPublish { get; set; } = true;

        /// <summary>
        /// Gives an asset an address, or returns null if it cannot or should not have one.
        /// </summary>
        private static string PublishAddressable(string assetPath)
        {
            if (!AutoPublish || string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            // Never during a build. Content has already been gathered by then, so a new entry would
            // not be in it — and mutating the settings mid-build is its own kind of trouble.
            if (BuildPipeline.isBuildingPlayer)
            {
                return null;
            }

            var settings = Settings();
            if (settings == null)
            {
                return null;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            var group = FindOrCreateAutoGroup(settings);
            if (group == null)
            {
                return null;
            }

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null)
            {
                return null;
            }

            entry.address = UniqueAddress(settings, guid, SimplifyAddress(assetPath));
            entry.SetLabel(AutoLabel, true, true, false);

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, false);

            // The address cache is keyed by GUID and this asset was in it as "no address".
            s_AddressByGuid[guid] = entry.address;
            return entry.address;
        }

        private static AddressableAssetGroup FindOrCreateAutoGroup(AddressableAssetSettings settings)
        {
            var group = settings.FindGroup(AutoGroupName);
            if (group != null)
            {
                return group;
            }

            return settings.CreateGroup(
                AutoGroupName,
                setAsDefaultGroup: false,
                readOnly: false,
                postEvent: true,
                schemasToCopy: null,
                types: new[] { typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema) });
        }

        /// <summary>
        /// The asset's file name, without folders or extension.
        /// </summary>
        /// <remarks>
        /// Addressables defaults an entry's address to its full asset path, which is long, leaks the
        /// project's folder layout into the document, and breaks the moment the file is moved. A bare
        /// name is what a designer would have typed.
        /// </remarks>
        private static string SimplifyAddress(string assetPath)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(name))
            {
                return "Asset";
            }

            var clean = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                clean.Append(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' ? c : '_');
            }

            return clean.Length == 0 ? "Asset" : clean.ToString();
        }

        /// <summary>
        /// Makes an address unique, since two files in different folders may share a name.
        /// </summary>
        /// <remarks>
        /// An address is how a build finds the asset, so a collision does not merely look untidy — one
        /// of the two becomes unreachable, and which one depends on group order.
        /// </remarks>
        private static string UniqueAddress(
            AddressableAssetSettings settings, string guid, string wanted)
        {
            var address = wanted;

            for (var suffix = 1; suffix < 1000; suffix++)
            {
                var clash = false;

                foreach (var group in settings.groups)
                {
                    if (group == null)
                    {
                        continue;
                    }

                    foreach (var entry in group.entries)
                    {
                        if (entry == null || entry.guid == guid)
                        {
                            continue;
                        }

                        if (string.Equals(entry.address, address, StringComparison.Ordinal))
                        {
                            clash = true;
                            break;
                        }
                    }

                    if (clash)
                    {
                        break;
                    }
                }

                if (!clash)
                {
                    return address;
                }

                address = $"{wanted}_{suffix}";
            }

            return $"{wanted}_{guid[..8]}";
        }
    }
}
