using System;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    [UxmlElement]
    public partial class Space : VisualElement
    {
        public Space()
        {
            style.flexGrow = 1f;
            style.flexShrink = 1f;
        }
    }

    [UxmlElement]
    public partial class App : VisualElement
    {
        // Provides the Theme Sheet
    }
    
    public partial class UnStyledButton : Box
    {
        
    }

    [UxmlElement]
    public partial class Button : UnStyledButton
    {
        [UxmlAttribute, UxmlTypeReference(typeof(VisualElement))]
        public Type LeftSection { get; set; }

        [UxmlAttribute, UxmlTypeReference(typeof(VisualElement))]
        public Type CenterSection { get; set; }

        [UxmlAttribute, UxmlTypeReference(typeof(VisualElement))]
        public Type RightSection { get; set; }

        public Button()
        {
            Add(new Group
                { }.WithChildren(p =>
            {
                p.AddChild(new Box
                    { name = "LeftSection", StyleProps = "m=20px" } );
                p.AddChild(new Box
                    { name = "CenterSection" });
                p.AddChild(new Box
                    { name = "RightSection", StyleProps = "m=20px"});
            }));
            
            

            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (LeftSection != null)
            {
                this.Q<Box>("LeftSection")?.AddChild((VisualElement)Activator.CreateInstance(LeftSection));
            }
            if (CenterSection != null)
            {
                this.Q<Box>("CenterSection")?.AddChild((VisualElement)Activator.CreateInstance(CenterSection));
            }
            if (RightSection != null)
            {
                this.Q<Box>("RightSection")?.AddChild((VisualElement)Activator.CreateInstance(RightSection));
            }
        }
    }
}