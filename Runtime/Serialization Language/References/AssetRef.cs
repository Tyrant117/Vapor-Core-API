using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Vapor.Serialization;
using Object = UnityEngine.Object;

namespace Vapor
{
    /// <summary>
    /// A lazy reference to an asset: the locator that VSL writes as <c>@(addressable, "key")</c> or
    /// <c>@(resource, "path")</c>, and nothing else. Reading a document that contains one loads
    /// nothing; the asset is fetched only when asked for.
    /// </summary>
    /// <remarks>
    /// The point, compared with a plain <see cref="Object"/> member: a document full of view prefabs
    /// can be loaded on a headless server, or in a tool, without pulling a single prefab into memory
    /// or into the build. Same text on the wire, same object field in the inspector, no eager load.
    /// </remarks>
    [Serializable]
    public struct AssetRef<T> : IEquatable<AssetRef<T>> where T : Object
    {
        [SerializeField] private VslAssetSource _source;
        [SerializeField] private string _key;

        public AssetRef(VslAssetSource source, string key)
        {
            _source = source;
            _key = key;
        }

        public static AssetRef<T> Addressable(string address) => new(VslAssetSource.Addressable, address);
        public static AssetRef<T> Resource(string path) => new(VslAssetSource.Resource, path);
        public static AssetRef<T> None => default;

        public VslAssetSource Source => _source;
        public string Key => _key;
        public bool IsSet => _source != VslAssetSource.None && !string.IsNullOrEmpty(_key);

        /// <summary>The locator as one string, for logs and dictionaries: <c>addressable:Views/Character</c>.</summary>
        public string Locator => IsSet ? $"{_source.ToString().ToLowerInvariant()}:{_key}" : string.Empty;

        /// <summary>Synchronous load through the VSL asset locator (Resources, Addressables, or the editor's AssetDatabase provider).</summary>
        public bool TryLoad(out T asset)
        {
            if (IsSet && VslAssetLocator.TryLoad(_source, _key, typeof(T), out var obj) && obj is T typed)
            {
                asset = typed;
                return true;
            }

            asset = null;
            return false;
        }

        /// <summary>
        /// Asynchronous load. Addressables and Resources both complete on the main thread; the
        /// callback receives null when the asset cannot be found. Addressable loads must be paired
        /// with <see cref="Release"/> when the user is done.
        /// </summary>
        public void LoadAsync(Action<T> onLoaded)
        {
            if (onLoaded == null) throw new ArgumentNullException(nameof(onLoaded));
            if (!IsSet)
            {
                onLoaded(null);
                return;
            }

            switch (_source)
            {
                case VslAssetSource.Addressable:
                {
                    var handle = Addressables.LoadAssetAsync<T>(_key);
                    handle.Completed += h => onLoaded(h.Status == AsyncOperationStatus.Succeeded ? h.Result : null);
                    break;
                }

                case VslAssetSource.Resource:
                {
                    var request = Resources.LoadAsync<T>(_key);
                    request.completed += _ => onLoaded(request.asset as T);
                    break;
                }

                default:
                    onLoaded(null);
                    break;
            }
        }

        /// <summary>Releases one addressable load of this asset. No-op for Resources.</summary>
        public void Release(T asset)
        {
            if (_source == VslAssetSource.Addressable && asset != null)
            {
                Addressables.Release(asset);
            }
        }

        public bool Equals(AssetRef<T> other) => _source == other._source && string.Equals(_key, other._key, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AssetRef<T> other && Equals(other);
        public override int GetHashCode() => unchecked(((int)_source * 397) ^ (_key?.GetHashCode() ?? 0));
        public override string ToString() => IsSet ? $"AssetRef<{typeof(T).Name}>[{Locator}]" : $"AssetRef<{typeof(T).Name}>[none]";
        public static bool operator ==(AssetRef<T> a, AssetRef<T> b) => a.Equals(b);
        public static bool operator !=(AssetRef<T> a, AssetRef<T> b) => !a.Equals(b);
    }
}

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes an <see cref="AssetRef{T}"/> exactly as a <c>UnityEngine.Object</c> member with a durable
    /// locator would be written — <c>@(addressable, "key")</c> — but reads it back without loading.
    /// </summary>
    public sealed class AssetRefFormatter<T> : VslFormatter<AssetRef<T>> where T : Object
    {
        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in AssetRef<T> value, VslContext context)
        {
            if (!value.IsSet)
            {
                writer.WriteNullReference();
                return;
            }

            writer.WriteReference(new VslObjectReference(value.Source, value.Key));
        }

        public override AssetRef<T> Read(ref VslReader reader, VslContext context)
        {
            if (!reader.TryReadReference(out var reference) || !reference.HasAssetKey)
            {
                return default;
            }

            return new AssetRef<T>(reference.Source, reference.Key);
        }
    }
}
