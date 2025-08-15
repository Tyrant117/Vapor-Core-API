using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class StyleAttribute : PropertyAttribute
    {
        public string Style { get; }

        public StyleAttribute(string style)
        {
            Style = style;
        }
    }
}