using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class RichTextTooltipAttribute : TooltipAttribute
    {
        public string Tooltip { get; }

        /// <summary>
        /// Converts a custom markup string using the <see cref="TooltipMarkup.FormatString"/> to a tooltip.
        /// </summary>
        /// <param name="tooltip">The tooltip to convert</param>
        public RichTextTooltipAttribute(string tooltip) : base(tooltip)
        {
            Tooltip = TooltipMarkup.FormatString(tooltip);
        }
    }
}
