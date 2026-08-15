using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor
{
    [UxmlElement]
    public partial class HelpUrlView : VisualElement
    {
        public HelpUrlView()
        {
            this.ConstructFromResourcePath("Styles/HelpUrlView");
        }

        public HelpUrlView(string helpText) : this()
        {
            tooltip = helpText;
        }

        public HelpUrlView(HelpUrlAttribute helpUrlAttribute) : this(helpUrlAttribute.HelpText)
        {
            
        }
    }
}
