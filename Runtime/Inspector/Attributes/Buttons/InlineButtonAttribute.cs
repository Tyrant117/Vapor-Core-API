using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public class InlineButtonAttribute : PropertyAttribute
    {
        public string MethodName { get; }
        public string Label { get; }
        public string Icon { get; }
        public string Tooltip { get; }

        public Color Tint { get; }
        public bool HasResolver { get; }
        public string TintResolver { get; } = "";
        public bool RebuildTree { get; }

        public InlineButtonAttribute(string methodName, string label = "", string icon = "", string iconTint = "", bool rebuildTree = false, string tooltip = "")
        {
            MethodName = methodName;
            Label = label;
            Icon = icon;
            RebuildTree = rebuildTree;
            Tooltip = tooltip;

            if (iconTint == string.Empty)
            {
                Tint = Color.white;
            }
            else
            {
                Tint = ResolverUtility.GetColor(iconTint, Color.white, out var hasResolver, out var parsed);
                HasResolver = hasResolver;
                    TintResolver = parsed;
            }
        }
    }
}
