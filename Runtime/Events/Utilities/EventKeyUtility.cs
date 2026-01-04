using System.Collections.Generic;
using System.Linq;
using Vapor.Inspector;
using Vapor.Keys;

namespace Vapor.Events
{
    /// <summary>
    /// A static class for managing keys generated from <see cref="EventKeySo"/> and <see cref="ProviderKeySo"/>
    /// </summary>
    public static class EventKeyUtility
    {
        public const string EVENTS_CATEGORY_NAME = "EventKeys";
        public const string PROVIDERS_CATEGORY_NAME = "ProviderKeys";

#if UNITY_EDITOR
        /// <summary>
        /// Gets a list of all <see cref="EventKeySo"/> in the project. Will return null if not in the UNITY_EDITOR
        /// </summary>
        /// <returns></returns>
        public static List<DropdownModel> GetAllEventKeys()
        {
            var allProviderKeys = RuntimeAssetDatabaseUtility.FindAssetsByType<EventKeySo>();
            return allProviderKeys.Select(so => new DropdownModel(so.DisplayName, so, so.DisplayName)).ToList();
        }


        /// <summary>
        /// Gets a list of all event keys defined in the EventKeyKeys script if it exists in the project.
        /// </summary>
        /// <returns>Returns either the EventKeyKeys.DropdownValues or a list with the "None" value if the EventKeyKeys does not exist.</returns>
        public static List<DropdownModel> GetAllEventKeyValues()
        {
            return KeyUtility.GetAllKeysFromCategory(EVENTS_CATEGORY_NAME);
        }

        /// <summary>
        /// Gets a list of all <see cref="ProviderKeySo"/> in the project. Will return null if not in the UNITY_EDITOR
        /// </summary>
        /// <returns></returns>
        public static List<DropdownModel> GetAllProviderKeys()
        {
            var allProviderKeys = RuntimeAssetDatabaseUtility.FindAssetsByType<ProviderKeySo>();
            return allProviderKeys.Select(so => new DropdownModel(so.DisplayName, so, so.DisplayName)).ToList();
        }

        /// <summary>
        /// Gets a list of all provider keys defined in the ProviderKeyKeys script if it exists in the project.
        /// </summary>
        /// <returns>Returns either the ProviderKeyKeys.DropdownValues or a list with the "None" value if the ProviderKeyKeys does not exist.</returns>
        public static List<DropdownModel> GetAllProviderKeyValues()
        {
            return KeyUtility.GetAllKeysFromCategory(PROVIDERS_CATEGORY_NAME);
        }
#endif
    }
}
