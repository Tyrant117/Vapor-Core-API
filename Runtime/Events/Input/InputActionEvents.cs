using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Vapor.Inspector;

namespace Vapor
{
    public static class InputActionEvents
    {
        public static string FormatInputActionTag(string tagPrefix, InputAction action)
        {
            string tagName = $"{tagPrefix}.{action.actionMap.name}.{action.name}".Replace(" ", "").Replace("-", "_").Replace("/", "_");
            return tagName;
        }
        
        
        private static readonly Dictionary<uint, InputActionEvent> s_Events = new();
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize() => s_Events.Clear();

        public static void Subscribe(uint eventId, Action<uint, InputAction.CallbackContext> callbackContext)
        {
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(InputActionEvents), nameof(Subscribe))} Input Event: {eventId} | {callbackContext.Method.Name}");
            if (s_Events.TryGetValue(eventId, out InputActionEvent inputActionEvent))
            {
                inputActionEvent.OnEventRaised -= callbackContext;
                inputActionEvent.OnEventRaised += callbackContext;
            }
            else
            {
                var evt = new InputActionEvent(eventId);
                evt.OnEventRaised -= callbackContext;
                evt.OnEventRaised += callbackContext;
                s_Events.Add(eventId, evt);
            }
        }

        public static void Unsubscribe(uint eventId, Action<uint, InputAction.CallbackContext> callbackContext)
        {
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(InputActionEvents), nameof(Unsubscribe))} Input Event: {eventId} | {callbackContext.Method.Name}");
            if (s_Events.TryGetValue(eventId, out InputActionEvent inputActionEvent))
            {
                inputActionEvent.OnEventRaised -= callbackContext;
            }
        }

        public static void TriggerEvent(uint eventId, InputAction.CallbackContext callbackContext)
        {
            if (s_Events.TryGetValue(eventId, out InputActionEvent inputActionEvent))
            {
                inputActionEvent.TriggerEvent(callbackContext);
            }
        }

        public static bool HasEvent(uint eventId) => s_Events.ContainsKey(eventId);
    }
}