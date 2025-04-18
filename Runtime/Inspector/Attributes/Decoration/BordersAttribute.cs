using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class BordersAttribute : PropertyAttribute
    {
        public int Roundness { get; }
        public int Left { get; }
        public int Right { get; }
        public int Top { get; }
        public int Bottom { get; }
        public StyleColor Color { get; }
        public bool HasResolver { get; }
        public string BorderResolver { get; }

        public BordersAttribute(int left = 1, int right = 1, int top = 1, int bottom = 1, string borderColor = "", int roundness = 3)
        {
            Roundness = roundness;
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
            Color = ResolverUtility.GetColor(borderColor, ContainerStyles.BorderColor.value, out var hasResolver, out var parsed);
            HasResolver = hasResolver;
            BorderResolver = parsed;
        }
    }
}
