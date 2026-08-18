using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class DisableIfAttribute : PropertyAttribute
    {
        public string Resolver { get; } = "";

        public DisableIfAttribute(string resolver)
        {
            if (!ResolverUtility.HasResolver(resolver, out var parsed)) return;
            
            Resolver = parsed;
        }
    }
}
