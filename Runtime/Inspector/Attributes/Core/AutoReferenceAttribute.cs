using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
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
