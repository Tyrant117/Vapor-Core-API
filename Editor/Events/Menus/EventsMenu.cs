using UnityEditor;
using Vapor.Events;
using VaporEditor.Keys;

namespace VaporEditor.Events
{
    public static class EventsMenu
    {
        [MenuItem("Assets/Create/Vapor/Keys/Event Key", priority = VaporConfig.AssetMenuPriority, secondaryPriority = 3)]
        private static void CreateEventKey()
        {
            ScriptableObjectUtility.Create<EventKeySo>();
        }

        [MenuItem("Assets/Create/Vapor/Keys/Provider Key", priority = VaporConfig.AssetMenuPriority, secondaryPriority = 4)]
        private static void CreateProviderKey()
        {
            ScriptableObjectUtility.Create<ProviderKeySo>();
        }
    }
}
