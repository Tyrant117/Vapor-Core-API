using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class ValidateInputAttribute : PropertyAttribute
    {
        public string MethodName { get; } = "";

        public ValidateInputAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
