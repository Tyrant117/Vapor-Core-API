using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Assemblies;

namespace Vapor.Networking
{
    /// <summary>
    /// Maps a type tag — xxHash32 of the type's full name — to a constructor, so a peer can rebuild an
    /// object or component it has only been told the tag of. Same scheme as opcodes and rpc ids: a
    /// function of the name and nothing else, so an editor host and a built client always agree.
    /// </summary>
    /// <remarks>
    /// Types are registered explicitly (the generator will emit registrations) or discovered lazily by
    /// scanning loaded assemblies for concrete <see cref="VaporNetworkObject"/> and
    /// <see cref="NetworkComponent"/> subclasses. Construction uses the non-public parameterless
    /// constructor when there is one, so replicated types can keep their constructors internal.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class NetworkTypeRegistry
    {
        private static readonly object s_Lock = new();
        private static readonly Dictionary<uint, Type> s_TypesByTag = new();
        private static readonly Dictionary<Type, uint> s_TagsByType = new();
        private static readonly Dictionary<Type, Func<object>> s_Factories = new();
        private static bool s_Scanned;

        public static uint TagOf(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            lock (s_Lock)
            {
                if (s_TagsByType.TryGetValue(type, out var tag))
                {
                    return tag;
                }

                tag = XxHash32.Hash(type);
                RegisterNoLock(type, tag, null);
                return tag;
            }
        }

        public static uint TagOf<T>() => TagOf(typeof(T));

        /// <summary>Registers a type with an explicit factory (or null to use its parameterless constructor).</summary>
        public static void Register(Type type, Func<object> factory = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            lock (s_Lock)
            {
                RegisterNoLock(type, XxHash32.Hash(type), factory);
            }
        }

        public static void Register<T>(Func<T> factory = null) where T : class =>
            Register(typeof(T), factory == null ? null : () => factory());

        public static bool TryGetType(uint tag, out Type type)
        {
            lock (s_Lock)
            {
                if (s_TypesByTag.TryGetValue(tag, out type))
                {
                    return true;
                }

                if (!s_Scanned)
                {
                    ScanNoLock();
                    return s_TypesByTag.TryGetValue(tag, out type);
                }

                return false;
            }
        }

        /// <summary>Builds an instance for a tag. Null when the tag is unknown or the type cannot be constructed.</summary>
        public static object Create(uint tag)
        {
            if (!TryGetType(tag, out var type))
            {
                return null;
            }

            return Create(type);
        }

        public static object Create(Type type)
        {
            Func<object> factory;
            lock (s_Lock)
            {
                s_Factories.TryGetValue(type, out factory);
            }

            if (factory != null)
            {
                return factory();
            }

            try
            {
                return Activator.CreateInstance(type, nonPublic: true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Cannot construct {type.FullName} for replication: it needs a parameterless constructor (any accessibility). {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        public static T Create<T>(uint tag) where T : class => Create(tag) as T;

        private static void RegisterNoLock(Type type, uint tag, Func<object> factory)
        {
            if (s_TypesByTag.TryGetValue(tag, out var existing) && existing != type)
            {
                Debug.LogError($"Network type tag collision: {type.FullName} and {existing.FullName} both hash to [{tag:X8}]. Rename one of them.");
                return;
            }

            s_TypesByTag[tag] = type;
            s_TagsByType[type] = tag;
            if (factory != null)
            {
                s_Factories[type] = factory;
            }
        }

        private static void ScanNoLock()
        {
            s_Scanned = true;
            var objectBase = typeof(VaporNetworkObject);
            var componentBase = typeof(NetworkComponent);
            // Unity's loaded set rather than the app domain's, which can still hand back unloaded assemblies.
            foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
            {
                if (assembly.IsDynamic) continue;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsGenericTypeDefinition) continue;
                    if (objectBase.IsAssignableFrom(type) || componentBase.IsAssignableFrom(type))
                    {
                        if (!s_TagsByType.ContainsKey(type))
                        {
                            RegisterNoLock(type, XxHash32.Hash(type), null);
                        }
                    }
                }
            }
        }

        /// <summary>Tests only.</summary>
        internal static void ResetForTests()
        {
            lock (s_Lock)
            {
                s_TypesByTag.Clear();
                s_TagsByType.Clear();
                s_Factories.Clear();
                s_Scanned = false;
            }
        }
    }
}
