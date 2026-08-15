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
using Vapor.Serialization;
using FilePathAttribute = Vapor.Inspector.FilePathAttribute;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public class InspectorTreeProperty
    {
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

        internal const BindingFlags MemberSearchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Returned instead of null by the child accessors so callers can always foreach. Never mutated.
        /// </summary>
        private static readonly List<InspectorTreeProperty> s_NoProperties = new();

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

        private MemberInfo _arrayEntryNameMember;

        /// <summary>
        /// True when the element type carries [ArrayEntryName], so its label is derived from its contents
        /// and goes stale as soon as those are edited.
        /// </summary>
        public bool HasArrayEntryName => _arrayEntryNameMember != null;

        /// <summary>
        /// Re-resolves [ArrayEntryName] against the element's current value and returns the result,
        /// updating <see cref="DisplayName"/> to match.
        /// </summary>
        public string ResolveDisplayName()
        {
            if (_arrayEntryNameMember != null
                && ReflectionUtility.TryResolveMemberValue<string>(ArrayElementObject, _arrayEntryNameMember, null, out var resolved))
            {
                DisplayName = resolved;
            }

            return DisplayName;
        }


        // Array
        /// <summary>
        /// Taken from the materialized elements rather than off the live collection. The count field binds
        /// to this and is polled continuously, so reading it must not cost a reflection walk; the elements
        /// are rebuilt on every mutation, which is exactly when this needs to change.
        /// </summary>
        [CreateProperty]
        public int ArraySize
        {
            get
            {
                if (!IsArray)
                {
                    return 0;
                }

                EnsureChildProperties();
                return _arrayData?.Count ?? 0;
            }
        }

        private List<InspectorTreeProperty> _arrayData;
        public List<InspectorTreeProperty> ArrayData
        {
            get
            {
                EnsureChildProperties();
                return _arrayData ?? s_NoProperties;
            }
        }

        private ArrayReflectionHelper _arrayHelper;


        // Members
        // Children are materialized on first access rather than up front, so building the visual tree
        // is what drives property construction - one level at a time, and only for what is drawn.
        private bool _childrenBuilt;

        private List<InspectorTreeProperty> _fields;
        public List<InspectorTreeProperty> Fields
        {
            get
            {
                EnsureChildProperties();
                return _fields ?? s_NoProperties;
            }
        }

        private List<InspectorTreeProperty> _methods;
        public List<InspectorTreeProperty> Methods
        {
            get
            {
                EnsureChildProperties();
                return _methods ?? s_NoProperties;
            }
        }

        private List<InspectorTreeProperty> _properties;
        public List<InspectorTreeProperty> Properties
        {
            get
            {
                EnsureChildProperties();
                return _properties ?? s_NoProperties;
            }
        }


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
            if (IsArray)
            {
                // Type-level only, so it is safe to build before any value exists. Array mutation and
                // ArraySize both need it before the elements themselves are materialized.
                _arrayHelper = CreateArrayHelper(PropertyType);
            }
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
            HasCustomDrawer = !TypeHasAttribute<IgnoreCustomDrawerAttribute>() && !HasAttribute<IgnoreCustomDrawerAttribute>() && SerializedDrawerUtility.HasUsableCustomPropertyDrawer(PropertyType, SerializedPropertyType == SerializedPropertyType.ManagedReference, IsUnitySerializedProperty);
            NoChildProperties = HasIgnoreChildNodes();
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
            if (IsArray)
            {
                // Type-level only, so it is safe to build before any value exists. Array mutation and
                // ArraySize both need it before the elements themselves are materialized.
                _arrayHelper = CreateArrayHelper(PropertyType);
            }
            IsStruct = PropertyType.IsValueType && !PropertyType.IsPrimitive;

            // A [VslSerialize] property is drawn as an editable field, so it needs the same null
            // initialization a field gets - a null list or nested object gives the drawer nothing to
            // bind to. Deliberately not done for a [ShowInInspector] property: that one is computed and
            // read-only, and writing through its setter here could have side effects.
            if (ReflectionUtility.IsVslSerialized(propInfo) && (!IsUnitySerializedProperty || IsArray))
            {
                var currentValue = GetValueSafe(true);
                if (currentValue == null)
                {
                    // Without the rebuild: this runs while the property is still being constructed, so
                    // there is no element list to rebuild and nothing drawn to redraw - only a coroutine
                    // per null collection, scheduled every time the tree is built.
                    SetValueWithoutArrayRebuild(GetDefaultValue(SerializedPropertyType, PropertyType));
                }
            }

            if (IsStruct)
            {
                _cachedStructObject = GetValue(true);
            }

            HasCustomDrawer = !TypeHasAttribute<IgnoreCustomDrawerAttribute>() && !HasAttribute<IgnoreCustomDrawerAttribute>() && SerializedDrawerUtility.HasUsableCustomPropertyDrawer(PropertyType, SerializedPropertyType == SerializedPropertyType.ManagedReference, IsUnitySerializedProperty);
            NoChildProperties = HasIgnoreChildNodes();
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
                _arrayEntryNameMember = ReflectionUtility.GetMember(fieldType, atr.Resolver);
                ResolveDisplayName();
            }

            PropertyInfoType = MemberInfoType.Field;
            ParentType = parentProperty.PropertyType;
            HasParentProperty = true;

            IsUnitySerializedProperty = InspectorObject.IsUnityObject;
            SerializedPropertyType = TypeToSerializedPropertyType(PropertyType);
            SerializedPropertyNumericType = TypeToSerializedPropertyNumericType(PropertyType);
            IsArray = IsArrayOrList(PropertyType);
            if (IsArray)
            {
                // Type-level only, so it is safe to build before any value exists. Array mutation and
                // ArraySize both need it before the elements themselves are materialized.
                _arrayHelper = CreateArrayHelper(PropertyType);
            }
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
                               SerializedDrawerUtility.HasUsableCustomPropertyDrawer(PropertyType, SerializedPropertyType == SerializedPropertyType.ManagedReference, IsUnitySerializedProperty));
            NoChildProperties = HasIgnoreChildNodes();
        }

        private bool HasIgnoreChildNodes()
        {
            return TypeHasAttribute<IgnoreChildNodesAttribute>() || HasAttribute<IgnoreChildNodesAttribute>();
        }

        private static ArrayReflectionHelper CreateArrayHelper(Type propertyType)
        {
            return propertyType.IsArray
                ? new ArrayReflectionHelper(propertyType, propertyType.GetElementType(), false)
                : new ArrayReflectionHelper(propertyType, propertyType.GetGenericArguments()[0], true);
        }

        /// <summary>
        /// Builds this property's immediate children the first time they are asked for. Recursion happens
        /// naturally as the visual tree descends, so nothing is reflected over until it is about to be drawn.
        /// </summary>
        private void EnsureChildProperties()
        {
            if (_childrenBuilt)
            {
                return;
            }

            _childrenBuilt = true;
            BuildChildProperties();
        }

        public void BuildChildProperties()
        {
            // Arrays hold their children as elements rather than members.
            if (IsArray)
            {
                BuildListProperties();
                return;
            }

            // Nothing to recurse into for Unity.Objects (drawn as an ObjectField), types that are neither
            // [Serializable] nor VSL-authored, or anything explicitly opted out with [IgnoreChildNodes].
            if (IsUnityObjectOrSubclass() || !ReflectionUtility.IsExpandableType(PropertyType) || NoChildProperties)
            {
                return;
            }

            var typeStack = new Stack<Type>();
            for (var targetType = PropertyType; targetType != null; targetType = targetType.BaseType)
            {
                typeStack.Push(targetType);
            }

            // Gathered base-first and kept in member kind order, because equal draw orders are sorted
            // stably later on and this is what decides the default layout.
            var fieldInfos = new List<FieldInfo>();
            var propertyInfos = new List<PropertyInfo>();
            var methodInfos = new List<MethodInfo>();
            while (typeStack.TryPop(out var type))
            {
                fieldInfos.AddRange(type.GetFields(MemberSearchFlags));
                propertyInfos.AddRange(type.GetProperties(MemberSearchFlags));
                methodInfos.AddRange(type.GetMethods(MemberSearchFlags));
            }

            _fields = new List<InspectorTreeProperty>(fieldInfos.Count);
            foreach (var field in fieldInfos)
            {
                if (!ReflectionUtility.FieldSearchPredicate(field))
                {
                    continue;
                }

                var path = ChildPath(field.Name);
                var prop = new InspectorTreeProperty(InspectorObject, this, field, path);
                _fields.Add(prop);
                InspectorObject.AddToMap(path, prop);
            }

            _methods = new List<InspectorTreeProperty>();
            foreach (var method in methodInfos)
            {
                if (!ReflectionUtility.MethodSearchPredicate(method))
                {
                    continue;
                }

                var path = ChildPath(method.Name);
                var prop = new InspectorTreeProperty(InspectorObject, this, method, path);
                _methods.Add(prop);
                InspectorObject.AddToMap(path, prop);
            }

            _properties = new List<InspectorTreeProperty>();
            foreach (var property in propertyInfos)
            {
                // See InspectorTreeObject: a [VslSerialize] property is editable stored state, so it
                // joins the fields rather than the read-only computed rows.
                var editable = ReflectionUtility.IsVslSerialized(property);
                if (!editable && !ReflectionUtility.PropertySearchPredicate(property))
                {
                    continue;
                }

                var path = ChildPath(property.Name);
                var prop = new InspectorTreeProperty(InspectorObject, this, property, path);
                (editable ? _fields : _properties).Add(prop);
                InspectorObject.AddToMap(path, prop);
            }
        }

        private void BuildListProperties()
        {
            var arrayObject = GetValueSafe();
            if (arrayObject == null)
            {
                Debug.LogWarning($"{PropertyPath} has a non-initialized list, but it is serialized. This shouldn't happen.");
                _arrayData = new List<InspectorTreeProperty>();
                return;
            }

            var count = _arrayHelper.GetSize(arrayObject);
            _arrayData = new List<InspectorTreeProperty>(count);
            for (int i = 0; i < count; i++)
            {
                var element = _arrayHelper.GetElementObject(arrayObject, i);
                var path = $"{PropertyPath}.Array.data[{i}]";
                var prop = new InspectorTreeProperty(InspectorObject, this, _arrayHelper.ElementType, element, i, path);
                _arrayData.Add(prop);
                InspectorObject.AddToMap(path, prop);
            }
        }

        private string ChildPath(string memberName)
        {
            return PropertyPath.Length > 0 ? $"{PropertyPath}.{memberName}" : memberName;
        }

        public InspectorTreeProperty FindPropertyRelative(string relativePath)
        {
            //InspectorDebug.Log($"Trying To Field Property At {PropertyPath}.{relativePath}");
            return InspectorObject.FindProperty($"{PropertyPath}.{relativePath}");
        }


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
                        return CastToType(ArrayElementObject, PropertyType);
                    }

                    var parentObject = GetParentObject();
                    return CastToType(FieldInfo.GetValue(parentObject), PropertyType);
                case MemberInfoType.Method:
                    return CastToType(MethodInfo.Invoke(GetParentObject(), null), PropertyType);
                case MemberInfoType.Property:
                    return IsStruct && !ignoreCaching ? _cachedStructObject : CastToType(PropertyInfo.GetValue(GetParentObject()), PropertyType);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Converts a value to the type a property expects. The single implementation for the whole
        /// inspector - reading a value, committing an edit and validating one all go through here.
        /// </summary>
        public static object CastToType(object obj, Type targetType)
        {
            if (obj == null)
            {
                if (targetType.IsClass || targetType.IsInterface || Nullable.GetUnderlyingType(targetType) != null)
                {
                    return null;
                }

                throw new ArgumentNullException(nameof(obj), $"Cannot cast null to the non-nullable value type {targetType}.");
            }

            if (targetType.IsAssignableFrom(obj.GetType()))
            {
                return obj;
            }

            // A Type-valued property is edited as its assembly qualified name.
            if (targetType == typeof(Type) && obj is string assemblyQualifiedName)
            {
                return Type.GetType(assemblyQualifiedName);
            }

            if (targetType.IsEnum && obj.GetType().IsPrimitive)
            {
                // Converted rather than unboxed: the boxed type is whatever the widget produced, which is
                // rarely the enum's own underlying type.
                return Enum.ToObject(targetType, Convert.ToInt64(obj));
            }

            try
            {
                return Convert.ChangeType(obj, targetType);
            }
            catch (InvalidCastException)
            {
                throw new InvalidCastException($"Cannot cast object of type {obj.GetType()} to type {targetType}.");
            }
        }

        public object GetValueSafe(bool ignoreCaching = false)
        {
            var parentObj = GetParentObject();
            return parentObj == null ? null : GetValue(ignoreCaching);
        }

        public void SetValue<T>(T val)
        {
            SetValue(val, true);
        }

        /// <summary>
        /// Writes the value without asking the owning list to rebuild its elements.
        /// <para>
        /// Used when a struct member's edit is propagated back up into its container. The element snapshot
        /// is refreshed in place either way, so the rebuild adds nothing - and the redraw it schedules tears
        /// down the widget the user is still typing into, which cost you every character after the first.
        /// </para>
        /// </summary>
        public void SetValueWithoutArrayRebuild<T>(T val)
        {
            SetValue(val, false);
        }

        private void SetValue<T>(T val, bool rebuildArray)
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
                        ParentProperty.SetValueAtIndex(ElementIndex, val, rebuildArray);
                        ArrayElementObject = ParentProperty.GetValueAtIndex(ElementIndex);
                    }
                    else
                    {
                        var target = GetParentObject();
                        if (target != null)
                        {
                            if (IsStruct)
                            {
                                InspectorDebug.Log($"Setting Struct Value On {target.GetType()}");
                            }
                            FieldInfo.SetValue(target, val);
                        }
                    }
                    if (IsArray)
                    {
                        InspectorObject.ApplyModifiedProperties();
#if UNITY_EDITOR_COROUTINES
                        // Only when a rebuild was actually asked for. rebuildArray was already threaded
                        // into SetValueAtIndex above but never consulted here, so every caller of
                        // SetValueWithoutArrayRebuild still scheduled the rebuild it was avoiding - and
                        // an element of a struct type propagates its edit up through that path on every
                        // keystroke, stacking a coroutine and a full list redraw per character.
                        if (rebuildArray)
                        {
                            EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.SetValue), this);
                        }
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
                        // See the field case: honoured so SetValueWithoutArrayRebuild actually skips it.
                        if (rebuildArray)
                        {
                            EditorCoroutineUtility.StartCoroutine(DelayedBuildListProperties(ArrayRebuildReason.SetValue), this);
                        }
