using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Tracing for the inspector tree. Calls are compiled out unless VAPOR_INSPECTOR_DEBUG is defined,
    /// including the evaluation of their arguments, so leaving them in place costs nothing.
    /// Genuine problems should still use <see cref="Debug.LogError"/> directly.
    /// </summary>
    internal static class InspectorDebug
    {
        [Conditional("VAPOR_INSPECTOR_DEBUG")]
        public static void Log(object message)
        {
            Debug.Log(message);
        }

        [Conditional("VAPOR_INSPECTOR_DEBUG")]
        public static void LogWarning(object message)
        {
            Debug.LogWarning(message);
        }
    }
}
