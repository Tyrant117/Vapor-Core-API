using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor
{
    /// <summary>
    /// Resolves a C# class named in a document back to its <see cref="Type"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authored data has to be able to say which class runs it — which <c>GameplayAbility</c> a spell
    /// spawns, which <c>GameplayEffect</c> a code-backed effect wraps. The obvious way to write that is
    /// the assembly-qualified name, which is unambiguous and completely unreadable. This resolves the
    /// plain class name instead, so a document says <c>"Fireball"</c> and means it.
    /// </para>
    /// <para>
    /// Candidates come from <see cref="VaporTypeCache"/>, which already holds every type reachable from
    /// a <c>[TypeCache]</c> base — including everything under <c>IData</c>. A name that turns out to be
    /// ambiguous is reported rather than silently resolved to whichever type was scanned first; the
    /// full name still works as the way out of a collision.
    /// </para>
    /// </remarks>
    public static class TypeNameResolver
    {
        /// <summary>Built once per base type, since the type cache does not change within a domain.</summary>
        private static readonly Dictionary<Type, Dictionary<string, Type>> s_ByBaseType = new();

        /// <summary>Names already reported as ambiguous, so a per-frame lookup cannot spam the console.</summary>
        private static readonly HashSet<string> s_ReportedCollisions = new();

        public static bool TryResolve<TBase>(string typeName, out Type type)
        {
            type = null;
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            return GetMap(typeof(TBase)).TryGetValue(typeName, out type) && type != null;
        }

        /// <summary>
        /// The type <paramref name="typeName"/> names, or null. Logs what went wrong, because a data
        /// document naming a class that no longer exists is an authoring mistake worth surfacing.
        /// </summary>
        public static Type Resolve<TBase>(string typeName, object context = null)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            var map = GetMap(typeof(TBase));
            if (map.TryGetValue(typeName, out var type))
            {
                if (type != null)
                {
                    return type;
                }

                if (s_ReportedCollisions.Add(typeName))
                {
                    Debug.LogError($"'{typeName}' matches more than one {typeof(TBase).Name}{Where(context)}. " +
                                   "Use the full namespace-qualified name to say which one.");
                }

                return null;
            }

            Debug.LogError($"No {typeof(TBase).Name} named '{typeName}'{Where(context)}.");
            return null;
        }

        /// <summary>Creates an instance of the named type, or null. Non-public constructors are allowed.</summary>
        public static TBase Create<TBase>(string typeName, object context = null) where TBase : class
        {
            var type = Resolve<TBase>(typeName, context);
            if (type == null)
            {
                return null;
            }

            try
            {
                return (TBase)Activator.CreateInstance(type, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not construct {typeName}{Where(context)} - {e.Message}");
                return null;
            }
        }

        /// <summary>The name a document should use for <paramref name="type"/>.</summary>
        public static string GetName(Type type) => type?.Name;

        private static Dictionary<string, Type> GetMap(Type baseType)
        {
            if (s_ByBaseType.TryGetValue(baseType, out var map))
            {
                return map;
            }

            map = new Dictionary<string, Type>(StringComparer.Ordinal);

            foreach (var type in VaporTypeCache.GetAllCachedTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || !baseType.IsAssignableFrom(type))
                {
                    continue;
                }

                // The full name is always unambiguous, so it is registered alongside the short one and
                // stays valid even when the short one collides.
                var fullName = type.FullName ?? type.Name;
                map[fullName] = type;

                // A null marks a collision rather than dropping the entry, so the second type to claim a
                // name is not silently ignored. Skipped for a type in the global namespace, whose full
                // name is its short name and which just registered itself above.
                if (fullName != type.Name)
                {
                    map[type.Name] = map.ContainsKey(type.Name) ? null : type;
                }
            }

            s_ByBaseType[baseType] = map;
            return map;
        }

        private static string Where(object context) => context == null ? string.Empty : $" (in {context})";
    }
}
