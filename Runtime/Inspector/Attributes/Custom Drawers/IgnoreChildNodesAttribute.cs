using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    /// <summary>
    /// Ignores the PropertyDrawer for this field if it exists.
    /// </summary>
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public class IgnoreChildNodesAttribute : Attribute
    {
    
    }
}
