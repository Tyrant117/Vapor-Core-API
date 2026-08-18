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

        /// <summary>
        /// Layout, not a member: a filter hides it whole so its heading goes with it, but a search
        /// narrows to the members inside rather than matching the box around them.
        /// </summary>
        public override InspectorFilterRole FilterRole => InspectorFilterRole.Group;

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
                BindGroupResolver<bool>(Group.ShowIfResolver, b => style.display = b ? DisplayStyle.Flex : DisplayStyle.None);
            }

            if (!Group.HideIfResolver.EmptyOrNull())
            {
                BindGroupResolver<bool>(Group.HideIfResolver, b => style.display = b ? DisplayStyle.None : DisplayStyle.Flex);
            }

            if(Group is HorizontalGroupAttribute horizontalGroupAttribute && GroupContent is StyledHorizontalGroup horizontalGroup)
            {
                if (horizontalGroupAttribute.UseSingleLabel)
                {
                    if (!horizontalGroupAttribute.SingleLabelResolver.EmptyOrNull())
                    {
                        BindGroupResolver<string>(horizontalGroupAttribute.SingleLabelResolver, s => horizontalGroup.Label.text = s);
                    }
                }
            }
            return new TreeView(GroupContent);
        }

        /// <summary>
        /// Group resolvers read off the object that owns the grouped property, and off the inspected
        /// object itself at the root.
        /// </summary>
        /// <remarks>
        /// The property branch used to look the member up on the property's own type while reading it
        /// from the property's parent — two different objects — so it only ever resolved by coincidence.
        /// The root branch, which was already consistent, is what this now mirrors: a group conditional
        /// lives next to the field it groups, exactly like <c>ShowIf</c> on a single row.
        /// </remarks>
        private void BindGroupResolver<T>(string resolverName, Action<T> onChanged)
        {
            if (HasProperty)
            {
                ResolverBinding.Bind(this, Property, resolverName, onChanged);
            }
            else
            {
                ResolverBinding.Bind(this, InspectorObject.Object, InspectorObject.Type, resolverName, onChanged);
            }
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
