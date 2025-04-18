using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
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
