using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class SectionElement : VisualElement
    {
        public SectionElement()
        {
            style.height = 1;
            style.marginTop = 4f;
            style.marginBottom = 2f;
            style.flexGrow = 1f;
            style.backgroundColor = ContainerStyles.TextDefault;
        }
    }
}
