using System;
using UnityEngine;

namespace Vapor.Inspector
{
    /// <summary>
    /// Ignores the PropertyDrawer for this field if it exists.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class IgnoreCustomDrawerAttribute : PropertyAttribute
    {
        
    }
}
