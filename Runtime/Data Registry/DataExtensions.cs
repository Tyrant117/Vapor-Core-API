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

        /// <summary>Points the icon at an asset, by whatever locator it already carries.</summary>
        public static T WithIcon<T>(this T data, AssetRef<Sprite> icon) where T : IDataIcon
        {
            data.IconRef = icon;
            return data;
        }

        /// <summary>Points the icon at an addressable, by address.</summary>
        public static T WithAddressableIcon<T>(this T data, string address) where T : IDataIcon
        {
            data.IconRef = string.IsNullOrEmpty(address) ? AssetRef<Sprite>.None : AssetRef<Sprite>.Addressable(address);
            return data;
        }

        /// <summary>Points the icon at a sprite under a Resources folder, by path.</summary>
        public static T WithResourceIcon<T>(this T data, string path) where T : IDataIcon
        {
            data.IconRef = string.IsNullOrEmpty(path) ? AssetRef<Sprite>.None : AssetRef<Sprite>.Resource(path);
            return data;
        }

        public static AsyncOperationHandle<Sprite> GetAddressableIconAsync<T>(this T data) where T : IDataIcon
        {
            if (!data.IconRef.IsSet)
            {
                return default;
            }

            return data.IconRef.LoadAsyncHandle();
        }

        public static Sprite GetIcon<T>(this T data) where T : IDataIcon
        {
            if (!data.IconRef.IsSet)
            {
                return null;
            }

            data.IconRef.TryLoad(out var sprite);
            return sprite;
        }

        public static async Awaitable<Sprite> GetIconAsync<T>(this T data) where T : IDataIcon
        {
            if (!data.IconRef.IsSet)
            {
                return null;
            }

            return await data.IconRef.LoadAsync();
        }

        public static void ReleaseIcon<T>(this T data, Sprite sprite) where T : IDataIcon
        {
            data.IconRef.Release(sprite);
        }
    }
}
