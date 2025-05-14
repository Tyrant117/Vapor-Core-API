#if UNITY_EDITOR_COROUTINES
using Unity.EditorCoroutines.Editor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Properties;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
using Vapor.Keys;
using FilePathAttribute = Vapor.Inspector.FilePathAttribute;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public delegate void ValueChangedHandler(TreePropertyField sender, object previous, object current);
    
    public class TreePropertyField : VisualElement
    {
        private static readonly Dictionary<Type, TypeCache.TypeCollection> s_CachedCollectionsByType = new ();
        private static readonly Dictionary<MethodInfo, IReadOnlyList<Type>> s_CachedCollectionsByMethod = new ();
        
        private static readonly Dictionary<Type, (List<string>, List<object>)> s_CachedComboBoxByType = new ();
        private static readonly Dictionary<int, (List<string>, List<object>)> s_CachedComboBoxByHashCode = new ();

        [InitializeOnLoadMethod]
        private static void ReloadCaches()
        {
            s_CachedComboBoxByType.Clear();
            s_CachedComboBoxByHashCode.Clear();
        }
        
        public bool IsValid { get; set; }
        public InspectorTreeProperty Property { get; }
        public InspectorTreeElement ParentTreeElement { get; }
        public Type PropertyType { get; }

        private Func<object, bool> _internalValidate = delegate { return true; };
        private Func<object, object> _internalProcessValidation = x => x;
        public Func<object, object> Validate;
        public event ValueChangedHandler ValueChanged = delegate { };

        private Label _internalLabel;
        private VisualElement _internalField;
        private DataBinding _fieldBinding;
        private readonly List<SerializedResolverContainer> _resolvers = new();
#if UNITY_EDITOR_COROUTINES
        private EditorCoroutine _resolverRoutine;
#endif

        public TreePropertyField(InspectorTreeProperty property, InspectorTreeElement parentTreeElement = null)
        {
            Property = property;
            PropertyType = property.PropertyType;
            ParentTreeElement = parentTreeElement;
            name = $"PropertyField:{Property.PropertyName}";
            DrawContent(parentTreeElement);
        }

        public TreePropertyField(InspectorTreeProperty property, Type overrideType, InspectorTreeElement parentTreeElement = null)
        {
            Property = property;
            PropertyType = overrideType;
            ParentTreeElement = parentTreeElement;
            DrawContent(parentTreeElement, true);
        }

        private void DrawContent(InspectorTreeElement parentTreeElement, bool overrideType = false)
        {
            name = $"PropertyField:{Property.PropertyName}";
            var field = DrawField(parentTreeElement, overrideType);
            if (field != null)
            {
                field.name = $"InputField:{Property.PropertyName}";
                _internalField = field;
                Add(_internalField);
                _internalLabel = this.Q<Label>();
                IsValid = true;


                DrawLabel();
                DrawLabelWidth();
                DrawHideLabel();
                DrawRichTooltip();
                DrawDecorators();
                DrawConditionals();
                DrawReadOnly();
                DrawPathSelection();
                DrawTitle();
                DrawInlineButtons();
                DrawSuffix();
                DrawAutoReference();
                DrawValidation();
                DrawRequireInterface();
                DrawChildGameObjectsOnly();
                DrawFlexBasis();

                RegisterCallbackOnce<DetachFromPanelEvent>(OnDetachedFromPanel);
            }
        }

        protected VisualElement DrawField(InspectorTreeElement parentTreeElement, bool overrideType = false)
        {
            //var source = Property.ParentObject;// Member.SourceObject.Object;
            var propertyType = Property.SerializedPropertyType;
            var numericType = Property.SerializedPropertyNumericType;
            if (overrideType)
            {
                propertyType = InspectorTreeProperty.TypeToSerializedPropertyType(PropertyType);
                numericType = InspectorTreeProperty.TypeToSerializedPropertyNumericType(PropertyType);
            }
            string niceName = Property.DisplayName;

            if (Property.HasCustomDrawer)
            {
                SerializedDrawerUtility.TryGetCustomPropertyDrawer(PropertyType, Property.SerializedPropertyType == SerializedPropertyType.ManagedReference, out var propertyDrawer);
                if (propertyDrawer is VaporPropertyDrawer vaporPropertyDrawer)
                {
                    var customElement = vaporPropertyDrawer.CreateVaporPropertyGUI(this);
                    if (customElement != null)
                    {
                        return customElement;
                    }
                }
                else if (Property.IsUnitySerializedProperty)
                {
                    var vaporProp = new VaporPropertyField(Property.InspectorObject.FindSerializedProperty(Property.PropertyPath))
                    {
                        bindingPath = Property.PropertyPath
                    };
                    return vaporProp;
                }
                else
                {

                    Debug.LogError($"Trying to draw {PropertyType} with a custom drawer at {Property.PropertyPath}, but drawer does not implement type:{nameof(VaporPropertyDrawer)}");
                    return null;
                }
            }

            if(!Property.IsArray && Property.HasAttribute<SerializeReference>())
            {
                var foldout = new StyledFoldout(niceName);

                List<string> keys = new();
                List<object> values = new();
                keys.Add("Null");
                values.Add(null);
                var types = ReflectionUtility.GetAssignableTypesOf(PropertyType).Select(t => new DropdownModel(t.Namespace, t.Name, t));
                SplitTupleToDropdown(keys, values, types);

                var current = Property.GetValue();
                var cIdx = current == null ? 0 : Mathf.Max(0, values.IndexOf(current.GetType()));
                var comboBox = new ComboBox<object>(niceName, cIdx, keys, values, false);                

                comboBox.SelectionChanged += OnSerializeReferenceSelectionChanged;
                foldout.Add(comboBox);
                if (cIdx > 0)
                {
                    InspectorTreeObject ito = new InspectorTreeObject(current, current.GetType()).WithParent(Property.InspectorObject);
                    InspectorTreeRootElement subRoot = new(ito);
                    subRoot.DrawToScreen(foldout);
                }
                return foldout;
            }

            if (Property.TryGetAttribute<DropdownAttribute>(out var dropdownAttribute) && !Property.IsArrayElement)
            {
                List<string> keys = new();
                List<object> values = new();

                switch (dropdownAttribute.Filter)
                {
                    case 0:
                        var mi = ReflectionUtility.GetMember(Property.ParentType, dropdownAttribute.Resolver);
                        if (ReflectionUtility.TryResolveMemberValue<IEnumerable<DropdownModel>>(Property.GetParentObject(), mi, null, out var convert))
                        {
                            SplitTupleToDropdown(keys, values, convert);
                        }
                        else
                        {
                            Debug.LogError($"Could Not Resolve IEnumerable<DropdownModel> at Property: {Property.InspectorObject.Type.Name} Resolver: {dropdownAttribute.Resolver}");
                        }
                        break;
                    case 1:
                        {
                           
                            // var keyUtilityType = Type.GetType("Vapor.Keys.KeyUtility, vapor.core.runtime, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
                            // if(keyUtilityType == null)
                            // {
                                // Debug.LogError("To resolve by category or type name, Vapor.Keys must be included in the project");
                            // }
                            // MethodInfo methodInfo = keyUtilityType.GetMethod("GetAllKeysFromCategory", BindingFlags.Public | BindingFlags.Static);
                            SplitTupleToDropdown(keys, values, KeyUtility.GetAllKeysFromCategory(dropdownAttribute.Resolver));
                        }
                        break;
                    case 2:
                        {
                            // var keyUtilityType = Type.GetType("Vapor.Keys.KeyUtility, vapor.core.runtime, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
                            // if (keyUtilityType == null)
                            // {
                                // Debug.LogError("To resolve by category or type name, Vapor.Keys must be included in the project");
                            // }
                            // MethodInfo methodInfo = keyUtilityType.GetMethod("GetAllKeysFromTypeName", BindingFlags.Public | BindingFlags.Static);
                            SplitTupleToDropdown(keys, values, KeyUtility.GetAllKeysFromTypeName(dropdownAttribute.Resolver));
                        }
                        break;
                }

                //Debug.Log($"Building Property: {Property.PropertyName} | IsArray: {Property.IsArray} | Options: {keys.Count}");
                if (Property.IsArray)
                {
                    if (dropdownAttribute.MultiSelectArray)
                    {
                        var comboBox = new ComboBox<object>(niceName, -1, keys, values, true, categorySplitCharacter: dropdownAttribute.CategorySplitCharacter);
                        List<int> selectedIdx = new();
                        foreach (var elem in Property.ArrayData)
                        {
                            int idx = values.IndexOf(elem.GetValue());
                            if (idx != -1)
                            {
                                selectedIdx.Add(idx);
                            }
                        }
                        List<string> selectedNames = new(selectedIdx.Count);
                        foreach (var idx in selectedIdx)
                        {
                            selectedNames.Add(keys[idx]);
                        }

                        comboBox.Select(selectedNames);
                        comboBox.SelectionChanged += OnComboBoxSelectionChanged;
                        return comboBox;
                    }
                }
                else
                {
                    var current = Property.GetValue();
                    var cIdx = Mathf.Max(0, values.IndexOf(current));
                    var comboBox = new ComboBox<object>(niceName, cIdx, keys, values, false, categorySplitCharacter: dropdownAttribute.CategorySplitCharacter);

                    comboBox.SelectionChanged += OnComboBoxSelectionChanged;
                    return comboBox;
                }
            }

            switch (propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (parentTreeElement == null)
                    {
                        return null;
                    }
                    return Property.IsArray ? new PaginatedList(parentTreeElement, Property, niceName) : (VisualElement)null;
                case SerializedPropertyType.Integer:
                    switch (numericType)
                    {
                        case SerializedPropertyNumericType.Int32:
                            {
                                if (Property.TryGetAttribute<RangeAttribute>(out var rangeAttribute))
                                {
                                    var field = new SliderInt((int)rangeAttribute.min, (int)rangeAttribute.max)
                                    {
                                        label = niceName,
                                        showInputField = true,
                                    };
                                    StyleLabel(field);

                                    SetupDefaultBinding(field);
                                    field.SetValueWithoutNotify(Property.GetValue<int>());
                                    field.RegisterValueChangedCallback(OnIntChanged);
                                    return field;
                                }
                                else
                                {
                                    var field = new IntegerField()
                                    {
                                        label = niceName,
                                        isDelayed = true,
                                    };
                                    StyleLabel(field);

                                    SetupDefaultBinding(field);

                                    field.SetValueWithoutNotify(Property.GetValue<int>());
                                    field.RegisterValueChangedCallback(OnIntChanged);
                                    return field;
                                }
                            }
                        case SerializedPropertyNumericType.UInt32:
                            {
                                var field = new UnsignedIntegerField()
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);
                                field.SetValueWithoutNotify(Property.GetValue<uint>());
                                field.RegisterValueChangedCallback(OnUIntChanged);
                                return field;
                            }
                        case SerializedPropertyNumericType.Int64:
                            {
                                var field = new LongField()
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);
                                field.SetValueWithoutNotify(Property.GetValue<long>());
                                field.RegisterValueChangedCallback(OnLongChanged);
                                return field;
                            }
                        case SerializedPropertyNumericType.UInt64:
                            {
                                var field = new UnsignedLongField()
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);
                                field.SetValueWithoutNotify(Property.GetValue<ulong>());
                                field.RegisterValueChangedCallback(OnULongChanged);
                                return field;
                            }

                        case SerializedPropertyNumericType.Int8:
                            {
                                var field = new IntegerField()
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);
                                _fieldBinding.sourceToUiConverters.AddConverter<sbyte,int>((ref sbyte value) => value);
                                field.SetValueWithoutNotify(Property.GetValue<sbyte>());
                                field.RegisterValueChangedCallback(OnTinyNumericChanged);
                                return field;
                            }
                        case SerializedPropertyNumericType.UInt8:
                            {
                                var field = new IntegerField()
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);
                                _fieldBinding.sourceToUiConverters.AddConverter<byte,int>((ref byte value) => value);
                                field.SetValueWithoutNotify(Property.GetValue<byte>());
                                field.RegisterValueChangedCallback(OnTinyNumericChanged);
                                return field;
                            }
                        case SerializedPropertyNumericType.Int16:
                            {
                                var field = new IntegerField()
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);

                                field.SetValueWithoutNotify(Property.GetValue<short>());
                                field.RegisterValueChangedCallback(OnTinyNumericChanged);
                                return field;
                            }
                        case SerializedPropertyNumericType.UInt16:
                            {
                                var field = new IntegerField()
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);

                                field.SetValueWithoutNotify(Property.GetValue<ushort>());
                                field.RegisterValueChangedCallback(OnTinyNumericChanged);
                                return field;
                            }
                        default:
                            {
                                Debug.LogError($"Invalid Numeric Type: {numericType} at {Property.PropertyPath}. Use Integer or Larger");
                                return null;
                            }
                    }
                case SerializedPropertyType.Boolean:
                    {
                        var field = new Toggle
                        {
                            label = niceName,
                            toggleOnLabelClick = false,
                        };
                        field.RegisterCallback<GeometryChangedEvent>(evt =>
                        {
                            var ve = (VisualElement)evt.target;
                            ve.hierarchy[0].style.width = this.layout.width * 0.33f;
                            ve.hierarchy[1].style.width = this.layout.width * 0.67f;
                        });
                        var label = field.Q<Label>();
                        label.style.flexGrow = 1f;
                        label.style.flexShrink = 1f;
                        label.style.overflow = Overflow.Hidden;
                        label.style.textOverflow = TextOverflow.Ellipsis;
                        label.style.unityTextAlign = TextAnchor.MiddleLeft;

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<bool>());
                        field.RegisterValueChangedCallback(OnBoolChanged);
                        return field;
                    }
                case SerializedPropertyType.Float:
                    switch (numericType)
                    {
                        case SerializedPropertyNumericType.Double:
                            {
                                var field = new DoubleField
                                {
                                    label = niceName,
                                    isDelayed = true,
                                };
                                StyleLabel(field);

                                SetupDefaultBinding(field);
                                field.SetValueWithoutNotify(Property.GetValue<double>());
                                field.RegisterValueChangedCallback(OnDoubleChanged);
                                return field;
                            }

                        default:
                            {
                                if (Property.TryGetAttribute<RangeAttribute>(out var rangeAttribute))
                                {
                                    var field = new Slider(rangeAttribute.min, rangeAttribute.max)
                                    {
                                        label = niceName,
                                        showInputField = true,
                                    };
                                    StyleLabel(field);

                                    SetupDefaultBinding(field);
                                    field.SetValueWithoutNotify(Property.GetValue<float>());
                                    field.RegisterValueChangedCallback(OnFloatChanged);
                                    return field;
                                }
                                else
                                {
                                    var field = new FloatField()
                                    {
                                        label = niceName,
                                        isDelayed = true,
                                    };
                                    StyleLabel(field);

                                    SetupDefaultBinding(field);
                                    field.SetValueWithoutNotify(Property.GetValue<float>());
                                    field.RegisterValueChangedCallback(OnFloatChanged);
                                    return field;
                                }
                            }
                    }
                case SerializedPropertyType.String:
                    {
                        if(Property.TryGetAttributes<TypeSelectorAttribute>(out var tsAtrs))
                        {
                            var current = Property.GetValue<string>();
                            var cType = current.EmptyOrNull() ? null : Type.GetType(current);
                            List<Type> types = new(tsAtrs.Length);
                            foreach (var atr in tsAtrs)
                            {
                                if (atr.AllTypes)
                                {
                                    var allTypeSelector = new TypeSelectorField(niceName, cType);
                                    allTypeSelector.AssemblyQualifiedNameChanged += OnNameSelectionChanged;
                                    return allTypeSelector;
                                }
                                
                                switch (atr.Selection)
                                {
                                    case TypeSelectorAttribute.T.Subclass:
                                    case TypeSelectorAttribute.T.Attribute:
                                        types.Add(atr.Type);
                                        break;
                                    case TypeSelectorAttribute.T.Resolver:
                                        var pType = Property.IsArrayElement ? Property.ParentProperty.ParentType : Property.ParentType;
                                        var method = ReflectionUtility.GetMethod(pType, atr.Resolver);
                                        if (method.IsStatic)
                                        {
                                            types.AddRange((IEnumerable<Type>)method.Invoke(null, null));
                                        }
                                        else
                                        {
                                            types.AddRange((IEnumerable<Type>)method.Invoke(Property.IsArrayElement ? Property.ParentProperty.GetParentObject() : Property.GetParentObject(), null));
                                        }
                                        break;
                                }
                            }
                            
                            var typeSelector = new TypeSelectorField(niceName, cType).WithValidTypes(tsAtrs[0].IncludeAbstract, types.ToArray());
                            typeSelector.AssemblyQualifiedNameChanged += OnNameSelectionChanged;
                            return typeSelector;

                            // List<string> keys = new();
                            // List<object> values = new();
                            // foreach (var atr in tsAtrs)
                            // {
                            //     switch (atr.Selection)
                            //     {
                            //         case TypeSelectorAttribute.T.Subclass:
                            //         {
                            //             if (s_CachedComboBoxByType.TryGetValue(atr.Type, out var cachedComboBox))
                            //             {
                            //                 keys = cachedComboBox.Item1;
                            //                 values = cachedComboBox.Item2;
                            //             }
                            //             else
                            //             {
                            //                 var ts = TypeCache.GetTypesDerivedFrom(atr.Type);
                            //                 List<string> ks = new(ts.Count);
                            //                 List<object> vs = new(ts.Count);
                            //                 foreach (var t in ts)
                            //                 {
                            //                     ks.Add(t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>" : t.Name);
                            //                     vs.Add(t.AssemblyQualifiedName);
                            //                 }
                            //
                            //                 s_CachedComboBoxByType.TryAdd(atr.Type, (ks, vs));
                            //                 keys = ks;
                            //                 values = vs;
                            //             }
                            //
                            //             break;
                            //         }
                            //         case TypeSelectorAttribute.T.Attribute:
                            //         {
                            //             if (s_CachedComboBoxByType.TryGetValue(atr.Type, out var cachedComboBox))
                            //             {
                            //                 keys = cachedComboBox.Item1;
                            //                 values = cachedComboBox.Item2;
                            //             }
                            //             else
                            //             {
                            //                 var ts = TypeCache.GetTypesDerivedFrom(atr.Type);
                            //                 List<string> ks = new(ts.Count);
                            //                 List<object> vs = new(ts.Count);
                            //                 foreach (var t in ts)
                            //                 {
                            //                     ks.Add(t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>" : t.Name);
                            //                     vs.Add(t.AssemblyQualifiedName);
                            //                 }
                            //
                            //                 s_CachedComboBoxByType.TryAdd(atr.Type, (ks, vs));
                            //                 keys = ks;
                            //                 values = vs;
                            //             }
                            //
                            //             break;
                            //         }
                            //         case TypeSelectorAttribute.T.Resolver:
                            //         {
                            //             var pType = Property.IsArrayElement ? Property.ParentProperty.ParentType : Property.ParentType;
                            //             var method = ReflectionUtility.GetMethod(pType, atr.Resolver);
                            //             var methodPropHash = HashCode.Combine(pType, method);
                            //             if (s_CachedComboBoxByHashCode.TryGetValue(methodPropHash, out var cachedComboBox))
                            //             {
                            //                 keys = cachedComboBox.Item1;
                            //                 values = cachedComboBox.Item2;
                            //             }
                            //             else
                            //             {
                            //                 if (method.IsStatic)
                            //                 {
                            //                     var ts = ((IEnumerable<Type>)method.Invoke(null, null)).ToArray();
                            //                     List<string> ks = new(ts.Length);
                            //                     List<object> vs = new(ts.Length);
                            //                     foreach (var t in ts)
                            //                     {
                            //                         ks.Add(t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>" : t.Name);
                            //                         vs.Add(t.AssemblyQualifiedName);
                            //                     }
                            //
                            //                     s_CachedComboBoxByHashCode.TryAdd(methodPropHash, (ks, vs));
                            //                     keys = ks;
                            //                     values = vs;
                            //                 }
                            //                 else
                            //                 {
                            //                     var ts = ((IEnumerable<Type>)method.Invoke(Property.IsArrayElement ? Property.ParentProperty.GetParentObject() : Property.GetParentObject(), null)).ToArray();
                            //                     List<string> ks = new(ts.Length);
                            //                     List<object> vs = new(ts.Length);
                            //                     foreach (var t in ts)
                            //                     {
                            //                         ks.Add(t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>" : t.Name);
                            //                         vs.Add(t.AssemblyQualifiedName);
                            //                     }
                            //
                            //                     s_CachedComboBoxByHashCode.TryAdd(methodPropHash, (ks, vs));
                            //                     keys = ks;
                            //                     values = vs;
                            //                 }
                            //             }
                            //
                            //             break;
                            //         }
                            //     }
                            // }
                            //
                            // var current = Property.GetValue();
                            // var cIdx = Mathf.Max(0, values.IndexOf(current));
                            // var comboBox = new ComboBox(niceName, cIdx, keys, values, false, true);
                            //
                            // comboBox.SelectionChanged += OnComboBoxSelectionChanged;
                            // return comboBox;
                        }

                        bool delayed = Property.IsArrayElement;
                        if (Property.TryGetAttribute<OnValueChangedAttribute>(out var ovcAtr))
                        {
                            delayed = Property.IsArray || ovcAtr.Delayed;
                        }

                        var field = new TextField()
                        {
                            label = niceName,
                            isDelayed = delayed,
                            maxLength = -1,
                        };
                        StyleLabel(field);

                        if (Property.TryGetAttribute<TextAreaAttribute>(out var multilineAtr))
                        {
                            field.multiline = true;
                            field.verticalScrollerVisibility = ScrollerVisibility.Auto;
                            field.style.minHeight = 14 * multilineAtr.minLines;
                            field.style.height = new StyleLength(StyleKeyword.Auto);
                            field.style.maxHeight = 14 * multilineAtr.maxLines;
                        }

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<string>());
                        field.RegisterValueChangedCallback(OnStringChanged);
                        return field;
                    }
                case SerializedPropertyType.Color:
                    {
                        var field = new ColorField()
                        {
                            label = niceName,
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Color>());
                        field.RegisterValueChangedCallback(OnColorChanged);
                        return field;
                    }
                case SerializedPropertyType.ObjectReference:
                    {
                        var field = new ObjectField()
                        {
                            label = niceName,
                            objectType = PropertyType,
                        };
                        StyleLabel(field);
                        
                        SetupDefaultBinding(field);
                        _fieldBinding.sourceToUiConverters.AddConverter<object, Object>((ref object obj) => (Object)obj);
                        field.SetValueWithoutNotify(Property.GetValue<Object>());
                        field.RegisterValueChangedCallback(OnObjectChanged);
                        return field;
                    }
                case SerializedPropertyType.LayerMask:
                    {
                        var field = new LayerMaskField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        _fieldBinding.sourceToUiConverters.AddConverter((ref LayerMask mask) => mask.value);
                        field.SetValueWithoutNotify(Property.GetValue<LayerMask>());
                        field.RegisterValueChangedCallback(OnLayerMaskChanged);
                        return field;
                    }
                case SerializedPropertyType.Enum:
                    {
                        //Debug.Log($"Is Enum: {Property.PropertyName}");
                        if (Property.TypeHasAttribute<FlagsAttribute>())
                        {
                            var field = new EnumFlagsField(Property.GetValue<Enum>())
                            {
                                label = niceName,
                            };
                            StyleLabel(field);

                            SetupDefaultBinding(field);
                            field.RegisterValueChangedCallback(OnEnumChanged);
                            return field;
                        }
                        else
                        {
                            var field = new EnumField(Property.GetValue<Enum>())
                            {
                                label = niceName
                            };
                            StyleLabel(field);

                            SetupDefaultBinding(field);
                            field.RegisterValueChangedCallback(OnEnumChanged);
                            return field;
                        }
                    }
                case SerializedPropertyType.Vector2:
                    {
                        var field = new Vector2Field()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Vector2>());
                        field.RegisterValueChangedCallback(OnVector2Changed);
                        return field;
                    }
                case SerializedPropertyType.Vector3:
                    {
                        var field = new Vector3Field()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        Debug.Log(Property.GetValue<Vector3>());
                        field.SetValueWithoutNotify(Property.GetValue<Vector3>());
                        field.RegisterValueChangedCallback(OnVector3Changed);
                        return field;
                    }
                case SerializedPropertyType.Vector4:
                    {
                        var field = new Vector4Field()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Vector4>());
                        field.RegisterValueChangedCallback(OnVector4Changed);
                        return field;
                    }
                case SerializedPropertyType.Rect:
                    {
                        var field = new RectField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Rect>());
                        field.RegisterValueChangedCallback(OnRectChanged);
                        return field;
                    }
                case SerializedPropertyType.ArraySize:
                    {
                        var field = new IntegerField()
                        {
                            isDelayed = true,
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<int>());
                        field.RegisterValueChangedCallback(OnIntChanged);
                        return field;
                    }
                case SerializedPropertyType.Character:
                    {
                        var field = new TextField()
                        {
                            maxLength = 1,
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<char>().ToString());
                        field.RegisterValueChangedCallback(OnCharChanged);
                        return field;
                    }
                case SerializedPropertyType.AnimationCurve:
                    {
                        var field = new CurveField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        //SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<AnimationCurve>());
                        field.RegisterValueChangedCallback(OnAnimationCurveChanged);
                        return field;
                    }
                case SerializedPropertyType.Bounds:
                    {
                        var field = new BoundsField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Bounds>());
                        field.RegisterValueChangedCallback(OnBoundsChanged);
                        return field;
                    }
                case SerializedPropertyType.Gradient:
                    {
                        var field = new GradientField()
                        {
                            label = niceName,
                        };
                        StyleLabel(field);

                        field.SetValueWithoutNotify(Property.GetValue<Gradient>());
                        field.RegisterValueChangedCallback(OnGradientChanged);
                        return field;
                    }
                case SerializedPropertyType.Quaternion:
                    return null;
                case SerializedPropertyType.ExposedReference:
                    return null;
                case SerializedPropertyType.FixedBufferSize:
                    {
                        var field = new IntegerField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<int>());
                        field.RegisterValueChangedCallback(OnIntChanged);
                        return field;
                    }
                case SerializedPropertyType.Vector2Int:
                    {
                        var field = new Vector2IntField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Vector2Int>());
                        field.RegisterValueChangedCallback(OnVector2IntChanged);
                        return field;
                    }
                case SerializedPropertyType.Vector3Int:
                    {
                        var field = new Vector3IntField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Vector3Int>());
                        field.RegisterValueChangedCallback(OnVector3IntChanged);
                        return field;
                    }
                case SerializedPropertyType.RectInt:
                    {
                        var field = new RectIntField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<RectInt>());
                        field.RegisterValueChangedCallback(OnRectIntChanged);
                        return field;
                    }
                case SerializedPropertyType.BoundsInt:
                    {
                        var field = new BoundsIntField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<BoundsInt>());
                        field.RegisterValueChangedCallback(OnBoundsIntChanged);
                        return field;
                    }
                case SerializedPropertyType.ManagedReference:
                    return new Label(Property.PropertyName);
                case SerializedPropertyType.Hash128:
                    {
                        var field = new Hash128Field()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<Hash128>());
                        field.RegisterValueChangedCallback(OnHash128Changed);
                        return field;
                    }

                case SerializedPropertyType.RenderingLayerMask:
                    {
                        var field = new RenderingLayerMaskField()
                        {
                            label = niceName
                        };
                        StyleLabel(field);

                        SetupDefaultBinding(field);
                        _fieldBinding.sourceToUiConverters.AddConverter((ref RenderingLayerMask mask) => mask.value);
                        field.SetValueWithoutNotify(Property.GetValue<RenderingLayerMask>());
                        field.RegisterValueChangedCallback(OnRenderingLayerMaskChanged);
                        return field;
                    }
                default:
                    return new Label($"Undefined Type: {PropertyType}");
            }
        }

        private void SetupDefaultBinding(VisualElement field)
        {
            //Debug.Log($"Binding: {Property.GetParentObject()} - {Property.PropertyName}");
            _fieldBinding = new DataBinding
            {
                dataSource = Property.GetParentObject(),
                dataSourcePath = new PropertyPath(Property.PropertyName),
                bindingMode = BindingMode.ToTarget,
                updateTrigger = BindingUpdateTrigger.OnSourceChanged
            };
            field.SetBinding("value", _fieldBinding);
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            _resolvers.Clear();
#if UNITY_EDITOR_COROUTINES
            if (_resolverRoutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(_resolverRoutine);
            }
#endif
        }

        #region - Change Callbacks -
        public void MarkDirtyWithValue(object previousValue, object newValue)
        {
            if (Property.InspectorObject.IsUnityObject)
            {
                if ((Object)Property.InspectorObject)
                {
                    Undo.RecordObject(Property.InspectorObject, name);
                }
            }
            Property.SetValue(newValue);
            if (Property.ParentIsStruct())
            {
                Debug.Log("Propagating Struct Data Up");
                if (_fieldBinding != null)
                {
                    Property.ParentProperty.SetValue(_fieldBinding.dataSource);
                    _fieldBinding.dataSource = Property.GetParentObject();
                }
                else
                {
                    Property.ParentProperty.SetValue(Property.GetParentObject());
                }

                var property = Property;
                while (true)
                {
                    if (property.ParentProperty?.ParentIsStruct() == true)
                    {
                        property.ParentProperty.ParentProperty.SetValue(property.ParentProperty.GetParentObject());
                        property = property.ParentProperty;
                        continue;
                    }

                    break;
                }
                
                // _UpdateParentProperty(Property);
            }

            using var evt = TreePropertyChangedEvent.GetPooled();
            evt.target = this;
            evt.Previous = previousValue;
            evt.Current = newValue;
            SendEvent(evt);
            Property.InspectorObject.ApplyModifiedProperties();

            // void _UpdateParentProperty(InspectorTreeProperty property)
            // {
            //     while (true)
            //     {
            //         if (property.ParentProperty?.ParentIsStruct() == true)
            //         {
            //             property.ParentProperty.ParentProperty.SetValue(property.ParentProperty.GetParentObject());
            //             property = property.ParentProperty;
            //             continue;
            //         }
            //
            //         break;
            //     }
            // }
        }

        private object ProcessValidation(object newValue)
        {
            return !_internalValidate.Invoke(newValue) ? _internalProcessValidation.Invoke(newValue) : newValue;
        }

        private void OnTinyNumericChanged(ChangeEvent<int> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                validatedValue = CastToType(validatedValue, PropertyType);
                Debug.Log($"On Tiny Numeric Changed {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnIntChanged(ChangeEvent<int> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"On Int Changed {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnUIntChanged(ChangeEvent<uint> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnUIntChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnLongChanged(ChangeEvent<long> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnLongChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnULongChanged(ChangeEvent<ulong> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnULongChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnBoolChanged(ChangeEvent<bool> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnBoolChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnDoubleChanged(ChangeEvent<double> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnDoubleChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnFloatChanged(ChangeEvent<float> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnFloatChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnColorChanged(ChangeEvent<Color> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnColorChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnObjectChanged(ChangeEvent<Object> evt)
        {
            if (ReferenceEquals(evt.previousValue, evt.newValue))
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                try
                {
                    Debug.Log($"OnObjectChanged {evt.previousValue} -> {validatedValue}");
                }
                catch (Exception e)
                {
                    // ignored
                }

                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnEnumChanged(ChangeEvent<Enum> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"On EnumChanged Changed {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnVector2Changed(ChangeEvent<Vector2> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnVector2Changed {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnVector3Changed(ChangeEvent<Vector3> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnVector3Changed {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnVector4Changed(ChangeEvent<Vector4> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnVector4Changed {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnRectChanged(ChangeEvent<Rect> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnRectChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnCharChanged(ChangeEvent<string> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnCharChanged {evt.previousValue} -> {validatedValue}");
                var c = ((string)validatedValue).FirstOrDefault();
                MarkDirtyWithValue(evt.previousValue, c);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                var c = ((string)validatedValue).FirstOrDefault();
                MarkDirtyWithValue(evt.previousValue, c);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnStringChanged(ChangeEvent<string> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnStringChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnAnimationCurveChanged(ChangeEvent<AnimationCurve> evt)
        {
            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnAnimationCurveChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnBoundsChanged(ChangeEvent<Bounds> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnBoundsChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnGradientChanged(ChangeEvent<Gradient> evt)
        {
            //if (evt.previousValue.Equals(evt.newValue))
            //{
            //return;
            //}

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnGradientChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnVector2IntChanged(ChangeEvent<Vector2Int> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnVector2IntChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnVector3IntChanged(ChangeEvent<Vector3Int> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnVector3IntChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnRectIntChanged(ChangeEvent<RectInt> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnRectIntChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnBoundsIntChanged(ChangeEvent<BoundsInt> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnBoundsIntChanged {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnHash128Changed(ChangeEvent<Hash128> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnHash128Changed {evt.previousValue} -> {validatedValue}");
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(evt.previousValue, validatedValue);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnLayerMaskChanged(ChangeEvent<int> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnLayerMaskChanged {evt.previousValue} -> {validatedValue}");
                LayerMask mask = new()
                {
                    value = (int)validatedValue
                };
                MarkDirtyWithValue(evt.previousValue, mask);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                LayerMask mask = new()
                {
                    value = (int)validatedValue
                };
                MarkDirtyWithValue(evt.previousValue, mask);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        private void OnRenderingLayerMaskChanged(ChangeEvent<uint> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            var validatedValue = ProcessValidation(evt.newValue);
            if (Validate == null)
            {
                Debug.Log($"OnRenderingLayerMaskChanged {evt.previousValue} -> {validatedValue}");
                RenderingLayerMask mask = new()
                {
                    value = (uint)validatedValue
                };
                MarkDirtyWithValue(evt.previousValue, mask);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                RenderingLayerMask mask = new()
                {
                    value = (uint)validatedValue
                };
                MarkDirtyWithValue(evt.previousValue, mask);
                ValueChanged.Invoke(this, evt.previousValue, validatedValue);
            }
        }

        public void OnComboBoxSelectionChanged(ComboBox<object> comboBox, List<int> selectedIndices)
        {
            IList selection = (IList)Property.CreateGenericListOfType();
            foreach (var idx in selectedIndices)
            {
                selection.Add(comboBox.Values[idx]);
            }
            //Debug.Log($"OnComboBoxSelectionChanged - New Selections: {selection.Count}");

            bool allMatching = true;
            if (Property.IsArray)
            {
                if (selection.Count != Property.ArraySize)
                {
                    //Debug.Log($"OnComboBoxSelectionChanged {selection.Count} != {Property.ArraySize}");
                    allMatching = false;
                }
                else
                {
                    int matching = 0;
                    foreach (var prop in Property.ArrayData)
                    {
                        var prev = prop.GetValue();
                        foreach (var sel in selection)
                        {
                            if (sel.Equals(prev))
                            {
                                matching++;
                                break;
                            }
                        }
                    }
                    //Debug.Log($"Matcing {matching}");
                    if (matching != selection.Count)
                    {
                        allMatching = false;
                    }
                }
            }
            else
            {
                var previousValue = Property.GetValue();
                foreach (var item in selection)
                {
                    if (previousValue == null || !previousValue.Equals(item))
                    {
                        allMatching = false;
                        break;
                    }
                }
            }

            if (allMatching)
            {
                return;
            }

            var validatedValue = ProcessValidation(Property.IsArray ? selection : selection[0]);
            if (Validate == null)
            {
                Debug.Log($"OnComboBoxSelectionChanged {Property.GetValue()} -> {validatedValue}");
                MarkDirtyWithValue(Property.GetValue(), validatedValue);
                ValueChanged.Invoke(this, Property.GetValue(), validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(Property.GetValue(), validatedValue);
                ValueChanged.Invoke(this, Property.GetValue(), validatedValue);
            }
        }

        public void OnNameSelectionChanged(VisualElement sender, string oldAqn, string newAqn)
        {
            var validatedValue = ProcessValidation(Property.IsArray ? newAqn : newAqn);
            if (Validate == null)
            {
                Debug.Log($"OnTypeSelectionChanged {Property.GetValue()} -> {validatedValue}");
                MarkDirtyWithValue(Property.GetValue(), validatedValue);
                ValueChanged.Invoke(this, oldAqn, validatedValue);
            }
            else
            {
                validatedValue = Validate.Invoke(validatedValue);
                validatedValue = CastToType(validatedValue, PropertyType);
                MarkDirtyWithValue(Property.GetValue(), validatedValue);
                ValueChanged.Invoke(this, oldAqn, validatedValue);
            }
        }
        
        public void OnSerializeReferenceSelectionChanged(ComboBox<object> comboBox, List<int> selectedIndices)
        {
            List<Type> selection = new();
            foreach (var idx in selectedIndices)
            {
                selection.Add((Type)comboBox.Values[idx]);
            }
            //Debug.Log($"OnComboBoxSelectionChanged - New Selections: {selection.Count}");

            bool allMatching = true;
            if (Property.IsArray)
            {
                if (selection.Count != Property.ArraySize)
                {
                    //Debug.Log($"OnComboBoxSelectionChanged {selection.Count} != {Property.ArraySize}");
                    allMatching = false;
                }
                else
                {
                    int matching = 0;
                    foreach (var prop in Property.ArrayData)
                    {
                        var prev = prop.GetValue();
                        foreach (var sel in selection)
                        {
                            if (sel.Equals(prev))
                            {
                                matching++;
                                break;
                            }
                        }
                    }
                    //Debug.Log($"Matcing {matching}");
                    if (matching != selection.Count)
                    {
                        allMatching = false;
                    }
                }
            }
            else
            {
                var previousValue = Property.GetValue();
                if(previousValue != null)
                {
                    previousValue = previousValue.GetType();
                }
                else
                {
                    previousValue = null;
                }
                foreach (var item in selection)
                {
                    if (previousValue == null && item != null)
                    {
                        allMatching = false;
                        break;
                    }

                    if (item == null && previousValue != null)
                    {
                        allMatching = false;
                        break;
                    }

                    if (item == null && previousValue == null)
                    {
                        allMatching = true;
                        break;
                    }

                    if (!item.Equals(previousValue))
                    {
                        allMatching = false;
                        break;
                    }
                }
            }

            if (allMatching)
            {
                return;
            }

            var prevObj = Property.GetValue();
            var newObj = selection[0] == null ? null : Activator.CreateInstance(selection[0]);

            var foldout = comboBox.GetFirstAncestorOfType<StyledFoldout>();
            if (foldout.childCount == 2)
            {
                foldout.RemoveAt(1);
            }
            if (newObj != null)
            {
                InspectorTreeObject ito = new InspectorTreeObject(newObj, newObj.GetType()).WithParent(Property.InspectorObject);
                InspectorTreeRootElement subRoot = new(ito);
                subRoot.DrawToScreen(foldout);
            }

            Debug.Log($"OnSerializeReferenceSelectionChanged {prevObj} -> {newObj}");
            MarkDirtyWithValue(prevObj, newObj);
            ValueChanged.Invoke(this, prevObj, newObj);
        }
        #endregion

        #region - Decorators -
        private void DrawLabel()
        {
            if (Property.TryGetAttribute<LabelAttribute>(out var atr))
            {
                var type = Property.ParentType;
                var label = _internalLabel;
                if (atr.HasLabelResolver)
                {
                    var resolverContainerProp = new SerializedResolverContainerType<string>(Property, ReflectionUtility.GetMember(type, atr.LabelResolver), s => label.text = s);
                    AddResolver(resolverContainerProp);
                }
                else
                {
                    label.text = atr.Label;
                }

                if (atr.HasLabelColorResolver)
                {
                    var resolverContainerProp = new SerializedResolverContainerType<Color>(Property, ReflectionUtility.GetMember(type, atr.LabelColorResolver), c => label.style.color = c);
                    AddResolver(resolverContainerProp);
                }
                else
                {
                    label.style.color = atr.LabelColor;
                }

                if (atr.HasIcon)
                {
                    var image = new Image
                    {
                        image = EditorGUIUtility.IconContent(atr.Icon).image,
                        scaleMode = ScaleMode.ScaleToFit,
                        pickingMode = PickingMode.Ignore
                    };
                    image.style.alignSelf = Align.FlexEnd;

                    if (atr.HasIconColorResolver)
                    {
                        var resolverContainerProp = new SerializedResolverContainerType<Color>(Property, ReflectionUtility.GetMember(type, atr.IconColorResolver), c => image.tintColor = c);
                        AddResolver(resolverContainerProp);
                    }
                    else
                    {
                        image.tintColor = atr.IconColor.value;
                    }

                    label.Add(image);
                }
            }
        }

        private void DrawLabelWidth()
        {
            if (!Property.TryGetAttribute<LabelWidthAttribute>(out var atr)) return;

            //field.hierarchy[0].RemoveFromClassList("unity-base-field__aligned");
            var label = _internalLabel;

            label.style.minWidth = new StyleLength(StyleKeyword.Auto);
            label.style.width = atr.Width;
            label.style.maxWidth = atr.Width;
        }

        private void DrawHideLabel()
        {
            if (Property.HasAttribute<HideLabelAttribute>())
            {
                var label = _internalLabel;
                if(label != null)
                {
                    label.style.display = DisplayStyle.None;
                }
            }
        }

        private void DrawRichTooltip()
        {
            if (!Property.TryGetAttribute<RichTextTooltipAttribute>(out var rtAtr)) return;

            var label = this.Q<Label>();
            if (label != null)
            {
                label.tooltip = rtAtr.Tooltip;
            }
        }

        private void DrawDecorators()
        {
            if (Property.TryGetAttribute<BackgroundColorAttribute>(out var backgroundColor))
            {
                var type = Property.ParentType;
                if (backgroundColor.HasBackgroundColorResolver)
                {
                    var resolverContainerProp = new SerializedResolverContainerType<Color>(Property, ReflectionUtility.GetMember(type, backgroundColor.BackgroundColorResolver), c => style.backgroundColor = c);
                    AddResolver(resolverContainerProp);
                }
                else
                {
                    style.backgroundColor = backgroundColor.BackgroundColor;
                }
            }

            if (Property.TryGetAttribute<MarginsAttribute>(out var margins))
            {
                if (margins.Bottom != null)
                {
                    style.marginBottom = margins.Bottom;
                }

                if (margins.Top != null)
                {
                    style.marginTop = margins.Top;
                }

                if (margins.Left != null)
                {
                    style.marginLeft = margins.Left;
                }

                if (margins.Right != null)
                {
                    style.marginRight = margins.Right;
                }
            }

            if (Property.TryGetAttribute<PaddingAttribute>(out var padding))
            {
                if (padding.Bottom != null)
                {
                    style.paddingBottom = padding.Bottom;
                }

                if (padding.Top != null)
                {
                    style.paddingTop = padding.Top;
                }

                if (padding.Left != null)
                {
                    style.paddingLeft = padding.Left;
                }

                if (padding.Right != null)
                {
                    style.paddingRight = padding.Right;
                }
            }

            if (Property.TryGetAttribute<BordersAttribute>(out var borders))
            {
                style.borderBottomWidth = borders.Bottom;
                style.borderBottomColor = borders.Color;

                style.borderTopWidth = borders.Top;
                style.borderTopColor = borders.Color;

                style.borderLeftWidth = borders.Left;
                style.borderLeftColor = borders.Color;

                style.borderRightWidth = borders.Right;
                style.borderRightColor = borders.Color;

                style.borderBottomLeftRadius = borders.Roundness;
                style.borderBottomRightRadius = borders.Roundness;
                style.borderTopLeftRadius = borders.Roundness;
                style.borderTopRightRadius = borders.Roundness;
            }
        }

        private void DrawConditionals()
        {
            var type = Property.ParentType;
            if (Property.TryGetAttribute<ShowIfAttribute>(out var showIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, showIf.Resolver),
                    b => ParentTreeElement.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
                AddResolver(resolverContainerProp);
            }

            if (Property.TryGetAttribute<HideIfAttribute>(out var hideIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, hideIf.Resolver),
                    b => ParentTreeElement.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
                AddResolver(resolverContainerProp);
            }

            if (Property.TryGetAttribute<DisableIfAttribute>(out var disableIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, disableIf.Resolver),
                    b => { _internalLabel?.SetEnabled(!b); _internalField.SetEnabled(!b); });
                AddResolver(resolverContainerProp);
            }

            if (Property.TryGetAttribute<EnableIfAttribute>(out var enableIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, enableIf.Resolver),
                    b => { _internalLabel?.SetEnabled(b); _internalField.SetEnabled(b); });
                AddResolver(resolverContainerProp);
            }

            if (Property.HasAttribute<HideInEditorModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => ParentTreeElement.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
                AddResolver(resolverContainerFunc);
            }

            if (Property.HasAttribute<HideInPlayModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => ParentTreeElement.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
                AddResolver(resolverContainerFunc);
            }

            if (Property.HasAttribute<DisableInEditorModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => { _internalLabel?.SetEnabled(b); _internalField.SetEnabled(b); });
                AddResolver(resolverContainerFunc);
            }

            if (Property.HasAttribute<DisableInPlayModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => { _internalLabel?.SetEnabled(!b); _internalField.SetEnabled(!b); });
                AddResolver(resolverContainerFunc);
            }
        }

        public static void DrawConditionals(InspectorTreeElement treeElement, VisualElement visualElement)
        {
            var property = treeElement.Property;
            var type = property.ParentType;
            if (property.TryGetAttribute<ShowIfAttribute>(out var showIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(property, ReflectionUtility.GetMember(type, showIf.Resolver),
                    b => visualElement.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
                treeElement.AddResolver(resolverContainerProp);
            }

            if (property.TryGetAttribute<HideIfAttribute>(out var hideIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(property, ReflectionUtility.GetMember(type, hideIf.Resolver),
                    b => visualElement.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
                treeElement.AddResolver(resolverContainerProp);
            }

            if (property.TryGetAttribute<DisableIfAttribute>(out var disableIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(property, ReflectionUtility.GetMember(type, disableIf.Resolver),
                    b => visualElement.SetEnabled(!b));
                treeElement.AddResolver(resolverContainerProp);
            }

            if (property.TryGetAttribute<EnableIfAttribute>(out var enableIf))
            {
                var resolverContainerProp = new SerializedResolverContainerType<bool>(property, ReflectionUtility.GetMember(type, enableIf.Resolver),
                    b => visualElement.SetEnabled(b));
                treeElement.AddResolver(resolverContainerProp);
            }

            if (property.HasAttribute<HideInEditorModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => visualElement.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
                treeElement.AddResolver(resolverContainerFunc);
            }

            if (property.HasAttribute<HideInPlayModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => visualElement.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
                treeElement.AddResolver(resolverContainerFunc);
            }

            if (property.HasAttribute<DisableInEditorModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => visualElement.SetEnabled(b));
                treeElement.AddResolver(resolverContainerFunc);
            }

            if (property.HasAttribute<DisableInPlayModeAttribute>())
            {
                var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
                    () => EditorApplication.isPlaying,
                    b => visualElement.SetEnabled(!b));
                treeElement.AddResolver(resolverContainerFunc);
            }
        }

        private void DrawReadOnly()
        {
            if (Property.HasAttribute<ReadOnlyAttribute>())
            {
                _internalLabel?.SetEnabled(false);
                _internalField.SetEnabled(false);
            }
        }

        private void DrawPathSelection()
        {
            if (Property.TryGetAttribute<FilePathAttribute>(out var fileAtr) && hierarchy[0] is TextField filePathTextField)
            {
                var inlineButton = new Button(() => filePathTextField.value = _FormatFilePath(fileAtr.AbsolutePath, fileAtr.FileExtension))
                {
                    text = "",
                };
                var image = new Image
                {
                    image = EditorGUIUtility.IconContent("d_FolderOpened Icon").image,
                    scaleMode = ScaleMode.ScaleToFit
                };
                filePathTextField.style.width = 0;
                inlineButton.style.paddingLeft = 3;
                inlineButton.style.paddingRight = 3;
                inlineButton.style.backgroundColor = new Color(0, 0, 0, 0);
                image.style.width = 16;
                image.style.height = 16;
                inlineButton.Add(image);
                Add(inlineButton);
                style.flexDirection = FlexDirection.Row;
                hierarchy[0].style.flexGrow = 1f;
            }

            if (Property.TryGetAttribute<FolderPathAttribute>(out var folderAtr) && hierarchy[0] is TextField folderPathTextField)
            {
                var inlineButton = new Button(() => folderPathTextField.value = _FormatFolderPath(folderAtr.AbsolutePath))
                {
                    text = "",
                };
                var image = new Image
                {
                    image = EditorGUIUtility.IconContent("d_FolderOpened Icon").image,
                    scaleMode = ScaleMode.ScaleToFit
                };
                folderPathTextField.style.width = 0;
                inlineButton.style.paddingLeft = 3;
                inlineButton.style.paddingRight = 3;
                inlineButton.style.backgroundColor = new Color(0, 0, 0, 0);
                image.style.width = 16;
                image.style.height = 16;
                inlineButton.Add(image);
                Add(inlineButton);
                style.flexDirection = FlexDirection.Row;
                hierarchy[0].style.flexGrow = 1f;
            }

            string _FormatFilePath(bool absolutePath, string fileExtension)
            {
                if (!absolutePath)
                {
                    var path = EditorUtility.OpenFilePanel("File Path", "Assets", fileExtension);
                    return string.IsNullOrEmpty(path) ? "" : FileUtil.GetLogicalPath(path);
                }
                else
                {
                    return EditorUtility.OpenFilePanel("File Path", "Assets", fileExtension);
                }
            }

            string _FormatFolderPath(bool absolutePath)
            {
                if (!absolutePath)
                {
                    var path = EditorUtility.OpenFolderPanel("Folder Path", "Assets", "");
                    return string.IsNullOrEmpty(path) ? "" : FileUtil.GetLogicalPath(path);
                }
                else
                {
                    return EditorUtility.OpenFolderPanel("Folder Path", "Assets", "");
                }
            }
        }

        private void DrawTitle()
        {
            if (!Property.TryGetAttribute<TitleAttribute>(out var atr)) return;

            var labelText = $"<b>{atr.Title}</b>";
            if (atr.Subtitle != string.Empty)
            {
                labelText = $"<b>{atr.Title}</b>\n<color=#9E9E9E><i><size=10>{atr.Subtitle}</size></i></color>";
            }

            var title = new Label(labelText)
            {
                style =
                {
                    borderBottomWidth = atr.Underline ? 1 : 0,
                    paddingBottom = 2,
                    borderBottomColor = ContainerStyles.TextDefault,
                    marginBottom = 1f
                }
            };
            // Has to insert at hierarchy because .Insert uses the content container which is the PropertyField.
            hierarchy.Insert(0, title);
        }

        private void DrawInlineButtons()
        {
            if (Property.TryGetAttributes<InlineButtonAttribute>(out var atrs))
            {
                var type = Property.ParentType;
                foreach (var atr in atrs)
                {
                    var methodInfo = ReflectionUtility.GetMethod(type, atr.MethodName);
                    if (methodInfo != null)
                    {
                        string tooltip = TooltipMarkup.FormatString(atr.Tooltip);
                        var inlineButton = new Button(() => {
                            methodInfo.Invoke(Property.GetParentObject(), null);
                            if (atr.RebuildTree)
                            {
                                GetFirstAncestorOfType<InspectorTreeElement>().Root.RebuildAndRedraw();
                            }
                        })
                        {
                            text = atr.Label,
                            tooltip = tooltip,
                        };
                        inlineButton.style.paddingLeft = 3;
                        inlineButton.style.paddingRight = 3;
                        if (atr.Icon != string.Empty)
                        {
                            var image = new Image
                            {
                                image = EditorGUIUtility.IconContent(atr.Icon).image,
                                scaleMode = ScaleMode.ScaleToFit,
                                tintColor = atr.Tint
                            };
                            if (atr.HasResolver)
                            {
                                var resolverContainerProp = new SerializedResolverContainerType<Color>(Property, ReflectionUtility.GetMember(type, atr.TintResolver), c => image.tintColor = c);
                                AddResolver(resolverContainerProp);
                            }

                            inlineButton.Add(image);
                        }

                        Add(inlineButton);
                        style.flexDirection = FlexDirection.Row;
                        hierarchy[0].style.flexGrow = 1f;
                    }
                }
            }

            if (Property.TryGetAttributes<InlineToggleButtonAttribute>(out var togAtrs))
            {
                var type = Property.ParentType;
                foreach (var atr in togAtrs)
                {
                    var methodInfo = ReflectionUtility.GetMethod(type, atr.MethodName);
                    var memberInfo = ReflectionUtility.GetMember(type, atr.PropertyResolver);
                    ReflectionUtility.TryResolveMemberValue<bool>(Property.GetParentObject(), memberInfo, null, out var state);

                    if (methodInfo != null)
                    {
                        string tooltip = TooltipMarkup.FormatString(atr.Tooltip);
                        var inlineButton = new Button()
                        {
                            tooltip = tooltip,
                        };
                        inlineButton.style.paddingLeft = 3;
                        inlineButton.style.paddingRight = 3;
                        var image = new Image
                        {
                            image = state ? EditorGUIUtility.IconContent(atr.IconOn).image : EditorGUIUtility.IconContent(atr.IconOff).image,
                            scaleMode = ScaleMode.ScaleToFit,
                            tintColor = state ? atr.TintOn : atr.TintOff
                        };
                        inlineButton.Add(image);

                        inlineButton.clickable = new Clickable(() =>
                        {
                            methodInfo.Invoke(Property.GetParentObject(), null);
                            ReflectionUtility.TryResolveMemberValue<bool>(Property.GetParentObject(), memberInfo, null, out var state);
                            if (state)
                            {
                                image.image = EditorGUIUtility.IconContent(atr.IconOn).image;
                                image.tintColor = atr.TintOn;
                            }
                            else
                            {
                                image.image = EditorGUIUtility.IconContent(atr.IconOff).image;
                                image.tintColor = atr.TintOff;
                            }
                            if (atr.RebuildTree)
                            {
                                GetFirstAncestorOfType<InspectorTreeElement>().Root.RebuildAndRedraw();
                            }
                        });                        

                        Add(inlineButton);
                        style.flexDirection = FlexDirection.Row;
                        hierarchy[0].style.flexGrow = 1f;
                    }
                }
            }
        }

        private void DrawSuffix()
        {
            if (Property.TryGetAttribute<SuffixAttribute>(out var atr))
            {
                var suffix = new Label(atr.Suffix);
                suffix.style.color = new Color(0.5f, 0.5f, 0.5f, 1);
                suffix.style.alignSelf = Align.Center;
                suffix.style.marginLeft = 3;
                suffix.style.paddingLeft = 3;
                Add(suffix);
                style.flexDirection = FlexDirection.Row;
                hierarchy[0].style.flexGrow = 1f;
            }
        }

        private void DrawAutoReference()
        {
            //if (Property.HasAttribute<AutoReferenceAttribute>())
            //{
            //    Debug.Log($"Attribute: {Property.TryGetAttribute<AutoReferenceAttribute>(out var dAtr)}");
            //    Debug.Log($"Prop Match: {Property.SerializedPropertyType == SerializedPropertyType.ObjectReference}");
            //    Debug.Log($"Obj Null: {(Object)Property.GetObject() == null}");
            //    Debug.Log($"Root Component: {Property.InspectorObject.Object is Component}");

            //    Debug.Log(Property.GetObject());
            //}
            //else
            //{
            //    return;
            //}

            if (Property.TryGetAttribute<AutoReferenceAttribute>(out var atr)
                && Property.SerializedPropertyType == SerializedPropertyType.ObjectReference
                && (Object)Property.GetValue() == null
                && Property.InspectorObject.Object is Component component)
            {
                var comp = component.GetComponent(PropertyType);
                if (!comp && atr.SearchChildren)
                {
                    comp = component.GetComponentInChildren(PropertyType, true);
                }

                if (!comp && atr.SearchParents)
                {
                    comp = component.GetComponentInParent(PropertyType, true);
                }

                //Property.SetValue(comp);
                schedule.Execute(() => Property.SetValue(comp));
            }
        }

        private void DrawValidation()
        {
            var type = Property.ParentType;
            if (Property.TryGetAttribute<OnValueChangedAttribute>(out var ovc))
            {
                var methodInfo = ReflectionUtility.GetMethod(type, ovc.MethodName);
                if (methodInfo != null)
                {
                    ValueChanged += (sender, old, @new) =>
                    {
                        var t = sender.PropertyType;
                        old = CastToType(old, t);
                        @new = CastToType(@new, t);

                        var target = sender.Property.GetParentObject();
                        if (methodInfo.IsStatic)
                        {
                            target = null;
                        }
                        
                        int length = methodInfo.GetParameters().Length;
                        if (length == 2)
                        {
                            methodInfo.Invoke(target, new [] { old, @new });
                        }
                        else if (length == 1)
                        {
                            methodInfo.Invoke(target, new [] { @new });
                        }
                        else
                        {
                            methodInfo.Invoke(target, null);
                        }

                        if (ovc.RebuildTree)
                        {
                            GetFirstAncestorOfType<InspectorTreeElement>().Root.RebuildAndRedraw();
                        }
                    };
                }
            }

            if (Property.TryGetAttribute<ValidateInputAttribute>(out var viatr))
            {
                var methodInfo = ReflectionUtility.GetMethod(type, viatr.MethodName);
                if (methodInfo != null)
                {
                    Validate = (x) =>
                    {
                        var t = PropertyType;
                        x = CastToType(x, t);
                        return methodInfo.Invoke(Property.GetParentObject(), new object[1] { x });
                    };
                }
            }

            if (Property.TryGetAttribute<RangeAttribute>(out var rangeAttribute))
            {
                _internalValidate = x => false;

                _internalProcessValidation = (x) =>
                {
                    switch (Property.SerializedPropertyNumericType)
                    {
                        case SerializedPropertyNumericType.Int8:
                        case SerializedPropertyNumericType.UInt8:
                        case SerializedPropertyNumericType.Int16:
                        case SerializedPropertyNumericType.UInt16:
                        case SerializedPropertyNumericType.Int32:
                            return Mathf.Clamp((int)x, (int)rangeAttribute.min, (int)rangeAttribute.max);
                        case SerializedPropertyNumericType.UInt32:
                            return Math.Clamp((uint)x, (uint)rangeAttribute.min, (uint)rangeAttribute.max);
                        case SerializedPropertyNumericType.Int64:
                            return Math.Clamp((long)x, (long)rangeAttribute.min, (long)rangeAttribute.max);
                        case SerializedPropertyNumericType.UInt64:
                            return Math.Clamp((ulong)x, (ulong)rangeAttribute.min, (ulong)rangeAttribute.max);
                        case SerializedPropertyNumericType.Float:
                            return Mathf.Clamp((float)x, rangeAttribute.min, rangeAttribute.max);
                        case SerializedPropertyNumericType.Double:
                            return Math.Clamp((double)x, rangeAttribute.min, rangeAttribute.max);
                        case SerializedPropertyNumericType.Unknown:
                        default:
                            Debug.LogError($"Range Attribute Applied To Invalid Type: {Property.PropertyPath} at {Property.InspectorObject.Object}");
                            return 0;
                    }
                };
            }
        }

        private void DrawRequireInterface()
        {
            if (!Property.TryGetAttribute<RequireInterfaceAttribute>(out var reqIntAtr)) return;

            var objDrawer = this.Q<ObjectField>();
            objDrawer.objectType = reqIntAtr.InterfaceType;

            _internalValidate = value =>
            {
                return value == null;
            };

            _internalProcessValidation = value =>
            {
                if (value is GameObject go)
                {
                    return go.TryGetComponent(reqIntAtr.InterfaceType, out var comp) ? comp : (Object)null;
                }
                else if (reqIntAtr.InterfaceType.IsInstanceOfType(value))
                {
                    return value;
                }
                else
                {
                    return (Object)null;
                }
            };

            var picker = objDrawer.hierarchy[1][1];
            picker.style.display = DisplayStyle.None;
            if (objDrawer.hierarchy[1].childCount == 2)
            {
                var pickerClone = new VisualElement();
                pickerClone.AddToClassList(ObjectField.selectorUssClassName);
                objDrawer.hierarchy[1].Add(pickerClone);
                pickerClone.RegisterCallback<MouseDownEvent>(x => _PickerSelect(x, reqIntAtr.InterfaceType, Property), TrickleDown.TrickleDown);
            }

            void _PickerSelect(MouseDownEvent evt, Type pickType, InspectorTreeProperty property)
            {
                var filter = ShowObjectPickerUtility.GetSearchFilter(typeof(Object), pickType);
                ShowObjectPickerUtility.ShowObjectPicker(typeof(Object), obj => property.SetValue(obj), null, property.GetValue<Object>(), ShowObjectPickerUtility.ObjectPickerSources.AssetsAndScene, filter);
                evt.StopPropagation();
            }
        }

        private void DrawChildGameObjectsOnly()
        {
            if (Property.InspectorObject.IsUnityObject && Property.TryGetAttribute<ChildGameObjectsOnlyAttribute>(out var atr))
            {
                if ((Object)Property.InspectorObject is not MonoBehaviour monoBehaviour)
                {
                    return;
                }
                
                var comps = monoBehaviour.GetComponentsInChildren(PropertyType, true);

                var inlineButton = new Button(() => 
                {
                    foreach (var c in comps)
                    {
                        Debug.Log(c.name);
                    }
                })
                {

                };
                inlineButton.style.paddingLeft = 3;
                inlineButton.style.paddingRight = 3;
                var image = new Image
                {
                    image = EditorGUIUtility.IconContent("UnityEditor.SceneHierarchyWindow").image,
                    scaleMode = ScaleMode.ScaleToFit,
                };

                inlineButton.Add(image);

                hierarchy[0].Insert(1, inlineButton);
                style.flexDirection = FlexDirection.Row;
                hierarchy[0].style.flexGrow = 1f;
            }
        }

        private void DrawFlexBasis()
        {
            if (Property.TryGetAttribute<HorizontalGroupAttribute>(out var atr))
            {
                ParentTreeElement.style.flexBasis = atr.FlexBasis;
            }
        }

        #endregion

        #region - Resolvers -
        private void AddResolver(SerializedResolverContainer resolver)
        {
            _resolvers.Add(resolver);
#if UNITY_EDITOR_COROUTINES
            _resolverRoutine ??= EditorCoroutineUtility.StartCoroutine(ResolveContainers(), this);
#endif
        }

        private IEnumerator ResolveContainers()
        {
            while (true)
            {
                foreach (var resolver in _resolvers)
                {
                    resolver.Resolve();
                }
                yield return null;
            }
        }
        #endregion

        #region - Helper -
        private static void SplitTupleToDropdown(List<string> keys, List<object> values, IEnumerable<DropdownModel> toConvert)
        {
            if (toConvert == null)
            {
                return;
            }

            foreach (var model in toConvert)
            {
                var category = model.Category;
                var name = model.Name;
                var value = model.Value;

                if (name == null || value == null)
                {
                    name = model.ToString();
                    value = model;
                }

                if (category != null)
                {
                    name = $"{category}/{name}";
                }

                keys.Add(name);
                values.Add(value);
            }
        }

        public static object CastToType(object obj, Type targetType)
        {
            if (obj == null)
            {
                if (targetType.IsClass || Nullable.GetUnderlyingType(targetType) != null)
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

            // Handle conversion using Convert.ChangeType if the target type is convertible
            try
            {
                return Convert.ChangeType(obj, targetType);
            }
            catch (InvalidCastException)
            {
                throw new InvalidCastException($"Cannot cast object of type {obj.GetType()} to type {targetType}.");
            }
        }

        private static void StyleLabel(VisualElement element)
        {
            var label = element.Q<Label>();
            if (label == null)
                return;
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.minWidth = new StyleLength(new Length(33, LengthUnit.Percent));
            label.style.maxWidth = new StyleLength(new Length(33, LengthUnit.Percent));
        }
        #endregion
    }
}
