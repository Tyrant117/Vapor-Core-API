using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class ChildGameObjectsOnlyAttribute : PropertyAttribute
    {
        public bool IncludeSelf { get; }

        public ChildGameObjectsOnlyAttribute(bool includeSelf = false)
        {
            IncludeSelf = includeSelf;
        }
    }
}
