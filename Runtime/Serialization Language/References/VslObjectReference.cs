using System;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Serialization
{
    /// <summary>
    /// How an asset can be loaded back at runtime, independently of the session it was written in.
    /// </summary>
    public enum VslAssetSource : byte
    {
        /// <summary>No durable locator. The reference is valid only within the writing session.</summary>
        None = 0,

        /// <summary>Loadable with <c>Resources.Load</c>, using a path relative to a Resources folder.</summary>
        Resource,

        /// <summary>Loadable through Addressables, using an address or label.</summary>
        Addressable,
    }

    /// <summary>
    /// Everything VSL records about a <c>UnityEngine.Object</c> reference: the session-scoped
    /// <c>EntityId</c>, and optionally a durable locator for loading it in a build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves answer different questions. <see cref="EntityId"/> is exact and instant, and
    /// covers scene objects and runtime instances that are not assets at all — but it does not
    /// survive a domain reload. <see cref="Source"/> plus <see cref="Key"/> survives anything, but
    /// only exists for assets that are actually shipped: something under a <c>Resources</c> folder,
    /// or something marked addressable.
    /// </para>
    /// <para>
    /// Writing both means the fast path is used where it works and the durable path is there where
    /// it does not — which is what makes a document written in the editor still load in a player.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public readonly struct VslObjectReference : IEquatable<VslObjectReference>
    {
        public static readonly VslObjectReference Null = default;

        /// <summary>The session-scoped id, or 0 when there is none.</summary>
        public readonly ulong EntityId;

        /// <summary>How <see cref="Key"/> should be loaded, or <see cref="VslAssetSource.None"/>.</summary>
        public readonly VslAssetSource Source;

        /// <summary>The Resources path or Addressables key, or null.</summary>
        public readonly string Key;

        public VslObjectReference(ulong entityId)
        {
            EntityId = entityId;
            Source = VslAssetSource.None;
            Key = null;
        }

        public VslObjectReference(ulong entityId, VslAssetSource source, string key)
        {
            EntityId = entityId;
            Source = string.IsNullOrEmpty(key) ? VslAssetSource.None : source;
            Key = string.IsNullOrEmpty(key) ? null : key;
        }

        public VslObjectReference(VslAssetSource source, string key) : this(0UL, source, key)
        {
        }

        public bool HasEntityId => EntityId != 0;

        public bool HasAssetKey => Source != VslAssetSource.None && !string.IsNullOrEmpty(Key);

        public bool IsNull => !HasEntityId && !HasAssetKey;

        public bool Equals(VslObjectReference other) =>
            EntityId == other.EntityId && Source == other.Source &&
            string.Equals(Key, other.Key, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is VslObjectReference other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = EntityId.GetHashCode();
                hash = (hash * 397) ^ (int)Source;
                hash = (hash * 397) ^ (Key != null ? Key.GetHashCode() : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            if (IsNull)
            {
                return "@null";
            }

            if (!HasAssetKey)
            {
                return $"@{EntityId}";
            }

            var source = Source == VslAssetSource.Resource ? "resource" : "addressable";
            return HasEntityId
                ? $"@({EntityId} {source} \"{Key}\")"
                : $"@({source} \"{Key}\")";
        }
    }
}
