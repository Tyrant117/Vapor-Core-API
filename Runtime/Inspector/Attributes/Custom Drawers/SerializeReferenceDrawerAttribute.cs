using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class SerializeReferenceDrawerAttribute : PropertyAttribute
    {
        /// <summary>
        /// Flattens all the categories in the ComboBox
        /// </summary>
        public bool FlattenCategories { get; }
        
        /// <summary>
        /// Should the ComboBox have a null selector option
        /// </summary>
        public bool ShowNullType { get; }

        /// <summary>
        /// Should the type selector be drawn for this field
        /// </summary>
        public bool DrawSelector { get; }

        public SerializeReferenceDrawerAttribute(bool flattenCategories = false, bool showNullType = true, bool drawSelector = true)
        {
            FlattenCategories = flattenCategories;
            ShowNullType = showNullType;
            DrawSelector = drawSelector;
        }

        
    }
}