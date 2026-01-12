using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Vapor
{
    public static class AddressableAssetUtility
    {
        public static GameObject Instantiate(string nameOrLabel, Transform parent, bool instantiateInWorldSpace)
        {
            return Addressables.InstantiateAsync(nameOrLabel, parent, instantiateInWorldSpace).WaitForCompletion();
        }

        public static GameObject Instantiate(string nameOrLabel, Vector3 position, Quaternion rotation, out AsyncOperationHandle<GameObject> handle)
        {
            handle = Addressables.InstantiateAsync(nameOrLabel, position, rotation);
            return handle.WaitForCompletion();
        }

        public static GameObject Instantiate(AssetReferenceGameObject reference, Transform parent, bool instantiateInWorldSpace)
        {
            return reference.InstantiateAsync(parent, instantiateInWorldSpace).WaitForCompletion();
        }

        public static GameObject Instantiate(AssetReferenceGameObject reference, Vector3 position, Quaternion rotation)
        {
            return reference.InstantiateAsync(position, rotation).WaitForCompletion();
        }

        public static void InstantiateAsync(string nameOrLabel, Action<AsyncOperationHandle<GameObject>> callback, Transform parent, bool instantiateInWorldSpace)
        {
            Addressables.InstantiateAsync(nameOrLabel, parent, instantiateInWorldSpace).Completed += callback;
        }

        public static void InstantiateAsync(string nameOrLabel, Action<AsyncOperationHandle<GameObject>> callback, Vector3 position, Quaternion rotation)
        {
            Addressables.InstantiateAsync(nameOrLabel, position, rotation).Completed += callback;
        }

        public static void InstantiateAsync(AssetReferenceGameObject reference, Action<AsyncOperationHandle<GameObject>> callback, Transform parent, bool instantiateInWorldSpace)
        {
            reference.InstantiateAsync(parent, instantiateInWorldSpace).Completed += callback;
        }

        public static void InstantiateAsync(AssetReferenceGameObject reference, Action<AsyncOperationHandle<GameObject>> callback, Vector3 position, Quaternion rotation)
        {
            reference.InstantiateAsync(position, rotation).Completed += callback;
        }

        public static T Load<T>(string nameOrLabel, out AsyncOperationHandle<T> handle)
        {
            handle = Addressables.LoadAssetAsync<T>(nameOrLabel);
            return handle.WaitForCompletion();
        }

        public static T Load<T>(AssetLabelReference referenceLabel, out AsyncOperationHandle<T> handle)
        {
            handle = Addressables.LoadAssetAsync<T>(referenceLabel);
            return handle.WaitForCompletion();
        }

        public static void LoadAsync<T>(string nameOrLabel, Action<AsyncOperationHandle<T>> callback)
        {
            Addressables.LoadAssetAsync<T>(nameOrLabel).Completed += callback;
        }

        public static void LoadAsync<T>(AssetLabelReference referenceLabel, Action<AsyncOperationHandle<T>> callback)
        {
            Addressables.LoadAssetAsync<T>(referenceLabel).Completed += callback;
        }

        public static IList<T> LoadAll<T>(Action<T> callback, object[] namesOrLabels)
        {
            return Addressables.LoadAssetsAsync(namesOrLabels, callback, Addressables.MergeMode.Union, false).WaitForCompletion();
        }

        public static IList<T> LoadAll<T>(Action<T> callback, AssetLabelReference referenceLabel)
        {
            return Addressables.LoadAssetsAsync(referenceLabel, callback, false).WaitForCompletion();
        }

        // public static IList<T> LoadAll<T>(Action<T> callback, IEnumerable enumerable)
        // {
        //     return Addressables.LoadAssetsAsync(enumerable, callback, Addressables.MergeMode.Union, false).WaitForCompletion();
        // }

        public static void LoadAllAsync<T>(Action<T> processor, Action<AsyncOperationHandle<IList<T>>> callback, params string[] namesOrLabels)
        {
            Addressables.LoadAssetsAsync(namesOrLabels.AsEnumerable(), processor, Addressables.MergeMode.Union, false).Completed += callback;
        }

        public static void LoadAllAsync<T>(Action<T> processor, Action<AsyncOperationHandle<IList<T>>> callback, AssetLabelReference referenceLabel)
        {
            Addressables.LoadAssetsAsync(referenceLabel, processor, false).Completed += callback;
        }

        public static void LoadAllAsync<T>(Action<T> processor, Action<AsyncOperationHandle<IList<T>>> callback, IEnumerable enumerable)
        {
            Addressables.LoadAssetsAsync(enumerable, processor, false).Completed += callback;
        }

        public static SceneInstance LoadScene(string nameOrLabel, out AsyncOperationHandle<SceneInstance> handle, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true,
            int priority = 100, SceneReleaseMode releaseMode = SceneReleaseMode.ReleaseSceneWhenSceneUnloaded)
        {
            handle = Addressables.LoadSceneAsync(nameOrLabel, loadMode, activateOnLoad, priority, releaseMode);
            return handle.WaitForCompletion();
        }

        public static void LoadSceneAsync(string nameOrLabel, Action<AsyncOperationHandle<SceneInstance>> callback, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true,
            int priority = 100, SceneReleaseMode releaseMode = SceneReleaseMode.ReleaseSceneWhenSceneUnloaded)
        {
            Addressables.LoadSceneAsync(nameOrLabel, loadMode, activateOnLoad, priority, releaseMode).Completed += callback;
        }
        
        public static bool CheckAddressableKeyValidity(object key)
        {
            // LoadResourceLocationsAsync returns an AsyncOperationHandle<IList<IResourceLocation>>
            // If the key is not found, the list will be empty and the operation will succeed without error.
            IList<IResourceLocation> opHandle = Addressables.LoadResourceLocationsAsync(key, typeof(object)).WaitForCompletion();
            bool hasKey = opHandle.Count > 0;
            Addressables.Release(opHandle); // Don't forget to release the handle!
            return hasKey;
        }
    }
}
