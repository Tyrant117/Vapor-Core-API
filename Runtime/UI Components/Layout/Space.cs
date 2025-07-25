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
}