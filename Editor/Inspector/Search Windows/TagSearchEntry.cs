using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class TagSearchEntry : VisualElement
    {
        public Toggle Toggle { get; private set; }
        public VisualElement LabelContainer { get; private set; }

        public TagSearchEntry()
        {
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1;
            AddToggle();
            AddLabelContainer();
        }

        private void AddToggle()
        {
            Toggle = new Toggle(null)
            {
                style =
                {
                    flexGrow = 0f,
                    flexShrink = 0f,
                    alignSelf = Align.Center,
                }
            };
            Toggle.WithPadding(0).WithMargins(0, 6, 0, 0);
            Add(Toggle);
        }

        private void AddLabelContainer()
        {
            LabelContainer = new VisualElement()
            {
                style =
                {
                    flexShrink = 1f,
                    flexGrow = 1f,
                    flexDirection = FlexDirection.Row,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                }
            };
            Add(LabelContainer);
        }
    }
}