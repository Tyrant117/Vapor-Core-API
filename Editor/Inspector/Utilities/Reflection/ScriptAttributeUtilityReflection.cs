using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VaporEditor.Inspector
{
    public static class ScriptAttributeUtilityReflection
    {
        private static Type _type;
        public static Type AsType
        {
            get
            {
                if (_type == null)
                {
                    _type = Type.GetType("UnityEditor.ScriptAttributeUtility, UnityEditor");
                }
                return _type;
            }
        }

        private static MethodInfo _getHandlerMethod;
        private static MethodInfo _canUseSameHandlerMethod;
        private static MethodInfo _GetFieldInfoFromPropertyMethod;
        private static MethodInfo _GetDrawerTypeForTypeMethod;
        private static MethodInfo _GetDrawerTypeForPropertyAndTypeMethod;
        private static PropertyInfo _propertyHandlerCacheProperty;

        public static object GetHandler(SerializedProperty property)
        {
            if (_getHandlerMethod == null)
            {
                _getHandlerMethod = AsType.GetMethod("GetHandler", BindingFlags.NonPublic | BindingFlags.Static);
            }

            return _getHandlerMethod.Invoke(null, new object[] { property });
        }

        public static void SetPropertyHandlerCache(object cache)
        {
            if (_propertyHandlerCacheProperty == null)
                _propertyHandlerCacheProperty = AsType.GetProperty("propertyHandlerCache", BindingFlags.NonPublic | BindingFlags.Static);
            _propertyHandlerCacheProperty.SetValue(null, cache);
        }

        public static bool CanUseSameHandler(SerializedProperty p1, SerializedProperty p2)
        {
            if (_canUseSameHandlerMethod == null)
                _canUseSameHandlerMethod = AsType.GetMethod("CanUseSameHandler", BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)_canUseSameHandlerMethod.Invoke(null, new object[] { p1, p2 });
        }

        public static FieldInfo GetFieldInfoFromProperty(SerializedProperty property, out Type type)
        {
            _GetFieldInfoFromPropertyMethod ??= AsType.GetMethod("GetFieldInfoFromProperty", BindingFlags.NonPublic | BindingFlags.Static);

            object[] parameters = new object[] { property, null };

            // Invoke the method
            FieldInfo fieldInfo = (FieldInfo)_GetFieldInfoFromPropertyMethod.Invoke(null, parameters);

            // Retrieve the 'out' parameter 'type'
            type = (Type)parameters[1];
            return fieldInfo;
        }

        public static Type GetDrawerTypeForType(Type propertyType, bool isPropertyTypeAManagedReference = false)
        {
            _GetDrawerTypeForTypeMethod ??= AsType.GetMethod("GetDrawerTypeForType", BindingFlags.NonPublic | BindingFlags.Static);

            var renderPipelineAssetTypes = new Type[1] { GraphicsSettings.currentRenderPipelineAssetType };

            object[] parameters = new object[] { propertyType, renderPipelineAssetTypes, isPropertyTypeAManagedReference };

            return (Type)_GetDrawerTypeForTypeMethod.Invoke(null, parameters);
        }

        public static Type GetDrawerTypeForPropertyAndType(SerializedProperty property, Type type)
        {
            _GetDrawerTypeForPropertyAndTypeMethod ??= AsType.GetMethod("GetDrawerTypeForPropertyAndType", BindingFlags.NonPublic | BindingFlags.Static);

            object[] parameters = new object[] { property, type };

            return (Type)_GetDrawerTypeForPropertyAndTypeMethod.Invoke(null, parameters);
        }
    }
}
