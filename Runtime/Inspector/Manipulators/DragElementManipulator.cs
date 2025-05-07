using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class DragElementManipulator : Manipulator
    {
        private static readonly List<VisualElement> s_HoverCache = new();

        public enum DragLocationMatch
        {
            /// <summary>
            /// No location matching, pointer will be at the anchor. Typically Top-Left
            /// </summary>
            None,
            /// <summary>
            /// The element will match the size of the content starting the drag, pointer will be at the same location,
            /// </summary>
            MatchDragSize,
            /// <summary>
            /// The elements pointer will be at the relative same position as the drag start with no resizing
            /// </summary>
            MatchRelativePosition,
            /// <summary>
            /// The element will be centered no resizing
            /// </summary>
            Center,
            /// <summary>
            /// The element will be centered with resizing
            /// </summary>
            MatchDragSizeAndCenter,
        }

        public VisualElement Container { get; protected set; }
        public bool IsDragging { get; protected set; }
        public VisualElement DragSourceElement { get; protected set; }

        private float _relativeXPoint;
        private float _relativeYPoint;

        public Vector2 LastWorldMousePosition { get; private set; }
        public Vector2 LastLocalMousePosition { get; private set; }
        public Vector2 DragStartMousePosition { get; protected set; }
        public bool FromSwap { get; protected set; }

        private readonly List<VisualElement> _hoverCollection = new();
        private Vector3 _releasePoint;

        public event Action<EventBase, VisualElement> BeginDrag = delegate { };
        public event Action<EventBase, VisualElement> DragUpdated = delegate { };
        public Func<EventBase, VisualElement, bool> CanEndDrag = delegate { return true; };
        private HashSet<KeyCode> _keys;
        private List<KeyCode> _activeKeys;
        public event Action<EventBase, VisualElement, bool> EndDrag = delegate { };

        public DragElementManipulator(VisualElement container = null)
        {
            Container = container;
        }

        public DragElementManipulator WithKeyListeners(params KeyCode[] keys)
        {
            _keys = new HashSet<KeyCode>(keys);
            _activeKeys = new List<KeyCode>(keys.Length);
            return this;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            Container ??= target.panel.visualTree;

            target.visible = false;
            target.RegisterCallback<PointerCaptureEvent>(OnBeginDragEvent);
            target.RegisterCallback<PointerMoveEvent>(OnDragUpdatedEvent);
            target.RegisterCallback<PointerUpEvent>(OnEndDragEvent);
            target.RegisterCallback<PointerCaptureOutEvent>(OnReleaseDragEvent);
            target.RegisterCallback<PointerCancelEvent>(OnCancelDragEvent);
            target.RegisterCallback<KeyDownEvent>(OnKeyDownEvent);
            target.RegisterCallback<KeyUpEvent>(OnKeyUpEvent);

            // Since the panel is draggable it must be absolutely positioned.
            target.RegisterCallbackOnce<GeometryChangedEvent>(OnSwitchToAbsolute);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerCaptureEvent>(OnBeginDragEvent);
            target.UnregisterCallback<PointerMoveEvent>(OnDragUpdatedEvent);
            target.UnregisterCallback<PointerUpEvent>(OnEndDragEvent);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnReleaseDragEvent);
            target.UnregisterCallback<PointerCancelEvent>(OnCancelDragEvent);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDownEvent);
            target.UnregisterCallback<KeyUpEvent>(OnKeyUpEvent);
        }

        private void OnSwitchToAbsolute(GeometryChangedEvent evt)
        {
            if (target.style.position == Position.Relative)
            {
                return;
            }

            var world = target.LocalToWorld(target.transform.position);
            target.style.position = Position.Absolute;
            target.transform.position = world;
        }

        public void SetupSwapDrag(VisualElement dragSourceElement, Vector3 worldPosition, Vector3 localPosition, Vector2 dragStartPosition, int pointerId, DragLocationMatch dragLocationMatch)
        {
            FromSwap = true;
            Debug.Log($"Swap Drag On {dragSourceElement.name} - From Swap [{FromSwap}]");
            SetupForDrag(dragSourceElement, worldPosition, localPosition, dragStartPosition, pointerId, dragLocationMatch);
        }

        public void SetupForDrag(VisualElement dragSourceElement, Vector2 worldPosition, Vector2 localPosition, Vector2 dragStartPosition, int pointerId, DragLocationMatch dragLocationMatch)
        {
            if (IsDragging)
            {
                Debug.Log($"Drag Already Exists On {DragSourceElement.name}");
                return;
            }

            LastWorldMousePosition = dragSourceElement.ChangeCoordinatesTo(Container, localPosition);
            LastLocalMousePosition = localPosition;
            DragStartMousePosition = dragStartPosition;
            DragSourceElement = dragSourceElement;

            switch (dragLocationMatch)
            {
                case DragLocationMatch.None:
                    _relativeXPoint = 0;
                    _relativeYPoint = 0;
                    break;
                case DragLocationMatch.MatchDragSize:
                    _relativeXPoint = DragStartMousePosition.x;
                    _relativeYPoint = DragStartMousePosition.y;
                    target.style.width = DragSourceElement.layout.width;
                    target.style.height = DragSourceElement.layout.height;
                    break;
                case DragLocationMatch.MatchRelativePosition:
                    _relativeXPoint = DragStartMousePosition.x / DragSourceElement.layout.width * target.layout.width;
                    _relativeYPoint = DragStartMousePosition.y / DragSourceElement.layout.height * target.layout.height;
                    break;
                case DragLocationMatch.Center:
                    _relativeXPoint = target.layout.width / 2f;
                    _relativeYPoint = target.layout.height / 2f;
                    break;
                case DragLocationMatch.MatchDragSizeAndCenter:
                    _relativeXPoint = DragSourceElement.layout.width / 2f;
                    _relativeYPoint = DragSourceElement.layout.height / 2f;
                    target.style.width = DragSourceElement.layout.width;
                    target.style.height = DragSourceElement.layout.height;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dragLocationMatch), dragLocationMatch, null);
            }

            target.CapturePointer(pointerId);
        }

        private void OnBeginDragEvent(PointerCaptureEvent evt)
        {
            Debug.Log($"Begin Drag On {DragSourceElement.name} - From Swap [{FromSwap}]");
            IsDragging = true;
            _hoverCollection.Clear();
            UpdateDragPosition(LastWorldMousePosition.x, LastWorldMousePosition.y);
            BeginDrag.Invoke(evt, DragSourceElement);
            target.RegisterCallbackOnce<GeometryChangedEvent>(OnRecenter);
            target.visible = true;
            target.BringToFront();
            evt.StopPropagation();
        }

        private void OnRecenter(GeometryChangedEvent evt)
        {
            var scaleX = evt.newRect.width/evt.oldRect.width;
            var scaleY = evt.newRect.height/evt.oldRect.height;
            _relativeXPoint = scaleX * _relativeXPoint;
            _relativeYPoint = scaleY * _relativeYPoint;
        }

        private void OnDragUpdatedEvent(PointerMoveEvent evt)
        {
            if (!target.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            var pos = target.ChangeCoordinatesTo(Container, evt.localPosition);
            UpdateDragPosition(pos.x, pos.y);
            HandleEnterExit(evt.position);

            DragUpdated.Invoke(evt, DragSourceElement);
            evt.StopPropagation();
        }

        private void OnEndDragEvent(PointerUpEvent evt)
        {
            if (IsDragging && target.HasPointerCapture(evt.pointerId))
            {
                Debug.Log($"Drag Ending On {DragSourceElement.name}");
                _releasePoint = evt.position;
                target.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
        }

        private void OnReleaseDragEvent(PointerCaptureOutEvent evt)
        {
            if (!IsDragging)
            {
                return;
            }
            Debug.Log($"End Drag On {DragSourceElement.name} - Swap [{FromSwap}]");

            IsDragging = false;
            target.visible = false;

            if (CanEndDrag.Invoke(evt, DragSourceElement))
            {
                Debug.Log($"End Drag On {DragSourceElement.name} - Good Drop - Swap [{FromSwap}]");
                FromSwap = false;
                if (!HandleDrop(_releasePoint))
                {
                    EndDrag.Invoke(evt, DragSourceElement, true);
                }
            }
            else
            {
                Debug.Log($"End Drag On {DragSourceElement.name} - Resetting - Swap [{FromSwap}]");
                if (FromSwap)
                {
                    LastWorldMousePosition = _releasePoint;
                    target.CapturePointer(evt.pointerId);
                }
                else
                {
                    if (!HandleDrop(_releasePoint))
                    {
                        EndDrag.Invoke(evt, DragSourceElement, false);
                    }
                    FromSwap = false;
                }
            }
            
            // Cleanup
            _activeKeys.Clear();
            
            evt.StopPropagation();
        }

        private void OnCancelDragEvent(PointerCancelEvent evt)
        {
            Debug.Log("OnCancelDragEvent");
            target.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }
        
        private void OnKeyDownEvent(KeyDownEvent evt)
        {
            if (!_keys.Contains(evt.keyCode) || _activeKeys.Contains(evt.keyCode))
            {
                return;
            }

            _activeKeys.Add(evt.keyCode);
            evt.StopPropagation();
        }

        private void OnKeyUpEvent(KeyUpEvent evt)
        {
            if (!_keys.Contains(evt.keyCode) || !_activeKeys.Contains(evt.keyCode))
            {
                return;
            }

            _activeKeys.Remove(evt.keyCode);
            evt.StopPropagation();
        }


        protected void UpdateDragPosition(float worldX, float worldY)
        {
            var offsetWorldX = worldX - _relativeXPoint;
            var offsetWorldY = worldY - _relativeYPoint;

            target.ClampToPanel(Container, offsetWorldX, offsetWorldY);
        }

        protected void HandleEnterExit(Vector3 position)
        {
            target.panel.PickAll(position, s_HoverCache);

            for (int i = _hoverCollection.Count - 1; i >= 0; i--)
            {
                if (!s_HoverCache.Contains(_hoverCollection[i]))
                {
                    // Removed
                    using var evt = DragExitEvent.GetPooled();
                    evt.target = _hoverCollection[i];
                    evt.source = target;
                    _hoverCollection[i].SendEvent(evt);

                    _hoverCollection.RemoveAt(i);
                }
            }

            foreach (var current in s_HoverCache)
            {
                if (!_hoverCollection.Contains(current) && !IsDragChild(current) && current is IDragDropTarget)
                {
                    // Added
                    using var evt = DragEnterEvent.GetPooled();
                    evt.target = current;
                    evt.source = target;
                    current.SendEvent(evt);

                    _hoverCollection.Add(current);
                }
            }
        }

        [SuppressMessage("ReSharper", "SuspiciousTypeConversion.Global")]
        protected bool HandleDrop(Vector3 position)
        {
            // Might need to switch back to this if there is issues with the drop position.
            
            // target.panel.PickAll(position, s_HoverCache);
            if (_hoverCollection.Count > 0)
            {
                for (int i = 0; i < _hoverCollection.Count; i++)
                {
                    var hover = _hoverCollection[i];
                    if (/*hover is IDragDropTarget && */target.worldBound.Overlaps(hover.worldBound))
                    {
                        using var evt = DragDropEvent.GetPooled();
                        evt.target = hover;
                        evt.source = DragSourceElement;
                        evt.heldKeys = _activeKeys?.ToArray();
                        evt.dropWorldPosition = position;
                        hover.SendEvent(evt);
                        return true;
                    }
                }
            }
            return false;
        }

        protected bool IsDragChild(VisualElement element)
        {
            return target == element || target.Contains(element);
        }
    }
}
