using System;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public enum ClickTypes
    {
        ClickOnUp,
        ClickOnDown
    }
    
    public class ButtonManipulator : SelectableManipulator
    {
        public InputAction Hotkey { get; private set; }

        public ClickTypes ClickType { get; set; }

        public event Action<EventBase> Clicked;

        public ButtonManipulator(string pseudoStateBaseName, VisualElement pseudoStateTarget = null) : base(pseudoStateBaseName, pseudoStateTarget)
        {

        }

        #region - Fluent -
        public ButtonManipulator WithOnClick(ClickTypes clickType, Action<EventBase> callback)
        {
            ClickType = clickType;
            Clicked += callback;
            return this;
        }

        public ButtonManipulator WithHotkey(InputAction hotkey)
        {
            Hotkey = hotkey;
            return this;
        }
        #endregion

        protected override void RegisterCallbacksOnTarget()
        {
            base.RegisterCallbacksOnTarget();

            if (Hotkey != null)
            {
                Hotkey.performed += OnKeyDown;
                Hotkey.canceled += OnKeyUp;
            }
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            base.UnregisterCallbacksFromTarget();

            if (Hotkey != null)
            {
                Hotkey.performed -= OnKeyDown;
                Hotkey.canceled -= OnKeyUp;
            }
        }

        #region - Manual Input -
        protected void OnKeyDown(InputAction.CallbackContext context)
        {
            ManualKeyDown();
        }

        protected void OnKeyUp(InputAction.CallbackContext context)
        {
            ManualKeyUp();
        }

        public void OnAction(object sender, bool isDown)
        {
            if (isDown)
            {
                ManualKeyDown();
            }
            else
            {
                ManualKeyUp();
            }
        }

        private void ManualKeyDown()
        {
            if (!IsEnabled)
            {
                return;
            }
            
            Active = true;

            if (target.enabledInHierarchy)
            {
                using var evt = KeyDownEvent.GetPooled();
                evt.target = target;
                InvokePressed(evt);
            }

            if (IsRepeatable)
            {
                if (Repeater == null)
                {
                    Repeater = target.schedule.Execute(OnTimer).Every(RepeatInterval).StartingIn(StartDelay);
                }
                else
                {
                    Repeater.ExecuteLater(StartDelay);
                }
            }

            PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Active);
        }

        private void ManualKeyUp()
        {
            if (!IsEnabled)
            {
                return;
            }
            
            if (Active)
            {
                Active = false;
                if (IsRepeatable)
                {
                    Repeater?.Pause();
                }
                else if (target.enabledInHierarchy)
                {
                    using var evt = KeyUpEvent.GetPooled();
                    evt.target = target;
                    InvokeRelease(evt);
                }

                PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Active);
            }
        }
        #endregion


        #region - Callback -
        protected override void InvokePressed(EventBase evt)
        {
            base.InvokePressed(evt);
            if(ClickType == ClickTypes.ClickOnDown)
            {
                InvokeClicked(evt);
            }    
        }

        protected override void InvokeRelease(EventBase evt)
        {
            base.InvokeRelease(evt);
            if (ClickType == ClickTypes.ClickOnUp)
            {
                InvokeClicked(evt);
            }
        }

        protected virtual void InvokeClicked(EventBase evt)
        {
            Clicked?.Invoke(evt);
        }
        #endregion
    }
}
