using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Vapor.GameplayTags;

namespace Vapor.GameplayFramework
{
    /// <summary>
    /// A <see cref="GameplayTag"/>-keyed service locator. Register a service or component under a tag and
    /// retrieve it from anywhere without a hard reference. This is the GameplayTag-native replacement for the
    /// legacy <c>ProviderBus</c> (which was keyed by the removed Keys system): the key space is the same
    /// <c>uint = Hash32(name)</c>, so any registered <see cref="IData"/> name is a valid service tag.
    ///
    /// <para>Registrations are process-wide and non-networked. A typical provider registers itself in
    /// <c>OnEnable</c> and unregisters in <c>OnDisable</c>:</para>
    /// <code>
    /// private void OnEnable()  => GameplayServices.Register(_serviceTag, this);
    /// private void OnDisable() => GameplayServices.Unregister(_serviceTag, this);
    /// </code>
    /// </summary>
    [AutoStaticsCleanup]
    public static partial class GameplayServices
    {
        private static readonly Dictionary<uint, object> s_Services = new();

        /// <summary>Registers a service under a gameplay tag, replacing any existing registration for that tag.</summary>
        public static void Register(GameplayTag tag, object service)
        {
            if (tag.IsNone())
            {
                Debug.LogError("[GameplayServices] Cannot register a service under GameplayTag.None.");
                return;
            }

            s_Services[tag.Key] = service;
        }

        /// <summary>Removes whatever service is registered under the tag.</summary>
        public static void Unregister(GameplayTag tag)
        {
            s_Services.Remove(tag.Key);
        }

        /// <summary>
        /// Removes the service registered under the tag only if it is the given instance. Use this from a
        /// provider's teardown so a later registration under the same tag is not clobbered.
        /// </summary>
        public static void Unregister(GameplayTag tag, object service)
        {
            if (s_Services.TryGetValue(tag.Key, out var existing) && ReferenceEquals(existing, service))
            {
                s_Services.Remove(tag.Key);
            }
        }

        /// <summary>Returns true and the service (cast to <typeparamref name="T"/>) if one is registered under the tag.</summary>
        public static bool TryGet<T>(GameplayTag tag, out T service)
        {
            if (s_Services.TryGetValue(tag.Key, out var value) && value is T typed)
            {
                service = typed;
                return true;
            }

            service = default;
            return false;
        }

        /// <summary>Returns the service registered under the tag, or <c>default</c> if none is registered or the type mismatches.</summary>
        public static T Get<T>(GameplayTag tag)
        {
            return s_Services.TryGetValue(tag.Key, out var value) && value is T typed ? typed : default;
        }

        /// <summary>True if any service is registered under the tag.</summary>
        public static bool IsRegistered(GameplayTag tag) => s_Services.ContainsKey(tag.Key);
    }
}
