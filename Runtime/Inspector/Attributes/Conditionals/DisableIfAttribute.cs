using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
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
