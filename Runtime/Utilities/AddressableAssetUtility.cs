using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Vapor
{
    public class AddressableAssetUtility : MonoBehaviour
    {
        public static GameObject Instantiate(string nameOrLabel, Transform parent, bool instantiateInWorldSpace)
        {
            return Addressables.InstantiateAsync(nameOrLabel, parent, instantiateInWorldSpace).WaitForCompletion();
        }

        public static GameObject Instantiate(string nameOrLabel, Vector3 position, Quaternion rotation)
        {
            return Addressables.InstantiateAsync(nameOrLabel, position, rotation).WaitForCompletion();
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

        public static T Load<T>(string nameOrLabel)
        {
            return Addressables.LoadAssetAsync<T>(nameOrLabel).WaitForCompletion();
        }

        public static T Load<T>(AssetLabelReference referenceLabel)
        {
            return Addressables.LoadAssetAsync<T>(referenceLabel).WaitForCompletion();
        }

        public static void LoadAsync<T>(string nameOrLabel, Action<AsyncOperationHandle<T>> callback)
        {
            Addressables.LoadAssetAsync<T>(nameOrLabel).Completed += callback;
        }

        public static void LoadAsync<T>(AssetLabelReference referenceLabel, Action<AsyncOperationHandle<T>> callback)
        {
            Addressables.LoadAssetAsync<T>(referenceLabel).Completed += callback;
        }

        public static IList<T> LoadAll<T>(Action<T> callback, params string[] namesOrLabels)
        {
            return Addressables.LoadAssetsAsync(namesOrLabels.AsEnumerable(), callback, Addressables.MergeMode.Union, false).WaitForCompletion();
        }

        public static IList<T> LoadAll<T>(Action<T> callback, AssetLabelReference referenceLabel)
        {
            return Addressables.LoadAssetsAsync<T>(referenceLabel, callback, false).WaitForCompletion();
        }

        public static IList<T> LoadAll<T>(Action<T> callback, IEnumerable enumerable)
        {
            return Addressables.LoadAssetsAsync(enumerable, callback, Addressables.MergeMode.Union, false).WaitForCompletion();
        }

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
    }
}
