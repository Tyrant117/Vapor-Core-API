#if UNITY_EDITOR_COROUTINES
using Unity.EditorCoroutines.Editor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using Vapor.Inspector;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public class InspectorTreeProperty
    {
        internal class Builder
        {
            // Core
            private InspectorTreeObject _inspectorObject;
            private InspectorTreeProperty _parentProperty;
            private string _path;
            private Type _parentType;
            private bool _hasParent;
            
            // Defaults
            private string _propertyName;
            private string _displayName;
            private Type _memberType;
            private MemberInfoType _memberInfoType;
            
            
            // Member Info
            private FieldInfo _fieldInfo;
            private MethodInfo _methodInfo;
            private PropertyInfo _propertyInfo;
            
            // Array
            private bool _isArrayElement;
            private int _elementIndex;
            
            
            public Builder()
            {
                
            }

            public Builder Init(InspectorTreeObject root, InspectorTreeProperty parentProperty, string path)
            {
                _inspectorObject = root;
                _parentProperty = parentProperty;
                _path = path;
                
                if (_parentProperty == null)
                {
                    _parentType = _inspectorObject.Type;
                }
                else
                {
                    _parentType = _parentProperty.PropertyType;
                    _hasParent = true;
                }
                return this;
            }

            public Builder AsField(FieldInfo fieldInfo)
            {
                _fieldInfo = fieldInfo;
                _propertyName = _fieldInfo.Name;
                _displayName = ObjectNames.NicifyVariableName(_propertyName);
                _memberType = _fieldInfo.FieldType;
                _memberInfoType = MemberInfoType.Field;
                
                return this;
            }
            
            public Builder AsMethod(MethodInfo methodInfo)
            {
                _methodInfo = methodInfo;
                _propertyName = _methodInfo.Name;
                _displayName = ObjectNames.NicifyVariableName(_propertyName);
                _memberType = _methodInfo.ReturnType;
                _memberInfoType = MemberInfoType.Method;
                
                return this;
            }
            
            public Builder AsProperty(PropertyInfo propertyInfo)
            {
                _propertyInfo = propertyInfo;
                _propertyName = _propertyInfo.Name;
                _displayName = ObjectNames.NicifyVariableName(_propertyName);
                _memberType = _propertyInfo.PropertyType;
                _memberInfoType = MemberInfoType.Property;
                
                return this;
            }
            
            public Builder AsArrayElement(object obj, Type elementType, int elementIndex)
            {
                _elementIndex = elementIndex;
                _isArrayElement = true;
                _propertyName = $"[{elementIndex}]";
                _memberType = elementType;
                _displayName = _propertyName;
            
                var atr = _memberType.GetCustomAttribute<ArrayEntryNameAttribute>();
                if (atr != null)
                {
                    var mi = ReflectionUtility.GetMember(_memberType, atr.Resolver);
                    if (ReflectionUtility.TryResolveMemberValue<string>(obj, mi, null, out var atrDisplayName))
                    {
                        _displayName = atrDisplayName;
                    }
                }
                
                return this;
            }

            public InspectorTreeProperty Build()
            {
                return null;
            }
        }

        internal static readonly Builder s_Builder = new Builder();
        
        public enum MemberInfoType
        {
            Field,
            Method,
            Property,
            // ArrayElement,
        }

        public enum ArrayRebuildReason
        {
            Remove,
            SetValue,
            SetElementValue,
            Resize,
            Insert,
            Swap
        }

        public class ArrayReflectionHelper
        {
            public bool IsList;
            public Type ArrayType;
            public Type ElementType;
            public PropertyInfo SizeInfo;

            private PropertyInfo _listElementGetter;
            private MethodInfo _arrayElementGetter;

            public ArrayReflectionHelper(Type arrayType, Type elementType, bool isList)
            {
                IsList = isList;
                ArrayType = arrayType;
                ElementType = elementType;
                if (IsList)
                {
                    SizeInfo = ArrayType.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                    _listElementGetter = ArrayType.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance, null, ElementType, new[] { typeof(int) }, null);
                }
                else
                {
                    SizeInfo = ArrayType.GetProperty("Length");
                    _arrayElementGetter = ArrayType.GetMethod("GetValue", new[] { typeof(int) });
                }
            }

            public int GetSize(object arrayObject)
            {
                return (int)SizeInfo.GetValue(arrayObject);
            }

            public object GetElementObject(object arrayObject, int index)
            {
                return IsList
                    ? _listElementGetter.GetValue(arrayObject, new object[] { index })
                    : _arrayElementGetter.Invoke(arrayObject, new object[] { index });
            }
        }

        private static readonly List<FieldInfo> s_FieldInfo = new();
        private static readonly List<MethodInfo> s_MethodInfo = new();
        private static readonly List<PropertyInfo> s_PropertyInfo = new();
        private static readonly Stack<Type> s_TypeStack = new();

        public InspectorTreeObject InspectorObject { get; }
        public InspectorTreeProperty ParentProperty { get; }
        public bool HasParentProperty { get; }
        //public object ParentObject { get; private set; }
        

        public Type ParentType { get; }
        public Type PropertyType { get; private set;}
        public string PropertyPath { get; }
        public string PropertyName { get; private set;}
        public string DisplayName { get; private set;}
        public MemberInfoType PropertyInfoType { get; }

        public SerializedPropertyType SerializedPropertyType { get; }
        public SerializedPropertyNumericType SerializedPropertyNumericType { get; }
        public bool IsArray { get; }
        public bool IsStruct { get; }
        public bool HasCustomDrawer { get; private set; }
        public bool NoChildProperties { get; private set; }
        public bool IsWrappedSystemObject { get; private set; }
        

        // Fields
        public FieldInfo FieldInfo { get; }
        public bool IsUnitySerializedProperty { get; }
        //public SerializedProperty UnityProperty { get; }
        private object _cachedStructObject;

        // Methods
        public MethodInfo MethodInfo { get; }

        // Properties
        public PropertyInfo PropertyInfo { get; }

        // Array Element
        public bool IsArrayElement { get; private set;}
        public object ArrayElementObject { get; private set; }
        public int ElementIndex { get; private set;}


        // Array
        [CreateProperty]
        public int ArraySize { get; set; }
        private List<InspectorTreeProperty> _arrayData;
        public List<InspectorTreeProperty> ArrayData => _arrayData;

        private ArrayReflectionHelper _arrayHelper;


        // Members
        private List<InspectorTreeProperty> _fields;
        public List<InspectorTreeProperty> Fields => _fields;

        private List<InspectorTreeProperty> _methods;
        public List<InspectorTreeProperty> Methods => _methods;

        private List<InspectorTreeProperty> _properties;        
        public List<InspectorTreeProperty> Properties => _properties;


        // Events
        public Action RequireRedraw = delegate { };

       
        /// <summary>
        /// A Field Property
        /// </summary>
        public InspectorTreeProperty(InspectorTreeObject root, InspectorTreeProperty parentProperty, FieldInfo fieldInfo, string path)
        {
            InspectorObject = root;
            ParentProperty = parentProperty;
            FieldInfo = fieldInfo;
            PropertyPath = path;
            PropertyName = FieldInfo?.Name;
            DisplayName = ObjectNames.NicifyVariableName(PropertyName);
            PropertyType = FieldInfo?.FieldType;
            PropertyInfoType = MemberInfoType.Field;

            if (ParentProperty == null)
            {
                ParentType = InspectorObject.Type;
            }
            else
            {
                ParentType = parentProperty.PropertyType;
                HasParentProperty = true;
            }

            IsUnitySerializedProperty = InspectorObject.IsUnityObject;
            SerializedPropertyType = TypeToSerializedPropertyType(PropertyType);
            SerializedPropertyNumericType = TypeToSerializedPropertyNumericType(PropertyType);
            IsArray = IsArrayOrList(PropertyType);
            IsStruct = PropertyType.IsValueType && !PropertyType.IsPrimitive;
            if (!IsUnitySerializedProperty)
            {
                var currentValue = GetValueSafe(true);
                if (currentValue == null)
                {
                    var instance = GetDefaultValue(SerializedPropertyType, PropertyType);
                    SetValue(instance);
                }
            }
            else
            {
                if (IsArray)
                {
                    var currentValue = GetValueSafe(true);
                    if (currentValue == null)
                    {
                        var instance = GetDefaultValue(SerializedPropertyType, PropertyType);
                        SetValue(instance);
                    }
                }
            }
            if (IsStruct)
            {
                _cachedStructObject = GetValue(true);
            }

            IsWrappedSystemObject = SerializedPropertyType == SerializedPropertyType.ManagedReference && PropertyType == typeof(object);
            HasCustomDrawer = !TypeHasAttribute<IgnoreCustomDrawerAttribute>() && !HasAttribute<IgnoreCustomDrawerAttribute>() && SerializedDrawerUtility.HasCustomPropertyDrawer(PropertyType, SerializedPropertyType == SerializedPropertyType.ManagedReference);
        }
        
        /// <summary>
        /// A Method Property
        /// </summary>
        public InspectorTreeProperty(InspectorTreeObject root, InspectorTreeProperty parentProperty, MethodInfo methodInfo, string path)
        {
            InspectorObject = root;
            ParentProperty = parentProperty;            
            MethodInfo = methodInfo;
            PropertyPath = path;
            PropertyName = MethodInfo.Name;
            DisplayName = ObjectNames.NicifyVariableName(PropertyName);
            PropertyType = MethodInfo.ReturnType;
            PropertyInfoType = MemberInfoType.Method;

            if (ParentProperty == null)
            {
                ParentType = InspectorObject.Type;
            }
            else
            {
                ParentType = parentProperty.PropertyType;
                HasParentProperty = true;
            }          
        }

        /// <summary>
        /// A Property Property
        /// </summary>
        public InspectorTreeProperty(InspectorTreeObject root, InspectorTreeProperty parentProperty, PropertyInfo propInfo, string path)
        {
            InspectorObject = root;
            ParentProperty = parentProperty;
            PropertyInfo = propInfo;
            PropertyPath = path;
            PropertyName = PropertyInfo.Name;
            DisplayName = ObjectNames.NicifyVariableName(PropertyName);
            PropertyType = PropertyInfo.PropertyType;
            PropertyInfoType = MemberInfoType.Property;

            if (ParentProperty == null)
            {
                ParentType = InspectorObject.Type;
            }
            else
            {
                ParentType = parentProperty.PropertyType;
                HasParentProperty = true;
            }

            IsUnitySerializedProperty = InspectorObject.IsUnityObject;//UnityProperty != null;
            SerializedPropertyType = TypeToSerializedPropertyType(PropertyType);
            SerializedPropertyNumericType = TypeToSerializedPropertyNumericType(PropertyType);
            IsArray = IsArrayOrList(PropertyType);
            IsStruct = PropertyType.IsValueType && !PropertyType.IsPrimitive;
            if (IsStruct)
            {
                _cachedStructObject = GetValue(true);
            }

            HasCustomDrawer = !TypeHasAttribute<IgnoreCustomDrawerAttribute>() && !HasAttribute<IgnoreCustomDrawerAttribute>() && SerializedDrawerUtility.HasCustomPropertyDrawer(PropertyType, SerializedPropertyType == SerializedPropertyType.ManagedReference);
        }

        /// <summary>
        /// An Array Element Property
        /// </summary>
        public InspectorTreeProperty(InspectorTreeObject root, InspectorTreeProperty parentProperty, Type fieldType, object fieldObject, int index, string path)
        {
            InspectorObject = root;
            ParentProperty = parentProperty;
            PropertyType = fieldType;
            ArrayElementObject = fieldObject;
            PropertyPath = path;
            ElementIndex = index;
            IsArrayElement = true;
            PropertyName = $"[{index}]";
            DisplayName = PropertyName;
            var atr = fieldType.GetCustomAttribute<ArrayEntryNameAttribute>();
            if (atr != null)
            {
                var mi = ReflectionUtility.GetMember(fieldType, atr.Resolver);
                if (ReflectionUtility.TryResolveMemberValue<string>(fieldObject, mi, null, out var atrDisplayName))
                {
                    DisplayName = atrDisplayName;
                }
            }

            PropertyInfoType = MemberInfoType.Field;
            ParentType = parentProperty.PropertyType;
            HasParentProperty = true;

            IsUnitySerializedProperty = InspectorObject.IsUnityObject;
            SerializedPropertyType = TypeToSerializedPropertyType(PropertyType);
            SerializedPropertyNumericType = TypeToSerializedPropertyNumericType(PropertyType);
            IsArray = IsArrayOrList(PropertyType);
            IsStruct = PropertyType.IsValueType && !PropertyType.IsPrimitive;
            if (!IsUnitySerializedProperty)
            {
                var currentValue = GetValueSafe(true);
                if (currentValue == null)
                {
                    var instance = GetDefaultValue(SerializedPropertyType, PropertyType);
                    SetValue(instance);
                }
            }
            else
            {
                if (IsArray)
                {
                    var currentValue = GetValueSafe(true);
                    if (currentValue == null)
                    {
                        var instance = GetDefaultValue(SerializedPropertyType, PropertyType);
                        SetValue(instance);
                    }
                }
            }
            if (IsStruct)
            {
                _cachedStructObject = GetValue(true);
            }

            HasCustomDrawer = ParentProperty.HasCustomDrawer ||
                              (!TypeHasAttribute<IgnoreCustomDrawerAttribute>() && !HasAttribute<IgnoreCustomDrawerAttribute>() &&
                               SerializedDrawerUtility.HasCustomPropertyDrawer(PropertyType, SerializedPropertyType == SerializedPropertyType.ManagedReference));
        }

        public void BuildChildProperties()
        {          
            // Need to handle getting the array properties manually.
            if (IsArray)
            {
                BuildListProperties();
                return;
            }

            // Dont need to recurse the fields for things that we shouldnt draw or things Unity.Objects that will be drawn with an ObjectField.
            if (IsUnityObjectOrSubclass() || !TypeHasAttribute<SerializableAttribute>())
            {
                return;
            }

            if(TypeHasAttribute<IgnoreChildNodesAttribute>() || HasAttribute<IgnoreChildNodesAttribute>())
            {
                NoChildProperties = true;
                return;
            }

            s_FieldInfo.Clear();
            s_MethodInfo.Clear();
            s_PropertyInfo.Clear();
            s_TypeStack.Clear();

            var targetType = PropertyType;
            while (targetType != null)
            {
                s_TypeStack.Push(targetType);
                targetType = targetType.BaseType;
            }

            while (s_TypeStack.TryPop(out var type))
            {
                s_FieldInfo.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                s_PropertyInfo.AddRange(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                s_MethodInfo.AddRange(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            }

            _fields = new(s_FieldInfo.Count);
            foreach (var field in s_FieldInfo.Where(ReflectionUtility.FieldSearchPredicate))
            {
                string path = PropertyPath.Length > 0 ? $"{PropertyPath}.{field.Name}" : $"{field.Name}";
                //Debug.Log($"Building FieldInfo at: {path}");
                var prop = new InspectorTreeProperty(InspectorObject, this, field, path);
                _fields.Add(prop);
                InspectorObject.AddToMap(path, prop);
            }

            _methods = new(s_MethodInfo.Count);
            foreach (var method in s_MethodInfo.Where(ReflectionUtility.MethodSearchPredicate))
            {
                string path = PropertyPath.Length > 0 ? $"{PropertyPath}.{method.Name}" : $"{method.Name}";
                //Debug.Log($"Building MethodInfo at: {path}");
                var prop = new InspectorTreeProperty(InspectorObject, this, method, path);
                _methods.Add(prop);
                InspectorObject.AddToMap(path, prop);
            }

            _properties = new(s_PropertyInfo.Count);
            foreach (var property in s_PropertyInfo.Where(ReflectionUtility.PropertySearchPredicate))
            {
                string path = PropertyPath.Length > 0 ? $"{PropertyPath}.{property.Name}" : $"{property.Name}";
                //Debug.Log($"Building PropertyInfo at: {path}");
                var prop = new InspectorTreeProperty(InspectorObject, this, property, path);
                _properties.Add(prop);
                InspectorObject.AddToMap(path, prop);
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

        private void BuildListProperties()
        {
            if (PropertyType.IsArray)
            {
                var listObj = GetValue();
                if (listObj == null)
                {
                    Debug.LogWarning($"{PropertyPath} has a non-initialized list, but it is serialized. This shouldn't happen.");
                    return;
                    // Array arr = Array.CreateInstance(PropertyType.GetElementType(), 0);
                    // SetValue(arr);
                }

                _arrayHelper = new ArrayReflectionHelper(PropertyType, PropertyType.GetElementType(), false);

                var lengthPropInfo = PropertyType.GetProperty("Length");
                int length = (int)lengthPropInfo.GetValue(GetValue());
                ArraySize = length;
                Type elementType = PropertyType.GetElementType();
                MethodInfo elementGetter = PropertyType.GetMethod("GetValue", new[] { typeof(int) });
                _arrayData = new List<InspectorTreeProperty>(length);
                for (int i = 0; i < length; i++)
                {
                    // Access each element
                    int idx = i;
                    object element = elementGetter.Invoke(GetValue(), new object[] { idx });
                    string path = PropertyPath.Length > 0 ? $"{PropertyPath}.Array.data[{idx}]" : $"{PropertyName}.Array.data[{idx}]";
                    var prop = new InspectorTreeProperty(InspectorObject, this, elementType, element, idx, path);
                    _arrayData.Add(prop);
                    InspectorObject.AddToMap(path, prop);
                }
            }
            else
            {
                var listObj = GetValue();
                if(listObj == null)
                {
                    Debug.LogWarning($"{PropertyPath} has a non-initialized list, but it is serialized. This shouldn't happen.");
                    return;
                    // var tempList = (IList)CreateGenericList(PropertyType.GetGenericArguments()[0]);
                    // SetValue(tempList);
                }

                _arrayHelper = new ArrayReflectionHelper(PropertyType, PropertyType.GetGenericArguments()[0], true);

                int count = _arrayHelper.GetSize(GetValue()); //countPropInfo.GetValue(TargetObject);
                ArraySize = count;
                _arrayData = new List<InspectorTreeProperty>(count);
                for (int i = 0; i < count; i++)
                {
                    // Access each element
                    int idx = i;
                    object element = _arrayHelper.GetElementObject(GetValue(), idx);// itemProperty.GetValue(TargetObject, new object[] { idx });
                    string path = PropertyPath.Length > 0 ? $"{PropertyPath}.Array.data[{idx}]" : $"{PropertyName}.Array.data[{idx}]";
                    var prop = new InspectorTreeProperty(InspectorObject, this, _arrayHelper.ElementType, element, idx, path);
                    _arrayData.Add(prop);
                    InspectorObject.AddToMap(path, prop);
                }
            }

            foreach (var prop in _arrayData)
            {
                prop.BuildChildProperties();
            }
        }

        public InspectorTreeProperty FindPropertyRelative(string relativePath)
        {
            //Debug.Log($"Trying To Field Property At {PropertyPath}.{relativePath}");
            return InspectorObject.FindProperty($"{PropertyPath}.{relativePath}");
        }

        // public object GetObject()
        // {
        //     if (PropertyInfoType == MemberInfoType.Field)
        //     {
        //         if (IsStruct)
        //         {
        //             return _cachedStructObject;
        //         }
        //
        //         var parentObject = GetParentObject();
        //         return parentObject == null ? null : FieldInfo.GetValue(parentObject);
        //     }
        //
        //     if (PropertyInfoType == MemberInfoType.Method)
        //         return MethodInfo.Invoke(GetParentObject(), null);
        //     if (PropertyInfoType == MemberInfoType.Property)
        //         return IsStruct ? _cachedStructObject : PropertyInfo.GetValue(GetParentObject());
        //     if (PropertyInfoType == MemberInfoType.ArrayElement)
        //         return GetValueAtIndex(ElementIndex); //ArrayElementObject
        //     return null;
        // }

        public object GetParentObject()
        {
            return HasParentProperty ? ParentProperty.GetValue() : InspectorObject.Object;
        }

        public Type GetParentsParentType()
        {
            return HasParentProperty ? ParentProperty.ParentType : InspectorObject.Type;
        }

        public T GetValue<T>()
        {
            var value = GetValue();
            return (T)value;
        }

        public object GetValue(bool ignoreCaching = false)
        {
            switch (PropertyInfoType)
            {
                case MemberInfoType.Field:
                    if (IsStruct && !ignoreCaching)
                    {
                        return _cachedStructObject;
                    }
                    
                    if (IsArrayElement)
                    {
                        return _CastToType(ArrayElementObject, PropertyType);;
                    }

                    var parentObject = GetParentObject();
                    return _CastToType(FieldInfo.GetValue(parentObject), PropertyType);
                case MemberInfoType.Method:
                    return _CastToType(MethodInfo.Invoke(GetParentObject(), null), PropertyType);
                case MemberInfoType.Property:
                    return IsStruct && !ignoreCaching ? _cachedStructObject : _CastToType(PropertyInfo.GetValue(GetParentObject()), PropertyType);
                default:
                    return null;
            }

            static object _CastToType(object obj, Type targetType)
            {
                if (obj == null)
                {
                    if (targetType.IsClass || targetType.IsInterface || Nullable.GetUnderlyingType(targetType) != null)
                    {
                        return null; // Null is a valid value for reference types and nullable types
                    }
                    throw new ArgumentNullException(nameof(obj), "Cannot cast null to a non-nullable value type.");
                }

                // Check if the object is already of the target type
                if (targetType.IsAssignableFrom(obj.GetType()))
                {
                    return obj; // No casting needed
                }
                
                if (targetType == typeof(Type) && obj is string s)
                {
                    // Assume that a proper assembly qualified type is being sent.
                    return Type.GetType(s);
                }
            
                if (targetType.IsEnum && obj.GetType().IsPrimitive)
                {
                    return Enum.ToObject(targetType, (long)obj);
                }

                return Convert.ChangeType(obj, targetType);
            }
        }

        public object GetValueSafe(bool ignoreCaching = false)
        {
            var parentObj = GetParentObject();
            return parentObj == null ? null : GetValue(ignoreCaching);
        }

        public void SetValue<T>(T val)
        {
            switch (PropertyInfoType)
            {
                case MemberInfoType.Field:
                {
                    if (IsStruct)
                    {
                        _cachedStructObject = val;
                    }

                    if (IsArrayElement)
                    {
                        ParentProperty.SetValueAtIndex(ElementIndex, val);
                        ArrayElementObject = ParentProperty.GetValueAtIndex(ElementIndex);
                    }
                    else
                    {
                        var target = GetParentObject();
                        if (target != null)
                        {
                            if (IsStruct)
                            {
                                Debug.Log($"Setting Struct Value On {target.GetType()}");
                            }
                            FieldInfo.SetValue(target, val);
                        }
                    }
                    if (IsArray)
                    {
                        InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
                        EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.SetValue), this);
#endif
                    }

                    break;
                }
                case MemberInfoType.Property:
                {
                    if (IsStruct)
                    {
                        _cachedStructObject = val;
                    }
                    var target = GetParentObject();
                    if (target != null)
                    {
                        PropertyInfo.SetValue(target, val);
                    }
                    if (IsArray)
                    {
                        InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
                        EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.SetValue), this);
#endif
                    }

                    break;
                }
            }
            // else if (PropertyInfoType == MemberInfoType.ArrayElement)
            // {
            //     Debug.Log($"Setting Array Element {val}");
            //     ParentProperty.SetValueAtIndex(ElementIndex, val);
            // }
        }

        public void Invoke(params object[] parameters)
        {
            if (PropertyInfoType == MemberInfoType.Method)
            {
                MethodInfo.Invoke(GetParentObject(), parameters);
            }
        }
        public T Invoke<T>(params object[] parameters)
        {
            return PropertyInfoType == MemberInfoType.Method ? (T)MethodInfo.Invoke(GetParentObject(), parameters) : default;
        }

        #region - List -
        public void SetValueAtIndex<T>(int index, T val)
        {
            if (!IsArray)
                return;

            if (!_arrayHelper.IsList)
            {
                if (GetValue() is Array array)
                {
                    array.SetValue(val, index);
                }
            }
            else
            {
                if (GetValue() is IList list)
                {
                    list[index] = val;
                }
            }
            InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
            EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.SetElementValue), this);
