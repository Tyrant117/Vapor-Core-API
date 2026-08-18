using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class ValidateInputAttribute : PropertyAttribute
    {
        public string MethodName { get; } = "";

        public ValidateInputAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
