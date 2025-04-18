using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Vapor.Events
{
    public class CachedProviderData<TResult> : IProviderData where TResult : class
    {
        private TResult _cached;
        public event Func<TResult> OnRequestRaised;

        public void Subscribe(Func<TResult> listener)
        {
            OnRequestRaised += listener;
        }

        public void Unsubscribe(Func<TResult> listener)
        {
            OnRequestRaised -= listener;
        }

        public void RemoveAllListeners() => OnRequestRaised = null;

        public T Request<T>() where T : TResult
        {
            if (_cached != null)
            {
                return (T)_cached;
            }

            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            _cached = OnRequestRaised.Invoke();

            return (T)_cached;
        }

        public IEnumerator RequestRoutine<T>(Action<T> callback) where T : TResult
        {
            while (_cached == null)
            {
                if (OnRequestRaised != null)
                {
                    _cached = OnRequestRaised.Invoke();
                }

                yield return null;
            }

            if (_cached != null)
            {
                callback.Invoke((T)_cached);
            }
        }

        public async Awaitable<T> RequestAsync<T>() where T : TResult
        {
            while (_cached == null)
            {
                if (OnRequestRaised != null)
                {
                    _cached = OnRequestRaised.Invoke();
                }
                await Awaitable.NextFrameAsync();
            }

            return (T)_cached;
        }
    }

    public class CachedProviderData<T1, TResult> : IProviderData where TResult : class
    {
        private TResult _cached;
        public event Func<T1, TResult> OnRequestRaised;

        public void Subscribe(Func<T1, TResult> listener)
        {
            OnRequestRaised += listener;
        }

        public void Unsubscribe(Func<T1, TResult> listener)
        {
            OnRequestRaised -= listener;
        }

        public void RemoveAllListeners() => OnRequestRaised = null;
        public T Request<T>(T1 value1) where T : TResult
        {
            if (_cached != null)
            {
                return (T)_cached;
            }

            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            _cached = OnRequestRaised.Invoke(value1);

            return (T)_cached;
        }
        
        public IEnumerator RequestRoutine<T>(T1 value1, Action<T> callback) where T : TResult
        {
            if (_cached != null)
            {
                callback.Invoke((T)_cached);
            }
            else
            {
                while (OnRequestRaised == null)
                {
                    yield return null;
                }

                _cached = OnRequestRaised.Invoke(value1);
                callback.Invoke((T)_cached);
            }
        }
    }

    public class CachedProviderData<T1, T2, TResult> : IProviderData where TResult : class
    {
        private TResult _cached;
        public event Func<T1, T2, TResult> OnRequestRaised;

        public void Subscribe(Func<T1, T2, TResult> listener)
        {
            OnRequestRaised += listener;
        }

        public void Unsubscribe(Func<T1, T2, TResult> listener)
        {
            OnRequestRaised -= listener;
        }

        public void RemoveAllListeners() => OnRequestRaised = null;

        public T Request<T>(T1 value1, T2 value2) where T : TResult
        {
            if (_cached != null)
            {
                return (T)_cached;
            }

            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            _cached = OnRequestRaised.Invoke(value1, value2);

            return (T)_cached;
        }
        
        public IEnumerator RequestRoutine<T>(T1 value1, T2 value2, Action<T> callback) where T : TResult
        {
            if (_cached != null)
            {
                callback.Invoke((T)_cached);
            }
            else
            {
                while (OnRequestRaised == null)
                {
                    yield return null;
                }

                _cached = OnRequestRaised.Invoke(value1, value2);
                callback.Invoke((T)_cached);
            }
        }
    }

    public class CachedProviderData<T1, T2, T3, TResult> : IProviderData where TResult : class
    {
        private TResult _cached;
        public event Func<T1, T2, T3, TResult> OnRequestRaised;

        public void Subscribe(Func<T1, T2, T3, TResult> listener)
        {
            OnRequestRaised += listener;
        }

        public void Unsubscribe(Func<T1, T2, T3, TResult> listener)
        {
            OnRequestRaised -= listener;
        }

        public void RemoveAllListeners() => OnRequestRaised = null;

        public T Request<T>(T1 value1, T2 value2, T3 value3) where T : TResult
        {
            if (_cached != null)
            {
                return (T)_cached;
            }

            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            _cached = OnRequestRaised.Invoke(value1, value2, value3);

            return (T)_cached;
        }
        
        public IEnumerator RequestRoutine<T>(T1 value1, T2 value2, T3 value3, Action<T> callback) where T : TResult
        {
            if (_cached != null)
            {
                callback.Invoke((T)_cached);
            }
            else
            {
                while (OnRequestRaised == null)
                {
                    yield return null;
                }

                _cached = OnRequestRaised.Invoke(value1, value2, value3);
                callback.Invoke((T)_cached);
            }
        }
    }

    public class CachedProviderData<T1, T2, T3, T4, TResult> : IProviderData where TResult : class
    {
        private TResult _cached;
        public event Func<T1, T2, T3, T4, TResult> OnRequestRaised;

        public void Subscribe(Func<T1, T2, T3, T4, TResult> listener)
        {
            OnRequestRaised += listener;
        }

        public void Unsubscribe(Func<T1, T2, T3, T4, TResult> listener)
        {
            OnRequestRaised -= listener;
        }

        public void RemoveAllListeners() => OnRequestRaised = null;

        public T Request<T>(T1 value1, T2 value2, T3 value3, T4 value4) where T : TResult
        {
            if (_cached != null)
            {
                return (T)_cached;
            }

            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            _cached = OnRequestRaised.Invoke(value1, value2, value3, value4);

            return (T)_cached;
        }
        
        public IEnumerator RequestRoutine<T>(T1 value1, T2 value2, T3 value3, T4 value4, Action<T> callback) where T : TResult
        {
            if (_cached != null)
            {
                callback.Invoke((T)_cached);
            }
            else
            {
                while (OnRequestRaised == null)
                {
                    yield return null;
                }

                _cached = OnRequestRaised.Invoke(value1, value2, value3, value4);
                callback.Invoke((T)_cached);
            }
        }
    }
}
