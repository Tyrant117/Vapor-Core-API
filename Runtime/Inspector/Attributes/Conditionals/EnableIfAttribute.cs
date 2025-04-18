using System;
using UnityEngine;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class EnableIfAttribute : PropertyAttribute
    {
        public string Resolver { get; } = "";

        public EnableIfAttribute(string resolver)
        {
            if (!ResolverUtility.HasResolver(resolver, out var parsed)) return;

            Resolver = parsed;
        }
    }
}
