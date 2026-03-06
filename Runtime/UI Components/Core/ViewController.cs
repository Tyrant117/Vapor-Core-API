using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor
{
    public interface IDisplayStateListener
    {
        void OnOpened();
        void OnClosed();
    }
    
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
    public abstract class RootController : ViewController
    {
        private static List<IRootInitializedListener> s_Listeners;
        public static bool IsInitialized { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRootController()
        {
            s_Listeners ??= new List<IRootInitializedListener>();
            s_Listeners.Clear();
        }

        public static void AddListener(IRootInitializedListener listener, bool callbackIfAlreadyInitialized = true)
        {
            s_Listeners.Add(listener);
            if (IsInitialized && callbackIfAlreadyInitialized)
            {
                listener.OnRootInitialized();
            }
        }

        public static void RemoveListener(IRootInitializedListener listener)
        {
            s_Listeners.Remove(listener);
        }

        public static void NotifyRootInitialized()
        {
            IsInitialized = true;
            foreach (var listener in s_Listeners)
            {
                listener.OnRootInitialized();
            }
        }
    }

    public interface IRootInitializedListener
    {
        void OnRootInitialized();
    }
}
