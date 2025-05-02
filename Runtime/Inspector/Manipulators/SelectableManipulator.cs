using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class SelectableManipulator : HoverManipulator
    {
        public bool Active { get; protected set; }

        public bool IsRepeatable => StartDelay > 0 || RepeatInterval > 0;
        public Vector2 LastLocalMousePosition { get; protected set; }
        public Vector2 LastWorldMousePosition { get; protected set; }

        private int _activePointerId = -1;

        protected long RepeatInterval;
        protected long StartDelay;
        protected IVisualElementScheduledItem Repeater;

        public event Action<EventBase> Pressed;
        public event Action<EventBase> Released;

        public event Action<VisualElement> Repeat;

        public SelectableManipulator(string pseudoStateBaseName, VisualElement pseudoStateTarget = null) : base(pseudoStateBaseName, pseudoStateTarget)
        {
            Active = false;

            PseudoStateActive = pseudoStateBaseName + StyleSheetUtility.PseudoStates.Active;
            PseudoStateChecked = pseudoStateBaseName + StyleSheetUtility.PseudoStates.Checked;
        }

        #region - Fluent Interface -
        public T WithOnPress<T>(Action<EventBase> callback) where T : SelectableManipulator
        {
            Pressed += callback;
            return (T)this;
        }

        public T WithOnRelease<T>(Action<EventBase> callback) where T : SelectableManipulator
        {
            Released += callback;
            return (T)this;
        }

        public T WithActivator<T>(EventModifiers modifiers, MouseButton button, int clickCount = 0) where T : SelectableManipulator
        {
            activators.Add(new ManipulatorActivationFilter()
            {
                modifiers = modifiers,
                button = button,
                clickCount = 0
            });

            return (T)this;
        }

        /// <summary>
        /// If the button is held the invoke event will be called every interval after the delay.
        /// </summary>
        /// <param name="repeatInterval">The interval to call the invoke event in miliseconds.</param>
        /// <param name="startDelay">The delay before the first repeated event is called in miliseconds.</param>
        /// <param name="repeatCallback"></param>
        /// <returns></returns>
        public T WithRepeatable<T>(long repeatInterval, long startDelay, Action<VisualElement> repeatCallback) where T : SelectableManipulator
        {
            RepeatInterval = repeatInterval;
            StartDelay = startDelay;
            Repeat += repeatCallback;
            return (T)this;
        }
        #endregion

        protected override void RegisterCallbacksOnTarget()
        {
            base.RegisterCallbacksOnTarget();

            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            base.UnregisterCallbacksFromTarget();

            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);

            PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Active);
        }

        protected void OnTimer(TimerState timerState)
        {
            if (IsRepeatable)
            {
                if (ContainsPointer(LastWorldMousePosition) && target.enabledInHierarchy)
                {
                    Repeat?.Invoke(target);
                    PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Active);
                }
                else
                {
                    PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Active);
                }
            }
        }

        #region - Clicking -
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (CanStartManipulation(evt))
            {
                ProcessDownEvent(evt);
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (Active)
            {
                ProcessMoveEvent(evt);
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (Active && CanStopManipulation(evt))
            {
                ProcessUpEvent(evt);
            }
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (Active && CanStopManipulation(evt))
            {
                ProcessCancelEvent(evt);
            }
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (Active)
            {
                ProcessCaptureOutEvent(evt);
            }
        }

        protected virtual void ProcessDownEvent(PointerDownEvent evt)
        {
            Active = true;
            _activePointerId = evt.pointerId;
            target.CapturePointer(evt.pointerId);

            LastLocalMousePosition = evt.localPosition;
            LastWorldMousePosition = evt.position;

            if (ContainsPointer(evt.position) && target.enabledInHierarchy)
            {
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
            evt.StopPropagation();
        }

        protected virtual void ProcessMoveEvent(PointerMoveEvent evt)
        {
            LastLocalMousePosition = evt.localPosition;
            LastWorldMousePosition = evt.position;
            if (ContainsPointer(evt.position))
            {
                PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Active);
            }
            else
            {
                PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Active);
            }

            evt.StopPropagation();
        }

        protected virtual void ProcessUpEvent(PointerUpEvent evt)
        {
            Active = false;
            _activePointerId = -1;
            target.ReleasePointer(evt.pointerId);

            PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Active);
            if (IsRepeatable)
            {
                Repeater?.Pause();
            }
            else if (ContainsPointer(evt.position) && target.enabledInHierarchy)
            {
                InvokeRelease(evt);
            }

            evt.StopPropagation();
        }

        protected virtual void ProcessCancelEvent(PointerCancelEvent evt)
        {
            ExitManipulator(evt, evt.pointerId);
        }

        protected virtual void ProcessCaptureOutEvent(PointerCaptureOutEvent evt)
        {
            ExitManipulator(evt, evt.pointerId);
        }

        private void ExitManipulator(EventBase evt, int pointerId)
        {
            Active = false;
            _activePointerId = -1;
            target.ReleasePointer(pointerId);

            PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Active);
            if (IsRepeatable)
            {
                Repeater?.Pause();
            }

            evt.StopPropagation();
        }


        public void Select()
        {
            PsuedoStateManipulator.EnablePseudoStateClass(PseudoState.Checked);
        }

        public void Deselect()
        {
            PsuedoStateManipulator.DisablePseudoStateClass(PseudoState.Checked);
        }
        #endregion

        #region - Callbacks -
        protected virtual void InvokePressed(EventBase evt)
        {
            Pressed?.Invoke(evt);
        }

        protected virtual void InvokeRelease(EventBase evt)
        {
            Released?.Invoke(evt);
        }        
        #endregion
    }
}
