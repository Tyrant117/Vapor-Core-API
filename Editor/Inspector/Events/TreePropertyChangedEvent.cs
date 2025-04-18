using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class TreePropertyChangedEvent: EventBase<TreePropertyChangedEvent>
    {
        public object Previous { get; set; }
        public object Current { get; set; }

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
            bubbles = true;
        }

        // Summary:
        //     Constructor. Avoid renewing events. Instead, use GetPooled() to get an event
        //     from a pool of reusable events.
        public TreePropertyChangedEvent()
        {
            LocalInit();
        }
    }
}
