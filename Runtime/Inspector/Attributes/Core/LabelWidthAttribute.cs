using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public class LabelWidthAttribute : PropertyAttribute
    {
        public StyleLength Width { get; }

        public LabelWidthAttribute(string width = null)
        {
            Width = ResolverUtility.GetStyleLength(width);
        }
    }
}
