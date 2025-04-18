using System;
using System.Collections.Generic;
using UnityEngine;
using Vapor;

namespace Vapor.Events
{
    /// <summary>
    /// Static class used to access events.
    /// </summary>
    public static class EventBus
    {
        public static readonly Dictionary<int, IEventData> EventMap = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            EventMap.Clear();
        }

        /// <summary>
        /// Gets or creates an instance of the event at the supplied id. This id should typically be a auto-generated guid, but any integer will work. <br />
        /// <br />The event should always be cached or only used in loading and unloading.
        /// </summary>
        /// <typeparam name="T">The type of event. Must implement <see cref="IEventData"/></typeparam>
        /// <param name="eventID">The id of the event.</param>
        /// <returns>The <see cref="IEventData"/> associated with the id</returns>
        public static T Get<T>(int eventID) where T : IEventData
        {
            if (EventMap.TryGetValue(eventID, out var handler))
            {
                return (T)handler;
            }

            EventLogging.Log($"[Event Bus] Adding Event: [{eventID}] of Type: {typeof(T)}");
            EventMap.Add(eventID, Activator.CreateInstance<T>());
            return (T)EventMap[eventID];
        }

        /// <summary>
        /// Gets or creates an instance of the event at the supplied id. This id should typically be a auto-generated guid, but any string that isn't empty or null will work. <br />
        /// <br />The event should always be cached or only used in loading and unloading. <br />
        /// <b>String/Int collisions will not be detected!</b>
        /// </summary>
        /// <typeparam name="T">The type of event. Must implement <see cref="IEventData"/></typeparam>
        /// <param name="eventName">The name of the event</param>
        /// <returns>The <see cref="IEventData"/> associated with the name</returns>
        public static T Get<T>(string eventName) where T : IEventData
        {
            var eventID = eventName.GetStableHashU16();
            if (EventMap.TryGetValue(eventID, out var handler))
            {
                return (T)handler;
            }

            EventLogging.Log($"[Event Bus] Adding Event: [{eventName}] of Type: {typeof(T)}");
            EventMap.Add(eventID, Activator.CreateInstance<T>());
            return (T)EventMap[eventID];
        }

        /// <summary>
        /// Gets or creates an instance of the event from the supplied eventKey. <br />
        /// <br />The event should always be cached or only used in loading and unloading. <br />
        /// </summary>
        /// <typeparam name="T">The type of event. Must implement <see cref="IEventData"/></typeparam>
        /// <param name="eventKey">The eventKey for the event</param>
        /// <returns>The <see cref="IEventData"/> associated with the key</returns>
        public static T Get<T>(EventKeySo eventKey) where T : IEventData
        {
            var eventID = eventKey.Key;
            if (EventMap.TryGetValue(eventID, out var handler))
            {
                return (T)handler;
            }

            EventLogging.Log($"[Event Bus] Adding Event: [{eventKey.name}] of Type: {typeof(T)}");
            EventMap.Add(eventID, Activator.CreateInstance<T>());
            return (T)EventMap[eventID];
        }
    }
}
