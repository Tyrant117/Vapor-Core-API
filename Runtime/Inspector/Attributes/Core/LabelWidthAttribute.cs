using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
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
