using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class DropdownAttribute : PropertyAttribute
    {
        public enum FilterType
        {
            Resolver,
            Category,
            TypeName
        }

        public int Filter { get; }
        public string Resolver { get; } = "";
        public bool Searchable { get; }
        public bool MultiSelectArray { get; }

        public DropdownAttribute(string filterName, FilterType filter = FilterType.Resolver, bool searchable = true, bool multiSelectArray = true)
        {
            Filter = (int)filter;
            switch (filter)
            {
                case FilterType.Resolver:
                    if (!ResolverUtility.HasResolver(filterName, out var parsed)) return;
                    Resolver = parsed;
                    break;
                case FilterType.Category:
                    Resolver = filterName;
                    break;
                case FilterType.TypeName:
                    Resolver = filterName;
                    break;
            }
            
            Searchable = searchable;
            MultiSelectArray = multiSelectArray;
        }
    }
}
