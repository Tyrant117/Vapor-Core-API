using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class ShowInInspectorAttribute : PropertyAttribute
    {
        public bool Dynamic { get; }

        /// <summary>
        /// Marks a property to be visible in the inspector. The property will be readonly and the data is not serialized.
        /// </summary>
        /// <param name="dynamic">If True, the property will update its values on an interval. Can be used when the property expression should be evaulated continously.</param>
        public ShowInInspectorAttribute(bool dynamic = false)
        {
            Dynamic = dynamic;
        }
    }
}
