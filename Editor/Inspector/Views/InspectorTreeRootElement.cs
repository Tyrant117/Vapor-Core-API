using System.Linq;
using UnityEngine.UIElements;
using Vapor.Inspector;
#if UNITY_EDITOR_COROUTINES
using Unity.EditorCoroutines.Editor;
#endif

namespace VaporEditor.Inspector
{
    public class InspectorTreeRootElement : InspectorTreeElement
    {
        public override VisualElement contentContainer { get; }

        public InspectorTreeRootElement(InspectorTreeObject inspectorObject)
        {
            Root = this;
            Parent = null;
            IsRoot = true;

            InspectorObject = inspectorObject;
            Property = null;
            HasProperty = false;

            BuildChildren(); // This is the expensive call.
            BuildGroupNodes();
            OrderChildren();

            contentContainer = this;
            AttachChildElements();
        }

        public InspectorTreeRootElement(InspectorTreeElement parentElement, InspectorTreeProperty property)
        {
            Root = parentElement.Root;
            Parent = parentElement;
            IsRoot = false;

            InspectorObject = property.InspectorObject;
            Property = property;
            HasProperty = true;

            ChildTreeElements.Add(new InspectorTreeFieldElement(this, property));

            //BuildChildren();
            //BuildGroupNodes();
            OrderChildren();

            contentContainer = this;
            AttachChildElements();
        }

        protected override void BuildChildren()
        {
            TempChildren.Clear();
            IsUnityObject = IsRoot ? InspectorObject.IsUnityObject : Property.IsUnityObjectOrSubclass();

            SurroundWithGroup = TryGetTypeAttribute<DrawWithVaporAttribute>(out var atr) ? atr.InlinedGroupType : UIGroupType.Vertical;

            foreach (var field in InspectorObject.Fields)
            {
                var node = new InspectorTreeFieldElement(this, field);
                TempChildren.Add(node);
            }

            foreach (var method in InspectorObject.Methods)
            {
                var node = new InspectorTreeMethodElement(this, method);
                TempChildren.Add(node);
            }

            foreach (var property in InspectorObject.Properties)
            {
                var node = new InspectorTreePropertyElement(this, property);
                TempChildren.Add(node);
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

        public void DrawToScreen(VisualElement parentElement)
        {
            parentElement.Add(this);
        }

        public void RebuildAndRedraw()
        {
            InspectorObject.ApplyModifiedProperties();
            var p = parent;
            if(p == null)
            {
                return;
            }

            RemoveFromHierarchy();
            if (InspectorObject.IsUnityObject)
            {
                var so = new InspectorTreeObject(InspectorObject.SerializedObject);
                var newRoot = new InspectorTreeRootElement(so);
                newRoot.DrawToScreen(p);
            }
            else
            {
                var so = new InspectorTreeObject(InspectorObject.Object, InspectorObject.Type);
                var newRoot = new InspectorTreeRootElement(so);
                newRoot.DrawToScreen(p);
            }
        }
    }
}
