using System;
using System.Diagnostics;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
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
