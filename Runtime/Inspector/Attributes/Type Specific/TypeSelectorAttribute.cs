using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    /// <summary>
    /// This must be placed on a string or list of string type.
    /// </summary>
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
    public class TypeSelectorAttribute : PropertyAttribute
    {
        public enum T
        {
            /// <summary>
            /// Gets all types derived from <see cref="Type"/>
            /// </summary>
            Subclass,
            /// <summary>
            /// Gets all class with attribute <see cref="Type"/>
            /// </summary>
            Attribute,
            /// <summary>
            /// Uses the resolver to return a list of types.
            /// </summary>
            Resolver,
        }

        public T Selection { get; }
        public Type Type { get; }
        public string Resolver { get; }
        public bool IncludeAbstract { get; }
        public bool AllTypes { get; }

        public TypeSelectorAttribute(bool includeAbstract = false)
        {
            AllTypes = true;
            IncludeAbstract = includeAbstract;
        }
        
        public TypeSelectorAttribute(T selection, Type type, bool includeAbstract = false)
        {
            Selection = selection;
            Type = type;
            IncludeAbstract = includeAbstract;
        }

        public TypeSelectorAttribute(string typeResolver, bool includeAbstract = false)
        {
            if (!ResolverUtility.HasResolver(typeResolver, out var parsed)) return;

            Selection = T.Resolver;
            Resolver = parsed;
            IncludeAbstract = includeAbstract;
        }
    }

    public class TypeResolverAttribute : PropertyAttribute
    {
        public string Resolver { get; }
        
        public TypeResolverAttribute(string typeResolver)
        {
            if (!ResolverUtility.HasResolver(typeResolver, out var parsed)) return;

            Resolver = parsed;
        }
    }
}
