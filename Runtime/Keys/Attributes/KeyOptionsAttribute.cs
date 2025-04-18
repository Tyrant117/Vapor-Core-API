using System;
using UnityEngine;

namespace Vapor.Keys
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class KeyOptionsAttribute : Attribute
    {
        public bool IncludeNone { get; }
        public bool UseNameAsGuid { get; }
        public string Category { get; }

        public KeyOptionsAttribute(bool includeNone = true, bool useNameAsGuid = false, string category = null)
        {
            IncludeNone = includeNone;
            UseNameAsGuid = useNameAsGuid;
            Category = category;
        }        
    }
}
