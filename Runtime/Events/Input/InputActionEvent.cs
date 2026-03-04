using System;
using UnityEngine.InputSystem;

namespace Vapor
{
    public class InputActionEvent
    {
        private readonly uint _eventId;

        public InputActionEvent(uint eventId)
        {
            _eventId = eventId;
        }

        public event Action<uint, InputAction.CallbackContext> OnEventRaised;

        public void TriggerEvent(InputAction.CallbackContext callbackContext)
        {
            OnEventRaised?.Invoke(_eventId, callbackContext);
        }
    }
}