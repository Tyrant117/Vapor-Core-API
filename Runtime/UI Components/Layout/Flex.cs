using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    [UxmlElement]
    public partial class Flex : VisualElement
    {
        public Flex()
        {
            AddToClassList("flex");
        }
    }
}