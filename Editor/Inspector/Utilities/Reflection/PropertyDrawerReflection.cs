using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static class PropertyDrawerReflection
    {
        private static FieldInfo _preferredLabelField;

        public static void SetPreferredLabel(this PropertyDrawer drawer, string label)
        {
            if (_preferredLabelField == null)
                _preferredLabelField = typeof(PropertyDrawer).GetField("m_PreferredLabel", BindingFlags.NonPublic | BindingFlags.Instance);
            _preferredLabelField.SetValue(drawer, label);
        }
    }
}
