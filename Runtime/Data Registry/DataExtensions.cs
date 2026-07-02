using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.ResourceManagement.AsyncOperations;


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

        public static T WithIcon<T>(this T data, uint iconAddressableKey) where T : IDataIcon
        {
            data.IconAddressableKey = iconAddressableKey;
            return data;
        }

        public static AsyncOperationHandle<Sprite> GetIconAsync<T>(this T data) where T : IDataIcon
        {
            return data.IconAddressableKey == 0 ? default : DataRegistry<AddressableData>.Get(data.IconAddressableKey).LoadAsync<Sprite>();
        }
    }
}