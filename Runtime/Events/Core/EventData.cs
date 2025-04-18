using System;

namespace Vapor.Events
{
    public class EventData : IEventData
    {
        public event Action<object> OnEventRaised;

        public void Subscribe(Action<object> listener)
        {
            OnEventRaised += listener;
        }

        public void Unsubscribe(Action<object> listener)
        {
            OnEventRaised -= listener;
        }

        public void RaiseEvent(object sender)
        {
            OnEventRaised?.Invoke(sender);
        }
    }
    
    public class EventData<T1> : IEventData
    {
        public event Action<object, T1> OnEventRaised;

        public void Subscribe(Action<object, T1> listener)
        {
            OnEventRaised += listener;
        }

        public void Unsubscribe(Action<object, T1> listener)
        {
            OnEventRaised -= listener;
        }

        public void RaiseEvent(object sender, T1 value)
        {
            OnEventRaised?.Invoke(sender, value);
        }
    }

    public class EventData<T1, T2> : IEventData
    {
        public event Action<object, T1, T2> OnEventRaised;

        public void Subscribe(Action<object, T1, T2> listener)
        {
            OnEventRaised += listener;
        }

        public void Unsubscribe(Action<object, T1, T2> listener)
        {
            OnEventRaised -= listener;
        }

        public void RaiseEvent(object sender, T1 value1, T2 value2)
        {
            OnEventRaised?.Invoke(sender, value1, value2);
        }
    }

    public class EventData<T1, T2, T3> : IEventData
    {
        public event Action<object, T1, T2, T3> OnEventRaised;

        public void Subscribe(Action<object, T1, T2, T3> listener)
        {
            OnEventRaised += listener;
        }

        public void Unsubscribe(Action<object, T1, T2, T3> listener)
        {
            OnEventRaised -= listener;
        }

        public void RaiseEvent(object sender, T1 value1, T2 value2, T3 value3)
        {
            OnEventRaised?.Invoke(sender, value1, value2, value3);
        }
    }

    public class EventData<T1, T2, T3, T4> : IEventData
    {
        public event Action<object, T1, T2, T3, T4> OnEventRaised;

        public void Subscribe(Action<object, T1, T2, T3, T4> listener)
        {
            OnEventRaised += listener;
        }

        public void Unsubscribe(Action<object, T1, T2, T3, T4> listener)
        {
            OnEventRaised -= listener;
        }

        public void RaiseEvent(object sender, T1 value1, T2 value2, T3 value3, T4 value4)
        {
            OnEventRaised?.Invoke(sender, value1, value2, value3, value4);
        }
    }
}
