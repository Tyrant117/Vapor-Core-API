using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class ShowIfAttribute : PropertyAttribute
    {
        public string Resolver { get; } = "";

        public ShowIfAttribute(string resolver)
        {
            if (!ResolverUtility.HasResolver(resolver, out var parsed)) return;
            
            Resolver = parsed;
        }
    }
}
