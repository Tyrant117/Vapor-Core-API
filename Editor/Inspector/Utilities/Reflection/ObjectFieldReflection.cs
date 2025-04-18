using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static class ObjectFieldReflection
    {
        private static MethodInfo _SetObjectTypeWithoutDisplayUpdateMethod;
        private static MethodInfo _UpdateDisplayMethod;


        public static void SetObjectTypeWithoutDisplayUpdate(this ObjectField field, Type type)
        {
            _SetObjectTypeWithoutDisplayUpdateMethod ??= typeof(ObjectField).GetMethod("SetObjectTypeWithoutDisplayUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            _SetObjectTypeWithoutDisplayUpdateMethod.Invoke(field, new object[] { type });
        }

        public static void UpdateDisplay(this ObjectField field)
        {
            _UpdateDisplayMethod ??= typeof(ObjectField).GetMethod("UpdateDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
            _UpdateDisplayMethod.Invoke(field, null);
        }
    }
}
