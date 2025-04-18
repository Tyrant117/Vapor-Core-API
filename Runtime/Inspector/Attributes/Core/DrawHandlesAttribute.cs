using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class DrawHandlesAttribute : Attribute
    {
        public string MethodName { get; } = "";

        public DrawHandlesAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
