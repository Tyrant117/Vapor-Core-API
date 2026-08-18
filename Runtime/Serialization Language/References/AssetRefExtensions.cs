using UnityEngine;
using UnityEngine.AddressableAssets;
using Vapor.Serialization;
using Object = UnityEngine.Object;

namespace Vapor
{
    /// <summary>
    /// Spawning for prefab references.
    /// </summary>
    /// <remarks>
    /// <see cref="AssetRef{T}"/> only loads, because loading is all every <c>T</c> has in common —
    /// instantiating a copy is a GameObject idea, and a <c>Sprite</c> or an <c>InputActionAsset</c> has
    /// no answer for it. These are the methods the old <c>AddressableData</c> carried, on the reference
    /// that replaced it.
    /// </remarks>
    public static class AssetRefExtensions
    {
        /// <summary>
        /// Loads and instantiates now. Null when the reference is empty or nothing answers to it.
        /// </summary>
        /// <remarks>
        /// Blocks on the load, which for an addressable can mean a frame's worth of disk. Fine behind a
        /// loading screen or in a tool; anything running while the game does should take the
        /// asynchronous overload.
        /// </remarks>
        public static GameObject Instantiate(this AssetRef<GameObject> reference, Vector3 position, Quaternion rotation)
        {
            return reference.TryLoad(out var prefab) && prefab ? Object.Instantiate(prefab, position, rotation) : null;
        }

        /// <inheritdoc cref="Instantiate(AssetRef{GameObject}, Vector3, Quaternion)"/>
        public static GameObject Instantiate(this AssetRef<GameObject> reference, Transform parent, bool instantiateInWorldSpace)
        {
            return reference.TryLoad(out var prefab) && prefab ? Object.Instantiate(prefab, parent, instantiateInWorldSpace) : null;
        }

        /// <summary>
        /// Loads and instantiates. Null when the reference is empty or nothing answers to it.
        /// </summary>
        /// <remarks>
        /// An addressable is spawned through <see cref="Addressables.InstantiateAsync(object, Vector3, Quaternion, Transform, bool)"/>
        /// rather than loaded and cloned, so the instance is one Addressables accounts for and
        /// <c>Addressables.ReleaseInstance</c> can hand back. A Resources prefab has no such
        /// bookkeeping and is simply cloned; <c>Destroy</c> is the whole of its lifetime.
        /// </remarks>
        public static async Awaitable<GameObject> InstantiateAsync(this AssetRef<GameObject> reference, Vector3 position, Quaternion rotation)
        {
            if (!reference.IsSet)
            {
                return null;
            }

            if (reference.Source == VslAssetSource.Addressable)
            {
                var handle = Addressables.InstantiateAsync(reference.Key, position, rotation);
                await handle.Task;
                return handle.Result;
            }

            var prefab = await reference.LoadAsync();
            return prefab ? Object.Instantiate(prefab, position, rotation) : null;
        }

        /// <inheritdoc cref="InstantiateAsync(AssetRef{GameObject}, Vector3, Quaternion)"/>
        public static async Awaitable<GameObject> InstantiateAsync(this AssetRef<GameObject> reference, Transform parent, bool instantiateInWorldSpace)
        {
            if (!reference.IsSet)
            {
                return null;
            }

            if (reference.Source == VslAssetSource.Addressable)
            {
                var handle = Addressables.InstantiateAsync(reference.Key, parent, instantiateInWorldSpace);
                await handle.Task;
                return handle.Result;
            }

            var prefab = await reference.LoadAsync();
            return prefab ? Object.Instantiate(prefab, parent, instantiateInWorldSpace) : null;
        }
    }
}
