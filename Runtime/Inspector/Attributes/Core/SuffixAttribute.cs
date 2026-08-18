using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class SuffixAttribute : PropertyAttribute
    {
        public string Suffix { get; }

        public SuffixAttribute(string suffix)
        {
            Suffix = suffix;
        }
    }
}
