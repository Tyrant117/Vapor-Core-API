using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public class InspectorTreeObject
    {
        private static readonly List<FieldInfo> s_FieldInfo = new();
        private static readonly List<MethodInfo> s_MethodInfo = new();
        private static readonly List<PropertyInfo> s_PropertyInfo = new();

        // Implicit conversion from SerializedInspectorObject to UnityEngine.Object
        public static implicit operator Object(InspectorTreeObject serializedInspectorObject)
        {
            return serializedInspectorObject.IsUnityObject ? serializedInspectorObject._serializedObject.targetObject : null;
        }

        public bool IsUnityObject { get; }
        private readonly object _internalObject;
        public object Object => IsUnityObject ? _serializedObject?.targetObject : _internalObject;
        public SerializedObject SerializedObject => IsUnityObject ? _serializedObject : null;
        public Type Type { get; }
        public InspectorTreeObject ParentObject { get; private set; }

        private readonly SerializedObject _serializedObject;
        private List<InspectorTreeProperty> _fields;
        private List<InspectorTreeProperty> _methods;
        private List<InspectorTreeProperty> _properties;
        private Dictionary<string, InspectorTreeProperty> _map;

        public List<InspectorTreeProperty> Fields => _fields;
        public List<InspectorTreeProperty> Methods => _methods;
        public List<InspectorTreeProperty> Properties => _properties;

        public InspectorTreeObject(SerializedObject serializedObject)
        {
            IsUnityObject = true;
            Type = serializedObject.targetObject.GetType();

            _serializedObject = serializedObject;

            BuildSerializedInspectorProperties();
        }

        public InspectorTreeObject(object @object, Type type)
        {
            IsUnityObject = false;
            _internalObject = @object;
            Type = type;

            BuildSerializedInspectorProperties();
        }

        public InspectorTreeObject WithParent(InspectorTreeObject parentObject)
        {
            ParentObject = parentObject;
            return this;
        }

        private void BuildSerializedInspectorProperties()
        {
            s_FieldInfo.Clear();
            s_MethodInfo.Clear();
            s_PropertyInfo.Clear();

            var targetType = Type;
            Stack<Type> typeStack = new();
            while (targetType != null)
            {
                typeStack.Push(targetType);
                targetType = targetType.BaseType;
            }

            while (typeStack.TryPop(out var type))
            {
                s_FieldInfo.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                s_PropertyInfo.AddRange(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                s_MethodInfo.AddRange(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            }

            _map = new Dictionary<string, InspectorTreeProperty>(s_FieldInfo.Count + s_MethodInfo.Count + s_PropertyInfo.Count);
            _fields = new(s_FieldInfo.Count);
            foreach (var field in s_FieldInfo.Where(ReflectionUtility.FieldSearchPredicate))
            {
                string path = $"{field.Name}";
                //Debug.Log($"Building FieldInfo at: {path}");
                //SerializedProperty unityProp = null;
                //if (IsUnityObject)
                //{
                //    unityProp = _serializedObject.FindProperty(path);
                //}
                var prop = new InspectorTreeProperty(this, null, field, path/*, unityProp*/);
                _fields.Add(prop);
                AddToMap(path, prop);
            }

            _methods = new(s_MethodInfo.Count);
            foreach (var method in s_MethodInfo.Where(ReflectionUtility.MethodSearchPredicate))
            {
                string path = $"{method.Name}";
                //Debug.Log($"Building MethodInfo at: {path}");
                var prop = new InspectorTreeProperty(this, null, method, path);
                _methods.Add(prop);
                AddToMap(path, prop);
            }

            _properties = new(s_PropertyInfo.Count);
            foreach (var property in s_PropertyInfo.Where(ReflectionUtility.PropertySearchPredicate))
            {
                string path = $"{property.Name}";
                //Debug.Log($"Building PropertyInfo at: {path}");
                var prop = new InspectorTreeProperty(this, null, property, path);
                _properties.Add(prop);
                AddToMap(path, prop);
            }

            foreach (var field in _fields)
            {
                field.BuildChildProperties();
            }

            foreach (var prop in _properties)
            {
                prop.BuildChildProperties();
            }
        }

        public InspectorTreeProperty FindProperty(string propertyPath)
        {
            return _map.TryGetValue(propertyPath, out var prop) ? prop : null;
        }

        public SerializedProperty FindSerializedProperty(string propertyPath)
        {
            //Debug.Log($"FindSerializedProperty: {propertyPath}");
            return IsUnityObject ? _serializedObject.FindProperty(propertyPath) : null;
        }

        public void ApplyModifiedProperties()
        {
            if (IsUnityObject && (Object)Object)
            {
                _serializedObject.Update();
                _serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty((Object)Object);
            }

            ParentObject?.ApplyModifiedProperties();
        }

        #region - Attributes -
        public bool HasAttribute<T>() where T : Attribute
        {
            return Type.IsDefined(typeof(T), true);
        }

        public bool TryGetAttribute<T>(out T atr) where T : Attribute
        {
            atr = Type.GetCustomAttribute<T>(true);
            return atr != null;
        }

        public bool TryGetAttributes<T>(out IEnumerable<T> atr) where T : Attribute
        {
            atr = Type.GetCustomAttributes<T>(true);
            return atr != null;
        }

        internal void AddToMap(string path, InspectorTreeProperty prop)
        {
            //Debug.Log($"Map Property At {path}");
            _map[path] = prop;
        }
        #endregion
    }
}
