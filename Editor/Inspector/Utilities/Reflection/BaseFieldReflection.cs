using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public static class BaseFieldReflection
    {
        public static VisualElement GetVisualInput<T>(this BaseField<T> field)
        {
            var visualInputProperty = typeof(BaseField<T>).GetProperty("visualInput", BindingFlags.NonPublic | BindingFlags.Instance);
            return (VisualElement)visualInputProperty.GetValue(field);
        }
    }
}
