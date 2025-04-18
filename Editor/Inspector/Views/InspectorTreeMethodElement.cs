using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
#if UNITY_EDITOR_COROUTINES
using Unity.EditorCoroutines.Editor;
#endif

namespace VaporEditor.Inspector
{
    public class InspectorTreeMethodElement : InspectorTreeElement
    {
        public override VisualElement contentContainer { get; }

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

            contentContainer = InitializeVisualElemet();
        }

        private VisualElement InitializeVisualElemet()
        {
            name = "Branch_Method";

            _button = DrawButton();
            hierarchy.Add(_button);
            return _button.contentContainer;
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
