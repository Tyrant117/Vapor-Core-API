using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    /// <summary>
    /// This attribute is used to ignore a type from the dropdown selection of a SerializableReference.
    /// </summary>
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public class IgnoreDropdownAttribute : Attribute { }
}