using System;
using UnityEngine;

namespace Vapor.Inspector
{
    /// <summary>
    /// Place this attribute on a <see cref="MonoBehaviour"/> or <see cref="ScriptableObject"/> that should be drawn with unity default drawers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public class DrawWithUnityAttribute : Attribute
    {
        public bool UseIMGUIContainer { get; }

        public DrawWithUnityAttribute(bool useIMGUIContainer = false)
        {
            UseIMGUIContainer = useIMGUIContainer;
        }
    }
}
