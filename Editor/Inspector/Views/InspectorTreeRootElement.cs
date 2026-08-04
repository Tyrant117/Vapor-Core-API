using System;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class InspectorTreeRootElement : InspectorTreeElement
    {
        /// <summary>
        /// Draws a Unity object. Constructing the tree is what builds the data model, so callers never
        /// build an <see cref="InspectorTreeObject"/> themselves.
        /// </summary>
        public InspectorTreeRootElement(SerializedObject serializedObject, InspectorTreeObject parentObject = null)
            : this(new InspectorTreeObject(serializedObject).WithParent(parentObject))
        {
        }

        /// <summary>
        /// Draws a plain C# object, such as the current target of a [SerializeReference] field.
        /// </summary>
        public InspectorTreeRootElement(object target, Type type, InspectorTreeObject parentObject = null)
            : this(new InspectorTreeObject(target, type).WithParent(parentObject))
        {
        }

        private InspectorTreeRootElement(InspectorTreeObject inspectorObject)
        {
            Root = this;
            Parent = null;
            IsRoot = true;

            InspectorObject = inspectorObject;
            Property = null;
            HasProperty = false;

            BuildChildren();
            BuildGroupNodes();
            OrderChildren();

            AttachChildElements();
        }

        /// <summary>
        /// Wraps a single already-resolved property. Used for array elements, which are owned by their list
        /// rather than by an object of their own.
        /// </summary>
        public InspectorTreeRootElement(InspectorTreeElement parentElement, InspectorTreeProperty property)
        {
            Root = parentElement.Root;
            Parent = parentElement;
            IsRoot = false;

            InspectorObject = property.InspectorObject;
            Property = property;
            HasProperty = true;

            ChildTreeElements.Add(new InspectorTreeFieldElement(this, property));
            OrderChildren();

            AttachChildElements();
        }

        protected override void BuildChildren()
        {
            TempChildren.Clear();
            IsUnityObject = IsRoot ? InspectorObject.IsUnityObject : Property.IsUnityObjectOrSubclass();


            foreach (var field in InspectorObject.Fields)
            {
                TempChildren.Add(new InspectorTreeFieldElement(this, field));
            }

            foreach (var method in InspectorObject.Methods)
            {
                TempChildren.Add(new InspectorTreeMethodElement(this, method));
            }

            foreach (var property in InspectorObject.Properties)
            {
                TempChildren.Add(new InspectorTreePropertyElement(this, property));
            }
        }

        protected void OrderChildren()
        {
            ChildTreeElements = ChildTreeElements.OrderBy(n => n.DrawOrder).ToList();
            foreach (var child in ChildTreeElements)
            {
                _OrderNodes(child);
            }

            static void _OrderNodes(InspectorTreeElement node)
            {
                node.ChildTreeElements = node.ChildTreeElements.OrderBy(n => n.DrawOrder).ToList();
                foreach (var child in node.ChildTreeElements)
                {
                    _OrderNodes(child);
                }
            }
        }

        /// <summary>
        /// Adds the tree to <paramref name="parentElement"/>, optionally at a specific sibling index.
        /// </summary>
        public void AttachTo(VisualElement parentElement, int index = -1)
        {
            if (index < 0 || index >= parentElement.childCount)
            {
                parentElement.Add(this);
            }
            else
            {
                parentElement.Insert(index, this);
            }
        }

        public void RebuildAndRedraw()
        {
            InspectorObject.ApplyModifiedProperties();
            var parentElement = parent;
            if (parentElement == null)
            {
                return;
            }

            // Kept at the same sibling index. Editors put content both above and below the tree, and
            // re-appending would drop it underneath everything.
            var index = parentElement.IndexOf(this);
            RemoveFromHierarchy();

            var rebuilt = InspectorObject.IsUnityObject
                ? new InspectorTreeRootElement(InspectorObject.SerializedObject, InspectorObject.ParentObject)
                : new InspectorTreeRootElement(InspectorObject.Object, InspectorObject.Type, InspectorObject.ParentObject);
            rebuilt.AttachTo(parentElement, index);
        }
    }
}
