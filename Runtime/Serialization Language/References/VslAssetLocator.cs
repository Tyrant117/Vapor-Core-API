using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Vapor.Serialization
{
    /// <summary>
    /// Finds the durable locator for an asset, and loads one back.
    /// </summary>
    /// <remarks>
    /// Implemented in the editor assembly, where <c>AssetDatabase</c> and the Addressables settings
    /// live. A player has no implementation and falls back to the runtime loaders on
    /// <see cref="VslAssetLocator"/>.
    /// </remarks>
    public interface IVslAssetLocator
    {
        /// <summary>Finds how <paramref name="obj"/> could be loaded in a build, if it can be at all.</summary>
        bool TryGetKey(Object obj, out VslAssetSource source, out string key);

        /// <summary>Loads an asset by locator without going through the runtime loaders.</summary>
        bool TryLoad(VslAssetSource source, string key, Type expectedType, out Object obj);
    }

    /// <summary>
    /// The bridge between VSL references and however assets are actually shipped.
    /// </summary>
    /// <remarks>
    /// The editor installs a <see cref="Provider"/> that reads asset paths and Addressables entries;
    /// a player has none and uses <c>Resources.Load</c> / Addressables directly. Everything an asset
    /// is loaded by is remembered in <see cref="Remember"/>, so a document loaded at runtime and
    /// written back out keeps the same durable keys instead of degrading to session-only ids.
    /// </remarks>
    public static class VslAssetLocator
    {
        private const string ResourcesFolder = "/Resources/";

        /// <summary>
        /// Editor-side locator, installed on domain load. Null in a player.
        /// </summary>
        public static IVslAssetLocator Provider { get; set; }

        // Objects we have loaded by key, so writing them back out reproduces the same key. Also the
        // load cache: an addressable handle must stay alive for the asset to stay valid, so results
        // are held rather than released.
        private static readonly Dictionary<Object, VslObjectReference> s_KnownObjects =
            new Dictionary<Object, VslObjectReference>();

        private static readonly Dictionary<string, Object> s_LoadedByKey = new Dictionary<string, Object>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            s_KnownObjects.Clear();
            s_LoadedByKey.Clear();
        }

        /// <summary>Records how an object was loaded, so it can be written back the same way.</summary>
        public static void Remember(Object obj, VslAssetSource source, string key)
        {
            if (obj == null || source == VslAssetSource.None || string.IsNullOrEmpty(key))
            {
                return;
            }

            s_KnownObjects[obj] = new VslObjectReference(source, key);
            s_LoadedByKey[CacheKey(source, key)] = obj;
        }

        /// <summary>
        /// Finds the durable locator for an asset: the editor provider first, then anything we
        /// previously loaded by key.
        /// </summary>
        public static bool TryGetKey(Object obj, out VslAssetSource source, out string key)
        {
            source = VslAssetSource.None;
            key = null;

            if (obj == null)
            {
                return false;
            }

            var provider = Provider;
            if (provider != null && provider.TryGetKey(obj, out source, out key))
            {
                return true;
            }

            if (s_KnownObjects.TryGetValue(obj, out var known) && known.HasAssetKey)
            {
                source = known.Source;
                key = known.Key;
                return true;
            }

            source = VslAssetSource.None;
            key = null;
            return false;
        }

        /// <summary>Loads an asset by locator, through the editor provider or the runtime loaders.</summary>
        public static bool TryLoad(VslAssetSource source, string key, Type expectedType, out Object obj)
        {
            obj = null;
            if (source == VslAssetSource.None || string.IsNullOrEmpty(key))
            {
                return false;
            }

            var cacheKey = CacheKey(source, key);
            if (s_LoadedByKey.TryGetValue(cacheKey, out var cached) && cached != null &&
                IsCompatible(cached, expectedType))
            {
                obj = cached;
                return true;
            }

            var provider = Provider;
            if (provider != null && provider.TryLoad(source, key, expectedType, out obj) && obj != null)
            {
                Remember(obj, source, key);
                return true;
            }

            obj = source switch
            {
                VslAssetSource.Resource => LoadResource(key, expectedType),
                VslAssetSource.Addressable => LoadAddressable(key, expectedType),
                _ => null,
            };

            if (obj == null)
            {
                return false;
            }

            Remember(obj, source, key);
            return true;
        }

        private static Object LoadResource(string key, Type expectedType)
        {
            var loaded = expectedType != null && typeof(Object).IsAssignableFrom(expectedType)
                ? Resources.Load(key, expectedType)
                : Resources.Load(key);

            // Resources.Load returns the main asset; a sub-asset of a different type needs the
            // whole path scanned.
            if (loaded == null && expectedType != null)
            {
                foreach (var candidate in Resources.LoadAll(key))
                {
                    if (IsCompatible(candidate, expectedType))
                    {
                        return candidate;
                    }
                }
            }

            return loaded;
        }

        private static Object LoadAddressable(string key, Type expectedType)
        {
            try
            {
                // Synchronous by necessity: deserialization has no point at which to await. The
                // handle is deliberately not released - releasing it would unload the asset the
                // caller is about to hold a reference to.
                var handle = Addressables.LoadAssetAsync<Object>(key);
                var loaded = handle.WaitForCompletion();
                return IsCompatible(loaded, expectedType) ? loaded : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VSL] Could not load addressable '{key}': {exception.Message}");
                return null;
            }
        }

        private static bool IsCompatible(Object obj, Type expectedType)
        {
            if (obj == null)
            {
                return false;
            }

            if (expectedType == null || expectedType.IsInstanceOfType(obj))
            {
                return true;
            }

            // A component is addressed through the prefab that holds it.
            if (typeof(Component).IsAssignableFrom(expectedType) && obj is GameObject gameObject)
            {
                return gameObject.GetComponent(expectedType) != null;
            }

            return false;
        }

        /// <summary>
        /// Narrows a loaded asset to what the member actually declared — a component on a loaded
        /// prefab, most often.
        /// </summary>
        public static Object Narrow(Object obj, Type expectedType)
        {
            if (obj == null || expectedType == null || expectedType.IsInstanceOfType(obj))
            {
                return obj;
            }

            if (typeof(Component).IsAssignableFrom(expectedType) && obj is GameObject gameObject)
            {
                return gameObject.GetComponent(expectedType);
            }

            return null;
        }

        /// <summary>
        /// Converts a project asset path to a <c>Resources.Load</c> path, or returns null when the
        /// asset does not live under a Resources folder.
        /// </summary>
        /// <remarks>
        /// Shared by the editor locator so both sides derive the same key. <c>Resources.Load</c>
        /// wants the path relative to the Resources folder and without an extension:
        /// <c>Assets/Game/Resources/UI/Icons/Sword.png</c> becomes <c>UI/Icons/Sword</c>.
        /// </remarks>
        public static string ToResourcePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var normalized = assetPath.Replace('\\', '/');
            var index = normalized.LastIndexOf(ResourcesFolder, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var relative = normalized.Substring(index + ResourcesFolder.Length);
            var dot = relative.LastIndexOf('.');
            if (dot > 0)
            {
                relative = relative.Substring(0, dot);
            }

            return relative.Length == 0 ? null : relative;
        }

        private static string CacheKey(VslAssetSource source, string key) => (byte)source + ":" + key;
    }
}
