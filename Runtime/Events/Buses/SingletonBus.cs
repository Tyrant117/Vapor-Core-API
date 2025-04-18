using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Events
{
    /// <summary>
    /// A static class that can be used to create singletons. 
    /// </summary>
    public static class SingletonBus
    {
        public static readonly Dictionary<Type, MonoBehaviour> SingletonMap = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            SingletonMap.Clear();
        }

        /// <summary>
        /// Creates a singleton of the supplied <see cref="MonoBehaviour"/>. <br />
        /// Does not set the returned singleton as <see cref="GameObject.DontDestroyOnLoad"/>. That should be done in the Awake implementation.
        /// </summary>
        /// <typeparam name="T">The <see cref="MonoBehaviour"/> to create as a singleton</typeparam>
        /// <returns>The instantiated singleton of type T</returns>
        public static T Get<T>() where T : MonoBehaviour
        {
            if (SingletonMap.TryGetValue(typeof(T), out var handler) && handler != null)
            {
                return (T)handler;
            }

            EventLogging.Log($"[Singleton Bus] Adding Provider: [{nameof(T)}] of Type: {typeof(T)}");
            var go = new GameObject();
            var comp = go.AddComponent<T>();
            SingletonMap.Add(typeof(T), comp);
            return (T)SingletonMap[typeof(T)];
        }
    }
}
