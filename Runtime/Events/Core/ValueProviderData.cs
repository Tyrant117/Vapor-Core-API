using System;
using System.Collections;

namespace Vapor.Events
{
    public class ValueProviderData<TResult> : IProviderData where TResult : struct
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

        public TResult Request(TResult defaultResult)
        {
            return OnRequestRaised != null ? OnRequestRaised.Invoke() : defaultResult;
        }

        public IEnumerator RequestRoutine(Action<TResult> callback)
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }
            callback.Invoke(OnRequestRaised.Invoke());
        }
    }

    public class ValueProviderData<T1, TResult> : IProviderData where TResult : struct
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

        public TResult Request(T1 value1, TResult defaultResult)
        {
            return OnRequestRaised != null ? OnRequestRaised.Invoke(value1) : defaultResult;
        }

        public IEnumerator RequestRoutine(T1 value1, Action<TResult> callback)
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }

            callback.Invoke(OnRequestRaised.Invoke(value1));
        }
    }

    public class ValueProviderData<T1, T2, TResult> : IProviderData where TResult : struct
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

        public TResult Request(T1 value1, T2 value2, TResult defaultResult)
        {
            return OnRequestRaised != null ? OnRequestRaised.Invoke(value1, value2) : defaultResult;
        }

        public IEnumerator RequestRoutine(T1 value1, T2 value2, Action<TResult> callback)
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }

            callback.Invoke(OnRequestRaised.Invoke(value1, value2));
        }
    }

    public class ValueProviderData<T1, T2, T3, TResult> : IProviderData where TResult : struct
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

        public TResult Request(T1 value1, T2 value2, T3 value3, TResult defaultResult)
        {
            return OnRequestRaised != null ? OnRequestRaised.Invoke(value1, value2, value3) : defaultResult;
        }

        public IEnumerator RequestRoutine(T1 value1, T2 value2, T3 value3, Action<TResult> callback)
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }
            callback.Invoke(OnRequestRaised.Invoke(value1, value2, value3));
        }
    }

    public class ValueProviderData<T1, T2, T3, T4, TResult> : IProviderData where TResult : struct
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

        public TResult Request(T1 value1, T2 value2, T3 value3, T4 value4, TResult defaultResult)
        {
            return OnRequestRaised != null ? OnRequestRaised.Invoke(value1, value2, value3, value4) : defaultResult;
        }

        public IEnumerator RequestRoutine(T1 value1, T2 value2, T3 value3, T4 value4, Action<TResult> callback)
        {
            while (OnRequestRaised == null)
            {
                yield return null;
            }
            callback.Invoke(OnRequestRaised.Invoke(value1, value2, value3, value4));
        }
    }
}
