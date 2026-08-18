using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class HideInPlayModeAttribute : PropertyAttribute
    {
        public string Resolver { get; } = "";

        public HideInPlayModeAttribute(string resolver)
        {
            if (!ResolverUtility.HasResolver(resolver, out var parsed)) return;

            Resolver = parsed;
        }
    }
}
