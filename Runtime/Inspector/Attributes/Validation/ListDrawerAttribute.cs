using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class ListDrawerAttribute : PropertyAttribute
    {
        public string ElementChangedMethodName { get; }
        public string SizeChangedMethodName { get; }
        public bool Editable { get; }

        public ListDrawerAttribute(string elementChangedMethodName = "", string sizeChangedMethodName = "", bool editable = true)
        {
            ElementChangedMethodName = elementChangedMethodName;
            SizeChangedMethodName = sizeChangedMethodName;
            Editable = editable;
        }
    }
}