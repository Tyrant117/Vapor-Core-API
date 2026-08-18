using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
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