using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class DisableInPlayModeAttribute : PropertyAttribute
    {
        public string Resolver { get; } = "";

        public DisableInPlayModeAttribute(string resolver)
        {
            if (!ResolverUtility.HasResolver(resolver, out var parsed)) return;

            Resolver = parsed;
        }
    }
}
