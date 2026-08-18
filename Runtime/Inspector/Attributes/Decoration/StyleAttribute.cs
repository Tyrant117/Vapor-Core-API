using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class StyleAttribute : PropertyAttribute
    {
        public string Style { get; }

        public StyleAttribute(string style)
        {
            Style = style;
        }
    }
}