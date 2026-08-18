using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    [NoAutoStaticsCleanup]
    public static class ClickableReflection
    {
        private static PropertyInfo _acceptClicksIfDisabledProperty;

        public static void SetAcceptClicksIfDisabled(this Clickable clickable, bool value)
        {
            _acceptClicksIfDisabledProperty ??= typeof(Clickable).GetProperty("acceptClicksIfDisabled", BindingFlags.NonPublic | BindingFlags.Instance);
            _acceptClicksIfDisabledProperty.SetValue(clickable, value);
        }
    }
}
