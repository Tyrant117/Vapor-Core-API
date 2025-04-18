using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public class InlineToggleButtonAttribute : PropertyAttribute
    {
        public string MethodName { get; }
        public string PropertyResolver { get; } = "";
        public string IconOn { get; }
        public string IconOff { get; }

        public Color TintOn { get; }
        public Color TintOff { get; }
        public bool RebuildTree { get; }
        public string Tooltip { get; set; }

        public InlineToggleButtonAttribute(string methodName, string propertyResolver, string iconOn, string iconOff,
            string iconTintOn = "", string iconTintOff = "", bool rebuildTree = false, string tooltip = "")
        {
            MethodName = methodName;
            ResolverUtility.HasResolver(propertyResolver, out var parsedResolver);
            PropertyResolver = parsedResolver;
            IconOn = iconOn;
            IconOff = iconOff;
            Tooltip = tooltip;
            RebuildTree = rebuildTree;

            TintOn = iconTintOn == string.Empty
                ? Color.white
                : ResolverUtility.GetColor(iconTintOn, Color.white, out var hasResolver, out var parsed);

            TintOff = iconTintOff == string.Empty
                ? Color.white
                : ResolverUtility.GetColor(iconTintOff, Color.white, out var hasResolver2, out var parsed2);
        }
    }
}
