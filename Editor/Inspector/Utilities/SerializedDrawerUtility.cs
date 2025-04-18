using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.Properties;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
using Label = UnityEngine.UIElements.Label;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public abstract class ScriptableWrapper : ScriptableObject
    {
        public abstract void Set(object obj);
        public abstract object Get();
    }
    
    public interface IScriptableWrapper
    {
        void Set(object obj);
        object Get();
    }
    
    public class ScriptableWrapper<T> : ScriptableObject, IScriptableWrapper
    {
        [HideLabel]
        public T Content;

        public void Set(object obj)
        {
            Content = (T)obj;
        }

        public object Get()
        {
            return Content;
        }
    }
    
    public static class ScriptableWrapperEmitter
    {
        private static readonly ModuleBuilder s_ModuleBuilder;
        private static readonly Dictionary<Type, Type> s_WrappedTypes = new();

        static ScriptableWrapperEmitter()
        {
            AssemblyName assemblyName = new AssemblyName("DynamicScriptableWrappers");
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            s_ModuleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name);
            s_WrappedTypes.Clear();
        }

        public static Type GenerateScriptableWrapperType(Type wrappedType)
        {
            string typeName = $"{wrappedType.Name}ScriptableWrapper";

            // Check if the type already exists
            Type existingType = Type.GetType(typeName);
            if (existingType != null)
            {
                return existingType;
            }

            // Define a new public class that inherits from ScriptableWrapper<T>
            TypeBuilder typeBuilder = s_ModuleBuilder.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(ScriptableWrapper<>).MakeGenericType(wrappedType)
            );

            return typeBuilder.CreateType();
        }

        public static ScriptableObject CreateInstanceForType(Type wrappedType)
        {
            if (s_WrappedTypes.TryGetValue(wrappedType, out var type))
            {
                var so = ScriptableObject.CreateInstance(type);
                if (!so)
                {
                    return null;
                }
                so.hideFlags = HideFlags.HideAndDontSave;
                return so;
            }

            Type wrapperType = GenerateScriptableWrapperType(wrappedType);
            if (wrapperType == null)
            {
                s_WrappedTypes[wrappedType] = null;
                return null;
            }

            {
                s_WrappedTypes[wrappedType] = wrapperType;
                var so = ScriptableObject.CreateInstance(wrapperType);
                if (!so)
                {
                    return null;
                }
                so.hideFlags = HideFlags.HideAndDontSave;
                return so;
            }
        }
    }
    
    
    public static class SerializedDrawerUtility
    {
        #region - Groups -
        public static VisualElement DrawGroupElement(VaporGroupAttribute groupAttribute)
        {
            var ve = groupAttribute.Type switch
            {
                UIGroupType.Horizontal => _DrawHorizontalGroupNode((HorizontalGroupAttribute)groupAttribute),
                UIGroupType.Vertical => _DrawVerticalGroupNode((VerticalGroupAttribute)groupAttribute),
                UIGroupType.Foldout => _DrawFoldoutGroupNode((FoldoutGroupAttribute)groupAttribute),
                UIGroupType.Box => _DrawBoxGroupNode((BoxGroupAttribute)groupAttribute),
                UIGroupType.Tab => _DrawTabGroupNode((TabGroupAttribute)groupAttribute),
                UIGroupType.Title => _DrawTitleGroupNode((TitleGroupAttribute)groupAttribute),
                _ => throw new ArgumentOutOfRangeException()
            };
            return ve;

            VisualElement _DrawHorizontalGroupNode(HorizontalGroupAttribute attribute)
            {
                var horizontal = new StyledHorizontalGroup(attribute.UseSingleLabel ? attribute.SingleLabel : null, attribute.SingleLabelWidth)
                {
                    name = attribute.GroupName
                };
                return horizontal;
            }

            VisualElement _DrawVerticalGroupNode(VerticalGroupAttribute attribute)
            {
                var vertical = new StyledVerticalGroup
                {
                    name = attribute.GroupName
                };
                return vertical;
            }

            VisualElement _DrawFoldoutGroupNode(FoldoutGroupAttribute attribute)
            {
                var foldout = new StyledFoldout(attribute.Header)
                {
                    name = attribute.GroupName
                };
                return foldout;
            }

            VisualElement _DrawBoxGroupNode(BoxGroupAttribute attribute)
            {
                var box = new StyledHeaderBox(attribute.Header)
                {
                    name = attribute.GroupName
                };
                return box;
            }

            VisualElement _DrawTabGroupNode(TabGroupAttribute attribute)
            {
                var tabs = new StyledTabGroup(attribute)
                {
                    name = attribute.GroupName
                };
                return tabs;
            }

            VisualElement _DrawTitleGroupNode(TitleGroupAttribute attribute)
            {
                var title = new StyledTitleGroup(attribute)
                {
                    name = attribute.GroupName
                };
                return title;
            }
        }

        private static void OnNodeGroupBuilt(GeometryChangedEvent evt)
        {
            var element = (VisualElement)evt.target;
            if (element.userData is not InspectorTreeGroupElement node)
            {
                return;
            }

            DrawDecorators(element, node);
            DrawConditionals(element, node);
        }
        #endregion

        #region - Fields -
        public static VisualElement DrawVaporField(InspectorTreeElement parentTreeElement, InspectorTreeProperty property, bool wrapVertically)
        {
            var field = new TreePropertyField(property, parentTreeElement)
            {
            };
            if (!field.IsValid)
            {
                return null;
            }
            if (wrapVertically)
            {
                var vertical = new VisualElement();
                vertical.Add(field);
                return vertical;
            }
            else
            {
                return field;
            }
        }
        #endregion

        #region - Decorators -
        private static void DrawDecorators(VisualElement visualElement, InspectorTreeGroupElement node)
        {
            if (node.TryGetAttribute<BackgroundColorAttribute>(out var backgroundColor))
            {
                visualElement.style.backgroundColor = backgroundColor.BackgroundColor;
            }

            if (node.TryGetAttribute<MarginsAttribute>(out var margins))
            {
                visualElement.style.marginBottom = margins.Bottom;

                visualElement.style.marginTop = margins.Top;

                visualElement.style.marginLeft = margins.Left;

                visualElement.style.marginRight = margins.Right;
            }

            if (node.TryGetAttribute<PaddingAttribute>(out var padding))
            {
                visualElement.style.paddingBottom = padding.Bottom;

                visualElement.style.paddingTop = padding.Top;

                visualElement.style.paddingLeft = padding.Left;

                visualElement.style.paddingRight = padding.Right;
            }

            if (node.TryGetAttribute<BordersAttribute>(out var borders))
            {
                visualElement.style.borderBottomWidth = borders.Bottom;
                visualElement.style.borderBottomColor = borders.Color;

                visualElement.style.borderTopWidth = borders.Top;
                visualElement.style.borderTopColor = borders.Color;

                visualElement.style.borderLeftWidth = borders.Left;
                visualElement.style.borderLeftColor = borders.Color;

                visualElement.style.borderRightWidth = borders.Right;
                visualElement.style.borderRightColor = borders.Color;

                visualElement.style.borderBottomLeftRadius = borders.Roundness;
                visualElement.style.borderBottomRightRadius = borders.Roundness;
                visualElement.style.borderTopLeftRadius = borders.Roundness;
                visualElement.style.borderTopRightRadius = borders.Roundness;
            }
        }

        private static void DrawConditionals(VisualElement visualElement, InspectorTreeGroupElement node)
        {
            //var type = node.Source.Type;
            //if (node.TryGetAttribute<ShowIfAttribute>(out var showIf))
            //{
            //    var resolverContainerProp = new SerializedResolverContainerType<bool>(node.Source, ReflectionUtility.GetMember(type, showIf.Resolver), b => visualElement.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
            //    node.VisualNode.AddResolver(resolverContainerProp);
            //}

            //if (node.TryGetAttribute<HideIfAttribute>(out var hideIf))
            //{
            //    var resolverContainerProp = new SerializedResolverContainerType<bool>(node.Source, ReflectionUtility.GetMember(type, hideIf.Resolver), b => visualElement.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
            //    node.VisualNode.AddResolver(resolverContainerProp);
            //}

            //if (node.TryGetAttribute<DisableIfAttribute>(out var disableIf))
            //{
            //    var resolverContainerProp = new SerializedResolverContainerType<bool>(node.Source, ReflectionUtility.GetMember(type, disableIf.Resolver), b => visualElement.SetEnabled(!b));
            //    node.VisualNode.AddResolver(resolverContainerProp);
            //}

            //if (node.TryGetAttribute<EnableIfAttribute>(out var enableIf))
            //{
            //    var resolverContainerProp = new SerializedResolverContainerType<bool>(node.Source, ReflectionUtility.GetMember(type, enableIf.Resolver), b => visualElement.SetEnabled(b));
            //    node.VisualNode.AddResolver(resolverContainerProp);
            //}

            //if (node.HasAttribute<HideInEditorModeAttribute>())
            //{
            //    var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
            //        () => EditorApplication.isPlaying,
            //        b => visualElement.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
            //    node.VisualNode.AddResolver(resolverContainerFunc);
            //}

            //if (node.HasAttribute<HideInPlayModeAttribute>())
            //{
            //    var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
            //        () => EditorApplication.isPlaying,
            //        b => visualElement.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
            //    node.VisualNode.AddResolver(resolverContainerFunc);
            //}

            //if (node.HasAttribute<DisableInEditorModeAttribute>())
            //{
            //    var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
            //        () => EditorApplication.isPlaying,
            //        b => visualElement.SetEnabled(b));
            //    node.VisualNode.AddResolver(resolverContainerFunc);
            //}

            //if (node.HasAttribute<DisableInPlayModeAttribute>())
            //{
            //    var resolverContainerFunc = new SerializedResolverContainerAction<bool>(
            //        () => EditorApplication.isPlaying,
            //        b => visualElement.SetEnabled(!b));
            //    node.VisualNode.AddResolver(resolverContainerFunc);
            //}
        }
        #endregion

        #region Value Type Drawers
        public static VisualElement DrawAny(object @object)
        {
            if (@object == null)
            {
                return new VisualElement();
            }
            
            var so = ScriptableWrapperEmitter.CreateInstanceForType(@object.GetType());
            if (so is not IScriptableWrapper wrapper)
            {
                return DrawFieldFromObject(@object, @object.GetType());
            }
            
            wrapper.Set(@object);
            var serializedObject = new SerializedObject(so);

            InspectorTreeObject ito = new(serializedObject);
            InspectorTreeRootElement root = new(ito);

            var ve = new VisualElement()
            {
                userData = so
            };
            ve.RegisterCallbackOnce<DetachFromPanelEvent>(evt => Object.DestroyImmediate(((VisualElement)evt.target).userData as ScriptableObject));
            root.DrawToScreen(ve);
            return ve;
        }
        
        public static VisualElement DrawFieldFromObject(object @object, Type fieldType)
        {
            if (@object == null || fieldType == null)
            {
                return new VisualElement();
            }

            InspectorTreeObject ito = new(@object, fieldType);
            InspectorTreeRootElement root = new(ito);

            var ve = new VisualElement();
            root.DrawToScreen(ve);
            return ve;
        }
        
        public static VisualElement DrawManagedReferenceAsField(InspectorTreeElement parentTreeElement, InspectorTreeProperty property, bool wrapVertically)
        {
            var target = property.GetValue();
            Type type;
            if(target == null)
            {
                if (property.TryGetAttribute<TypeResolverAttribute>(out var typeResolver))
                {
                    var pType = property.IsArrayElement ? property.ParentProperty.ParentType : property.ParentType;
                    var method = ReflectionUtility.GetMethod(pType, typeResolver.Resolver);
                    if (method.IsStatic)
                    {
                        type = (Type)method.Invoke(null, null);
                    }
                    else
                    {
                        type = (Type)method.Invoke(property.GetParentObject(), null);
                    }
                }
                else
                {
                    return new VisualElement();
                }
            }
            else
            {
                type = target.GetType();
            }
            var propertyType = TypeToSerializedPropertyType(type);
            if (propertyType == SerializedPropertyType.ManagedReference)
            {
                InspectorTreeObject ito = new(target, type);
                InspectorTreeRootElement root = new(ito);

                var ve = new VisualElement();
                root.DrawToScreen(ve);
                if (!wrapVertically)
                {
                    return ve;
                }

                var vertical = new VisualElement();
                vertical.Add(root);
                return vertical;

            }
            else
            {
                var field = new TreePropertyField(property, type, parentTreeElement);
                if (!field.IsValid)
                {
                    return null;
                }

                if (!wrapVertically)
                {
                    return field;
                }

                var vertical = new VisualElement();
                vertical.Add(field);
                return vertical;
            }
        }

        public static VisualElement DrawFieldFromObjectAndField(object parent, object target, FieldInfo fieldInfo)
        {
            var type = target.GetType();
            var propertyType = TypeToSerializedPropertyType(type);
            if (propertyType == SerializedPropertyType.ManagedReference)
            {
                InspectorTreeObject ito = new(target, type);
                InspectorTreeRootElement root = new(ito);

                var ve = new VisualElement();
                root.DrawToScreen(ve);
                return ve;
            }
            
            return DrawFieldFromType(parent, type, fieldInfo, true);
        }
        
        public static VisualElement DrawFieldFromType(object source, Type type, FieldInfo fieldInfo, bool setToInitialValue = false)
        {
            var propertyType = TypeToSerializedPropertyType(type);
            var numericType = TypeToSerializedPropertyNumericType(type);
            var niceName = ObjectNames.NicifyVariableName(fieldInfo.Name);
            switch (propertyType)
            {
                case SerializedPropertyType.Generic:
                    return new Label("Generic Types Not Supported");
                case SerializedPropertyType.Integer:
                    switch (numericType)
                    {
                        case SerializedPropertyNumericType.UInt32:
                        {
                            var field = new UnsignedIntegerField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                field.SetValueWithoutNotify((uint)fieldInfo.GetValue(source));
                            }

                            SetupDefaultBinding(field, source, fieldInfo.Name);
                            return field;
                        }
                        case SerializedPropertyNumericType.Int64:
                        {
                            var field = new LongField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                field.SetValueWithoutNotify((long)fieldInfo.GetValue(source));
                            }

                            SetupDefaultBinding(field, source, fieldInfo.Name);
                            return field;
                        }
                        case SerializedPropertyNumericType.UInt64:
                        {
                            var field = new UnsignedLongField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                field.SetValueWithoutNotify((ulong)fieldInfo.GetValue(source));
                            }

                            SetupDefaultBinding(field, source, fieldInfo.Name);
                            return field;
                        }
                        case SerializedPropertyNumericType.Int8:
                        {
                            var field = new IntegerField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                var converted = Math.Clamp(Convert.ToInt32(fieldInfo.GetValue(source)), sbyte.MinValue, sbyte.MaxValue);
                                field.SetValueWithoutNotify(converted);
                            }

                            var binding = SetupDefaultBinding(field, source, fieldInfo.Name);
                            binding.sourceToUiConverters.AddConverter<object, int>((ref object value) =>
                                Math.Clamp(Convert.ToInt32(value), sbyte.MinValue, sbyte.MaxValue));
                            binding.uiToSourceConverters.AddConverter<int, object>((ref int value) =>
                                Convert.ToSByte(Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue)));
                            return field;
                        }
                        case SerializedPropertyNumericType.UInt8:
                        {
                            var field = new IntegerField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                var converted = Math.Clamp(Convert.ToInt32(fieldInfo.GetValue(source)), byte.MinValue, byte.MaxValue);
                                field.SetValueWithoutNotify(converted);
                            }

                            var binding = SetupDefaultBinding(field, source, fieldInfo.Name);
                            binding.sourceToUiConverters.AddConverter<object, int>((ref object value) =>
                                Math.Clamp(Convert.ToInt32(value), byte.MinValue, byte.MaxValue));
                            binding.uiToSourceConverters.AddConverter<int, object>((ref int value) =>
                                Convert.ToByte(Math.Clamp(value, byte.MinValue, byte.MaxValue)));
                            return field;
                        }
                        case SerializedPropertyNumericType.Int16:
                        {
                            var field = new IntegerField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                var converted = Math.Clamp(Convert.ToInt32(fieldInfo.GetValue(source)), short.MinValue, short.MaxValue);
                                field.SetValueWithoutNotify(converted);
                            }

                            var binding = SetupDefaultBinding(field, source, fieldInfo.Name);
                            binding.sourceToUiConverters.AddConverter<object, int>((ref object value) =>
                                Math.Clamp(Convert.ToInt32(value), short.MinValue, short.MaxValue));
                            binding.uiToSourceConverters.AddConverter<int, object>((ref int value) =>
                                Convert.ToInt16(Math.Clamp(value, short.MinValue, short.MaxValue)));
                            return field;
                        }
                        case SerializedPropertyNumericType.UInt16:
                        {
                            var field = new IntegerField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                var converted = Math.Clamp(Convert.ToInt32(fieldInfo.GetValue(source)), ushort.MinValue, ushort.MaxValue);
                                field.SetValueWithoutNotify(converted);
                            }

                            var binding = SetupDefaultBinding(field, source, fieldInfo.Name);
                            binding.sourceToUiConverters.AddConverter<object, int>((ref object value) =>
                                Math.Clamp(Convert.ToInt32(value), ushort.MinValue, ushort.MaxValue));
                            binding.uiToSourceConverters.AddConverter<int, object>((ref int value) =>
                                Convert.ToUInt16(Math.Clamp(value, ushort.MinValue, ushort.MaxValue)));
                            return field;
                        }
                        case SerializedPropertyNumericType.Int32:
                        default:
                        {
                            var field = new IntegerField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                field.SetValueWithoutNotify((int)fieldInfo.GetValue(source));
                            }

                            SetupDefaultBinding(field, source, fieldInfo.Name);
                            return field;
                        }
                    }
                case SerializedPropertyType.Boolean:
                {
                    var field = new Toggle()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((bool)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Float:
                    switch (numericType)
                    {
                        case SerializedPropertyNumericType.Double:
                        {
                            var field = new DoubleField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                field.SetValueWithoutNotify((double)fieldInfo.GetValue(source));
                            }

                            SetupDefaultBinding(field, source, fieldInfo.Name);
                            return field;
                        }

                        default:
                        {
                            var field = new FloatField()
                            {
                                label = niceName,
                                isDelayed = false,
                            };
                            if (setToInitialValue)
                            {
                                field.SetValueWithoutNotify((float)fieldInfo.GetValue(source));
                            }

                            SetupDefaultBinding(field, source, fieldInfo.Name);
                            return field;
                        }
                    }
                case SerializedPropertyType.String:
                {
                    var field = new TextField()
                    {
                        label = niceName,
                        isDelayed = false,
                        maxLength = -1,
                    };

                    var multilineAtr = fieldInfo.GetCustomAttribute<TextAreaAttribute>();
                    if (multilineAtr != null)
                    {
                        field.multiline = true;
                        field.verticalScrollerVisibility = ScrollerVisibility.Auto;
                        field.style.minHeight = 14 * multilineAtr.minLines;
                        field.style.height = new StyleLength(StyleKeyword.Auto);
                        field.style.maxHeight = 14 * multilineAtr.maxLines;
                    }

                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((string)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Color:
                {
                    var field = new ColorField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Color)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var field = new ObjectField()
                    {
                        label = niceName,
                        objectType = type,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Object)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.LayerMask:
                {
                    var field = new LayerMaskField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        var mask = (LayerMask)fieldInfo.GetValue(source);
                        field.SetValueWithoutNotify(mask.value);
                    }

                    var binding = SetupDefaultBinding(field, source, fieldInfo.Name);
                    binding.sourceToUiConverters.AddConverter<object, int>((ref object value) => ((LayerMask)value).value);
                    binding.uiToSourceConverters.AddConverter<int, object>((ref int value) => (object)((LayerMask)value));
                    return field;
                }
                case SerializedPropertyType.Enum:
                {
                    if (type.IsDefined(typeof(FlagsAttribute), false))
                    {
                        var field = new EnumFlagsField((Enum)fieldInfo.GetValue(source))
                        {
                            label = niceName,
                        };
                        if (setToInitialValue)
                        {
                            field.SetValueWithoutNotify((Enum)fieldInfo.GetValue(source));
                        }

                        SetupDefaultBinding(field, source, fieldInfo.Name);
                        return field;
                    }
                    else
                    {
                        var field = new EnumField((Enum)fieldInfo.GetValue(source))
                        {
                            label = niceName,
                        };
                        if (setToInitialValue)
                        {
                            field.SetValueWithoutNotify((Enum)fieldInfo.GetValue(source));
                        }

                        SetupDefaultBinding(field, source, fieldInfo.Name);
                        return field;
                    }
                }
                case SerializedPropertyType.Vector2:
                {
                    var field = new Vector2Field()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Vector2)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Vector3:
                {
                    var field = new Vector3Field()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Vector3)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Vector4:
                {
                    var field = new Vector4Field()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Vector4)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Rect:
                {
                    var field = new RectField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Rect)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Character:
                {
                    var field = new TextField()
                    {
                        label = niceName,
                        isDelayed = false,
                        maxLength = 1,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify(Convert.ToString(fieldInfo.GetValue(source)));
                    }

                    var binding = SetupDefaultBinding(field, source, fieldInfo.Name);
                    binding.sourceToUiConverters.AddConverter<object, string>((ref object value) => Convert.ToString(value));
                    binding.uiToSourceConverters.AddConverter<string, object>((ref string value) => value.EmptyOrNull() ? '\0' : value[0]);
                    return field;
                }
                case SerializedPropertyType.AnimationCurve:
                {
                    var field = new CurveField()
                    {
                        label = niceName,
                        userData = (source, fieldInfo),
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((AnimationCurve)fieldInfo.GetValue(source));
                    }

                    field.RegisterValueChangedCallback(evt =>
                    {
                        var f = (CurveField)evt.target;
                        (object source, FieldInfo info) data = ((object source, FieldInfo info))f.userData;
                        data.info.SetValue(data.source, evt.newValue);
                    });
                    return field;
                }
                case SerializedPropertyType.Bounds:
                {
                    var field = new BoundsField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Bounds)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Gradient:
                {
                    var field = new GradientField()
                    {
                        label = niceName,
                        userData = (source, fieldInfo),
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Gradient)fieldInfo.GetValue(source));
                    }

                    field.RegisterValueChangedCallback(evt =>
                    {
                        var f = (GradientField)evt.target;
                        (object source, FieldInfo info) data = ((object source, FieldInfo info))f.userData;
                        data.info.SetValue(data.source, evt.newValue);
                    });
                    return field;
                }
                case SerializedPropertyType.Quaternion:
                    return new Label($"Undefined Type: {type}");
                case SerializedPropertyType.ExposedReference:
                    return new Label($"Undefined Type: {type}");
                case SerializedPropertyType.Vector2Int:
                {
                    var field = new Vector2IntField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Vector2Int)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.Vector3Int:
                {
                    var field = new Vector3IntField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Vector3Int)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.RectInt:
                {
                    var field = new RectIntField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((RectInt)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.BoundsInt:
                {
                    var field = new BoundsIntField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((BoundsInt)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.ManagedReference:
                    return DrawFieldFromObject(fieldInfo.GetValue(source), type);
                case SerializedPropertyType.Hash128:
                {
                    var field = new Hash128Field()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        field.SetValueWithoutNotify((Hash128)fieldInfo.GetValue(source));
                    }

                    SetupDefaultBinding(field, source, fieldInfo.Name);
                    return field;
                }
                case SerializedPropertyType.RenderingLayerMask:
                {
                    var field = new RenderingLayerMaskField()
                    {
                        label = niceName,
                    };
                    if (setToInitialValue)
                    {
                        var mask = (RenderingLayerMask)fieldInfo.GetValue(source);
                        field.SetValueWithoutNotify(mask.value);
                    }

                    var binding = SetupDefaultBinding(field, source, fieldInfo.Name);
                    binding.sourceToUiConverters.AddConverter<object, uint>((ref object value) => ((RenderingLayerMask)value).value);
                    binding.uiToSourceConverters.AddConverter<uint, object>((ref uint value) => (object)((RenderingLayerMask)value));
                    return field;
                }
                default:
                    return new Label($"Undefined Type: {type}");
            }
        }

        private static DataBinding SetupDefaultBinding(VisualElement field, object dataSource, string fieldName)
        {
            //Debug.Log($"Binding: {Property.GetParentObject()} - {Property.PropertyName}");
            var fieldBinding = new DataBinding
            {
                dataSource = dataSource,
                dataSourcePath = new PropertyPath(fieldName),
                bindingMode = BindingMode.TwoWay,
                updateTrigger = BindingUpdateTrigger.OnSourceChanged
            };
            field.SetBinding("value", fieldBinding);
            return fieldBinding;
        }

        #endregion

        #region - Helpers -
        public static bool HasCustomPropertyDrawer(Type type, bool isManagedReference)
        {
            var drawerType = ScriptAttributeUtilityReflection.GetDrawerTypeForType(type, isManagedReference);
            return drawerType != null;
        }

        public static bool TryGetCustomPropertyDrawer(Type type, bool isManagedReference, out PropertyDrawer propertyDrawer)
        {
            var drawerType = ScriptAttributeUtilityReflection.GetDrawerTypeForType(type, isManagedReference);
            if (drawerType != null)
            {
                propertyDrawer = PropertyHandleReflection.CreatePropertyDrawerWithDefaultObjectReferences(drawerType);
                return propertyDrawer != null;
            }
            else
            {
                propertyDrawer = null;
                return false;
            }
        }

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

                    if (type.IsEnum)
                    {
                        return SerializedPropertyType.Enum;
                    }

                    if (type == typeof(Color))
                    {
                        return SerializedPropertyType.Color;
                    }

                    if (type == typeof(Object) || type.IsSubclassOf(typeof(Object)))
                    {
                        return SerializedPropertyType.ObjectReference;
                    }

                    if (type == typeof(LayerMask))
                    {
                        return SerializedPropertyType.LayerMask;
                    }

                    if (type == typeof(RenderingLayerMask))
                    {
                        return SerializedPropertyType.RenderingLayerMask;
                    }

                    if (type == typeof(Vector2))
                    {
                        return SerializedPropertyType.Vector2;
                    }

                    if (type == typeof(Vector3))
                    {
                        return SerializedPropertyType.Vector3;
                    }

                    if (type == typeof(Vector4))
                    {
                        return SerializedPropertyType.Vector4;
                    }

                    if (type == typeof(Rect))
                    {
                        return SerializedPropertyType.Rect;
                    }

                    if (type == typeof(AnimationCurve) || type.IsSubclassOf(typeof(AnimationCurve)))
                    {
                        return SerializedPropertyType.AnimationCurve;
                    }

                    if (type == typeof(Bounds))
                    {
                        return SerializedPropertyType.Bounds;
                    }

                    if (type == typeof(Gradient) || type.IsSubclassOf(typeof(Gradient)))
                    {
                        return SerializedPropertyType.Gradient;
                    }

                    if (type == typeof(Quaternion))
                    {
                        return SerializedPropertyType.Quaternion;
                    }

                    if (type == typeof(Vector2Int))
                    {
                        return SerializedPropertyType.Vector2Int;
                    }

                    if (type == typeof(Vector3Int))
                    {
                        return SerializedPropertyType.Vector3Int;
                    }

                    if (type == typeof(RectInt))
                    {
                        return SerializedPropertyType.RectInt;
                    }

                    if (type == typeof(BoundsInt))
                    {
                        return SerializedPropertyType.BoundsInt;
                    }

                    if (type == typeof(Hash128))
                    {
                        return SerializedPropertyType.Hash128;
                    }

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
        #endregion
    }
}
