using UnityEngine;

namespace VaporEditor.Inspector
{
    public class InspectorTreePropertyElement : InspectorTreeElement
    {
        public InspectorTreePropertyElement(InspectorTreeElement parentElement, InspectorTreeProperty property)
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
        }
    }
}
