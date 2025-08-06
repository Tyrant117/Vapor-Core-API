using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public class StyledElement : VisualElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-styled-element-root";
        
        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        public IVaporUIComponent VaporComponent => this;

        public StyledElement(string style = null)
        {
            AddToClassList(ROOT_SELECTOR);
            
            VaporComponent.SetStyle(style);
            VaporComponent.ApplyStyles();
        }
        
        public void ApplyCustomStyles()
        {
            
        }
    }
}