using Vapor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class InspectorTreeMethodElement : InspectorTreeElement
    {
        private StyledButton _button;
        private ButtonAttribute _attribute;

        public InspectorTreeMethodElement(InspectorTreeElement parentElement, InspectorTreeProperty property)
        {
            Root = parentElement.Root;
            Parent = parentElement;
            IsRoot = false;

            InspectorObject = property.InspectorObject;
            Property = property;
            HasProperty = true;

            FindGroupsAndDrawOrder();

            InitializeView();
        }

        protected override TreeView BuildView()
        {
            name = "Branch_Method";

            _button = DrawButton();
            return new TreeView(_button);
        }

        private StyledButton DrawButton()
        {
            TryGetAttribute(out _attribute);
            var label = _attribute.Label;
            if (label.EmptyOrNull())
            {
                label = Property.DisplayName;
            }

            var tooltip = "";
            if (TryGetAttribute<RichTextTooltipAttribute>(out var rtAtr))
            {
                tooltip = rtAtr.Tooltip;
            }

            var button = new StyledButton(_attribute.Size, Invoke)
            {
                tooltip = tooltip,
                text = label,
            };
            return button;
        }

        public void Invoke()
        {
            Property.Invoke();
            if (_attribute.RebuildTree)
            {
                Root.RebuildAndRedraw();
            }
        }
    }
}
