using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public static class InspectorElementReflection
    {
        private static PropertyInfo _editorProperty;
        private static MethodInfo _setWideModeForWidthMethod;

        public static Editor GetEditorProperty(this InspectorElement inspectorElement)
        {
            if (_editorProperty == null)
                _editorProperty = typeof(InspectorElement).GetProperty("editor", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Editor)_editorProperty.GetValue(inspectorElement);
        }

        public static bool SetWideModeForWidth(VisualElement displayElement)
        {
            if (_setWideModeForWidthMethod == null)
                _setWideModeForWidthMethod = typeof(InspectorElement).GetMethod("SetWideModeForWidth", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)_setWideModeForWidthMethod.Invoke(null, new object[] { displayElement });
        }
    }
}
