using System;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace Vapor.Inspector
{
    public class ToggleManipulator : SelectableManipulator
    {
        public event Action<EventBase, bool> Toggled;

        private bool _isOn;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value)
                {
                    return;
                }

                _isOn = value;
                if (_isOn)
                {
                    PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Checked);
                }
                else
                {
                    PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Checked);
                }
                 
                if (!target.enabledInHierarchy)
                {
                    return;
                }

                using var evt = PointerDownEvent.GetPooled();
                evt.target = target;
                InvokeToggled(evt, _isOn);
            }
        }

        public ToggleManipulator(bool isOn, string pseudoStateBaseName, VisualElement pseudoStateTarget = null) : base(pseudoStateBaseName, pseudoStateTarget)
        {
            _isOn = isOn;
        }

        public ToggleManipulator WithOnToggle(Action<EventBase, bool> callback)
        {
            Toggled += callback;
            return this;
        }

        public void SetValueWithoutNotify(bool value)
        {
            _isOn = value;
            if (_isOn)
            {
                PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Checked);
            }
            else
            {
                PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Checked);
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            base.RegisterCallbacksOnTarget();
            SetValueWithoutNotify(_isOn);
        }

        #region - Callback -
        protected override void InvokePressed(EventBase evt)
        {
            base.InvokePressed(evt);
            IsOn = !IsOn;
        }

        protected virtual void InvokeToggled(EventBase evt, bool isToggled)
        {
            Toggled?.Invoke(evt, isToggled);
        }
        #endregion
    }
}
