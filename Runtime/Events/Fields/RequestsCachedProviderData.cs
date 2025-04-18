using System;
using UnityEngine;
using UnityEngine.Serialization;
using Vapor.Keys;

namespace Vapor.Events
{
    /// <summary>
    /// A serializable class container for requesting <see cref="CachedProviderData{TResult}"/> results.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class RequestsCachedProviderData<TResult> where TResult : class
    {
        [SerializeField]
        private KeyDropdownValue _key;
        
        private CachedProviderData<TResult> _providerData;
        private Coroutine _requestRoutine;

        /// <summary>
        /// Requests for a component to be returned once its value is not null.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> doing the requesting</param>
        /// <param name="callback">The callback to fire once the result is not null</param>
        public void RequestComponent(MonoBehaviour requester, Action<TResult> callback)
        {
            Debug.Assert(!_key.IsNone,$"{requester}: RequestComponent is set to None");
            _providerData ??= ProviderBus.Get<CachedProviderData<TResult>>(_key);
            _requestRoutine = requester.StartCoroutine(_providerData.RequestRoutine(callback));
        }

        /// <summary>
        /// Stops requesting for a value to be returned.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> that is stopping the request</param>
        public void StopRequesting(MonoBehaviour requester)
        {
            requester.StopCoroutine(_requestRoutine);
        }
    }
    
    /// <summary>
    /// A serializable class container for requesting <see cref="CachedProviderData{T1,TResult}"/> results.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class RequestsCachedProviderData<TValue1, TResult> where TResult : class
    {
        [SerializeField]
        private KeyDropdownValue _key;
        
        private CachedProviderData<TValue1, TResult> _providerData;
        private Coroutine _requestRoutine;

        /// <summary>
        /// Requests for a component to be returned once its value is not null.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> doing the requesting</param>
        /// <param name="callback">The callback to fire once the result is not null</param>
        public void RequestComponent(MonoBehaviour requester, TValue1 value1, Action<TResult> callback)
        {
            Debug.Assert(!_key.IsNone,$"{requester}: RequestComponent is set to None");
            _providerData ??= ProviderBus.Get<CachedProviderData<TValue1, TResult>>(_key);
            _requestRoutine = requester.StartCoroutine(_providerData.RequestRoutine(value1, callback));
        }

        /// <summary>
        /// Stops requesting for a value to be returned.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> that is stopping the request</param>
        public void StopRequesting(MonoBehaviour requester)
        {
            requester.StopCoroutine(_requestRoutine);
        }
    }
    
    /// <summary>
    /// A serializable class container for requesting <see cref="CachedProviderData{T1,T2,TResult}"/> results.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class RequestsCachedProviderData<TValue1, TValue2, TResult> where TResult : class
    {
        [SerializeField]
        private KeyDropdownValue _key;
        
        private CachedProviderData<TValue1, TValue2, TResult> _providerData;
        private Coroutine _requestRoutine;

        /// <summary>
        /// Requests for a component to be returned once its value is not null.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> doing the requesting</param>
        /// <param name="callback">The callback to fire once the result is not null</param>
        public void RequestComponent(MonoBehaviour requester, TValue1 value1, TValue2 value2, Action<TResult> callback)
        {
            Debug.Assert(!_key.IsNone,$"{requester}: RequestComponent is set to None");
            _providerData ??= ProviderBus.Get<CachedProviderData<TValue1, TValue2, TResult>>(_key);
            _requestRoutine = requester.StartCoroutine(_providerData.RequestRoutine(value1, value2, callback));
        }

        /// <summary>
        /// Stops requesting for a value to be returned.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> that is stopping the request</param>
        public void StopRequesting(MonoBehaviour requester)
        {
            requester.StopCoroutine(_requestRoutine);
        }
    }
    
    /// <summary>
    /// A serializable class container for requesting <see cref="CachedProviderData{T1,T2,T3,TResult}"/> results.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class RequestsCachedProviderData<TValue1, TValue2, TValue3, TResult> where TResult : class
    {
        [SerializeField]
        private KeyDropdownValue _key;
        
        private CachedProviderData<TValue1, TValue2, TValue3, TResult> _providerData;
        private Coroutine _requestRoutine;

        /// <summary>
        /// Requests for a component to be returned once its value is not null.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> doing the requesting</param>
        /// <param name="callback">The callback to fire once the result is not null</param>
        public void RequestComponent(MonoBehaviour requester, TValue1 value1, TValue2 value2, TValue3 value3, Action<TResult> callback)
        {
            Debug.Assert(!_key.IsNone,$"{requester}: RequestComponent is set to None");
            _providerData ??= ProviderBus.Get<CachedProviderData<TValue1, TValue2, TValue3, TResult>>(_key);
            _requestRoutine = requester.StartCoroutine(_providerData.RequestRoutine(value1, value2, value3, callback));
        }

        /// <summary>
        /// Stops requesting for a value to be returned.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> that is stopping the request</param>
        public void StopRequesting(MonoBehaviour requester)
        {
            requester.StopCoroutine(_requestRoutine);
        }
    }
    
    /// <summary>
    /// A serializable class container for requesting <see cref="CachedProviderData{T1,T2,T3,T4,TResult}"/> results.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class RequestsCachedProviderData<TValue1, TValue2, TValue3, TValue4, TResult> where TResult : class
    {
        [SerializeField]
        private KeyDropdownValue _key;
        
        private CachedProviderData<TValue1, TValue2, TValue3, TValue4, TResult> _providerData;
        private Coroutine _requestRoutine;

        /// <summary>
        /// Requests for a component to be returned once its value is not null.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> doing the requesting</param>
        /// <param name="callback">The callback to fire once the result is not null</param>
        public void RequestComponent(MonoBehaviour requester, TValue1 value1, TValue2 value2, TValue3 value3, TValue4 value4, Action<TResult> callback)
        {
            Debug.Assert(!_key.IsNone,$"{requester}: RequestComponent is set to None");
            _providerData ??= ProviderBus.Get<CachedProviderData<TValue1, TValue2, TValue3, TValue4, TResult>>(_key);
            _requestRoutine = requester.StartCoroutine(_providerData.RequestRoutine(value1, value2, value3, value4, callback));
        }

        /// <summary>
        /// Stops requesting for a value to be returned.
        /// </summary>
        /// <param name="requester">The <see cref="MonoBehaviour"/> that is stopping the request</param>
        public void StopRequesting(MonoBehaviour requester)
        {
            requester.StopCoroutine(_requestRoutine);
        }
    }
}
