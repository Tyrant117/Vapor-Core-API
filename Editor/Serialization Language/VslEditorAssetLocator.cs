using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Vapor.Serialization;
using Object = UnityEngine.Object;

namespace VaporEditor.Serialization
{
    /// <summary>
    /// Supplies VSL with the durable locator for an asset, and loads one back, using the asset
    /// database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of reference resolution that cannot exist at runtime: only the editor knows
    /// an object's asset path, and only the editor can read the Addressables settings to find the
    /// address an asset is published under. It installs itself into
    /// <see cref="VslAssetLocator.Provider"/> on domain load, and a player simply has no provider
    /// and uses <c>Resources.Load</c> / Addressables directly.
    /// </para>
    /// <para>
    /// Loading goes through the asset database rather than the Addressables runtime because that
    /// works in edit mode, where the Addressables content may not have been built.
    /// </para>
    /// </remarks>
    public sealed partial class VslEditorAssetLocator : IVslAssetLocator
    {
        // Addressable lookups walk every group, so the answer is cached per asset GUID. Invalidated
        // whenever the Addressables settings change.
        private static readonly Dictionary<string, string> s_AddressByGuid = new Dictionary<string, string>();
        private static bool s_SubscribedToSettings;

        /// <summary>
        /// Installs this locator as the provider on every code load.
        /// </summary>
        /// <remarks>
        /// An explicit callback rather than the static constructor <c>[InitializeOnLoad]</c> used to
        /// force: installation now happens at a defined point in the code lifecycle instead of
        /// whenever the type first happened to be touched.
        /// </remarks>
        [OnCodeInitializing]
        private static void Install()
        {
            VslAssetLocator.Provider = new VslEditorAssetLocator();
        }

        public bool TryGetKey(Object obj, out VslAssetSource source, out string key)
        {
            source = VslAssetSource.None;
            key = null;

            if (obj == null)
            {
                return false;
            }

            // A component lives on a prefab; the asset that can be loaded is the prefab itself.
            var asset = obj is Component component ? component.gameObject : obj;

            var path = AssetDatabase.GetAssetPath(asset.GetEntityId());
            if (string.IsNullOrEmpty(path))
            {
                // Not an asset: a scene object or a runtime instance. Nothing durable to record.
                return false;
            }

            // An AnimationClip inside an imported model, a sprite inside a sheet: the path names the
            // file, not the object. Without the sub-asset name the reference reads back as the model
            // itself, so the clip that was picked is not the clip that loads.
            var subAsset = AssetDatabase.IsSubAsset(obj) ? obj.name : null;

            // Resources first: it needs no build step and no package, so it is the more dependable
            // of the two when an asset qualifies for both.
            var resourcePath = VslAssetLocator.ToResourcePath(path);
            if (!string.IsNullOrEmpty(resourcePath))
            {
                source = VslAssetSource.Resource;
                key = VslAssetLocator.CombineSubKey(resourcePath, subAsset);
                return true;
            }

            var address = FindAddressableAddress(path);
            if (!string.IsNullOrEmpty(address))
            {
                source = VslAssetSource.Addressable;
                key = VslAssetLocator.CombineSubKey(address, subAsset);
                return true;
            }

            return false;
        }

        public bool TryLoad(VslAssetSource source, string key, Type expectedType, out Object obj)
        {
            obj = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            var assetType = ToAssetType(expectedType);
            var hasSubAsset = VslAssetLocator.TrySplitSubKey(key, out var mainKey, out var subAsset);

            switch (source)
            {
                case VslAssetSource.Resource:
                    obj = hasSubAsset
                        ? FindSubAsset(AssetDatabase.GetAssetPath(Resources.Load(mainKey)), subAsset, assetType)
                        : Resources.Load(mainKey, assetType);
                    break;

                case VslAssetSource.Addressable:
                {
                    var path = FindAssetPathByAddress(mainKey);
                    if (string.IsNullOrEmpty(path))
                    {
                        break;
                    }

                    obj = hasSubAsset
                        ? FindSubAsset(path, subAsset, assetType)
                        : AssetDatabase.LoadAssetAtPath(path, assetType);
                    break;
                }
            }

            return obj != null;
        }

        /// <summary>
        /// Picks a named object out of an asset file.
        /// </summary>
        /// <remarks>
        /// Matched on name and type together. A model can hold several clips and a mesh and a
        /// material, so either check alone can land on the wrong object.
        /// </remarks>
        private static Object FindSubAsset(string assetPath, string subAsset, Type assetType)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            foreach (var candidate in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (candidate != null && candidate.name == subAsset &&
                    (assetType == null || assetType.IsInstanceOfType(candidate)))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// A component is not itself loadable; the prefab carrying it is. Narrowing back to the
        /// component happens in <see cref="VslAssetLocator.Narrow"/>.
        /// </summary>
        private static Type ToAssetType(Type expectedType)
        {
            if (expectedType == null || !typeof(Object).IsAssignableFrom(expectedType))
            {
                return typeof(Object);
            }

            return typeof(Component).IsAssignableFrom(expectedType) ? typeof(GameObject) : expectedType;
        }

        private static string FindAddressableAddress(string assetPath)
        {
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

            if (s_AddressByGuid.TryGetValue(guid, out var cached))
            {
                return cached;
            }

            var entry = settings.FindAssetEntry(guid);
            var address = entry?.address;
            s_AddressByGuid[guid] = address;
            return address;
        }

        private static string FindAssetPathByAddress(string address)
        {
            var settings = Settings();
            if (settings == null)
            {
                return null;
            }

            foreach (var group in settings.groups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (var entry in group.entries)
                {
                    if (entry != null && string.Equals(entry.address, address, StringComparison.Ordinal))
                    {
                        return entry.AssetPath;
                    }
                }
            }

            return null;
        }

        private static AddressableAssetSettings Settings()
        {
            // Checked rather than created: a project that does not use Addressables must not sprout
            // a settings asset just because something got serialized.
            if (!AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                return null;
            }

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                return null;
            }

            if (!s_SubscribedToSettings)
            {
                AddressableAssetSettings.OnModificationGlobal += (_, _, _) => s_AddressByGuid.Clear();
                s_SubscribedToSettings = true;
            }

            return settings;
        }
    }
}
