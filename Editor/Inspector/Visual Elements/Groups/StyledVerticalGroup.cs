using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class StyledVerticalGroup : VisualElement
    {
        public StyledVerticalGroup(float top = 0, float bottom = 0, bool overrideLabelPositions = false)
        {
            StyleContent(top, bottom);
            if (!overrideLabelPositions) return;

            AddToClassList("unity-inspector-element");
            AddToClassList("unity-inspector-main-container");
        }

        protected void StyleContent(float top, float bottom)
        {
            name = "styled-vertical-group";
            style.marginTop = top;
            style.marginBottom = bottom;
        }
    }
}
