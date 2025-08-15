using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct)]
    public class DropdownTooltipAttribute : Attribute
    {
        public string Tooltip { get; }

        /// <summary>
        /// Converts a custom markup string using the <see cref="TooltipMarkup.FormatString"/> to a tooltip.
        /// </summary>
        /// <param name="tooltip">The tooltip to convert</param>
        public DropdownTooltipAttribute(string tooltip)
        {
            Tooltip = TooltipMarkup.FormatString(tooltip);
        }
    }
}