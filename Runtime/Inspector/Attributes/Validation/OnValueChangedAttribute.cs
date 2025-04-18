using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class OnValueChangedAttribute : PropertyAttribute
    {
        public string MethodName { get; }
        public bool RebuildTree { get; }
        /// <summary>
        /// This will mark the value changed as delayed if possible, meaning the change will wait for submit before firing.
        /// </summary>
        public bool Delayed { get; }

        public OnValueChangedAttribute([NotNull]string methodName, bool rebuildTree, bool delayed = false)
        {
            MethodName = methodName;
            RebuildTree = rebuildTree;
            Delayed = delayed;
        }
    }
}
