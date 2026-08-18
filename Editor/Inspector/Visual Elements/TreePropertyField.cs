using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
using Vapor.Serialization;
using FilePathAttribute = Vapor.Inspector.FilePathAttribute;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public delegate void ValueChangedHandler(TreePropertyField sender, object previous, object current);
    
    public class TreePropertyField : VisualElement
    {
        /// <summary>
        /// How the drawn widget keeps up with the value behind it.
        /// </summary>
        public enum ValueSource
        {
            /// <summary>Bound to the source and refreshed when it reports a change. The default for fields.</summary>
            Bound,

            /// <summary>Read once when the row is built. Suitable for a value that cannot be edited here.</summary>
            ReadOnce,

            /// <summary>Re-read from the source on every update.</summary>
            Polled,
        }

        public bool IsValid { get; set; }
        public InspectorTreeProperty Property { get; }

        /// <summary>
        /// The controller that owns this field, for navigating the tree - finding the root to rebuild, or
        /// parenting nested trees. This element never styles or lays out its owner.
        /// </summary>
        public InspectorTreeElement Owner { get; }

        public Type PropertyType { get; }

        private Func<object, bool> _internalValidate = delegate { return true; };
        private Func<object, object> _internalProcessValidation = x => x;
        public Func<object, object> Validate;
        public event ValueChangedHandler ValueChanged = delegate { };

        private Label _internalLabel;
        private VisualElement _internalField;
        private DataBinding _fieldBinding;
        private readonly ValueSource _valueSource = ValueSource.Bound;

        public TreePropertyField(InspectorTreeProperty property, InspectorTreeElement owner = null)
        {
            Property = property;
            PropertyType = property.PropertyType;
            Owner = owner;
            DrawContent();
        }

        public TreePropertyField(InspectorTreeProperty property, InspectorTreeElement owner, ValueSource valueSource)
        {
            Property = property;
            PropertyType = property.PropertyType;
            Owner = owner;
            _valueSource = valueSource;
            DrawContent();
        }

        public TreePropertyField(InspectorTreeProperty property, Type overrideType, InspectorTreeElement owner = null)
        {
            Property = property;
            PropertyType = overrideType;
            Owner = owner;
            DrawContent(true);
        }

        private void DrawContent(bool overrideType = false)
        {
            name = $"PropertyField:{Property.PropertyName}";
            var field = DrawField(overrideType);
            if (field != null)
            {
                field.name = $"InputField:{Property.PropertyName}";
                _internalField = field;
                Add(_internalField);
                _internalLabel = this.Q<Label>();
                IsValid = true;


                // Widget-level decorators only. Anything about the row - visibility, spacing, framing -
                // belongs to the controller and is applied there.
                DrawLabel();
                DrawLabelWidth();
                DrawHideLabel();
                DrawRichTooltip();
                DrawHelpUrl();
                DrawReadOnly();
                DrawPathSelection();
                DrawTitle();
                DrawInlineButtons();
                DrawSuffix();
                DrawAutoReference();
                DrawValidation();
                DrawRequireInterface();
                DrawChildGameObjectsOnly();
            }
        }

        protected VisualElement DrawField(bool overrideType = false)
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
                SerializedDrawerUtility.TryGetCustomPropertyDrawer(PropertyType, propertyType == SerializedPropertyType.ManagedReference, out var propertyDrawer);
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
                    return new PropertyField(Property.InspectorObject.FindSerializedProperty(Property.PropertyPath))
                    {
                        bindingPath = Property.PropertyPath
                    };
                }
            }

            if(!Property.IsArray && Property.IsManagedReference)
            {
                VisualElement referenceGroup;
                if (Property.TryGetTypeAttribute<DrawWithVaporAttribute>(out var vaporGroupAttribute))
                {
                    referenceGroup = InspectorTreeElement.SurroundWithVaporGroup(vaporGroupAttribute.InlinedGroupType, niceName, niceName);
                }
                else
                {
                    referenceGroup = new StyledFoldout(niceName);
                }

                bool flattenCategories = false;
                bool showNullType = true;
                bool drawSelector = true;
                if (Property.TryGetAttribute<SerializeReferenceDrawerAttribute>(out var drawerAttribute))
                {
                    flattenCategories = drawerAttribute.FlattenCategories;
                    showNullType = drawerAttribute.ShowNullType;
                    drawSelector = drawerAttribute.DrawSelector;
                }


                if (drawSelector)
                {
                    List<string> keys = new();
                    List<object> values = new();
                    List<string> tooltips = new();
                    if (showNullType)
                    {
                        keys.Add("Null");
                        values.Add(null);
                    }

                    // Which types are offered depends on who is going to persist the value. Unity's
                    // [SerializeReference] can only rebuild a [Serializable] type; VSL rebuilds anything
                    // it can construct, and its types have no reason to carry [Serializable] at all.
                    var isVslReference = Property.HasAttribute<VslSerializeReferenceAttribute>();
                    var candidates = ReflectionUtility.GetAssignableTypesOf(PropertyType)
                        .Where(t => !t.IsDefined(typeof(IgnoreDropdownAttribute))
                                    && !t.IsSubclassOf(typeof(Object))
                                    && (isVslReference ? IsVslConstructable(t) : t.IsDefined(typeof(SerializableAttribute))))
                        .ToList();

                    if (!PropertyType.IsAbstract)
                    {
                        candidates.Add(PropertyType);
                    }

                    // Flat and spaced out, not filed under a namespace every sibling shares. The suffix
                    // is taken from the candidates so it can only ever drop what they have in common.
                    var sharedSuffix = TypeDisplayNames.FindSharedSuffix(candidates);
                    if (!PropertyType.IsAbstract)
                    {
                        candidates.Remove(PropertyType);
                        keys.Add(TypeDisplayNames.GetDisplayName(PropertyType, sharedSuffix));
                        values.Add(PropertyType);
                    }

                    var dropdownModels = candidates.Select(t => new DropdownModel(
                        null,
                        TypeDisplayNames.GetDisplayName(t, sharedSuffix),
                        t,
                        TypeDisplayNames.Describe(t) ?? t.Name));
                    SplitTupleToDropdown(keys, values, tooltips, dropdownModels);

                    var current = Property.GetValue();
                    var cIdx = current == null ? 0 : Mathf.Max(0, values.IndexOf(current.GetType()));
                    var comboBox = new ComboBox<object>(niceName, cIdx, keys, values, tooltips, false, flattenCategories: flattenCategories);

                    comboBox.SelectionChanged += OnSerializeReferenceSelectionChanged;
                    referenceGroup.Add(comboBox);
                    
                    if (current != null)
                    {
                        new InspectorTreeRootElement(current, current.GetType(), Property.InspectorObject).AttachTo(referenceGroup);
                    }
                }
                else
                {
                    var current = Property.GetValue();
                    ValueChanged += (sender, old, @new) =>
                    {
                        referenceGroup.Q<InspectorTreeRootElement>().RemoveFromHierarchy();
                        if (@new != null)
                        {
                            new InspectorTreeRootElement(@new, @new.GetType(), Property.InspectorObject).AttachTo(referenceGroup);
                        }
                    };

                    if (current != null)
                    {
                        new InspectorTreeRootElement(current, current.GetType(), Property.InspectorObject).AttachTo(referenceGroup);
                    }
                }

                return referenceGroup;
            }

            if (Property.TryGetAttribute<DropdownAttribute>(out var dropdownAttribute) && !Property.IsArrayElement)
            {
                List<string> keys = new();
                List<object> values = new();
                List<string> tooltips = new();

                switch (dropdownAttribute.Filter)
                {
                    case 0:
                        var mi = ReflectionUtility.GetMember(Property.ParentType, dropdownAttribute.Resolver);
                        if (ReflectionUtility.TryResolveMemberValue<IEnumerable<DropdownModel>>(Property.GetParentObject(), mi, null, out var convert))
                        {
                            SplitTupleToDropdown(keys, values, tooltips, convert);
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
                        SplitTupleToDropdown(keys, values, tooltips, DataKeyUtility.GetAllKeysFromCategory(dropdownAttribute.Resolver));
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
                        SplitTupleToDropdown(keys, values, tooltips, DataKeyUtility.GetAllKeysFromTypeName(dropdownAttribute.Resolver));
                    }
                        break;
                }

                //InspectorDebug.Log($"Building Property: {Property.PropertyName} | IsArray: {Property.IsArray} | Options: {keys.Count}");
                if (Property.IsArray)
                {
                    if (dropdownAttribute.MultiSelectArray)
                    {
                        int idNone = keys.IndexOf("None");
                        if (idNone != -1)
                        {
                            keys.RemoveAt(idNone);
                            values.RemoveAt(idNone);
                        }
                        
                        var comboBox = new ComboBox<object>(niceName, -1, keys, values, null, true, categorySplitCharacter: dropdownAttribute.CategorySplitCharacter);
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
                            string c = dropdownAttribute.CategorySplitCharacter.EmptyOrNull() ? keys[idx] : keys[idx].Replace(dropdownAttribute.CategorySplitCharacter[0], '/');
                            selectedNames.Add(c);
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
                    var comboBox = new ComboBox<object>(niceName, cIdx, keys, values, null, false, categorySplitCharacter: dropdownAttribute.CategorySplitCharacter);

                    comboBox.SelectionChanged += OnComboBoxSelectionChanged;
                    return comboBox;
                }
            }

            switch (propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (Owner == null)
                    {
                        return null;
                    }
                    if (Property.IsArray)
                    {
                        return new PaginatedList(Owner, Property, niceName);
                    }

                    return Property.IsDictionary ? new PaginatedDictionary(Owner, Property, niceName) : (VisualElement)null;
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
                                    field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                    field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                            style =
                            {
                                paddingRight = 0,
                            }
                        };
                        StyleLabel(field);
                        // var label = field.Q<Label>();
                        // label.style.minWidth = StyleKeyword.Null;
                        // label.style.maxWidth = StyleKeyword.Null;
                        // field.RegisterCallback<GeometryChangedEvent>(evt =>
                        // {
                        //     var ve = (VisualElement)evt.target;
                        //     ve.hierarchy[0].style.width = layout.width * 0.33f;
                        //     ve.hierarchy[1].style.width = layout.width * 0.67f;
                        // });
                        // var label = field.Q<Label>();
                        // label.style.flexGrow = 1f;
                        // label.style.flexShrink = 1f;
                        // label.style.overflow = Overflow.Hidden;
                        // label.style.textOverflow = TextOverflow.Ellipsis;
                        // label.style.unityTextAlign = TextAnchor.MiddleLeft;

                        SetupDefaultBinding(field);
                        field.SetValueWithoutNotify(Property.GetValue<bool>());
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                    field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                                    field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                            bool flattenCategories = false;
                            foreach (var atr in tsAtrs)
                            {
                                flattenCategories = atr.FlattenCategories;
                                if (atr.AllTypes)
                                {
                                    var allTypeSelector = new TypeSelectorField(niceName, cType, flattenCategories: flattenCategories);
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
                                        if (method == null)
                                        {
                                            // Named rather than thrown. An unresolved name used to take the
                                            // whole inspector down with a stack trace that didn't say which field.
                                            Debug.LogError($"[TypeSelector] on {pType.Name}.{Property.PropertyName} names '{atr.Resolver}', " +
                                                           $"which is not a method on {pType.Name}. The resolver must be declared on the type that owns the field.");
                                            break;
                                        }

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
                            
                            var typeSelector = new TypeSelectorField(niceName, cType, flattenCategories: flattenCategories).WithValidTypes(tsAtrs[0].IncludeAbstract, types.ToArray());
                            typeSelector.AssemblyQualifiedNameChanged += OnNameSelectionChanged;
                            return typeSelector;
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt, ToLayerMask));
                        return field;
                    }
                case SerializedPropertyType.Enum:
                    {
                        //InspectorDebug.Log($"Is Enum: {Property.PropertyName}");
                        if (Property.TypeHasAttribute<FlagsAttribute>())
                        {
                            var field = new EnumFlagsField(Property.GetValue<Enum>())
                            {
                                label = niceName,
                            };
                            StyleLabel(field);

                            SetupDefaultBinding(field);
                            field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                            field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        // InspectorDebug.Log(Property.GetValue<Vector3>());
                        field.SetValueWithoutNotify(Property.GetValue<Vector3>());
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt, ToChar));
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
                        field.RegisterValueChangedCallback(evt => OnMutableWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt));
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
                        field.RegisterValueChangedCallback(evt => OnWidgetValueChanged(evt, ToRenderingLayerMask));
                        return field;
                    }
                default:
                    return new Label($"Undefined Type: {PropertyType}");
            }
        }

        /// <summary>
        /// Re-reads the value from the model and pushes it into the widget. Anything that mutates the value
        /// behind the UI's back - an inline button invoking a method, for instance - has to ask for this.
        /// <para>
        /// Marking the binding dirty is not enough on its own: the field binds ToTarget with
        /// <see cref="BindingUpdateTrigger.OnSourceChanged"/>, and a plain C# object never reports a change,
        /// so the value is set directly as well.
        /// </para>
        /// </summary>
        public void RefreshFromSource()
        {
            _fieldBinding?.MarkDirty();

            if (_internalField == null)
            {
                return;
            }

            // The widget's value type isn't known here - it varies per SerializedPropertyType - so it is
            // recovered from whichever INotifyValueChanged<T> the field implements.
            var notifyInterface = _internalField.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotifyValueChanged<>));
            if (notifyInterface == null)
            {
                return;
            }

            object converted;
            try
            {
                converted = CastToType(Property.GetValue(), notifyInterface.GetGenericArguments()[0]);
            }
            catch (Exception)
            {
                // Types the widget represents indirectly - masks drawn as ints, for instance - have no
                // general conversion. Those keep whatever they are showing rather than throwing.
                return;
            }

            notifyInterface.GetMethod(nameof(INotifyValueChanged<int>.SetValueWithoutNotify))
                ?.Invoke(_internalField, new[] { converted });
        }

        private void SetupDefaultBinding(VisualElement field)
        {
            //InspectorDebug.Log($"Binding: {Property.GetParentObject()} - {Property.PropertyName}");
            _fieldBinding = new DataBinding
            {
                dataSource = Property.GetParentObject(),
                dataSourcePath = new PropertyPath(Property.PropertyName),
                bindingMode = BindingMode.ToTarget,
                updateTrigger = _valueSource == ValueSource.Polled
                    ? BindingUpdateTrigger.EveryUpdate
                    : BindingUpdateTrigger.OnSourceChanged
            };

            if (_valueSource == ValueSource.ReadOnce || !IsPathResolvable())
            {
                // Built but never attached. The numeric and mask cases add converters to _fieldBinding right
                // after calling this, so it still has to exist; the widget just keeps the value it was
                // seeded with instead of tracking the source.
                return;
            }

            field.SetBinding("value", _fieldBinding);
        }

        /// <summary>
        /// Whether UI Toolkit can actually resolve <see cref="DataBinding.dataSourcePath"/> for this
        /// member.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The binding path is resolved by <c>Unity.Properties</c> against a reflected property bag,
        /// which is built from fields — public ones, and non-public ones carrying
        /// <see cref="SerializeField"/>. A C# property is not in it at all unless it asks to be with
        /// <see cref="CreatePropertyAttribute"/>; accessor visibility has nothing to do with it. Attach
        /// a binding to anything outside that set and UI Toolkit logs "could not retrieve the value at
        /// path" on every scheduler tick and never delivers a value.
        /// </para>
        /// <para>
        /// That makes this the common case rather than the exception here: a <c>[VslSerialize]</c>
        /// member is drawn as an editable field but owes nothing to Unity's serializer, so it is
        /// routinely a property, or a private field with no <c>[SerializeField]</c> on it.
        /// </para>
        /// <para>
        /// Nothing is lost by skipping it. Every call site seeds the widget with
        /// <c>SetValueWithoutNotify</c> straight after building the binding, and edits travel back
        /// through this field's own change handling rather than through the binding — so the binding
        /// only ever added live refresh from a source changing behind the inspector's back.
        /// </para>
        /// </remarks>
        private bool IsPathResolvable()
        {
            // An array element is addressed by index into its collection rather than by member name,
            // so the member's own visibility says nothing about it.
            if (Property.IsArrayElement)
            {
                return true;
            }

            switch (Property.PropertyInfoType)
            {
                case InspectorTreeProperty.MemberInfoType.Field:
                    var field = Property.FieldInfo;
                    if (field == null || field.IsDefined(typeof(CompilerGeneratedAttribute), true))
                    {
                        // An auto-property's backing field. [field: SerializeField] puts it in reach of
                        // the drawing predicates, but its name is '<Foo>k__BackingField' — angle
                        // brackets and all — which is not a path Unity.Properties will ever resolve.
                        return false;
                    }

                    return field.IsPublic
                           || field.IsDefined(typeof(SerializeField), true)
                           || field.IsDefined(typeof(CreatePropertyAttribute), true);

                case InspectorTreeProperty.MemberInfoType.Property:
                    var info = Property.PropertyInfo;
                    return info != null && info.IsDefined(typeof(CreatePropertyAttribute), true);

                default:
                    return true;
            }
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
                InspectorDebug.Log("Propagating Struct Data Up");
                // Propagated without a rebuild all the way up. Each step refreshes the element snapshot it
                // writes to, so a rebuild adds nothing and its redraw would destroy the widget being edited.
                if (_fieldBinding != null)
                {
                    Property.ParentProperty.SetValueWithoutArrayRebuild(_fieldBinding.dataSource);
                    _fieldBinding.dataSource = Property.GetParentObject();
                }
                else
                {
                    Property.ParentProperty.SetValueWithoutArrayRebuild(Property.GetParentObject());
                }

                var property = Property;
                while (true)
                {
                    if (property.ParentProperty?.ParentIsStruct() == true)
                    {
                        property.ParentProperty.ParentProperty.SetValueWithoutArrayRebuild(property.ParentProperty.GetParentObject());
                        property = property.ParentProperty;
                        continue;
                    }

                    break;
                }
            }

            using var evt = TreePropertyChangedEvent.GetPooled();
            evt.target = this;
            evt.Previous = previousValue;
            evt.Current = newValue;
            SendEvent(evt);
            Property.InspectorObject.ApplyModifiedProperties();
        }

        private object ProcessValidation(object newValue)
        {
            return !_internalValidate.Invoke(newValue) ? _internalProcessValidation.Invoke(newValue) : newValue;
        }

        /// <summary>
        /// The single path every widget's value change takes: skip no-ops, validate, convert to the
        /// property's type, write, notify.
        /// </summary>
        /// <param name="toPropertyValue">
        /// Converts the widget's value to the property's for the cases where the two types differ - a char
        /// edits as a string, a mask as an int. Defaults to <see cref="CastToType"/>.
        /// </param>
        private void OnWidgetValueChanged<TValue>(ChangeEvent<TValue> evt, Func<object, object> toPropertyValue = null)
        {
            if (EqualityComparer<TValue>.Default.Equals(evt.previousValue, evt.newValue))
            {
                return;
            }

            CommitWidgetValue(evt.previousValue, evt.newValue, toPropertyValue);
        }

        /// <summary>
        /// As <see cref="OnWidgetValueChanged{TValue}"/>, minus the no-op check. For widgets that edit their
        /// value in place and so report the same instance as both old and new - skipping equal values there
        /// would discard every edit.
        /// </summary>
        private void OnMutableWidgetValueChanged<TValue>(ChangeEvent<TValue> evt)
        {
            CommitWidgetValue(evt.previousValue, evt.newValue, null);
        }

        private void OnObjectChanged(ChangeEvent<Object> evt)
        {
            // Compared by reference on purpose. Unity's Object.Equals reports a destroyed object as equal to
            // null, which would swallow the edit that clears a field still pointing at one.
            if (ReferenceEquals(evt.previousValue, evt.newValue))
            {
                return;
            }

            CommitWidgetValue(evt.previousValue, evt.newValue, null);
        }

        private void CommitWidgetValue(object previous, object widgetValue, Func<object, object> toPropertyValue)
        {
            var validated = ProcessValidation(widgetValue);
            if (Validate != null)
            {
                validated = Validate.Invoke(validated);
            }

            var propertyValue = toPropertyValue != null ? toPropertyValue(validated) : CastToType(validated, PropertyType);

            InspectorDebug.Log($"{Property.PropertyPath}: {previous} -> {propertyValue}");
            MarkDirtyWithValue(previous, propertyValue);

            // Notified with the widget's value rather than the property's - listeners are registered against
            // the field, so they expect what the field edits.
            ValueChanged.Invoke(this, previous, validated);
        }

        private static object ToChar(object value)
        {
            // Empty is valid here; the text field can be cleared.
            return value is string text ? text.FirstOrDefault() : default(char);
        }

        private static object ToLayerMask(object value)
        {
            return new LayerMask { value = (int)value };
        }

        private static object ToRenderingLayerMask(object value)
        {
            return new RenderingLayerMask { value = (uint)value };
        }

        public void OnComboBoxSelectionChanged(ComboBox<object> comboBox, List<int> selectedIndices)
        {
            IList selection = (IList)Property.CreateGenericListOfType();
            foreach (var idx in selectedIndices)
            {
                // ComboBox resolves indices by display name and yields -1 when a name no longer matches a
                // choice, which used to index straight off the end of Values.
                if (idx < 0 || idx >= comboBox.Values.Count)
                {
                    continue;
                }

                selection.Add(comboBox.Values[idx]);
            }
            //InspectorDebug.Log($"OnComboBoxSelectionChanged - New Selections: {selection.Count}");

            bool allMatching = true;
            if (Property.IsArray)
            {
                if (selection.Count != Property.ArraySize)
                {
                    //InspectorDebug.Log($"OnComboBoxSelectionChanged {selection.Count} != {Property.ArraySize}");
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
                    //InspectorDebug.Log($"Matcing {matching}");
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

            // Read before the write. This used to call Property.GetValue() again afterwards, so listeners
            // were handed the new value as the old one.
            var previous = Property.GetValue();
            CommitWidgetValue(previous, Property.IsArray ? selection : selection[0], null);
        }

        public void OnNameSelectionChanged(VisualElement sender, string oldAqn, string newAqn)
        {
            CommitWidgetValue(oldAqn, newAqn, null);
        }
        
        /// <summary>
        /// Whether VSL could rebuild this type from a document, and so whether the picker should offer it.
        /// </summary>
        /// <remarks>
        /// The same test <c>VslReflection.CreateInstance</c> makes — a non-abstract type with a
        /// parameterless constructor of any accessibility. Offering one it cannot construct would let you
        /// pick a value that fails to load back.
        /// </remarks>
        private static bool IsVslConstructable(Type type)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            return !type.IsAbstract
                   && !type.IsInterface
                   && !type.IsGenericTypeDefinition
                   && (type.IsValueType || type.GetConstructor(Any, null, Type.EmptyTypes, null) != null);
        }

        public void OnSerializeReferenceSelectionChanged(ComboBox<object> comboBox, List<int> selectedIndices)
        {
            List<Type> selection = new();
            foreach (var idx in selectedIndices)
            {
                selection.Add((Type)comboBox.Values[idx]);
            }
            //InspectorDebug.Log($"OnComboBoxSelectionChanged - New Selections: {selection.Count}");

            bool allMatching = true;
            if (Property.IsArray)
            {
                if (selection.Count != Property.ArraySize)
                {
                    //InspectorDebug.Log($"OnComboBoxSelectionChanged {selection.Count} != {Property.ArraySize}");
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
                    //InspectorDebug.Log($"Matcing {matching}");
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

            // var comboParent = (VisualElement)comboBox.GetFirstAncestorOfType<IElementGroup>();
            var comboParent = comboBox.parent;
            var comboIdx = comboParent.IndexOf(comboBox);
            for (int i = comboParent.childCount - 1; i > comboIdx; i--)
            {
                comboParent.RemoveAt(i);
            }
            // if (foldout.childCount == 2)
            // {
            //     foldout.RemoveAt(1);
            // }
            if (newObj != null)
            {
                new InspectorTreeRootElement(newObj, newObj.GetType(), Property.InspectorObject).AttachTo(comboParent);
            }

            InspectorDebug.Log($"OnSerializeReferenceSelectionChanged {prevObj} -> {newObj}");
            MarkDirtyWithValue(prevObj, newObj);
            ValueChanged.Invoke(this, prevObj, newObj);
        }
        #endregion

        #region - Decorators -
        private void DrawLabel()
        {
            if (Property.TryGetAttribute<LabelAttribute>(out var labelAtr))
            {
                var label = _internalLabel;
                if (labelAtr.HasLabelResolver)
                {
                    ResolverBinding.Bind<string>(this, Property, labelAtr.LabelResolver, s => label.text = s);
                }
                else
                {
                    label.text = labelAtr.Label;
                }

                if (labelAtr.HasLabelColorResolver)
                {
                    ResolverBinding.Bind<Color>(this, Property, labelAtr.LabelColorResolver, c => label.style.color = c);
                }
                else
                {
                    label.style.color = labelAtr.LabelColor;
                }

                if (labelAtr.HasIcon)
                {
                    var image = new Image
                    {
                        image = EditorGUIUtility.IconContent(labelAtr.Icon).image,
                        scaleMode = ScaleMode.ScaleToFit,
                        pickingMode = PickingMode.Ignore
                    };
                    image.style.alignSelf = Align.FlexEnd;

                    if (labelAtr.HasIconColorResolver)
                    {
                        ResolverBinding.Bind<Color>(this, Property, labelAtr.IconColorResolver, c => image.tintColor = c);
                    }
                    else
                    {
                        image.tintColor = labelAtr.IconColor.value;
                    }

                    label.Add(image);
                }
            }

            if (Property.TryGetAttribute<InheritedLabel>(out var inheritedLabelAtr))
            {
                // Bound against the parent property, so the resolver is looked for on the type that
                // declares it — the same place the DisplayName fallback below comes from. The old code
                // paired that parent target with this property's own parent type, which is one level
                // deeper and could only ever have matched when the two happened to coincide.
                var label = _internalLabel;
                if (inheritedLabelAtr.HasLabelResolver)
                {
                    ResolverBinding.Bind<string>(this, Property.ParentProperty, inheritedLabelAtr.LabelResolver, s => label.text = s);
                }
                else
                {
                    label.text = Property.ParentProperty.DisplayName;
                }

                if (inheritedLabelAtr.HasLabelColorResolver)
                {
                    ResolverBinding.Bind<Color>(this, Property.ParentProperty, inheritedLabelAtr.LabelColorResolver, c => label.style.color = c);
                }
                else
                {
                    label.style.color = inheritedLabelAtr.LabelColor;
                }

                if (inheritedLabelAtr.HasIcon)
                {
                    var image = new Image
                    {
                        image = EditorGUIUtility.IconContent(inheritedLabelAtr.Icon).image,
                        scaleMode = ScaleMode.ScaleToFit,
                        pickingMode = PickingMode.Ignore
                    };
                    image.style.alignSelf = Align.FlexEnd;

                    if (inheritedLabelAtr.HasIconColorResolver)
                    {
                        ResolverBinding.Bind<Color>(this, Property.ParentProperty, inheritedLabelAtr.IconColorResolver, c => image.tintColor = c);
                    }
                    else
                    {
                        image.tintColor = inheritedLabelAtr.IconColor.value;
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
            var label = _internalLabel;
            if (label == null)
            {
                return;
            }

            if (Property.TryGetAttribute<RichTextTooltipAttribute>(out var rtAtr))
            {
                label.tooltip = rtAtr.Tooltip;
                return;
            }

            // The comment VSL writes into the document is the member's documentation; the inspector
            // shows the same words rather than asking for them twice.
            if (Property.TryGetAttribute<VslCommentAttribute>(out var comment) && !string.IsNullOrEmpty(comment.Comment))
            {
                label.tooltip = comment.Comment;
            }
        }

        private void DrawHelpUrl()
        {
            if (!Property.TryGetAttribute<HelpUrlAttribute>(out var helpUrlAtr))
            {
                // This is a hack to get the width of labels working correctly, because Unity's Toggle is not good.
                if (_internalField is Toggle)
                {
                    _internalLabel.Add(new VisualElement());
                    _internalLabel.style.alignItems = Align.FlexEnd;
                    _internalLabel.style.justifyContent = Justify.Center;
                }
                return;
            }

            if (_internalField is PaginatedList paginatedList)
            {
                var helpView = new HelpUrlView(helpUrlAtr)
                {
                    style =
                    {
                        alignSelf = Align.Center,
                        marginLeft = 3,
                    }
                };
                paginatedList.Header.Insert(1, helpView);
            }
            else if (_internalLabel != null)
            {
                _internalLabel.Add(new HelpUrlView(helpUrlAtr));
                _internalLabel.style.alignItems = Align.FlexEnd;
                _internalLabel.style.justifyContent = Justify.Center;
            }
        }

        private void DrawReadOnly()
        {
            if (Property.HasAttribute<ReadOnlyAttribute>())
            {
                // _internalLabel?.SetEnabled(false);
                // _internalField.SetEnabled(false);
                foreach (var visualElement in _internalField.Children())
                {
                    visualElement.SetEnabled(false);
                }
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
                style.flexDirection = FlexDirection.Row;
                hierarchy[0].style.flexGrow = 1f;
                filePathTextField.Add(inlineButton);
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
                style.flexDirection = FlexDirection.Row;
                hierarchy[0].style.flexGrow = 1f;
                folderPathTextField.Add(inlineButton);
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
                        string tooltipText = TooltipMarkup.FormatString(atr.Tooltip);
                        var inlineButton = new VisualElement()
                        {
                            // text = atr.Label,
                            tooltip = tooltipText,
                            style =
                            {
                                flexDirection = FlexDirection.Row,
                                paddingLeft = 3,
                                paddingRight = 3
                            }
                        }.WithManipulator(new ButtonManipulator(/*Button.ussClassName*/)
                        {
                            IgnoreDisabled = true,
                        }.WithOnClick(ClickTypes.ClickOnDown, _ =>
                        {
                            methodInfo.Invoke(Property.GetParentObject(), null);
                            RefreshFromSource();
                            if (atr.RebuildTree)
                            {
                                Owner.Root.RebuildAndRedraw();
                            }
                        }).WithActivator(EventModifiers.None, MouseButton.LeftMouse));

                        inlineButton.AddToClassList(Button.ussClassName);
                        inlineButton.AddStylesheetFromResourcePath("Styles/InlineButton");
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
                                ResolverBinding.Bind<Color>(this, Property, atr.TintResolver, c => image.tintColor = c);
                            }

                            inlineButton.Add(image);
                        }

                        if (atr.Label != string.Empty)
                        {
                            var textField = new TextElement()
                            {
                                text = atr.Label,
                            };
                            inlineButton.Add(textField);
                        }

                        if (_internalField is PaginatedList list)
                        {
                            list.Header.Add(inlineButton);
                        }
                        else
                        {
                            style.flexDirection = FlexDirection.Row;
                            hierarchy[0].style.flexGrow = 1f;
                            hierarchy[0].Add(inlineButton);
                        }
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
                            RefreshFromSource();
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
                                Owner.Root.RebuildAndRedraw();
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
            if (Property.TryGetAttribute<AutoReferenceAttribute>(out var atr)
                && Property.SerializedPropertyType == SerializedPropertyType.ObjectReference
                && !(Object)Property.GetValue()
                && Property.InspectorObject.Object is Component component)
            {
                // Has A Required Interface Attribute, So Check The Type of The Interface
                if (Property.TryGetAttribute<RequireInterfaceAttribute>(out var requireInterfaceAtr))
                {
                    var comp = component.GetComponent(requireInterfaceAtr.InterfaceType);
                    if (!comp && atr.SearchChildren)
                    {
                        comp = component.GetComponentInChildren(requireInterfaceAtr.InterfaceType, true);
                    }

                    if (!comp && atr.SearchParents)
                    {
                        comp = component.GetComponentInParent(requireInterfaceAtr.InterfaceType, true);
                    }
                
                    schedule.Execute(() => MarkDirtyWithValue(comp, comp));
                }
                else
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
                
                    schedule.Execute(() => MarkDirtyWithValue(comp, comp));
                }
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
                            Owner.Root.RebuildAndRedraw();
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
                
                var inlineButton = new Button(() => ShowChildObjectPicker(monoBehaviour, atr))
                {
                    tooltip = "Pick from this object's children",
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

        /// <summary>
        /// Lists the components of this field's type found beneath <paramref name="root"/>, keyed by their
        /// path relative to it so two children of the same name stay distinguishable.
        /// </summary>
        private void ShowChildObjectPicker(Component root, ChildGameObjectsOnlyAttribute atr)
        {
            var candidates = root.GetComponentsInChildren(PropertyType, true);
            var models = new List<GenericSearchModel>(candidates.Length + 1);
            var byName = new Dictionary<string, Object>(candidates.Length + 1);

            const string noneName = "None";
            models.Add(new GenericSearchModel(noneName, string.Empty, noneName, false));
            byName[noneName] = null;

            foreach (var candidate in candidates)
            {
                if (!atr.IncludeSelf && candidate.transform == root.transform)
                {
                    continue;
                }

                var path = GetRelativePath(root.transform, candidate.transform);
                // Two components of the same type on one object would collide on path alone.
                var name = path;
                for (var suffix = 2; byName.ContainsKey(name); suffix++)
                {
                    name = $"{path} ({suffix})";
                }

                models.Add(new GenericSearchModel(name, string.Empty, name));
                byName[name] = candidate;
            }

            var provider = new GenericSearchProvider(selected =>
            {
                if (selected == null || selected.Length == 0 || !byName.TryGetValue(selected[0].Name, out var picked))
                {
                    return;
                }

                var previous = Property.GetValue();
                MarkDirtyWithValue(previous, picked);
                ValueChanged.Invoke(this, previous, picked);
                RefreshFromSource();
            }, models, false);

            var worldRect = GUIUtility.GUIToScreenRect(worldBound);
            var position = new Vector2(worldRect.position.x + 24, worldRect.position.y + worldBound.height + 16);
            GenericSearchWindow.Show(position, position, provider, true, PropertyType.Name);
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
            {
                return target.name;
            }

            var path = target.name;
            for (var current = target.parent; current != null && current != root; current = current.parent)
            {
                path = $"{current.name}/{path}";
            }

            return path;
        }

        #endregion

        #region - Helper -
        private static void SplitTupleToDropdown(List<string> keys, List<object> values, List<string> tooltips, IEnumerable<DropdownModel> toConvert)
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
                var tooltip = model.Tooltip;

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
                tooltips.Add(tooltip ?? name);
            }
        }

        /// <summary>
        /// Kept as the entry point drawers already call. The conversion itself lives on
        /// <see cref="InspectorTreeProperty"/>, alongside the reads it has to agree with.
        /// </summary>
        public static object CastToType(object obj, Type targetType)
        {
            return InspectorTreeProperty.CastToType(obj, targetType);
        }

        public static void StyleLabel(VisualElement element)
        {
            if (element is not Label label)
            {
                label = element.Q<Label>();
            }
            if (label == null)
            {
                return;
            }

            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.minWidth = new StyleLength(new Length(33, LengthUnit.Percent));
            label.style.maxWidth = new StyleLength(new Length(33, LengthUnit.Percent));
        }

        public static VisualElement DrawSerializableReference(string displayName, VisualElement outerElement, object currentType, Type referenceType,
            Action<ComboBox<object>, List<int>> OnReferenceChanged, InspectorTreeObject parent)
        {
            List<string> keys = new();
            List<object> values = new();
            List<string> tooltips = new();
            keys.Add("Null");
            values.Add(null);
            var types = ReflectionUtility.GetAssignableTypesOf(referenceType)
                .Select(t => new DropdownModel(t.Namespace, t.Name, t, t.GetCustomAttribute<DropdownTooltipAttribute>()?.Tooltip ?? t.Name));
            SplitTupleToDropdown(keys, values, tooltips, types);

            var current = currentType;
            var cIdx = current == null ? 0 : Mathf.Max(0, values.IndexOf(current.GetType()));
            var comboBox = new ComboBox<object>(displayName, cIdx, keys, values, null, false);

            comboBox.SelectionChanged += OnReferenceChanged;
            outerElement.Add(comboBox);
            if (cIdx > 0)
            {
                new InspectorTreeRootElement(current, current.GetType(), parent).AttachTo(outerElement);
            }

            return outerElement;
        }

        #endregion
    }
}
