using UnityEngine;
using Vapor.Keys;

namespace Vapor.Events
{
    /// <summary>
    /// A serializable class container for sending <see cref="EventData{T1}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [System.Serializable]
    public class ChangedEventDataSender
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData _eventData;

        /// <summary>
        /// Raises an event linked with Key.
        /// </summary>
        /// <param name="sender">The object sending the event</param>
        public void RaiseEvent(object sender)
        {
            Debug.Assert(!_key.IsNone,$"{sender}: RaiseEvent is set to None");
            _eventData ??= EventBus.Get<EventData>(_key);
            _eventData.RaiseEvent(sender);
        }
    }
    
    /// <summary>
    /// A serializable class container for sending <see cref="EventData{T1}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [System.Serializable]
    public class ChangedEventDataSender<T>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T> _eventData;

        /// <summary>
        /// Raises an event linked with Key.
        /// </summary>
        /// <param name="sender">The object sending the event</param>
        /// <param name="value">The value being sent</param>
        public void RaiseEvent(object sender, T value)
        {
            Debug.Assert(!_key.IsNone,$"{sender}: RaiseEvent is set to None");
            _eventData ??= EventBus.Get<EventData<T>>(_key);
            _eventData.RaiseEvent(sender, value);
        }
    }

    /// <summary>
    /// A serializable class container for sending <see cref="EventData{T1,T2}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [System.Serializable]
    public class ChangedEventDataSender<T1, T2>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T1, T2> _eventData;

        /// <summary>
        /// Raises an event linked with Key.
        /// </summary>
        /// <param name="sender">The object sending the event</param>
        /// <param name="value1">The first value being sent</param>
        /// <param name="value2">The second value being sent</param>
        public void RaiseEvent(object sender, T1 value1, T2 value2)
        {
            Debug.Assert(!_key.IsNone,$"{sender}: RaiseEvent is set to None");
            _eventData ??= EventBus.Get<EventData<T1, T2>>(_key);
            _eventData.RaiseEvent(sender, value1, value2);
        }
    }
    
    /// <summary>
    /// A serializable class container for sending <see cref="EventData{T1,T2,T3}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [System.Serializable]
    public class ChangedEventDataSender<T1, T2, T3>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T1, T2, T3> _eventData;

        /// <summary>
        /// Raises an event linked with Key.
        /// </summary>
        /// <param name="sender">The object sending the event</param>
        /// <param name="value1">The first value being sent</param>
        /// <param name="value2">The second value being sent</param>
        /// <param name="value3">The third value being sent</param>
        public void RaiseEvent(object sender, T1 value1, T2 value2, T3 value3)
        {
            Debug.Assert(!_key.IsNone,$"{sender}: RaiseEvent is set to None");
            _eventData ??= EventBus.Get<EventData<T1, T2, T3>>(_key);
            _eventData.RaiseEvent(sender, value1, value2, value3);
        }
    }
    
    /// <summary>
    /// A serializable class container for sending <see cref="EventData{T1,T2,T3,T4}"/> events.
    /// Should be used to link keys to events in inspectors.
    /// </summary>
    [System.Serializable]
    public class ChangedEventDataSender<T1, T2, T3, T4>
    {
        [SerializeField]
        private KeyDropdownValue _key;

        private EventData<T1, T2, T3, T4> _eventData;

        /// <summary>
        /// Raises an event linked with Key.
        /// </summary>
        /// <param name="sender">The object sending the event</param>
        /// <param name="value1">The first value being sent</param>
        /// <param name="value2">The second value being sent</param>
        /// <param name="value3">The third value being sent</param>
        /// <param name="value4">The fourth value being sent</param>
        public void RaiseEvent(object sender, T1 value1, T2 value2, T3 value3, T4 value4)
        {
            Debug.Assert(!_key.IsNone,$"{sender}: RaiseEvent is set to None");
            _eventData ??= EventBus.Get<EventData<T1, T2, T3, T4>>(_key);
            _eventData.RaiseEvent(sender, value1, value2, value3, value4);
        }
    }
}
