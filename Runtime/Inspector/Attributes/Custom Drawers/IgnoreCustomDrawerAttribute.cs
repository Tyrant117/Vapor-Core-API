using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    /// <summary>
    /// Ignores the PropertyDrawer for this field if it exists.
    /// </summary>
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class IgnoreCustomDrawerAttribute : PropertyAttribute
    {
        
    }
}
