using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class InspectorTreeGroupElement : InspectorTreeElement
    {
        public override VisualElement contentContainer { get; }

        public VisualElement GroupContent { get; private set; }
        public bool HasTabs { get; private set; }

        public InspectorTreeGroupElement(InspectorTreeElement parent, VaporGroupAttribute groupAttribute)
        {
            Root = parent.Root;
            Parent = parent;
            IsRoot = false;

            InspectorObject = parent.InspectorObject;
            Property = parent.Property;
            HasProperty = parent.HasProperty;

            Group = groupAttribute;
            HasTabs = Group.Type == UIGroupType.Tab;
            DrawOrder = groupAttribute.Order;

            contentContainer = InitializeVisualElement();
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        private VisualElement InitializeVisualElement()
        {
            name = $"Branch_Group";

            GroupContent = SerializedDrawerUtility.DrawGroupElement(Group);
            if (!Group.ShowIfResolver.EmptyOrNull())
            {
                if (HasProperty)
                {
                    var property = Property;
                    var type = property.PropertyType;

                    var resolverContainerProp = new SerializedResolverContainerType<bool>(property, 
                        ReflectionUtility.GetMember(type, Group.ShowIfResolver),
                        b => GroupContent.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
                    AddResolver(resolverContainerProp);
                }
                else
                {
                    var resolverContainerProp = new SerializedResolverContainerObject<bool>(InspectorObject.Object, 
                        ReflectionUtility.GetMember(InspectorObject.Type, Group.ShowIfResolver),
                        b => GroupContent.style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
                    AddResolver(resolverContainerProp);
                }                
            }

            if (!Group.HideIfResolver.EmptyOrNull())
            {
                if (HasProperty)
                {
                    var property = Property;
                    var type = property.PropertyType;

                    var resolverContainerProp = new SerializedResolverContainerType<bool>(property, 
                        ReflectionUtility.GetMember(type, Group.HideIfResolver),
                        b => GroupContent.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
                    AddResolver(resolverContainerProp);
                }
                else
                {
                    var resolverContainerProp = new SerializedResolverContainerObject<bool>(InspectorObject.Object, 
                        ReflectionUtility.GetMember(InspectorObject.Type, Group.HideIfResolver),
                        b => GroupContent.style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
                    AddResolver(resolverContainerProp);
                }       
            }
            if(Group is HorizontalGroupAttribute horizontalGroupAttribute && GroupContent is StyledHorizontalGroup horizontalGroup)
            {
                if (horizontalGroupAttribute.UseSingleLabel)
                {
                    if (!horizontalGroupAttribute.SingleLabelResolver.EmptyOrNull())
                    {
                        if (HasProperty)
                        {
                            var property = Property;
                            var type = property.PropertyType;

                            var resolverContainerProp = new SerializedResolverContainerType<string>(property, 
                                ReflectionUtility.GetMember(type, horizontalGroupAttribute.SingleLabelResolver),
                                s => horizontalGroup.Label.text = s);
                            AddResolver(resolverContainerProp);
                        }
                        else
                        {
                            var resolverContainerProp = new SerializedResolverContainerObject<string>(InspectorObject.Object, 
                                ReflectionUtility.GetMember(InspectorObject.Type, horizontalGroupAttribute.SingleLabelResolver),
                                s => horizontalGroup.Label.text = s);
                            AddResolver(resolverContainerProp);
                        }
                    }
                }
            }
            hierarchy.Add(GroupContent);
            return GroupContent.contentContainer;
        }

        public override void AttachChildElements()
        {
            if (HasTabs)
            {
                foreach (var child in ChildTreeElements)
                {
                    if (child.TryGetAttribute<TabGroupAttribute>(out var tabGroup) && TryGetTab(tabGroup.TabName, out var tab))
                    {
                        tab.Add(child);
                    }
                    else
                    {
                        Add(child);
                    }

                    child.AttachChildElements();
                }
            }
            else
            {
                base.AttachChildElements();
            }
        }

        public bool TryGetTab(string tabName, out Tab tab)
        {
            var styledTabs = (StyledTabGroup)GroupContent;
            return styledTabs.TryGetTab(tabName, out tab);
        }
    }
}
