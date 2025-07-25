using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public class Container : VisualElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-container-root";

        private static readonly CustomStyleProperty<string> s_ContainerSize = new("--container-size");
        
        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        
        public IVaporUIComponent VaporComponent => this;

        private bool _fluid;

        public bool Fluid
        {
            get => _fluid;
            set
            {
                _fluid = value;
                VaporComponent.ApplyStyles();
            }
        }

        private string _size;

        public string Size
        {
            get => _size;
            set
            {
                _size = value;
                _cachedSize = StyleHelper.ParseLength(Size);
                VaporComponent.ApplyStyles();
            }
        }
        
        private StyleLength? _cachedSize;

        public Container(string style)
        {
            AddToClassList(ROOT_SELECTOR);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            
            VaporComponent.SetStyle(style);
            VaporComponent.ApplyStyles();
        }

        public void ApplyCustomStyles()
        {
            _cachedSize ??= StyleHelper.ParseLength(Size);
            if (Fluid)
            {
                style.maxWidth = new Length(100, LengthUnit.Percent);
            }
            else
            {
                if (_cachedSize.HasValue)
                {
                    style.maxWidth = _cachedSize.Value;
                }
                else if (customStyle?.TryGetValue(s_ContainerSize, out var value) ?? false)
                {
                    style.maxWidth = StyleHelper.ParseLength(value) ?? Length.None();
                }
            }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            VaporComponent.ApplyStyles();
        }
    }
}