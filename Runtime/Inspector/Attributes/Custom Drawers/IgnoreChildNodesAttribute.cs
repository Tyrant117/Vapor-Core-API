using System;

namespace Vapor.Inspector
{
    /// <summary>
    /// Ignores the PropertyDrawer for this field if it exists.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public class IgnoreChildNodesAttribute : Attribute
    {
    
    }
}
