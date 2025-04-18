using System;
using System.Collections;
using UnityEngine;

namespace Vapor.Events
{
    public class ReferenceProviderData<TResult> : IProviderData where TResult : class
    {
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
            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            return (T)OnRequestRaised.Invoke();
        }

        public IEnumerator RequestRoutine<T>(Action<T> callback) where T : TResult
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }
            callback.Invoke((T)OnRequestRaised.Invoke());
        }
    }

    public class ReferenceProviderData<T1, TResult> : IProviderData where TResult : class
    {
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
            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            return (T)OnRequestRaised.Invoke(value1);
        }

        public IEnumerator RequestRoutine<T>(T1 value1, Action<T> callback) where T : TResult
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }

            callback.Invoke((T)OnRequestRaised.Invoke(value1));
        }
    }

    public class ReferenceProviderData<T1, T2, TResult> : IProviderData where TResult : class
    {
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
            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            return (T)OnRequestRaised.Invoke(value1, value2);
        }

        public IEnumerator RequestRoutine<T>(T1 value1, T2 value2, Action<T> callback) where T : TResult
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }

            callback.Invoke((T)OnRequestRaised.Invoke(value1, value2));
        }
    }

    public class ReferenceProviderData<T1, T2, T3, TResult> : IProviderData where TResult : class
    {
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
            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            return (T)OnRequestRaised.Invoke(value1, value2, value3);
        }

        public IEnumerator RequestRoutine<T>(T1 value1, T2 value2, T3 value3, Action<T> callback) where T : TResult
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }
            callback.Invoke((T)OnRequestRaised.Invoke(value1, value2, value3));
        }
    }

    public class ReferenceProviderData<T1, T2, T3, T4, TResult> : IProviderData where TResult : class
    {
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
            Debug.Assert(OnRequestRaised != null, $"CachedProviderData<{typeof(TResult)}> was requested before any events were subscribed.");
            return (T)OnRequestRaised.Invoke(value1, value2, value3, value4);
        }

        public IEnumerator RequestRoutine<T>(T1 value1, T2 value2, T3 value3, T4 value4, Action<T> callback) where T : TResult
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }
            callback.Invoke((T)OnRequestRaised.Invoke(value1, value2, value3, value4));
        }
    }
}
