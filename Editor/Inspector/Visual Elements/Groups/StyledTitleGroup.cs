using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class StyledTitleGroup : VisualElement, ILabeledGroup
    {
        public Label Label { get; private set; }

        public StyledTitleGroup(TitleGroupAttribute titleAttribute, float top = 1, float bottom = 1)
        {
            StyleContent(top, bottom);
            StyleHeader(titleAttribute);
            Add(Label);
        }

        protected virtual void StyleContent(float top, float bottom)
        {
            name = "styled-title-group";
            style.marginTop = top;
            style.marginBottom = bottom;
        }

        protected virtual void StyleHeader(TitleGroupAttribute attribute)
        {
            string labelText = $"<b>{attribute.Title}</b>";
            if (attribute.Subtitle != string.Empty)
            {
                labelText = $"<b>{attribute.Title}</b>\n<color=#9E9E9E><i><size=10>{attribute.Subtitle}</size></i></color>";
            }
            Label = new Label(labelText) { name = "styled-title-group-label" };
            Label.style.borderBottomWidth = attribute.Underline ? 1 : 0;
            Label.style.paddingBottom = 2;
            Label.style.borderBottomColor = ContainerStyles.TextDefault;
            Label.style.marginBottom = 1f;
        }
    }
}
