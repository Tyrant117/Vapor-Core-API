using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class HelpUrlAttribute : PropertyAttribute
    {
        public string HelpText { get; }

        public HelpUrlAttribute(string helpText)
        {
            HelpText = TooltipMarkup.FormatString(helpText);
        }
    }
}