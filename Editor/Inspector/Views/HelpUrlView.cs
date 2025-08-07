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

        public HelpUrlView(string helpText, string helpUrl = null) : this()
        {
            tooltip = helpText;
            if (helpUrl != null)
            {
                // Click handler
                RegisterCallback<ClickEvent>(evt =>
                {
                    Application.OpenURL(helpUrl);
                });
            }
        }

        public HelpUrlView(HelpUrlAttribute helpUrlAttribute) : this(helpUrlAttribute.HelpText, helpUrlAttribute.HelpUrl)
        {
            
        }
    }
}