#endif
        }

        public object GetValueAtIndex(int index)
        {
            if (!IsArray)
            {
                return null;
            }

            if (index < 0)
            {
                return null;
            }
            
            if (GetValue() is Array array && index < array.Length)
            {
                return array.GetValue(index);
            }

            if (GetValue() is IList list && index < list.Count)
            {
                
                return list[index];
            }
            
            return null;
        }

        public int IndexOf(object value)
        {
            if (!IsArray)
            {
                return -1;
            }

            if (!_arrayHelper.IsList)
            {
                if (GetValue() is Array array)
                {
                    return Array.IndexOf(array, value);
                }
            }
            else
            {
                if (GetValue() is IList list)
                { 
                    return list.IndexOf(value);
                }
            }

            return -1;
        }
        
        public void ResizeArray(int newSize)
        {
            if (!IsArray)
                return;

            if (newSize < 0)
                newSize = 0;

            if(ArraySize == newSize)
                return;

            if (!_arrayHelper.IsList)
            {
                if (GetValue() is Array array)
                {
                    var tempList = (IList)CreateGenericList(_arrayHelper.ElementType);
                    if(newSize < array.Length)
                    {
                        for (int i = 0; i < newSize; i++)
                        {
                            tempList.Add(array.GetValue(i));
                        }
                    }
                    else
                    {
                        for (int i = 0; i < array.Length; i++)
                        {
                            tempList.Add(array.GetValue(i));
                        }
                        for (int i = array.Length; i < newSize; i++)
                        {
                            tempList.Add(GetDefaultValueForArrayElement(_arrayHelper.ElementType)/*Activator.CreateInstance(_arrayHelper.ElementType)*/);
                        }
                    }
                    Array arr = Array.CreateInstance(_arrayHelper.ElementType, newSize);
                    tempList.CopyTo(arr, 0);
                    SetValue(arr);
                }
            }
            else
            {
                if (GetValue() is IList list)
                {
                    if (newSize < list.Count)
                    {
                        while (list.Count > newSize)
                        {
                            list.RemoveAt(list.Count - 1);
                        }
                    }
                    else
                    {
                        for (int i = list.Count; i < newSize; i++)
                        {
                            list.Add(GetDefaultValueForArrayElement(_arrayHelper.ElementType)/*Activator.CreateInstance(_arrayHelper.ElementType)*/);
                        }
                    }
                }
            }
            InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
            EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.Resize), this);
