using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class HideInEditorModeAttribute : PropertyAttribute
    {
        public string Resolver { get; } = "";

        public HideInEditorModeAttribute(string resolver)
        {
            if (!ResolverUtility.HasResolver(resolver, out var parsed)) return;

            Resolver = parsed;
        }
    }
}
