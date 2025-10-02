using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class HoverManipulator : PointerManipulator, IPseudoStateManipulator
    {
        public bool IsEnabled { get; protected set; }
        public bool IsHovering { get; protected set; }

        public string PseudoStateHover { get; protected set; }
        public string PseudoStateActive { get; protected set; }
        public string PseudoStateFocus { get; protected set; }
        public string PseudoStateChecked { get; protected set; }
        public string PseudoStateDisabled { get; protected set; }
        public VisualElement PseudoStateTarget { get; set; }

        protected readonly IPseudoStateManipulator PsuedoStateManipulator;
        protected float HoveringTime;

        public event Action<EventBase> Entered = delegate { };
        public event Action<EventBase> Exited = delegate { };

        public HoverManipulator(string pseudoStateBaseName, VisualElement pseudoStateTarget = null)
        {
            IsHovering = false;

            PseudoStateHover = pseudoStateBaseName + StyleSheetUtility.PseudoStates.Hover;
            PseudoStateDisabled = pseudoStateBaseName + StyleSheetUtility.PseudoStates.Disabled;
            PsuedoStateManipulator = this;
            PseudoStateTarget = pseudoStateTarget;
        }

        #region - Fluent Builder -
        public T As<T>() where T : HoverManipulator
        {
            return (T)this;
        }

        public T WithHoverEntered<T>(Action<EventBase> entered) where T : HoverManipulator
        {
            Entered += entered;
            return (T)this;
        }

        public T WithHoverExited<T>(Action<EventBase> exited) where T : HoverManipulator
        {
            Exited += exited;
            return (T)this;
        }
        #endregion

        protected override void RegisterCallbacksOnTarget()
        {
            PseudoStateTarget ??= target;

            target.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
            target.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
        }

        #region - Hovering -
        private void OnPointerEnter(PointerEnterEvent evt)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            if (!IsHovering)
            {
                ProcessPointerEnter(evt);
            }
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            if (IsHovering)
            {
                ProcessPointerLeave(evt);
            }
        }

        protected virtual void ProcessPointerEnter(PointerEnterEvent evt)
        {
            ForcePointerEnter();
            Entered.Invoke(evt);
        }

        public void ForcePointerEnter()
        {
            PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Hover);
            IsHovering = true;
            HoveringTime = Time.time;
        }

        protected virtual void ProcessPointerLeave(PointerLeaveEvent evt)
        {
            ForcePointerLeave();
            Exited.Invoke(evt);
        }

        public void ForcePointerLeave()
        {
            PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Hover);
            IsHovering = false;
        }
        #endregion

        #region - Helpers -
        protected bool ContainsPointer(Vector2 worldPoint)
        {
            VisualElement topElementUnderPointer = target.panel.Pick(worldPoint);
            return target == topElementUnderPointer || target.Contains(topElementUnderPointer);
        }

        public void SetEnabled(bool enabled)
        {
            var old = IsEnabled;
            IsEnabled = enabled;
            if (IsEnabled)
            {
                PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Disabled);
            }
            else
            {
                PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Disabled);
            }

            if (old != IsEnabled)
            {
                OnEnableStateChanged();
            }
        }

        protected virtual void OnEnableStateChanged()
        {
            if (IsHovering)
            {
                ForcePointerLeave();
            }
        }

        #endregion
    }
}
