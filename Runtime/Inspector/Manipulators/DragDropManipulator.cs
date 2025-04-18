using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class DragDropManipulator : SelectableManipulator
    {
        public DragElementManipulator DragElement { get; protected set; }
        public Vector2 InitialLocalPointerPosition { get; protected set; }
        public DragElementManipulator.DragLocationMatch DragLocationMatch { get; protected set; }
        public float DragStartDistance { get; protected set; } = 25;
        public bool ToggleDrag { get; private set; }

        public DragDropManipulator(string pseudoStateBaseName, DragElementManipulator dragElement) : base(pseudoStateBaseName)
        {
            DragElement = dragElement;
        }

        public DragDropManipulator WithDragLocationMatch(DragElementManipulator.DragLocationMatch dragLocationMatch)
        {
            DragLocationMatch = dragLocationMatch;
            return this;
        }

        public DragDropManipulator WithDragStartDistance(float distance)
        {
            DragStartDistance = distance;
            return this;
        }

        public DragDropManipulator WithToggleDrag()
        {
            ToggleDrag = true;
            return this;
        }

        public void StartDrag()
        {
            Debug.Log("DragDrop StartDrag " + target.name);
            DragElement.SetupSwapDrag(target, DragElement.target.worldBound.center, DragElement.target.localBound.center, target.WorldToLocal(target.worldBound.center), 0, DragLocationMatch);
        }

        protected override void ProcessDownEvent(PointerDownEvent evt)
        {
            InitialLocalPointerPosition = evt.localPosition;

            base.ProcessDownEvent(evt);

            Debug.Log($"DragDrop Pointer Down {target.name}");
            if (ToggleDrag)
            {
                DragElement.SetupForDrag(target, LastWorldMousePosition, InitialLocalPointerPosition, InitialLocalPointerPosition, evt.pointerId, DragLocationMatch);
            }
        }

        protected override void ProcessMoveEvent(PointerMoveEvent evt)
        {
            base.ProcessMoveEvent(evt);

            if (target.HasPointerCapture(evt.pointerId))
            {
                Vector2 diff = LastLocalMousePosition - InitialLocalPointerPosition;
                if (diff.magnitude > DragStartDistance)
                {
                    DragElement.SetupForDrag(target, LastWorldMousePosition, LastLocalMousePosition, InitialLocalPointerPosition, evt.pointerId, DragLocationMatch);
                }
            }
        }
    }
}