#endif
            //BuildListProperties();
        }

        public void Add()
        {
            if (!IsArray)
                return;

            //FormatterServices.GetUninitializedObject(_arrayHelper.ElementType);
            try
            {
                Insert(ArraySize, GetDefaultValueForArrayElement(_arrayHelper.ElementType));
            }
            catch
            {
                Insert(ArraySize, null);
            }
        }

        public void Insert(int index, object @object)
        {
            if (!IsArray)
                return;

            if (!_arrayHelper.IsList)
            {
                if (GetValue() is Array array)
                {
                    var tempList = (IList)CreateGenericList(_arrayHelper.ElementType);
                    foreach (var item in array)
                    {
                        tempList.Add(item);
                    }
                    tempList.Insert(index, @object);
                    Array arr = Array.CreateInstance(_arrayHelper.ElementType, tempList.Count);
                    tempList.CopyTo(arr, 0);
                    SetValue(arr);
                }
            }
            else
            {
                if (GetValue() is IList list)
                {
                    list.Insert(index, @object);
                }
            }
            InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
            EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.Insert), this);
#endif
        }

        public void RemoveLast()
        {
            if (!IsArray)
                return;

            RemoveAt(ArraySize - 1);
        }

        public void RemoveAt(int elementIndex)
        {
            if (!IsArray)
                return;

            if (!_arrayHelper.IsList)
            {
                if (GetValue() is IEnumerable enumerable)
                {
                    var tempList = (IList)CreateGenericList(_arrayHelper.ElementType);
                    foreach (var item in enumerable)
                    {
                        tempList.Add(item);
                    }
                    tempList.RemoveAt(elementIndex);
                    Array arr = Array.CreateInstance(_arrayHelper.ElementType, tempList.Count);
                    tempList.CopyTo(arr, 0);
                    SetValue(arr);
                    Debug.Log($"Set New Array {arr.Length}");
                }
            }
            else
            {
                if (GetValue() is IList list)
                {
                    list.RemoveAt(elementIndex);
                }
            }
            InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
            EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.Remove), this);
