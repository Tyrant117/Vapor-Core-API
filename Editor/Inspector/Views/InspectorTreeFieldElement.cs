using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class InspectorTreeFieldElement : InspectorTreeElement
    {
        public override VisualElement contentContainer { get; }

        private TreePropertyField _propertyField;

        public InspectorTreeFieldElement(InspectorTreeElement parentElement, InspectorTreeProperty property)
        {
            Root = parentElement.Root;
            Parent = parentElement;
            IsRoot = false;

            InspectorObject = property.InspectorObject;
            Property = property;
            HasProperty = true;

            FindGroupsAndDrawOrder();
            BuildChildren();
            BuildGroupNodes();

            contentContainer = InitializeContentContainer();
            RegisterCallback<TreePropertyChangedEvent>(OnTreePropertyChanged);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        private void OnTreePropertyChanged(TreePropertyChangedEvent evt)
        {
            //Debug.Log($"TreePropertyChanged: {evt.target} - {Property.PropertyName}");
            if (TryGetAttribute<OnValueChangedAttribute>(out var ovc))
            {
                var methodInfo = ReflectionUtility.GetMethod(Property.GetParentsParentType(), ovc.MethodName);
                if (methodInfo != null)
                {
                    var t = Property.PropertyType;
                    var @new = TreePropertyField.CastToType(Property.GetValue(), t);

                    var target = Property.IsArrayElement ? Property.ParentProperty.GetParentObject() : Property.GetParentObject();
                    if (methodInfo.IsStatic)
                    {
                        target = null;
                    }
                    
                    int length = methodInfo.GetParameters().Length;
                    switch (length)
                    {
                        case 3:
                        {
                            var tpf = evt.target as TreePropertyField;
                            methodInfo.Invoke(target, new[] { tpf?.Property.PropertyName, evt.Previous, @new });
                            break;
                        }
                        case 2:
                        {
                            methodInfo.Invoke(target, new[] { null, @new });
                            break;
                        }
                        case 1:
                        {
                            methodInfo.Invoke(target, new[] { @new });
                            break;
                        }
                        default:
                            Debug.Log(target);
                            Debug.Log(methodInfo.Name);
                            methodInfo.Invoke(target, new object[] { });
                            break;
                    }
                    
                    if (ovc.RebuildTree)
                    {
                        GetFirstAncestorOfType<InspectorTreeElement>().Root.RebuildAndRedraw();
                    }
                }
            }
        }

        private VisualElement InitializeContentContainer()
        {
            if (!Property.IsArrayElement && HasAttribute<SectionAttribute>())
            {
                hierarchy.Add(new SectionElement());
            }

            var shouldFlexGrow = Group is { Type: UIGroupType.Horizontal };
            if (TypeHasAttribute<DrawWithVaporAttribute>() && !IsUnityObject && !Property.HasCustomDrawer && !Property.IsWrappedSystemObject && !HasAttribute<SerializeReference>())
            {
                var vaporPropertyGroup = SurroundWithVaporGroup(SurroundWithGroup, Property.PropertyPath, Property.DisplayName);
                TreePropertyField.DrawConditionals(this, vaporPropertyGroup);
                hierarchy.Add(vaporPropertyGroup);
                return vaporPropertyGroup.contentContainer;
            }

            if (Property.SerializedPropertyType == SerializedPropertyType.ManagedReference && !Property.HasCustomDrawer && !Property.NoChildProperties && !Property.IsWrappedSystemObject && !HasAttribute<SerializeReference>())
            {
                var vaporPropertyGroup = SurroundWithVaporGroup(UIGroupType.Foldout, Property.PropertyPath, Property.DisplayName);
                TreePropertyField.DrawConditionals(this, vaporPropertyGroup);
                hierarchy.Add(vaporPropertyGroup);
                return vaporPropertyGroup.contentContainer;
            }

            if (shouldFlexGrow)
            {
                style.flexGrow = 1f;
                if (Property.IsWrappedSystemObject)
                {
                    var layoutElement = SerializedDrawerUtility.DrawManagedReferenceAsField(this, Property, true);
                    if (layoutElement != null)
                    {
                        hierarchy.Add(layoutElement);
                        return layoutElement.contentContainer;
                    }
                }
                else
                {
                    var layoutElement = SerializedDrawerUtility.DrawVaporField(this, Property, true);
                    if (layoutElement != null)
                    {
                        _propertyField = (TreePropertyField)layoutElement[0];
                        hierarchy.Add(layoutElement);
                        return _propertyField.contentContainer;
                    }
                }
            }
            else
            {
                if (Property.IsWrappedSystemObject)
                {
                    var layoutElement = SerializedDrawerUtility.DrawManagedReferenceAsField(this, Property, false);
                    if (layoutElement != null)
                    {
                        hierarchy.Add(layoutElement);
                        return layoutElement.contentContainer;
                    }
                }
                else
                {
                    _propertyField = (TreePropertyField)SerializedDrawerUtility.DrawVaporField(this, Property, false);
                    if (_propertyField != null)
                    {
                        hierarchy.Add(_propertyField);
                        return _propertyField.contentContainer;
                    }
                }

            }

            return null;
        }
    }
}
