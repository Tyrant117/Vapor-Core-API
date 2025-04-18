using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class AutoReferenceAttribute : PropertyAttribute
    {
        public bool SearchChildren { get; }
        public bool SearchParents { get; }
        public bool AddIfNotFound { get; }

        public AutoReferenceAttribute(bool searchChildren = false, bool searchParents = false, bool addIfNotFound = false)
        {
            SearchChildren = searchChildren;
            SearchParents = searchParents;
            AddIfNotFound = addIfNotFound;
        }
    }
}
