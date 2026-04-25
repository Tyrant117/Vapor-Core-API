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
            Key = Name.Hash32();
            AddressableName = addressableName;
        }

        public T Load<T>(out AsyncOperationHandle<T> handle) where T : class
        {
            if (AddressableName.EmptyOrNull())
            {
                handle = default;
                return null;
            }

            return AddressableAssetUtility.Load(AddressableName, out handle);
        }

        public void LoadAsync<T>(out AsyncOperationHandle<T> handle) where T : class
        {
            if (AddressableName.EmptyOrNull())
            {
                handle = default;
                return;
            }

            handle = Addressables.LoadAssetAsync<T>(AddressableName);
        }
        
        // GameObjects
        public GameObject Instantiate(Vector3 position, Quaternion rotation)
        {
            InstantiateAsync(position, rotation, out var handle);
            handle.WaitForCompletion();
            return handle.Result;
        }
        
        public GameObject Instantiate(Transform parent, bool instantiateInWorldSpace)
        {
            InstantiateAsync(parent, instantiateInWorldSpace, out var handle);
            handle.WaitForCompletion();
            return handle.Result;
        }
        
        public void InstantiateAsync(Vector3 position, Quaternion rotation, out AsyncOperationHandle<GameObject> handle)
        {
            if (AddressableName.EmptyOrNull())
            {
                handle = default;
                return;
            }

            handle = Addressables.InstantiateAsync(AddressableName, position, rotation);
        }
        
        public void InstantiateAsync(Transform parent, bool instantiateInWorldSpace, out AsyncOperationHandle<GameObject> handle)
        {
            if (AddressableName.EmptyOrNull())
            {
                handle = default;
                return;
            }

            handle = Addressables.InstantiateAsync(AddressableName, parent, instantiateInWorldSpace);
        }
    }
}
