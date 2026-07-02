using Unity.Properties;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    [UxmlElement]
    public partial class Gap : VisualElement
    {
        private Length _horizontal;
        private Length _vertical;

        [UxmlAttribute, CreateProperty]
        public Length Horizontal
        {
            get => _horizontal;
            set
            {
                _horizontal = value;
                style.width = value;
                style.minWidth = value;
                style.maxWidth = value;
            }
        }

        [UxmlAttribute, CreateProperty]
        public Length Vertical
        {
            get => _vertical;
            set
            {
                _vertical = value;
                style.height = value;
                style.minHeight = value;
                style.maxHeight = value;
            }
        }

        public Gap()
        {
            AddToClassList("gap");
        }

        public Gap(Length horizontal, Length vertical) : this()
        {
            Horizontal = horizontal;
            Vertical = vertical;
        }
    }
}