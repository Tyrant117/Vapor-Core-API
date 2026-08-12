using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace Vapor
{
    [AutoStaticsCleanup]
    public static partial class GameSessionState
    {
        private static readonly Dictionary<string, object> s_SessionData = new();

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
