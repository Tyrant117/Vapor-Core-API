using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor
{
    public abstract class ViewController : VisualElement
    {
        /// <summary>
        /// Called after the rootVisualElement has been initialized and all default elements constructed.
        /// </summary>
        /// <param name="initializer">The object calling this method, typically the PlayerController</param>
        public abstract void OnInitialized(object initializer);
    }
    
    /// <summary>
    /// This is the first ViewController to be initialized. It asks as the root element for the PlayerController.HudRoot.
    /// </summary>
    public abstract class RootController : ViewController { }
}
