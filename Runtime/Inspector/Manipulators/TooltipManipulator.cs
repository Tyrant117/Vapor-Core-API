using System;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class TooltipManipulator : HoverManipulator
    {
        public enum LocationSnapType
        {
            Mouse,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Center,
        }

        public VisualElement TooltipPanel { get; set; }
        public Func<EventBase, VisualElement> CreateTooltip { get; }
        public LocationSnapType LocationSnap { get; }

        public VisualElement ActiveTooltip { get; set; }

        public TooltipManipulator(VisualElement panel, Func<EventBase, VisualElement> createTooltip, LocationSnapType locationSnap)
        {
            TooltipPanel = panel;
            CreateTooltip = createTooltip;
            LocationSnap = locationSnap;
        }

        protected override void ProcessPointerEnter(PointerEnterEvent evt)
        {
            base.ProcessPointerEnter(evt);
            ActiveTooltip?.RemoveFromHierarchy();
            ActiveTooltip = CreateTooltip?.Invoke(evt);
            if (ActiveTooltip != null)
            {
                ActiveTooltip.style.position = Position.Absolute;
                var offsetWorldX = evt.position.x;
                var offsetWorldY = evt.position.y;
                if (evt.target is VisualElement parent)
                {
                    switch (LocationSnap)
                    {
                        case LocationSnapType.TopLeft:
                            offsetWorldX = parent.worldBound.x;
                            offsetWorldY = parent.worldBound.y;
                            break;
                        case LocationSnapType.TopRight:
                            offsetWorldX = parent.worldBound.xMax;
                            offsetWorldY = parent.worldBound.y;
                            break;
                        case LocationSnapType.BottomLeft:
                            offsetWorldX = parent.worldBound.x;
                            offsetWorldY = parent.worldBound.yMax;
                            break;
                        case LocationSnapType.BottomRight:
                            offsetWorldX = parent.worldBound.xMax;
                            offsetWorldY = parent.worldBound.yMax;
                            break;
                        case LocationSnapType.Center:
                            offsetWorldX = parent.worldBound.center.x;
                            offsetWorldY = parent.worldBound.center.y;
                            break;
                        case LocationSnapType.Mouse:
                        default:
                            break;
                    }
                }

                ActiveTooltip.RegisterCallbackOnce<GeometryChangedEvent>(_ => { ActiveTooltip.ClampToPanel(TooltipPanel, offsetWorldX, offsetWorldY); });
                TooltipPanel.Add(ActiveTooltip);
            }
        }

        protected override void ProcessPointerLeave(PointerLeaveEvent evt)
        {
            base.ProcessPointerLeave(evt);
            ActiveTooltip?.RemoveFromHierarchy();
        }
    }
}
