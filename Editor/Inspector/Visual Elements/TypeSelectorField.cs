using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    
    public class TypeSelectorField : VisualElement
    {
        public delegate void TypeChangedSignature(TypeSelectorField field, Type previousType, Type currentType);
        public delegate void AssemblyQualifiedNameChangedSignature(TypeSelectorField field, string previousType, string currentType);
            
        private class TypeBuilder
        {
            public bool Complete => _index >= _genericArguments.Length;

            private readonly Type _genericType;
            private readonly Type[] _genericArguments;
            private int _index;
            private TypeBuilder _childBuilder;

            public TypeBuilder(Type genericType, Type[] genericArguments)
            {
                _genericType = genericType;
                _genericArguments = genericArguments;
                _index = 0;
            }

            public void AddType(Type type)
            {
                if (_childBuilder is { Complete: false })
                {
                    _childBuilder.AddType(type);
                    if (_childBuilder.Complete)
                    {
                        _genericArguments[_index] = _childBuilder.MakeType();
                        _index++;
                    }
                }
                else
                {
                    _genericArguments[_index] = type;
                    _index++;
                }
            }

            public Type MakeType()
            {
                return _genericType.MakeGenericType(_genericArguments);
            }

            public void StackGenericType(Type cachedGenericType, Type[] genericTypeArguments)
            {
                if (_childBuilder != null)
                {
                    _childBuilder.StackGenericType(cachedGenericType, genericTypeArguments);
                }
                else
                {
                    _childBuilder = new TypeBuilder(cachedGenericType, genericTypeArguments);
                }
            }

            public Type GetCurrentGenericTypeArgument()
            {
                return _childBuilder is { Complete: false } ? _childBuilder.GetCurrentGenericTypeArgument() : _genericType.GetGenericArguments()[_index];
            }

            public string GetCurrentPartialType()
            {
                string genericTypeName = _genericType.Name.Split('`')[0]; // Remove arity suffix
                string genericArgs = string.Join(", ", _genericArguments
                    .Take(_index) // Only take the assigned generic arguments
                    .Select(arg => arg != null ? arg.Name : "_")); // Placeholder for unfilled types

                // If there is a child builder, append its current partial type
                if (_childBuilder is { Complete: false })
                {
                    genericArgs += (genericArgs.Length > 0 ? ", " : "") + _childBuilder.GetCurrentPartialType();
                }

                return _genericArguments.Length > 0 ? $"{genericTypeName}<{genericArgs}>" : genericTypeName;
            }
        }

        public readonly Label Label;
        private readonly Label _typeLabel;
        private readonly Button _typeSelector;
        private TypeBuilder _typeBuilder;
        private Vector2 _screenPosition;

        public Type CurrentType { get; private set; }
        public string LabelName => _typeLabel.text;
        public event TypeChangedSignature TypeChanged;
        public event AssemblyQualifiedNameChangedSignature AssemblyQualifiedNameChanged;
        private readonly Func<Type, bool> _filter;
        private readonly bool _ignoreFilterForGenerics;
        private bool _includeAbstract;

        private Type _genericTypeDefinition;
        private Type[] _validTypes;
        private HashSet<Assembly> _validAssemblies;

        public TypeSelectorField(string labelName, Type defaultType, Func<Type,bool> filter = null, bool ignoreFilterForGenerics = true, bool typeLabelVisible = true)
        {
            _filter = filter;
            _ignoreFilterForGenerics = ignoreFilterForGenerics;
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Row;
            style.marginLeft = 3f;
            
            CurrentType = defaultType;
            labelName ??= string.Empty;
            Label = new Label(labelName)
            {
                style =
                {
                    display = labelName.EmptyOrNull() ? DisplayStyle.None : DisplayStyle.Flex,
                    flexGrow = 1f,
                    flexShrink = 1f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    minWidth = new StyleLength(new Length(33, LengthUnit.Percent)),
                    maxWidth = new StyleLength(new Length(33, LengthUnit.Percent))
                }
            };
            var ve = new VisualElement()
            {
                style =
                {
                    flexGrow = 1f,
                    flexDirection = FlexDirection.Row,
                    marginLeft = 0f,
                }
            };
            _typeLabel = new Label(GetReadableTypeName(CurrentType))
            {
                tooltip = GetReadableTypeName(CurrentType, true),
                style =
                {
                    marginLeft = 4f,
                    flexGrow = 1f,
                    fontSize = 12f,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    display = typeLabelVisible ? DisplayStyle.Flex : DisplayStyle.None,
                }
            };
            _typeSelector = new Button(OnSelectType)
            {
                text = "T",
                style =
                {
                    width = 20,
                    height = 20,
                    unityFontStyleAndWeight = FontStyle.Bold,
                }
            };
            
            Add(Label);
            ve.Add(_typeSelector);
            ve.Add(_typeLabel);
            Add(ve);
        }

        public TypeSelectorField WithGenericDefinition(Type genericType)
        {
            if (genericType.IsGenericType)
            {
                _genericTypeDefinition = genericType.GetGenericTypeDefinition();
            }
            else if (genericType.IsGenericTypeDefinition)
            {
                _genericTypeDefinition = genericType;
            }
            else
            {
                _genericTypeDefinition = null;
            }
            return this;
        }

        public TypeSelectorField WithValidTypes(bool includeAbstract, params Type[] types)
        {
            _includeAbstract = includeAbstract;
            _validTypes = types;
            return this;
        }

        public TypeSelectorField WithValidAssemblies(params Assembly[] assemblies)
        {
            _validAssemblies = new HashSet<Assembly>(assemblies);
            return this;
        }

        public void SetTypeLabelVisibility(bool labelVisible)
        {
            if (labelVisible)
            {
                _typeLabel.Show();
                
            }
            else
            {
                _typeLabel.Hide();
            }
        }

        private void OnSelectType()
        {
            _typeBuilder = _genericTypeDefinition == null ? null : new TypeBuilder(_genericTypeDefinition, new Type[_genericTypeDefinition.GetGenericArguments().Length]);
            _screenPosition = GUIUtility.GUIToScreenPoint(_typeSelector.worldBound.position) + new Vector2(0, 36);
            var worldRect = GUIUtility.GUIToScreenRect(_typeSelector.worldBound);
            var pos = new Vector2(worldRect.position.x, worldRect.position.y + _typeSelector.worldBound.height);
            var windowRect = GUIUtility.GUIToScreenRect(_typeSelector.panel.visualTree.worldBound);
            var height = Mathf.Min(300f, windowRect.y + windowRect.height - pos.y);
            var size = new Vector2(_typeSelector.resolvedStyle.width, height);
            Rect rect = new(pos, size);

            if (_validTypes == null)
            {
                TypeSearchWindow.Show(_screenPosition, _screenPosition, new TypeSearchProvider(OnTypeSelected, _validAssemblies, _filter), true, false);
            }
            else
            {
                TypeSearchWindow.Show(_screenPosition, _screenPosition, new TypeCollectionSearchProvider(OnTypeSelected, _validAssemblies, _filter, _includeAbstract, _validTypes), true, false);
            }
        }

        private void OnTypeSelected(TypeSearchModel model)
        {
            var type = model.Type;
            if (type.IsGenericType)
            {
                var cachedGenericType = type.GetGenericTypeDefinition();
                var genericTypeArguments = new Type[cachedGenericType.GetGenericArguments().Length];
                if (_typeBuilder == null)
                {
                    _typeBuilder = new TypeBuilder(cachedGenericType, genericTypeArguments);
                }
                else
                {
                    _typeBuilder.StackGenericType(cachedGenericType, genericTypeArguments);
                }

                var worldRect = GUIUtility.GUIToScreenRect(_typeSelector.worldBound);
                var pos = new Vector2(worldRect.position.x, worldRect.position.y + 20);
                var windowRect = GUIUtility.GUIToScreenRect(_typeSelector.panel.visualTree.worldBound);
                var height = Mathf.Min(300f, windowRect.y + windowRect.height - pos.y);
                var size = new Vector2(_typeSelector.resolvedStyle.width, height);
                Rect rect = new(pos, size);

                var typeArg = _typeBuilder.GetCurrentGenericTypeArgument();
                var filter = _ignoreFilterForGenerics ? null : _filter;
                if (HasGenericTypeConstraints(typeArg))
                {
                    var validTypes = FindValidTypesForGenericParameters(typeArg);
                    TypeSearchWindow.Show(_screenPosition, _screenPosition, new TypeCollectionSearchProvider(OnTypeSelected, _validAssemblies, filter, _includeAbstract, validTypes.ToArray()), true, false, _typeBuilder.GetCurrentPartialType());
                }
                else
                {
                    TypeSearchWindow.Show(_screenPosition, _screenPosition, new TypeSearchProvider(OnTypeSelected, _validAssemblies, filter), true, false, _typeBuilder.GetCurrentPartialType());
                }
            }
            else if(_typeBuilder != null)
            {
                _typeBuilder.AddType(type);
                if (_typeBuilder.Complete)
                {
                    SetValue(_typeBuilder.MakeType());
                }
                else
                {
                    var worldRect = GUIUtility.GUIToScreenRect(_typeSelector.worldBound);
                    var pos = new Vector2(worldRect.position.x, worldRect.position.y + 20);
                    var windowRect = GUIUtility.GUIToScreenRect(_typeSelector.panel.visualTree.worldBound);
                    var height = Mathf.Min(300f, windowRect.y + windowRect.height - pos.y);
                    var size = new Vector2(_typeSelector.resolvedStyle.width, height);
                    Rect rect = new(pos, size);
                    
                    var typeArg = _typeBuilder.GetCurrentGenericTypeArgument();
                    var filter = _ignoreFilterForGenerics ? null : _filter;
                    if (HasGenericTypeConstraints(typeArg))
                    {
                        var validTypes = FindValidTypesForGenericParameters(typeArg);
                        TypeSearchWindow.Show(_screenPosition, _screenPosition, new TypeCollectionSearchProvider(OnTypeSelected, _validAssemblies, filter, _includeAbstract, validTypes.ToArray()), true, false, _typeBuilder.GetCurrentPartialType());
                    }
                    else
                    {
                        TypeSearchWindow.Show(_screenPosition, _screenPosition, new TypeSearchProvider(OnTypeSelected, _validAssemblies, filter), true, false, _typeBuilder.GetCurrentPartialType());
                    }
                }
            }
            else
            {
                SetValue(type);
            }
        }

        public void SetValue(Type type)
        {
            var oldType = CurrentType;
            CurrentType = type;
            _typeLabel.text = GetReadableTypeName(CurrentType);
            _typeLabel.tooltip = GetReadableTypeName(CurrentType, true);
            TypeChanged?.Invoke(this, oldType, CurrentType );
            AssemblyQualifiedNameChanged?.Invoke(this, oldType?.AssemblyQualifiedName, CurrentType?.AssemblyQualifiedName);
        }

        public void SetValueWithoutNotify(Type type)
        {
            CurrentType = type;
            _typeLabel.text = GetReadableTypeName(CurrentType);
            _typeLabel.tooltip = GetReadableTypeName(CurrentType, true);
        }
        
        public static string GetReadableTypeName(Type type, bool fullName = false)
        {
            if (type == null)
            {
                return "None";
            }
            
            if (!type.IsGenericType)
            {
                return fullName ? type.FullName : type.Name;
            }

            // Get the base type name without `1, `2, etc.
            string baseTypeName = type.Name.Split('`')[0];

            // Recursively resolve generic type arguments
            string genericArgs = string.Join(", ", type.GetGenericArguments().Select(t => GetReadableTypeName(t, fullName)));

            return fullName ? $"{type.Namespace}.{baseTypeName}<{genericArgs}>" : $"{baseTypeName}<{genericArgs}>";
        }

        public static bool HasGenericTypeConstraints(Type genericTypeArgument)
        {
            var constraints = genericTypeArgument.GetGenericParameterConstraints();
            var attrs = genericTypeArgument.GenericParameterAttributes;
            return !attrs.HasFlag(GenericParameterAttributes.None) || constraints.Length != 0;
        }
        
        public static List<Type> FindValidTypesForGenericParameters(Type genericTypeArgument)
        {
            var resultList = new List<Type>(1000);
            // Find matching types
            ReflectionUtility.FindTypesByPredicate(GetTypePredicate(genericTypeArgument), resultList);

            return resultList;
        }
        
        private static Func<Type, bool> GetTypePredicate(Type typeParam)
        {
            var constraints = typeParam.GetGenericParameterConstraints();
            var attrs = typeParam.GenericParameterAttributes;

            return type =>
            {
                // Check class (reference type) constraint
                if (attrs.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint) && type.IsValueType)
                    return false;

                // Check struct (value type) constraint
                if (attrs.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint) && !type.IsValueType)
                    return false;

                // Check new() constraint (must have a parameterless constructor)
                if (attrs.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && type.GetConstructor(Type.EmptyTypes) == null)
                    return false;

                // Check specific base class or interface constraints
                if (constraints.Length > 0 && !constraints.All(c => c.IsAssignableFrom(type)))
                    return false;

                return true;
            };
        }
    }
}
