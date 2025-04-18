using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
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
