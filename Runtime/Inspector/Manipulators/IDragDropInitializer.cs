using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public interface IDragDropInitializer
    {
        VisualElement CreateDragDropElement();
    }
}