#endif
                    }

                    break;
                }
            }
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
        public void SetValueAtIndex<T>(int index, T val, bool rebuildArray = true)
        {
            if (!IsArray)
            {
                return;
            }

            if (!_arrayHelper.IsList)
            {
                if (GetValue() is Array array)
                {
                    var oldV = array.GetValue(index);
                    if (oldV == null && val == null)
                    {
                        return;
                    }
                    array.SetValue(val, index);
                }
            }
            else
            {
                if (GetValue() is IList list)
                {
                    var oldV = list[index];
                    if (oldV == null && val == null)
                    {
                        return;
                    }
                    list[index] = val;
                }
            }
            InspectorObject.ApplyModifiedProperties();
            if (!rebuildArray)
            {
                return;
            }
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
                    InspectorDebug.Log($"Set New Array {arr.Length}");
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
            InspectorDebug.Log(reason);
            // Marked built as well, so a later ArrayData access doesn't rebuild what we just rebuilt.
            _childrenBuilt = true;
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
        /// <summary>
        /// The attributes an array element inherits from the collection field that owns it.
        /// <para>
        /// Everything absent from this set describes the field as a single row - a tooltip, a suffix, an
        /// inline button, a group, a conditional - and handing it to every element as well drew it once for
        /// the list and again for each entry. What remains here describes the <i>value</i>, so it is meant
        /// to reach each element: a range clamps every entry, [ReadOnly] locks every entry, and so on.
        /// </para>
        /// <para>Add to this set to make a new attribute propagate into elements.</para>
        /// </summary>
        private static readonly HashSet<Type> s_ArrayElementInheritedAttributes = new()
        {
            typeof(RangeAttribute),
            typeof(SerializeReference),
            typeof(VslSerializeReferenceAttribute),
            typeof(ReadOnlyAttribute),
            typeof(HideLabelAttribute),
            typeof(LabelWidthAttribute),
            typeof(RequireInterfaceAttribute),
            typeof(ValidateInputAttribute),
            typeof(OnValueChangedAttribute),
            typeof(IgnoreCustomDrawerAttribute),
            typeof(IgnoreChildNodesAttribute),
            typeof(TypeSelectorAttribute),
            typeof(TypeResolverAttribute),
            typeof(AutoReferenceAttribute),
            typeof(ChildGameObjectsOnlyAttribute),
            typeof(FilePathAttribute),
            typeof(FolderPathAttribute),
        };

        private static bool InheritsToArrayElements(Type attributeType)
        {
            return s_ArrayElementInheritedAttributes.Contains(attributeType);
        }

        /// <summary>
        /// True when this member holds a chosen type rather than a fixed one, and so is drawn with a
        /// type picker.
        /// </summary>
        /// <remarks>
        /// Two attributes mean the same thing here. <see cref="SerializeReference"/> is Unity's, and
        /// only works on fields; <see cref="VslSerializeReferenceAttribute"/> is VSL's, and is what a
        /// property has to use, since Unity does not serialize properties at all. Every place that used
        /// to ask for the first one asks this instead, so a VSL-authored type gets the same treatment as
        /// a Unity-serialized one.
        /// </remarks>
        public bool IsManagedReference => HasAttribute<SerializeReference>() || HasAttribute<VslSerializeReferenceAttribute>();

        public bool HasAttribute<T>() where T : Attribute
        {
            return PropertyInfoType switch
            {
                MemberInfoType.Field => IsArrayElement
                    ? InheritsToArrayElements(typeof(T)) && ParentProperty.HasAttribute<T>()
                    : FieldInfo.IsDefined(typeof(T), true),
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
                        if (!InheritsToArrayElements(typeof(T)))
                        {
                            atr = null;
                            return false;
                        }

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
                        if (!InheritsToArrayElements(typeof(T)))
                        {
                            atr = null;
                            return false;
                        }

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

        /// <summary>
        /// True when a single widget draws the whole value, so there is nothing beneath it left to draw.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unity's value types are <see cref="SerializableAttribute"/> structs like any other, and the ones
        /// that expose their components as public fields - <see cref="Vector2"/>, <see cref="Vector3"/>,
        /// <see cref="Vector4"/>, <see cref="Color"/> - read to a reflected walk as things to descend into.
        /// The result was a Vector2Field with an X and a Y row underneath it, three widgets editing the
        /// same two floats.
        /// </para>
        /// <para>
        /// The exclusions are the cases where the widget is not the whole story.
        /// <see cref="SerializedPropertyType.Generic"/> is a collection, or a type with no mapping at all;
        /// <see cref="SerializedPropertyType.ManagedReference"/> is drawn <i>by</i> recursing into it, and
        /// is where every authored type lands; <see cref="SerializedPropertyType.Quaternion"/> and
        /// <see cref="SerializedPropertyType.ExposedReference"/> have no widget of their own, so their
        /// members are the only thing that draws them.
        /// </para>
        /// </remarks>
        public bool IsDrawnAsSingleField => SerializedPropertyType
            is not (SerializedPropertyType.Generic
                or SerializedPropertyType.ManagedReference
                or SerializedPropertyType.Quaternion
                or SerializedPropertyType.ExposedReference);

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
                    return new RectInt();
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

        public static bool IsArrayOrList(Type type)
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
