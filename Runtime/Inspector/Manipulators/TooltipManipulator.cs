using System;
using UnityEngine;

namespace Vapor.Inspector
{
    public class TooltipManipulator : HoverManipulator
    {       
        public float Delay { get; set; }

        public TooltipManipulator(string pseudoStateBaseName, float delay) : base(pseudoStateBaseName)
        {
            Delay = delay;
        }

        public bool CanShowTooltip()
        {
            return IsHovering && Time.time - Delay >= HoveringTime;
        }
    }
}
