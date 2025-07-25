using System.Linq;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public class Flex : VisualElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-flex-root";
        
        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        public IVaporUIComponent VaporComponent => this;

        private Align _align;

        public Align Align
        {
            get => _align;
            set
            {
                _align = value;
                VaporComponent.ApplyStyles();
            }
        }

        private Justify _justify;
        public Justify Justify
        {
            get => _justify;
            set
            {
                _justify = value;
                VaporComponent.ApplyStyles();
            }
        }

        private FlexDirection _direction;
        public FlexDirection FlexDirection
        {
            get => _direction;
            set
            {
                _direction = value;
                VaporComponent.ApplyStyles();
            }
        }

        private Wrap _wrap;
        public Wrap Wrap
        {
            get => _wrap;
            set
            {
                _wrap = value;
                VaporComponent.ApplyStyles();
            }
        }

        private string _gap;
        public string Gap
        {
            get => _gap;
            set
            {
                _gap = value;
                _cachedGap = StyleHelper.ParseLength(_gap) ?? Length.Auto();
                VaporComponent.ApplyStyles();
            }
        }

        private Length? _cachedGap;

        public Flex(string style = null)
        {
            AddToClassList(ROOT_SELECTOR);
            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);
            
            VaporComponent.SetStyle(style);
            VaporComponent.ApplyStyles();
        }

        public void ApplyCustomStyles()
        {
            style.alignItems = Align;
            style.justifyContent = Justify;
            style.flexDirection = FlexDirection;
            style.flexWrap = Wrap;
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            this.Query<Gap>().ForEach(g => g.SetGap(_cachedGap.Value));
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            var children = Children().ToList();
            Clear();

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is Gap)
                {
                    continue;
                }

                Add(children[i]);

                // Add Gap after each element except the last
                if (i < children.Count - 1)
                {
                    Add(new Gap(_cachedGap.Value)); // or whatever spacing size you prefer
                }
            }
        }

        public void AddChild(VisualElement child)
        {
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            if (childCount > 0)
            {
                Add(new Gap(_cachedGap.Value));
            }
            Add(child);
        }
    }
}