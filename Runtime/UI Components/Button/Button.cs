using System;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public class Button : VisualElement, IVaporUIComponent
    {
        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        public IVaporUIComponent VaporComponent => this;
        
        public Type LeftSection { get; set; }
        public Type CenterSection { get; set; }
        public Type RightSection { get; set; }

        public Button(string style = null)
        {
            Add(new Group
                { }.VaporComponent.WithChildren<Group>(p =>
            {
                p.AddChild(new Container("m=20px")
                    { name = "LeftSection" });
                p.AddChild(new Container(null)
                    { name = "CenterSection" });
                p.AddChild(new Container("m=20px")
                    { name = "RightSection" });
            }));

            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);

            VaporComponent.SetStyle(style);
            VaporComponent.ApplyStyles();
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (LeftSection != null)
            {
                this.Q<Container>("LeftSection")?.VaporComponent.AddChild((VisualElement)Activator.CreateInstance(LeftSection));
            }
            if (CenterSection != null)
            {
                this.Q<Container>("CenterSection")?.VaporComponent.AddChild((VisualElement)Activator.CreateInstance(CenterSection));
            }
            if (RightSection != null)
            {
                this.Q<Container>("RightSection")?.VaporComponent.AddChild((VisualElement)Activator.CreateInstance(RightSection));
            }
        }
        
        public void ApplyCustomStyles()
        {
            
        }
    }
}