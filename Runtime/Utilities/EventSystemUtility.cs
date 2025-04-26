using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Vapor.Inspector;

namespace Vapor
{
    public static class EventSystemUtility
    {
        private static InputSystemUIInputModule s_Module;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_Module = null;
        }

        /// <summary>
        /// Checks if the pointer is over a GUI element
        /// </summary>
        /// <returns>True if the pointer is over a GUI element, otherwise false</returns>
        public static bool IsPointerOverGUI()
        {
            return EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>
        /// Checks if the pointer is over a GUI element. Works inside InputAction callbacks
        /// </summary>
        /// <returns>True if the pointer is over a GUI element, otherwise false</returns>
        public static bool IsPointerOverGUIAction()
        {
            if (!EventSystem.current)
            {
                Debug.Log($"{TooltipMarkup.ClassMethod(nameof(EventSystemUtility), nameof(IsPointerOverGUIAction))} - No Event System");
                return false;
            }

            if (!s_Module)
            {
                s_Module = (InputSystemUIInputModule)EventSystem.current.currentInputModule;
            }

            return s_Module.GetLastRaycastResult(Pointer.current.deviceId).isValid;
        }

        public static void Disable()
        {
            if (!EventSystem.current)
            {
                Debug.Log($"{TooltipMarkup.ClassMethod(nameof(EventSystemUtility), nameof(IsPointerOverGUIAction))} - No Event System");
                return;
            }

            if (!s_Module)
            {
                s_Module = (InputSystemUIInputModule)EventSystem.current.currentInputModule;
            }

            if (!s_Module.enabled)
            {
                return;
            }

            s_Module.DeactivateModule();
            s_Module.enabled = false;
        }

        public static void Enable()
        {
            if (!EventSystem.current)
            {
                Debug.Log($"{TooltipMarkup.ClassMethod(nameof(EventSystemUtility), nameof(IsPointerOverGUIAction))} - No Event System");
                return;
            }

            if (!s_Module)
            {
                s_Module = (InputSystemUIInputModule)EventSystem.current.currentInputModule;
            }

            if(s_Module.enabled)
            {
                return;
            }

            s_Module.enabled = true;
            s_Module.ActivateModule();
        }
    }
}
