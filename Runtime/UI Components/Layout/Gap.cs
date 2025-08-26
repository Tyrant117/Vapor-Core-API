using Unity.Properties;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    [UxmlElement]
    public partial class Gap : VisualElement
    {
        [UxmlAttribute, CreateProperty]
        public Length Size { get; private set; }
        

        public Gap()
        {
            style.flexGrow = 0;
            style.flexShrink = 0;
            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);
        }
        
        public Gap(string gap)
        {
            style.flexGrow = 0;
            style.flexShrink = 0;
            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);

            Size = StyleHelper.ParseLength(gap) ?? Length.Auto();
        }

        public Gap(Length gap)
        {
            style.flexGrow = 0;
            style.flexShrink = 0;
            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);

            Size = gap;
        }

        public void SetGap(Length gap)
        {
            Size = gap;
            if (parent == null)
            {
                return;
            }

            var fd = parent.style.flexDirection.value;
            if (fd is FlexDirection.Column or FlexDirection.ColumnReverse)
            {
                style.height = Size;
                style.minHeight = Size;
                style.maxHeight = Size;
            }
            else
            {
                style.width = Size;
                style.minWidth = Size;
                style.maxWidth = Size;
            }
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (parent == null)
            {
                return;
            }
           
            schedule.Execute(_ =>
            {
                var fd = parent.resolvedStyle.flexDirection;
                if (fd is FlexDirection.Column or FlexDirection.ColumnReverse)
                {
                    style.height = Size;
                    style.minHeight = Size;
                    style.maxHeight = Size;
                }
                else
                {
                    style.width = Size;
                    style.minWidth = Size;
                    style.maxWidth = Size;
                }
            }).ExecuteLater(100);
            
        }
    }
}