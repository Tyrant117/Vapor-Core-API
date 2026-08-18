using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class RequireInterfaceAttribute : PropertyAttribute
    {
        /// <summary>
        /// The interface type that the referenced object should implement.
        /// </summary>
        public Type InterfaceType { get; }

        /// <summary>
        /// Initializes the attribute specifying the interface that the reference Unity Object should implement.
        /// </summary>
        /// <param name="interfaceType">The interface type.</param>
        public RequireInterfaceAttribute(Type interfaceType)
        {
            InterfaceType = interfaceType;
        }
    }
}
