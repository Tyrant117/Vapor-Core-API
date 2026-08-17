#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Vapor.Serialization
{
    /// <summary>
    /// The default reference resolver: an <c>EntityId</c> for speed, plus a durable asset locator so
    /// the reference still resolves in a build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="EntityId"/> alone is only valid while the object stays loaded in the session that
    /// wrote it — which is fine for clipboard, undo and live tooling, and useless for a saved file
    /// or a player. So a reference to an asset also records how to load it: a <c>Resources</c> path,
    /// or an Addressables key.
    /// </para>
    /// <para>
    /// Resolution tries the id first because it is exact and free, then falls back to the locator.
    /// A scene object or a runtime instance has no asset, so it gets an id only and remains
    /// session-scoped — there is nothing durable to record about it.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class VslObjectReferenceResolver : IVslReferenceResolver
    {
        public static readonly VslObjectReferenceResolver Instance = new VslObjectReferenceResolver();

        public bool TryGetReference(Object obj, out VslObjectReference reference)
        {
            reference = VslObjectReference.Null;

            // The Unity null check is deliberate: a destroyed object should serialize as @null
            // rather than as a dangling reference.
            if (obj == null)
            {
                return false;
            }

            var id = EntityId.ToULong(obj.GetEntityId());
            VslAssetLocator.TryGetKey(obj, out var source, out var key);

            reference = new VslObjectReference(id, source, key);
            return !reference.IsNull;
        }

        public bool TryResolve(in VslObjectReference reference, Type expectedType, out Object obj)
        {
            obj = null;
            if (reference.IsNull)
            {
                return false;
            }

            if (reference.HasEntityId && TryResolveEntityId(reference.EntityId, expectedType, out obj))
            {
                if (reference.HasAssetKey)
                {
                    VslAssetLocator.Remember(obj, reference.Source, reference.Key);
                }

                return true;
            }

            if (reference.HasAssetKey &&
                VslAssetLocator.TryLoad(reference.Source, reference.Key, expectedType, out var loaded))
            {
                // Narrowed because the member may be typed as a component while the asset that
                // carries it is a prefab.
                obj = VslAssetLocator.Narrow(loaded, expectedType);
                if (obj != null)
                {
                    VslAssetLocator.Remember(obj, reference.Source, reference.Key);
                    return true;
                }
            }

            obj = null;
            return false;
        }

        private static bool TryResolveEntityId(ulong id, Type expectedType, out Object obj)
        {
            obj = null;
            var entityId = EntityId.FromULong(id);

#if UNITY_EDITOR
            // The editor lookup also finds assets that are not currently loaded, which the runtime
            // one cannot. Fall through to the runtime lookup when it comes back empty.
            obj = EditorUtility.EntityIdToObject(entityId);
#endif
            if (obj == null)
            {
                obj = Resources.EntityIdToObject(entityId);
            }

            if (obj == null)
            {
                return false;
            }

            obj = VslAssetLocator.Narrow(obj, expectedType);
            return obj != null;
        }
    }
}
