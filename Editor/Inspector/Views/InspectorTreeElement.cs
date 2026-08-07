using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class InspectorTreeElement : VisualElement
    {
        private static readonly Func<VaporGroupAttribute, int> s_ShortestToLongestName = group => group.GroupName.Length;

        /// <summary>
        /// What a controller draws: the element to attach, and the container its children belong in.
        /// The two differ when the drawn element is wrapped, so the controller never has to reach into
        /// the view to find the real content container.
        /// </summary>
        public readonly struct TreeView
        {
            public readonly VisualElement Element;
            public readonly VisualElement Content;

            public TreeView(VisualElement element) : this(element, element?.contentContainer)
            {
            }

            public TreeView(VisualElement element, VisualElement content)
            {
                Element = element;
                Content = content;
            }

            public static TreeView None => default;
        }

        private VisualElement _contentContainer;

        /// <summary>
        /// Falls back to this element when there is no view, so a type we can't draw yields an empty row
        /// rather than throwing the moment something is added to it.
        /// </summary>
        public override VisualElement contentContainer => _contentContainer ?? this;

        /// <summary>
        /// The drawn element this controller owns, or null when it draws nothing itself.
        /// </summary>
        public VisualElement View { get; private set; }

        // Visual
        public InspectorTreeRootElement Root { get; protected set; }
        public InspectorTreeElement Parent { get; protected set; }
        public bool IsRoot { get; protected set; }
        public int DrawOrder { get; protected set; }
        public VaporGroupAttribute Group { get; protected set; }
        public List<InspectorTreeElement> ChildTreeElements { get; set; } = new();

        // Data
        public InspectorTreeObject InspectorObject { get; protected set; }
        public InspectorTreeProperty Property { get; protected set; }
        public bool HasProperty { get; protected set; }
        public bool IsUnityObject { get; protected set; }


        protected List<VaporGroupAttribute> Groups;
        protected List<InspectorTreeElement> TempChildren = new();

        #region - Building -
        protected void FindGroupsAndDrawOrder()
        {
            if (TryGetAttribute<PropertyOrderAttribute>(out var propOrder))
            {
                DrawOrder = propOrder.Order;
            }

            if (!TryGetAttributes<VaporGroupAttribute>(out var attributes))
            {
                return;
            }

            Groups = new List<VaporGroupAttribute>();
            var vaporGroupAttributes = attributes as VaporGroupAttribute[] ?? attributes.ToArray();
            if (vaporGroupAttributes.Length > 1)
            {
                Groups = vaporGroupAttributes.OrderBy(s_ShortestToLongestName).ToList();
            }
            else
            {
                Groups.Add(vaporGroupAttributes.First());
            }

            Group = Groups[^1];
        }

        protected virtual void BuildChildren()
        {
            IsUnityObject = Property.IsUnityObjectOrSubclass();
            if (IsUnityObject || !Property.TypeHasAttribute<SerializableAttribute>() || Property.IsManagedReference)
            {
                // Exit if the object is a unity object or it isnt serializable.
                // This is so it doesnt redraw Monobehaviours instead of giving you an object field.
                // Also if I try do draw anything it will use custom drawers for things like Vector2, so tag everything with DrawWithVapor that should be drawn this way.
                // Also need to remove serialize reference because the serialize reference can handle drawing its entire contents and you will get duplicated fields.
                return;
            }

            if (Property.IsArray)
            {
                return;
            }

            if (Property.HasCustomDrawer)
            {
                return;
            }

            if (Property.NoChildProperties)
            {
                return;
            }

            TempChildren.Clear();

            //Debug.Log($"Building Children For: {Property.PropertyPath}");
            foreach (var field in Property.Fields)
            {
                var node = new InspectorTreeFieldElement(this, field);
                TempChildren.Add(node);
            }

            foreach (var method in Property.Methods)
            {
                var node = new InspectorTreeMethodElement(this, method);
                TempChildren.Add(node);
            }

            foreach (var property in Property.Properties)
            {
                var node = new InspectorTreePropertyElement(this, property);
                TempChildren.Add(node);
            }
        }

        protected void BuildGroupNodes()
        {
            if (TempChildren.Count == 0)
            {
                return;
            }

            InspectorTreeGroupElement unmanagedNode = null;
            var nodeBag = new Dictionary<string, InspectorTreeGroupElement>();
            var rootNodeList = new List<InspectorTreeGroupElement>();
            foreach (var child in TempChildren)
            {
                if (child.Group == null)
                {
                    unmanagedNode ??= new InspectorTreeGroupElement(this, new VerticalGroupAttribute("k_UnGrouped", int.MaxValue));
                }
                else
                {
                    foreach (var group in child.Groups)
                    {
                        if (!nodeBag.ContainsKey(group.GroupName))
                        {
                            nodeBag.Add(group.GroupName, new InspectorTreeGroupElement(this, group));
                        }
                    }
                }
            }

            // Add the group to either the root or another group.
            foreach (var groupNode in nodeBag.Values)
            {
                if (groupNode.Group.ParentName == string.Empty)
                {
                    rootNodeList.Add(groupNode);
                }
                else
                {
                    string parentName = groupNode.Group.ParentName;
                    while (!parentName.EmptyOrNull())
                    {
                        if (nodeBag.TryGetValue(parentName, out var parentGroupNode))
                        {
                            parentGroupNode.AddTreeElement(groupNode);
                            break;
                        }
                        
                        parentName = MoveUpParentName(parentName);
                    }

                    if (parentName.EmptyOrNull())
                    {
                        rootNodeList.Add(groupNode);
                    }
                }
            }

            // Add the child to the last group.
            foreach (var child in TempChildren)
            {
                if (child.Group == null)
                {
                    if (unmanagedNode == null) continue;

                    unmanagedNode.AddTreeElement(child);
                }
                else
                {
                    if (nodeBag.TryGetValue(child.Group.GroupName, out var node))
                    {
                        node.AddTreeElement(child);
                    }
                    else
                    {
                        unmanagedNode?.AddTreeElement(child);
                    }
                }
            }

            foreach (var rootNode in rootNodeList)
            {
                AddTreeElement(rootNode);
            }

            if (unmanagedNode != null)
            {
                AddTreeElement(unmanagedNode);
            }
        }

        private static string MoveUpParentName(string parentName)
        {
            var last = parentName.LastIndexOf('/');
            return last != -1 ? parentName[..last] : null;
        }

        public void AddTreeElement(InspectorTreeElement child)
        {
            child.Parent = this;
            ChildTreeElements.Add(child);
        }

        /// <summary>
        /// Walks the controller tree rather than the visual tree, so it works before anything is attached.
        /// </summary>
        protected void ForEachDescendantField(Action<TreePropertyField> action)
        {
            foreach (var child in ChildTreeElements)
            {
                // A row in a horizontal group has its field wrapped in a layout element, so the view is the
                // wrapper. Either way the content container is the field itself.
                var field = child.View as TreePropertyField ?? child.contentContainer as TreePropertyField;
                if (field != null)
                {
                    action(field);
                }

                child.ForEachDescendantField(action);
            }
        }
        #endregion

        #region - Drawing -
        public virtual void AttachChildElements()
        {
            foreach (var child in ChildTreeElements)
            {
                Add(child);
                child.AttachChildElements();
            }
        }

        /// <summary>
        /// Produces the element this controller draws. Subclasses build it and hand it back; they do not
        /// attach it or decide where children go.
        /// </summary>
        protected virtual TreeView BuildView()
        {
            return TreeView.None;
        }

        /// <summary>
        /// Attaches the drawn element. This is the only place a controller touches its view's hierarchy -
        /// everything the controller wants to say about the row it says on itself.
        /// </summary>
        protected void InitializeView()
        {
            var view = BuildView();
            View = view.Element;
            if (view.Element == null)
            {
                return;
            }

            hierarchy.Add(view.Element);
            _contentContainer = view.Content ?? view.Element;
        }

        public static VisualElement SurroundWithVaporGroup(UIGroupType drawnWithGroup, string groupName, string header)
        {
            VaporGroupAttribute vaporGroup = drawnWithGroup switch
            {
                UIGroupType.Horizontal => new HorizontalGroupAttribute(groupName),
                UIGroupType.Vertical => new VerticalGroupAttribute(groupName),
                UIGroupType.Foldout => new FoldoutGroupAttribute(groupName, header),
                UIGroupType.Box => new BoxGroupAttribute(groupName, header),
                UIGroupType.Tab => new TabGroupAttribute(groupName, header),
                UIGroupType.Title => new TitleGroupAttribute(groupName, header),
                _ => throw new ArgumentOutOfRangeException()
            };

            return SerializedDrawerUtility.DrawGroupElement(vaporGroup);
        }
        #endregion

        #region - Row Decorators -
        /// <summary>
        /// Everything that positions or frames the row rather than the input widget. Applied before the
        /// view exists, so the separator lands above it and the view is free to ignore all of this.
        /// </summary>
        protected void ApplyRowDecorators()
        {
            if (!HasProperty)
            {
                return;
            }

            if (!Property.IsArrayElement && HasAttribute<SectionAttribute>())
            {
                hierarchy.Add(new SectionElement());
            }

            if (Group is { Type: UIGroupType.Horizontal })
            {
                style.flexGrow = 1f;
            }

            if (TryGetAttribute<HorizontalGroupAttribute>(out var horizontal))
            {
                style.flexBasis = horizontal.FlexBasis;
            }

            if (TryGetAttribute<BackgroundColorAttribute>(out var backgroundColor))
            {
                if (backgroundColor.HasBackgroundColorResolver)
                {
                    AddResolver(new SerializedResolverContainerType<Color>(Property,
                        ReflectionUtility.GetMember(Property.ParentType, backgroundColor.BackgroundColorResolver),
                        c => style.backgroundColor = c));
                }
                else
                {
                    style.backgroundColor = backgroundColor.BackgroundColor;
                }
            }

            if (TryGetAttribute<MarginsAttribute>(out var margins))
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

            if (TryGetAttribute<PaddingAttribute>(out var padding))
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

            if (!TryGetAttribute<BordersAttribute>(out var borders))
            {
                return;
            }

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

        /// <summary>
        /// Show/hide and enable/disable for the whole row. These belong to the controller because they are
        /// about whether the row is there at all, not about how the value is drawn.
        /// </summary>
        protected void ApplyRowConditionals()
        {
            if (!HasProperty)
            {
                return;
            }

            var type = Property.ParentType;

            if (TryGetAttribute<ShowIfAttribute>(out var showIf))
            {
                AddResolver(new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, showIf.Resolver),
                    b => style.display = b ? DisplayStyle.Flex : DisplayStyle.None));
            }

            if (TryGetAttribute<HideIfAttribute>(out var hideIf))
            {
                AddResolver(new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, hideIf.Resolver),
                    b => style.display = b ? DisplayStyle.None : DisplayStyle.Flex));
            }

            if (TryGetAttribute<DisableIfAttribute>(out var disableIf))
            {
                AddResolver(new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, disableIf.Resolver),
                    b => SetEnabled(!b)));
            }

            if (TryGetAttribute<EnableIfAttribute>(out var enableIf))
            {
                AddResolver(new SerializedResolverContainerType<bool>(Property, ReflectionUtility.GetMember(type, enableIf.Resolver),
                    SetEnabled));
            }

            if (HasAttribute<HideInEditorModeAttribute>())
            {
                AddResolver(new SerializedResolverContainerAction<bool>(() => EditorApplication.isPlaying,
                    b => style.display = b ? DisplayStyle.Flex : DisplayStyle.None));
            }

            if (HasAttribute<HideInPlayModeAttribute>())
            {
                AddResolver(new SerializedResolverContainerAction<bool>(() => EditorApplication.isPlaying,
                    b => style.display = b ? DisplayStyle.None : DisplayStyle.Flex));
            }

            if (HasAttribute<DisableInEditorModeAttribute>())
            {
                AddResolver(new SerializedResolverContainerAction<bool>(() => EditorApplication.isPlaying, SetEnabled));
            }

            if (HasAttribute<DisableInPlayModeAttribute>())
            {
                AddResolver(new SerializedResolverContainerAction<bool>(() => EditorApplication.isPlaying, b => SetEnabled(!b)));
            }
        }
        #endregion

        #region - Attributes -
        public bool HasAttribute<T>() where T : Attribute
        {
            return HasProperty && Property.HasAttribute<T>();
        }
        public bool TryGetAttribute<T>(out T atr) where T : Attribute
        {
            atr = null;
            return HasProperty && Property.TryGetAttribute(out atr);
        }
        public bool TryGetAttributes<T>(out T[] atr) where T : Attribute
        {
            atr = null;
            return HasProperty && Property.TryGetAttributes(out atr);
        }

        public bool TypeHasAttribute<T>() where T : Attribute
        {
            return HasProperty ? Property.TypeHasAttribute<T>() : InspectorObject.HasAttribute<T>();
        }
        public bool TryGetTypeAttribute<T>(out T atr) where T : Attribute
        {
            return HasProperty ? Property.TryGetTypeAttribute(out atr) : InspectorObject.TryGetAttribute(out atr);
        }
        public bool TryGetTypeAttributes<T>(out IEnumerable<T> atr) where T : Attribute
        {
            return HasProperty ? Property.TryGetTypeAttributes(out atr) : InspectorObject.TryGetAttributes(out atr);
        }
        #endregion

        #region - Resolvers -
        public void AddResolver(SerializedResolverContainer resolver)
        {
            InspectorResolverTicker.Register(this, resolver);
        }
        #endregion
    }
}
