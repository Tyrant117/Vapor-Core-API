using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class DragEnterEvent : EventBase<DragEnterEvent>
    {
        public VisualElement source { get; set; }

        // Summary:
        //     Resets the event members to their initial values.
        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        private void LocalInit()
        {
            tricklesDown = true;
            bubbles = false;
        }

        // Summary:
        //     Constructor. Avoid renewing events. Instead, use GetPooled() to get an event
        //     from a pool of reusable events.
        public DragEnterEvent()
        {
            LocalInit();
        }
    }
}
