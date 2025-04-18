using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class BackgroundColorAttribute : PropertyAttribute
    {
        public StyleColor BackgroundColor { get; }
        public bool HasBackgroundColorResolver { get; }
        public string BackgroundColorResolver { get; }

        public BackgroundColorAttribute(string color)
        {
            BackgroundColor = ResolverUtility.GetColor(color, ContainerStyles.InspectorBackgroundColor.value, out var hasResolver, out var parsedColor);
            HasBackgroundColorResolver = hasResolver;
            BackgroundColorResolver = parsedColor;
        }
    }
}
