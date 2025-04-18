using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static class SerializedPropertyReflection
    {
        private static FieldInfo _pointerFieldInfo;
        private static PropertyInfo _isValidPropertyInfo;

        public static IntPtr GetNativePropertyPointer(this SerializedProperty property)
        {
            if (_pointerFieldInfo == null)
            {
                _pointerFieldInfo = typeof(SerializedProperty).GetField("m_NativePropertyPtr", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return (IntPtr)_pointerFieldInfo.GetValue(property);
        }

        public static bool IsValid(this SerializedProperty property)
        {
            if (_isValidPropertyInfo == null)
            {
                _isValidPropertyInfo = typeof(SerializedProperty).GetProperty("isValid", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return (bool)_isValidPropertyInfo.GetValue(property);
        }
    }
}
