using System;
using UnityEngine;

namespace Vapor.Inspector
{
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
