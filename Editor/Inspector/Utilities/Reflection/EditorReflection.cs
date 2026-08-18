using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;

namespace VaporEditor.Inspector
{
    [NoAutoStaticsCleanup]
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
