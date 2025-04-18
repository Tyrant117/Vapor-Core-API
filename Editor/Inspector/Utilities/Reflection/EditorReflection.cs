using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static class EditorReflection
    {
        private static PropertyInfo _propertyHandlerCacheProperty;

        public static object GetPropertyHandlerCache(this Editor editor)
        {
            if (_propertyHandlerCacheProperty == null)
                _propertyHandlerCacheProperty = typeof(Editor).GetProperty("propertyHandlerCache", BindingFlags.NonPublic | BindingFlags.Instance);
            return _propertyHandlerCacheProperty.GetValue(editor);
        }
    }
}
