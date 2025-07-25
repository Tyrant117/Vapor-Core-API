using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public sealed class Text : TextElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-text-root";
        
        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        public IVaporUIComponent VaporComponent => this;

        public Text(string text = null, string style = null)
        {
            AddToClassList(ROOT_SELECTOR);
            this.text = text ?? string.Empty;
            
            VaporComponent.SetStyle(style);
            VaporComponent.ApplyStyles();
        }
        
        public void ApplyCustomStyles()
        {
            
        }
    }
}