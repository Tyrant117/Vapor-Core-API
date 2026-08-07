using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.ResourceManagement.AsyncOperations;
using Vapor.GameplayTags;

namespace Vapor
{
    public static class DataExtensions
    {
        public static T WithLocalization<T>(this T data, (string table, string entry) name, (string table, string entry) description) where T : ILocalizedData
        {
            data.LocalizedName = new LocalizedString(name.table, name.entry);
            data.LocalizedDescription = new LocalizedString(description.table, description.entry);
            return data;
        }

        public static T WithIcon<T>(this T data, GameplayTag iconAddressableKey) where T : IDataIcon
        {
            data.IconAddressableKey = iconAddressableKey;
            return data;
        }

        public static AsyncOperationHandle<Sprite> GetIconAsync<T>(this T data) where T : IDataIcon
        {
            if (data.IconAddressableKey.IsNone())
            {
                return default;
            }

            // Looked up rather than assumed: a key can name an addressable that has since been removed
            // from the registry, and Get answers null for that rather than throwing.
            var addressable = DataRegistry<AddressableData>.Get(data.IconAddressableKey.Key);
            return addressable == null ? default : addressable.LoadAsync<Sprite>();
        }
    }
}