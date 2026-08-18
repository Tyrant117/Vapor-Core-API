using System;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;

namespace VaporEditor.Inspector
{
    [NoAutoStaticsCleanup]
    public static class SerializedObjectReflection
    {
        private static FieldInfo _pointerFieldInfo;
        private static PropertyInfo _isValidPropertyInfo;

        public static IntPtr GetNativeObjectPointer(this SerializedObject serializedObject)
        {
            if (_pointerFieldInfo == null)
            {
                _pointerFieldInfo = typeof(SerializedObject).GetField("m_NativeObjectPtr", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return (IntPtr)_pointerFieldInfo.GetValue(serializedObject);
        }

        public static bool IsValid(this SerializedObject serializedObject)
        {
            if (_isValidPropertyInfo == null)
            {
                _isValidPropertyInfo = typeof(SerializedObject).GetProperty("isValid", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return (bool)_isValidPropertyInfo.GetValue(serializedObject);
        }
    }
}
