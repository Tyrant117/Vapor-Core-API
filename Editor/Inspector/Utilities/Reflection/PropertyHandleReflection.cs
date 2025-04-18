using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static class PropertyHandleReflection
    {
        private static Type _type;
        public static Type AsType
        {
            get
            {
                if (_type == null)
                {
                    _type = Type.GetType("UnityEditor.PropertyHandler, UnityEditor");
                }
                return _type;
            }
        }

        private static PropertyInfo _hasPropertyDrawerProperty;
        private static PropertyInfo _getPropertyDrawerProperty;
        private static PropertyInfo _skipDecoratorDrawersProperty;
        private static FieldInfo _getDecoratorDrawersField;
        private static MethodInfo _applyNestingContextMethod;
        private static MethodInfo s_CreatePropertyDrawerWithDefaultObjectReferencesMethod;

        public static bool HasPropertyDrawer(object handler)
        {
            if (_hasPropertyDrawerProperty == null)
            {
                _hasPropertyDrawerProperty = AsType.GetProperty("hasPropertyDrawer", BindingFlags.Public | BindingFlags.Instance);
            }

            return (bool)_hasPropertyDrawerProperty.GetValue(handler);
        }

        public static PropertyDrawer GetPropertyDrawer(object handler)
        {
            if (_getPropertyDrawerProperty == null)
                _getPropertyDrawerProperty = AsType.GetProperty("propertyDrawer", BindingFlags.NonPublic | BindingFlags.Instance);
            return (PropertyDrawer)_getPropertyDrawerProperty.GetValue(handler);
        }

        public static List<DecoratorDrawer> GetDecoratorDrawers(object handler)
        {
            if (_getDecoratorDrawersField == null)
                _getDecoratorDrawersField = AsType.GetField("m_DecoratorDrawers", BindingFlags.NonPublic | BindingFlags.Instance);
            return (List<DecoratorDrawer>)_getDecoratorDrawersField.GetValue(handler);
        }

        public static bool GetSkipDecoratorDrawers(object handler)
        {
            if (_skipDecoratorDrawersProperty == null)
                _hasPropertyDrawerProperty = AsType.GetProperty("skipDecoratorDrawers", BindingFlags.NonPublic | BindingFlags.Instance);
            return (bool)_hasPropertyDrawerProperty.GetValue(handler);
        }

        public static void SetSkipDecoratorDrawers(object handler, bool skip)
        {
            if(_skipDecoratorDrawersProperty == null)
                _hasPropertyDrawerProperty = AsType.GetProperty("skipDecoratorDrawers", BindingFlags.NonPublic | BindingFlags.Instance);
            _hasPropertyDrawerProperty.SetValue(handler, skip);
        }

        public static IDisposable ApplyNestingContext(object handler, int nestingLevel)
        {
            if (_applyNestingContextMethod == null)
            {
                _applyNestingContextMethod = AsType.GetMethod("ApplyNestingContext", BindingFlags.Public | BindingFlags.Instance);
            }

            return (IDisposable)_applyNestingContextMethod.Invoke(handler, new object[] { nestingLevel });
        }

        public static PropertyDrawer CreatePropertyDrawerWithDefaultObjectReferences(Type drawerType)
        {
            if(s_CreatePropertyDrawerWithDefaultObjectReferencesMethod == null)
            {
                s_CreatePropertyDrawerWithDefaultObjectReferencesMethod = AsType.GetMethod("CreatePropertyDrawerWithDefaultObjectReferences", BindingFlags.NonPublic | BindingFlags.Static);
            }

            return (PropertyDrawer)s_CreatePropertyDrawerWithDefaultObjectReferencesMethod.Invoke(null, new object[1] { drawerType });
        }
    }
}
