using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Vapor
{
    public struct InputActionCallbackContext
    {
        private InputAction.CallbackContext _context;

        public InputActionCallbackContext(in InputAction.CallbackContext context)
        {
            _context = context;
            phase = context.phase;
            Interaction = context.interaction;
            ManualValue = null;
            IsManualContext = false;
        }

        public InputActionCallbackContext(InputActionPhase phase, IInputInteraction interaction = null, object manualValue = null)
        {
            _context = default;
            this.phase = phase;
            Interaction = interaction;
            ManualValue = manualValue;
            IsManualContext = true;
        }
        public bool IsManualContext { get; }

        public InputAction Action => _context.action;
        public InputControl Control => _context.control;
        public InputActionPhase phase { get; }
        public IInputInteraction Interaction { get; }
        public object ManualValue { get; }

        public double Time => _context.time;
        public double StartTime => _context.startTime;
        public double Duration => _context.duration;
        public bool performed => phase == InputActionPhase.Performed;
        public bool started => phase == InputActionPhase.Started;
        public bool canceled => phase == InputActionPhase.Canceled;
        
        public TValue ReadValue<TValue>() where TValue : struct => _context.ReadValue<TValue>();
        public object ReadValueAsObject() => _context.ReadValueAsObject();
        public bool ReadValueAsButton() => _context.ReadValueAsButton();
    }

    public delegate void InputActionEventHandler(uint eventId, InputActionCallbackContext context);
    
    public class InputActionEvent
    {
        private readonly uint _eventId;

        public InputActionEvent(uint eventId)
        {
            _eventId = eventId;
        }
        
        public event InputActionEventHandler OnEventRaised;

        public void TriggerEvent(InputAction.CallbackContext callbackContext)
        {
            var wrapper = new InputActionCallbackContext(callbackContext);
            OnEventRaised?.Invoke(_eventId, wrapper);
        }

        public void TriggerManualEvent(InputActionPhase phase, IInputInteraction interaction = null, object manualValue = null)
        {
            var context = new InputActionCallbackContext(phase, interaction, manualValue);
            OnEventRaised?.Invoke(_eventId, context);
        }
    }
}