using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class LabelAttribute : PropertyAttribute
    {
        public string Label { get; }
        public bool HasLabel { get; }
        public bool HasLabelResolver { get; }
        public string LabelResolver { get; }

        public StyleColor LabelColor { get; }
        public bool HasLabelColorResolver { get; }
        public string LabelColorResolver { get; }

        public string Icon { get; }
        public bool HasIcon { get; }
        public StyleColor IconColor { get; }
        public bool HasIconColorResolver { get; }
        public string IconColorResolver { get; }

        public LabelAttribute(string label = "", string labelColor = "", string icon = "", string iconColor = "")
        {
            Label = label;
            HasLabel = label != string.Empty;
            HasLabelResolver = ResolverUtility.HasResolver(label, out var parsedLabel);
            LabelResolver = parsedLabel;

            LabelColor = ResolverUtility.GetColor(labelColor, ContainerStyles.LabelDefault.value, out var labelColorResolverType, out var parsedColor);
            HasLabelColorResolver = labelColorResolverType;
            LabelColorResolver = parsedColor;

            Icon = icon;
            HasIcon = icon != string.Empty;
            IconColor = ResolverUtility.GetColor(iconColor, Color.white, out var iconColorResolverType, out var parsedIconColor);
            HasIconColorResolver = iconColorResolverType;
            IconColorResolver = parsedIconColor;
        }
    }
}
