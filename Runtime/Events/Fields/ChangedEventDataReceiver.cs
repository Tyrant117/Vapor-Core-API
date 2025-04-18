using System;
using UnityEngine;
using Vapor.Keys;

namespace Vapor.Events
{
    /// <summary>
    /// A serializable class container for receiving <see cref="EventData"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class ChangedEventDataReceiver
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData _eventData;

        public void Subscribe(Action<object> callback)
        {
            Debug.Assert(!_key.IsNone,$"Subscribe key is set to None");
            _eventData ??= EventBus.Get<EventData>(_key);
            _eventData.Subscribe(callback);
        }

        public void Unsubscribe(Action<object> callback)
        {
            _eventData ??= EventBus.Get<EventData>(_key);
            _eventData.Unsubscribe(callback);
        }
    }
    
    /// <summary>
    /// A serializable class container for receiving <see cref="EventData{T1}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class ChangedEventDataReceiver<T>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T> _eventData;

        public void Subscribe(Action<object, T> callback)
        {
            Debug.Assert(!_key.IsNone,$"Subscribe key is set to None");
            _eventData ??= EventBus.Get<EventData<T>>(_key);
            _eventData.Subscribe(callback);
        }

        public void Unsubscribe(Action<object, T> callback)
        {
            _eventData ??= EventBus.Get<EventData<T>>(_key);
            _eventData.Unsubscribe(callback);
        }
    }
    
    /// <summary>
    /// A serializable class container for receiving <see cref="EventData{T1,T2}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class ChangedEventDataReceiver<T1, T2>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T1, T2> _eventData;

        /// <summary>
        /// Subscribe to the event.
        /// </summary>
        /// <param name="callback">The callback to fire when an event with this key is raised.</param>
        public void Subscribe(Action<object, T1, T2> callback)
        {
            Debug.Assert(!_key.IsNone,$"Subscribe key is set to None");
            _eventData ??= EventBus.Get<EventData<T1, T2>>(_key);
            _eventData.Subscribe(callback);
        }

        /// <summary>
        /// Unsubscribe from the event.
        /// </summary>
        public void Unsubscribe(Action<object, T1, T2> callback)
        {
            _eventData ??= EventBus.Get<EventData<T1, T2>>(_key);
            _eventData.Unsubscribe(callback);
        }
    }
    
    /// <summary>
    /// A serializable class container for receiving <see cref="EventData{T1,T2,T3}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class ChangedEventDataReceiver<T1, T2, T3>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T1, T2, T3> _eventData;

        /// <summary>
        /// Subscribe to the event.
        /// </summary>
        /// <param name="callback">The callback to fire when an event with this key is raised.</param>
        public void Subscribe(Action<object, T1, T2, T3> callback)
        {
            Debug.Assert(!_key.IsNone,$"Subscribe key is set to None");
            _eventData ??= EventBus.Get<EventData<T1, T2, T3>>(_key);
            _eventData.Subscribe(callback);
        }

        /// <summary>
        /// Unsubscribe from the event.
        /// </summary>
        public void Unsubscribe(Action<object, T1, T2, T3> callback)
        {
            _eventData ??= EventBus.Get<EventData<T1, T2, T3>>(_key);
            _eventData.Unsubscribe(callback);
        }
    }
    
    /// <summary>
    /// A serializable class container for receiving <see cref="EventData{T1,T2,T3,T4}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [Serializable]
    public class ChangedEventDataReceiver<T1, T2, T3, T4>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T1, T2, T3, T4> _eventData;

        /// <summary>
        /// Subscribe to the event.
        /// </summary>
        /// <param name="callback">The callback to fire when an event with this key is raised.</param>
        public void Subscribe(Action<object, T1, T2, T3, T4> callback)
        {
            Debug.Assert(!_key.IsNone,$"Subscribe key is set to None");
            _eventData ??= EventBus.Get<EventData<T1, T2, T3, T4>>(_key);
            _eventData.Subscribe(callback);
        }

        /// <summary>
        /// Unsubscribe from the event.
        /// </summary>
        public void Unsubscribe(Action<object, T1, T2, T3, T4> callback)
        {
            _eventData ??= EventBus.Get<EventData<T1, T2, T3, T4>>(_key);
            _eventData.Unsubscribe(callback);
        }
    }
}
