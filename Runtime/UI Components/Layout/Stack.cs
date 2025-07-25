using System.Linq;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public class Stack : VisualElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-stack-root";
        
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
                _cachedGap = StyleHelper.ParseLength(value) ?? Length.Auto();
                VaporComponent.ApplyStyles();
            }
        }

        private bool _grow;
        public bool Grow
        {
            get => _grow;
            set
            {
                _grow = value;
                VaporComponent.ApplyStyles();
            }
        }

        private bool _reverse;
        public bool Reverse
        {
            get => _reverse;
            set
            {
                _reverse = value;
                VaporComponent.ApplyStyles();
            }
        }

        private Length? _cachedGap;
        
        public Stack(string style = null)
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
            style.flexDirection = Reverse ? FlexDirection.ColumnReverse : FlexDirection.Column;
            style.flexWrap = Wrap;

            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            this.Query<Gap>().ForEach(g => g.SetGap(_cachedGap.Value));

            foreach (var c in Children().OfType<IVaporUIComponent>())
            {
                c.StyleOverride.SetFlexGrow(Grow ? 1f : null);
                c.ApplyStyles();
            }
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
                
                if (Grow)
                {
                    if (children[i] is IVaporUIComponent hasOverride)
                    {
                        hasOverride.StyleOverride.SetFlexGrow(1f);
                    }
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

            if (Grow)
            {
                if (child is IVaporUIComponent hasOverride)
                {
                    hasOverride.StyleOverride.SetFlexGrow(1f);
                }
            }

            Add(child);
        }
    }
}