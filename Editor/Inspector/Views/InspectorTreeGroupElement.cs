using System;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class InspectorTreeGroupElement : InspectorTreeElement
    {
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

            InitializeView();
        }

        protected override TreeView BuildView()
        {
            name = "Branch_Group";

            GroupContent = SerializedDrawerUtility.DrawGroupElement(Group);

            // Visibility is applied to the group element itself rather than to the content it draws.
            // The content is its only child, so this looks identical and keeps the resolver off the view.
            if (!Group.ShowIfResolver.EmptyOrNull())
            {
                AddResolver(BoolResolver(Group.ShowIfResolver, b => style.display = b ? DisplayStyle.Flex : DisplayStyle.None));
            }

            if (!Group.HideIfResolver.EmptyOrNull())
            {
                AddResolver(BoolResolver(Group.HideIfResolver, b => style.display = b ? DisplayStyle.None : DisplayStyle.Flex));
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
            return new TreeView(GroupContent);
        }

        /// <summary>
        /// Group resolvers read off the owning property when there is one, and off the inspected object
        /// itself at the root.
        /// </summary>
        private SerializedResolverContainer BoolResolver(string resolverName, Action<bool> onChanged)
        {
            return HasProperty
                ? new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(Property.PropertyType, resolverName), onChanged)
                : new SerializedResolverContainerObject<bool>(InspectorObject.Object, ReflectionUtility.GetMember(InspectorObject.Type, resolverName), onChanged);
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
                    else if (child is InspectorTreeGroupElement childGroup)
                    {
                        var splitName = childGroup.Group.ParentName?.Split('/') ?? Array.Empty<string>();
                        if (splitName.Length > 0 && TryGetTab(splitName[^1], out var tab2))
                        {
                            tab2.Add(child);
                        }
                        else
                        {
                            Add(child);
                        }
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
