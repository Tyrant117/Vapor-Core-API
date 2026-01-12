using UnityEngine;

namespace Vapor
{
    public static class GameSessionState
    {
        private static readonly System.Collections.Generic.Dictionary<string, object> s_SessionData = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_SessionData.Clear();
        }

        public static void SetValue<T>(string key, T value)
        {
            s_SessionData[key] = value;
        }

        public static T GetValue<T>(string key, T defaultValue = default)
        {
            if (s_SessionData.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        public static bool HasKey(string key)
        {
            return s_SessionData.ContainsKey(key);
        }

        public static void Clear()
        {
            s_SessionData.Clear();
        }
    }
}
