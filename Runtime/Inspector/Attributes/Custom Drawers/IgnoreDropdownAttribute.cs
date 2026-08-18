using System;

namespace Vapor.Inspector
{
    /// <summary>
    /// This attribute is used to ignore a type from the dropdown selection of a SerializableReference.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public class IgnoreDropdownAttribute : Attribute { }
}