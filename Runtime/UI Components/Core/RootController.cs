using System.Collections.Generic;
using UnityEngine;

namespace Vapor
{
    /// <summary>
    /// This is the first ViewController to be initialized. It asks as the root element for the PlayerController.HudRoot.
    /// </summary>
    public abstract class RootController : ViewController
    {
        private static List<IRootInitializedListener> s_Listeners;
        public static bool IsInitialized { get; private set; }

        public RootControllerRunner Runner { get; private set; }
        
        public void SetRunner(RootControllerRunner runner)
        {
            Runner = runner;
        }

        public abstract void SetLayerVisibility(int layer, bool isVisible);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRootController()
        {
            IsInitialized = false;
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
}