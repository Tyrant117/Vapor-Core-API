using System;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;

namespace VaporEditor.Inspector
{
    [NoAutoStaticsCleanup]
    public static class EditorGUIReflection
    {
        private static MethodInfo _hasVisibleChildFieldsMethod;
        private static Type _enumNamesCacheType;
        private static MethodInfo _getEnumDisplayNamesMethod;

        public static bool HasVisibleChildFields(SerializedProperty property, bool isUIElements = false)
        {
            _hasVisibleChildFieldsMethod ??= typeof(EditorGUI).GetMethod("HasVisibleChildFields", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)_hasVisibleChildFieldsMethod.Invoke(null, new object[] { property, isUIElements });
        }

        public static string[] GetEnumDisplayNames(SerializedProperty property)
        {
            _enumNamesCacheType ??= typeof(EditorGUI).GetNestedType("EnumNamesCache", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            _getEnumDisplayNamesMethod ??= _enumNamesCacheType.GetMethod("GetEnumDisplayNames", BindingFlags.NonPublic | BindingFlags.Static);
            return (string[])_getEnumDisplayNamesMethod.Invoke(null, new object[] { property });
        }
    }
}
