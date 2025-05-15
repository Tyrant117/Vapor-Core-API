using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;
#if UNITY_EDITOR_COROUTINES
using Unity.EditorCoroutines.Editor;
#endif

namespace VaporEditor.Inspector
{
    public class InspectorTreeElement : VisualElement
    {
        protected static Func<VaporGroupAttribute, int> ShortestToLongestName => group => group.GroupName.Length;
        

        // Visual
        public InspectorTreeRootElement Root { get; protected set; }
        public InspectorTreeElement Parent { get; protected set; }
        public bool IsRoot { get; protected set; }
        public int DrawOrder { get; protected set; }
        public UIGroupType SurroundWithGroup { get; protected set; }
        public VaporGroupAttribute Group { get; protected set; }
        public List<InspectorTreeElement> ChildTreeElements { get; set; } = new();

        // Data
        public InspectorTreeObject InspectorObject { get; protected set; }
        public InspectorTreeProperty Property { get; protected set; }
        public bool HasProperty { get; protected set; }
        public bool IsUnityObject { get; protected set; }


        protected List<VaporGroupAttribute> Groups;
        protected List<InspectorTreeElement> TempChildren = new();

        private readonly List<SerializedResolverContainer> _resolvers = new();
#if UNITY_EDITOR_COROUTINES
        private EditorCoroutine _resolverRoutine;
#endif

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
                Groups = vaporGroupAttributes.OrderBy(ShortestToLongestName).ToList();
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
            if (IsUnityObject || !Property.TypeHasAttribute<SerializableAttribute>() || Property.HasAttribute<SerializeReference>())
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
            SurroundWithGroup = TryGetTypeAttribute<DrawWithVaporAttribute>(out var atr) ? atr.InlinedGroupType : UIGroupType.Vertical;

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
                    if (nodeBag.TryGetValue(groupNode.Group.ParentName, out var parentGroupNode))
                    {
                        parentGroupNode.AddTreeElement(groupNode);
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

        public void AddTreeElement(InspectorTreeElement child)
        {
            child.Parent = this;
            ChildTreeElements.Add(child);
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

        public VisualElement SurroundWithVaporGroup(UIGroupType drawnWithGroup, string groupName, string header)
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

            var vaporPropertyGroup = SerializedDrawerUtility.DrawGroupElement(vaporGroup);
            return vaporPropertyGroup;
        }
        #endregion

        #region - Events -
        protected virtual void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            _resolvers.Clear();
#if UNITY_EDITOR_COROUTINES
            if (_resolverRoutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(_resolverRoutine);
            }
#endif
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
    }
}
