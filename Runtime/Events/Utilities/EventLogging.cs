using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Vapor.Events
{
    /// <summary>
    /// A static class for conditionally logging messages from events.
    /// </summary>
    public static class EventLogging
    {
        /// <summary>
        /// Logs a message to the Unity Console when "VAPOR_EVENT_LOGGING is defined in the project.
        /// </summary>
        /// <param name="message">String or object to be converted to string representation for display.</param>
        [Conditional("VAPOR_EVENT_LOGGING")]
        public static void Log(object message)
        {
            Debug.Log(message);
        }
    }
}
