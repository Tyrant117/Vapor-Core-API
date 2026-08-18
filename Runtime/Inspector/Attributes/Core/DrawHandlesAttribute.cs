using System;

namespace Vapor.Inspector
{
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
