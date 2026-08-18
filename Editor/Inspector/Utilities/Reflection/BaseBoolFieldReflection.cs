using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    [NoAutoStaticsCleanup]
    public static class BaseBoolFieldReflection
    {
        private static FieldInfo _clickableField;

        public static Clickable GetClickable(this BaseBoolField field)
        {
            _clickableField ??= typeof(BaseBoolField).GetField("m_Clickable", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Clickable)_clickableField.GetValue(field);
        }
    }
}