#endif
        }

        public void Swap(int fromIdx, int toIdx)
        {
            if (!IsArray)
            {
                return;
            }

            if (_arrayHelper.IsList)
            {
                if (GetValue() is IList list)
                {
                    (object, object) swap = (list[fromIdx], list[toIdx]);
                    list[toIdx] = swap.Item1;
                    list[fromIdx] = swap.Item2;
                }
            }
            else
            {
                if (GetValue() is Array arr)
                {
                    (object, object) swap = (arr.GetValue(fromIdx), arr.GetValue(toIdx));
                    arr.SetValue(swap.Item1, toIdx);
                    arr.SetValue(swap.Item2, fromIdx);

                    SetValue(arr);
                }
            }
            InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
            EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.Swap), this);
#endif
        }

        public void DuplicateArrayProperty(InspectorTreeProperty property)
        {
            if (!IsArray)
                return;

            var dup = ClipboardUtility.CopyObject(property.GetValue());
            var idx = property.ElementIndex;
            Insert(idx, dup);
        }

        private IEnumerator DelayedBuildListProperties(ArrayRebuildReason reason)
        {
            yield return null;
            Debug.Log(reason);
            BuildListProperties();
            RequireRedraw.Invoke();
        }

        public static object CreateGenericList(Type typeParameter)
        {
            // Get the generic type definition for List<T>
            Type listType = typeof(List<>);

            // Construct the List<T> type with the specified type parameter
            Type constructedListType = listType.MakeGenericType(typeParameter);

            // Create an instance of the constructed List<T> type
            return Activator.CreateInstance(constructedListType);
        }

        /// <summary>
        /// Creates a generic list from either the array element type if the object is an array, or creates a list of the property type.
        /// </summary>
        /// <returns></returns>
        public object CreateGenericListOfType()
        {
            return IsArray ? CreateGenericList(_arrayHelper.ElementType) : CreateGenericList(PropertyType);
        }
        #endregion

        #region - Attributes -
        public bool HasAttribute<T>() where T : Attribute
        {
            return PropertyInfoType switch
            {
                MemberInfoType.Field => IsArrayElement ? ParentProperty.HasAttribute<T>() : FieldInfo.IsDefined(typeof(T), true),
                MemberInfoType.Method => MethodInfo.IsDefined(typeof(T), true),
                MemberInfoType.Property => PropertyInfo.IsDefined(typeof(T), true),
                _ => false,
            };
        }
        public bool TryGetAttribute<T>(out T atr) where T : Attribute
        {            
            switch (PropertyInfoType)
            {
                case MemberInfoType.Field:
                    if (IsArrayElement)
                    {
                        return ParentProperty.TryGetAttribute(out atr);
                    }
                    atr = FieldInfo.GetCustomAttribute<T>(true);
                    return atr != null;
                case MemberInfoType.Method:
                    atr = MethodInfo.GetCustomAttribute<T>(true);
                    return atr != null;
                case MemberInfoType.Property:
                    atr = PropertyInfo.GetCustomAttribute<T>(true);
                    return atr != null;
                // case MemberInfoType.ArrayElement:
                //     return ParentProperty.TryGetAttribute(out atr);
                default:
                    atr = null;
                    return false;
            }
        }
        public bool TryGetAttributes<T>(out T[] atr) where T : Attribute
        {
            switch (PropertyInfoType)
            {
                case MemberInfoType.Field:
                    if (IsArrayElement)
                    {
                        return ParentProperty.TryGetAttributes(out atr);
                    }
                    atr = FieldInfo.GetCustomAttributes<T>(true).ToArray();
                    return atr is { Length: > 0 };
                case MemberInfoType.Method:
                    atr = MethodInfo.GetCustomAttributes<T>(true).ToArray();
                    return atr is { Length: > 0 };
                case MemberInfoType.Property:
                    atr = PropertyInfo.GetCustomAttributes<T>(true).ToArray();
                    return atr is { Length: > 0 };
                // case MemberInfoType.ArrayElement:
                //     return ParentProperty.TryGetAttributes(out atr);
                default:
                    atr = null;
                    return false;
            }
        }

        public bool TypeHasAttribute<T>() where T : Attribute
        {
            return PropertyType.IsDefined(typeof(T), true);
        }
        public bool TryGetTypeAttribute<T>(out T atr) where T : Attribute
        {
            atr = PropertyType.GetCustomAttribute<T>(true);
            return atr != null;
        }
        public bool TryGetTypeAttributes<T>(out IEnumerable<T> atr) where T : Attribute
        {
            atr = PropertyType.GetCustomAttributes<T>(true);
            return atr.Any();
        }
        #endregion

        #region - Helpers -
        public bool IsUnityObjectOrSubclass() => SerializedPropertyType is SerializedPropertyType.ObjectReference or SerializedPropertyType.ExposedReference;

        public bool ParentIsStruct() => ParentType.IsValueType && !ParentType.IsPrimitive;

        public static SerializedPropertyType TypeToSerializedPropertyType(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    if (type.IsEnum)
                    {
                        return SerializedPropertyType.Enum;
                    }
                    return SerializedPropertyType.Integer;
                case TypeCode.Single:
                case TypeCode.Double:
                    return SerializedPropertyType.Float;
                case TypeCode.Boolean:
                    return SerializedPropertyType.Boolean;
                case TypeCode.Char:
                    return SerializedPropertyType.Character;
                case TypeCode.String:
                    return SerializedPropertyType.String;
                case TypeCode.Object:
                    if (IsArrayOrList(type))
                    {
                        return SerializedPropertyType.Generic;
                    }
                    else if (type.IsEnum)
                    {
                        return SerializedPropertyType.Enum;
                    }
                    else if (type == typeof(Color))
                    {
                        return SerializedPropertyType.Color;
                    }
                    else if (type == typeof(Object) || type.IsSubclassOf(typeof(Object)))
                    {
                        return SerializedPropertyType.ObjectReference;
                    }
                    else if (type == typeof(LayerMask))
                    {
                        return SerializedPropertyType.LayerMask;
                    }
                    else if (type == typeof(RenderingLayerMask))
                    {
                        return SerializedPropertyType.RenderingLayerMask;
                    }
                    else if (type == typeof(Vector2))
                    {
                        return SerializedPropertyType.Vector2;
                    }
                    else if (type == typeof(Vector3))
                    {
                        return SerializedPropertyType.Vector3;
                    }
                    else if (type == typeof(Vector4))
                    {
                        return SerializedPropertyType.Vector4;
                    }
                    else if (type == typeof(Rect))
                    {
                        return SerializedPropertyType.Rect;
                    }
                    else if (type == typeof(AnimationCurve) || type.IsSubclassOf(typeof(AnimationCurve)))
                    {
                        return SerializedPropertyType.AnimationCurve;
                    }
                    else if (type == typeof(Bounds))
                    {
                        return SerializedPropertyType.Bounds;
                    }
                    else if (type == typeof(Gradient) || type.IsSubclassOf(typeof(Gradient)))
                    {
                        return SerializedPropertyType.Gradient;
                    }
                    else if (type == typeof(Quaternion))
                    {
                        return SerializedPropertyType.Quaternion;
                    }
                    else if (type == typeof(Vector2Int))
                    {
                        return SerializedPropertyType.Vector2Int;
                    }
                    else if (type == typeof(Vector3Int))
                    {
                        return SerializedPropertyType.Vector3Int;
                    }
                    else if (type == typeof(RectInt))
                    {
                        return SerializedPropertyType.RectInt;
                    }
                    else if (type == typeof(BoundsInt))
                    {
                        return SerializedPropertyType.BoundsInt;
                    }
                    else if (type == typeof(Hash128))
                    {
                        return SerializedPropertyType.Hash128;
                    }
                    else
                    {                        
                        return SerializedPropertyType.ManagedReference;
                    }
                default:
                    return SerializedPropertyType.Generic;
            }
        }

        public static SerializedPropertyNumericType TypeToSerializedPropertyNumericType(Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.SByte => SerializedPropertyNumericType.Int8,
                TypeCode.Byte => SerializedPropertyNumericType.UInt8,
                TypeCode.Int16 => SerializedPropertyNumericType.Int16,
                TypeCode.UInt16 => SerializedPropertyNumericType.UInt16,
                TypeCode.Int32 => SerializedPropertyNumericType.Int32,
                TypeCode.UInt32 => SerializedPropertyNumericType.UInt32,
                TypeCode.Int64 => SerializedPropertyNumericType.Int64,
                TypeCode.UInt64 => SerializedPropertyNumericType.UInt64,
                TypeCode.Single => SerializedPropertyNumericType.Float,
                TypeCode.Double => SerializedPropertyNumericType.Double,
                _ => SerializedPropertyNumericType.Unknown,
            };
        }

        public static object GetDefaultValue(SerializedPropertyType propertyType, Type type)
        {
            switch (propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (!type.IsClass)
                    {
                        return Activator.CreateInstance(type);
                    }

                    if (IsArrayOrList(type))
                    {
                        if (type.IsArray)
                        {
                            var elementType = type.GetElementType();
                            Assert.IsNotNull(elementType);
                            return Array.CreateInstance(elementType, 0);
                        }

                        return (IList)CreateGenericList(type.GetGenericArguments()[0]);
                    }

                    return null;
                case SerializedPropertyType.Integer:
                    return 0;
                case SerializedPropertyType.Boolean:
                    return false;
                case SerializedPropertyType.Float:
                    return 0f;
                case SerializedPropertyType.String:
                    return string.Empty;
                case SerializedPropertyType.Color:
                    return Color.clear;
                case SerializedPropertyType.ObjectReference:
                    return (Object)null;
                case SerializedPropertyType.LayerMask:
                    return new LayerMask();
                case SerializedPropertyType.Enum:
                    return FormatterServices.GetUninitializedObject(type);
                case SerializedPropertyType.Vector2:
                    return Vector2.zero;
                case SerializedPropertyType.Vector3:
                    return Vector3.zero;
                case SerializedPropertyType.Vector4:
                     return Vector4.zero;
                case SerializedPropertyType.Rect:
                     return Rect.zero;
                case SerializedPropertyType.ArraySize:
                    return 0;
                case SerializedPropertyType.Character:
                    return '\0';
                case SerializedPropertyType.AnimationCurve:
                    return new AnimationCurve();
                case SerializedPropertyType.Bounds:
                    return new Bounds();
                case SerializedPropertyType.Gradient:
                    return new Gradient();
                case SerializedPropertyType.Quaternion:
                    return Quaternion.identity;
                case SerializedPropertyType.ExposedReference:
                    return null;
                case SerializedPropertyType.FixedBufferSize:
                    return 0;
                case SerializedPropertyType.Vector2Int:
                    return Vector2Int.zero;
                case SerializedPropertyType.Vector3Int:
                    return Vector3Int.zero;
                case SerializedPropertyType.RectInt:
                    return Rect.zero;
                case SerializedPropertyType.BoundsInt:
                    return new BoundsInt();
                case SerializedPropertyType.ManagedReference:
                    try
                    {
                        return type.IsClass ? type.IsSerializable ? FormatterServices.GetUninitializedObject(type) : null : Activator.CreateInstance(type);
                    }
                    catch
                    {
                        return null;
                    }
                case SerializedPropertyType.Hash128:
                    return new Hash128();
                case SerializedPropertyType.RenderingLayerMask:
                    return new RenderingLayerMask();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static bool IsArrayOrList(Type type)
        {
            // Check if the type is an array
            if (type.IsArray)
            {
                return true;
            }

            // Check if the type is a List<> or a derived type
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        private static object GetDefaultValueForArrayElement(Type type)
        {
            var spt = TypeToSerializedPropertyType(type);
            return GetDefaultValue(spt, type);
        }
        #endregion
    }
}
