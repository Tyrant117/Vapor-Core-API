using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Vapor.Unsafe;

namespace Vapor
{
    public class AddressableData : IData
    {
        public string Name { get; }
        public uint Key { get; }
        public string AddressableName { get; }

        public AddressableData(string name, string addressableName)
        {
            Name = name;
            Key = string.IsNullOrEmpty(name) ? 0 : name.Hash32();
            AddressableName = addressableName;
        }

        public T Load<T>(out AsyncOperationHandle<T> handle) where T : class
        {
            if (string.IsNullOrEmpty(AddressableName))
            {
                handle = default;
                return null;
            }

            handle = LoadAsync<T>();
            handle.WaitForCompletion();
            return handle.Result;
        }

        public AsyncOperationHandle<T> LoadAsync<T>() where T : class
        {
            return string.IsNullOrEmpty(AddressableName) ? default : Addressables.LoadAssetAsync<T>(AddressableName);
        }

        // GameObjects
        public GameObject Instantiate(Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(AddressableName))
            {
                return null;
            }
            
            var handle = InstantiateAsync(position, rotation);
            handle.WaitForCompletion();
            return handle.Result;
        }
        
        public GameObject Instantiate(Transform parent, bool instantiateInWorldSpace)
        {
            if (string.IsNullOrEmpty(AddressableName))
            {
                return null;
            }
            
            var handle = InstantiateAsync(parent, instantiateInWorldSpace);
            handle.WaitForCompletion();
            return handle.Result;
        }
        
        public AsyncOperationHandle<GameObject> InstantiateAsync(Vector3 position, Quaternion rotation)
        {
            return string.IsNullOrEmpty(AddressableName) ? default : Addressables.InstantiateAsync(AddressableName, position, rotation);
        }
        
        public AsyncOperationHandle<GameObject> InstantiateAsync(Transform parent, bool instantiateInWorldSpace)
        {
            return string.IsNullOrEmpty(AddressableName) ? default : Addressables.InstantiateAsync(AddressableName, parent, instantiateInWorldSpace);
        }
    }
}
