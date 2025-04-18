using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class SuffixAttribute : PropertyAttribute
    {
        public string Suffix { get; }

        public SuffixAttribute(string suffix)
        {
            Suffix = suffix;
        }
    }
}
