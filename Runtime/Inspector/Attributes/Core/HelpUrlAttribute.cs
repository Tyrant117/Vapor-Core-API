using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class HelpUrlAttribute : PropertyAttribute
    {
        public string HelpText { get; }
        public string HelpUrl { get; }

        public HelpUrlAttribute(string helpText, string helpUrl = null)
        {
            HelpText = TooltipMarkup.FormatString(helpText);
            HelpUrl = helpUrl;
        }
    }
}