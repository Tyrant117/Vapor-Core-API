using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class PanelDragManipulator : ButtonManipulator
    {
        public VisualElement PanelElement { get; protected set; }
        public VisualElement Container { get; protected set; }
        public bool IsDragging { get; protected set; }
        public Vector2 InitialLocalPointerPosition { get; protected set; }

        private float _relativeXPoint;
        private float _relativeYPoint;

        public event Action BeginDrag = delegate { };
        public event Action DragUpdated = delegate { };
        public event Action EndDrag = delegate { };



        public PanelDragManipulator(/*string psuedoStateBaseName,*/ VisualElement panelElement) : base(/*psuedoStateBaseName*/)
        {
            PanelElement = panelElement;
        }

        public PanelDragManipulator WithOnBeginDrag(Action callback)
        {
            BeginDrag += callback;
            return this;
        }

        public PanelDragManipulator WithOnDragUpdated(Action callback)
        {
            DragUpdated += callback;
            return this;
        }

        public PanelDragManipulator WithOnEndDrag(Action callback)
        {
            EndDrag += callback;
            return this;
        }

        public ButtonManipulator WithContainer(VisualElement container)
        {
            Container = container;
            return this;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            base.RegisterCallbacksOnTarget();
            PanelElement ??= target;
            Container ??= PanelElement.panel.visualTree;


            PanelElement.RegisterCallback<PointerCaptureEvent>(OnBeginDragEvent);
            PanelElement.RegisterCallback<PointerMoveEvent>(OnDragUpdatedEvent);
            PanelElement.RegisterCallback<PointerUpEvent>(OnEndDragEvent);
            PanelElement.RegisterCallback<PointerCaptureOutEvent>(OnReleaseDragEvent);
            PanelElement.RegisterCallback<PointerCancelEvent>(OnCancelDragEvent);

            // Since the panel is draggable it must be aboslute positioned.
            PanelElement.RegisterCallbackOnce<GeometryChangedEvent>(OnSwitchToObsolute);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            base.UnregisterCallbacksFromTarget();

            PanelElement.UnregisterCallback<PointerCaptureEvent>(OnBeginDragEvent);
            PanelElement.UnregisterCallback<PointerMoveEvent>(OnDragUpdatedEvent);
            PanelElement.UnregisterCallback<PointerUpEvent>(OnEndDragEvent);
            PanelElement.UnregisterCallback<PointerCaptureOutEvent>(OnReleaseDragEvent);
            PanelElement.UnregisterCallback<PointerCancelEvent>(OnCancelDragEvent);
        }

        private void OnSwitchToObsolute(GeometryChangedEvent evt)
        {
            if (PanelElement.style.position == Position.Absolute)
            {
                return;
            }

            var world = PanelElement.LocalToWorld(PanelElement.transform.position);
            PanelElement.style.position = Position.Absolute;
            PanelElement.transform.position = world;
        }

        protected override void ProcessDownEvent(PointerDownEvent evt)
        {
            base.ProcessDownEvent(evt);

            InitialLocalPointerPosition = target.ChangeCoordinatesTo(PanelElement, evt.localPosition);

            Debug.Log($"Evt: {evt.localPosition} | Lcl {InitialLocalPointerPosition}");

            _relativeXPoint = InitialLocalPointerPosition.x;
            _relativeYPoint = InitialLocalPointerPosition.y;

            if (!IsDragging)
            {
                PanelElement.CapturePointer(evt.pointerId);
            }
        }

        /// <summary>
        /// Begins the drag, but only for this panel's own capture.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The callbacks are registered on <see cref="PanelElement"/> because that is what
        /// <see cref="ProcessDownEvent"/> captures the pointer on — so move events keep arriving once
        /// the pointer leaves the drag handle. That is correct, and it has one consequence:
        /// <b>a capture by any descendant bubbles through here too.</b>
        /// </para>
        /// <para>
        /// Without the target check, a resize grip or an item slot capturing the pointer started a drag
        /// nobody asked for — and because this immediately calls <see cref="UpdateDragPosition"/> with
        /// the <em>last</em> recorded pointer position, the panel jumped on mouse-down, before a single
        /// move event. On a fresh session that stale position is zero, so the panel went to the corner.
        /// </para>
        /// <para>
        /// The drag is a gesture on this panel, not on anything that happens to live inside it.
        /// </para>
        /// </remarks>
        private void OnBeginDragEvent(PointerCaptureEvent evt)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (!ReferenceEquals(evt.target, PanelElement))
            {
                return;
            }

            IsDragging = true;

            BeginDrag.Invoke();
            UpdateDragPosition(LastWorldMousePosition.x, LastWorldMousePosition.y);

            evt.StopPropagation();
        }

        private void OnDragUpdatedEvent(PointerMoveEvent evt)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            if (!IsDragging)
            {
                return;
            }

            UpdateDragPosition(evt.position.x, evt.position.y);

            DragUpdated.Invoke();
            evt.StopPropagation();
        }

        private void OnEndDragEvent(PointerUpEvent evt)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            if (!IsDragging)
            {
                return;
            }

            EndDrag.Invoke();
            PanelElement.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnReleaseDragEvent(PointerCaptureOutEvent evt)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            IsDragging = false;
            evt.StopPropagation();
        }

        private void OnCancelDragEvent(PointerCancelEvent evt)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            PanelElement.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        /// <summary>
        /// Places the panel for a pointer at the given world position.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Virtual so a subclass can decide where the panel actually lands — snapping, docking, a grid.
        /// It was non-virtual, which made overriding it silently do nothing: the pointer handlers below
        /// are private and bind to this implementation at compile time, so a <c>new</c> in a derived
        /// class never ran and nothing reported it.
        /// </para>
        /// <para>
        /// <b>Position is expressed as <c>style.translate</c>, which is an offset from where layout put
        /// the element.</b> That makes the values here equivalent to world coordinates only while the
        /// panel's laid-out position is the origin — so a draggable panel wants
        /// <c>position: absolute; left: 0; top: 0</c> and should be moved by translate alone. Mixing in
        /// a laid-out offset (a percentage, an anchor) composes with the translate and puts the panel
        /// at the sum of the two.
        /// </para>
        /// </remarks>
        protected virtual void UpdateDragPosition(float worldX, float worldY)
        {
            var offsetWorldX = worldX - _relativeXPoint;
            var offsetWorldY = worldY - _relativeYPoint;

            PanelElement.ClampToPanel(Container, offsetWorldX, offsetWorldY);
        }
    }
}
