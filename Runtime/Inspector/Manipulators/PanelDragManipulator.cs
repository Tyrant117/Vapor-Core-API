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



        public PanelDragManipulator(string psuedoStateBaseName, VisualElement panelElement) : base(psuedoStateBaseName)
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

        private void OnBeginDragEvent(PointerCaptureEvent evt)
        {
            if (!IsEnabled)
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

        protected void UpdateDragPosition(float worldX, float worldY)
        {
            var offsetWorldX = worldX - _relativeXPoint;
            var offsetWorldY = worldY - _relativeYPoint;

            PanelElement.ClampToPanel(Container, offsetWorldX, offsetWorldY);
        }
    }
}
