using System;
using System.Collections.Concurrent;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Assemblies;

namespace Vapor.Serialization
{
    /// <summary>
    /// Maps between concrete types and the <c>!Name</c> tags written for polymorphic slots.
    /// </summary>
    /// <remarks>
    /// Tags are short by design — <c>!FireballAbility</c> rather than an assembly-qualified name —
    /// because the tag is something a model has to reproduce. Resolution therefore has to be a
    /// search rather than a lookup: explicit <see cref="VslTypeAttribute"/> registrations first, then
    /// subclasses of the declared type by name, then a full type name as a last resort.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class VslTypeRegistry
    {
        private static readonly ConcurrentDictionary<Type, string> s_TagsByType =
            new ConcurrentDictionary<Type, string>();

        // A tag maps to a list, not a single type: tags are short by design, so two unrelated
        // hierarchies can reasonably both declare '!Heal'. Resolution picks the candidate that fits
        // the slot rather than whichever was registered last.
        private static readonly ConcurrentDictionary<string, Type[]> s_TypesByTag =
            new ConcurrentDictionary<string, Type[]>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, Type> s_ResolvedByTagAndBase =
            new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

        private static bool s_Scanned;
        private static readonly object s_ScanLock = new object();

        /// <summary>Registers the tag for a type, overriding any <see cref="VslTypeAttribute"/>.</summary>
        public static void Register(string tag, Type type)
        {
            if (string.IsNullOrEmpty(tag))
            {
                throw new ArgumentException("A type tag cannot be empty.", nameof(tag));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            AddCandidate(tag, type);
            s_TagsByType[type] = tag;
            s_ResolvedByTagAndBase.Clear();
        }

        private static void AddCandidate(string tag, Type type)
        {
            s_TypesByTag.AddOrUpdate(
                tag,
                _ => new[] { type },
                (_, existing) =>
                {
                    foreach (var candidate in existing)
                    {
                        if (candidate == type)
                        {
                            return existing;
                        }
                    }

                    var grown = new Type[existing.Length + 1];
                    Array.Copy(existing, grown, existing.Length);
                    grown[existing.Length] = type;
                    return grown;
                });
        }

        /// <summary>The tag written for a type: its <see cref="VslTypeAttribute"/> or its short name.</summary>
        public static string GetTag(Type type)
        {
            if (s_TagsByType.TryGetValue(type, out var tag))
            {
                return tag;
            }

            var attribute = type.GetCustomAttribute<VslTypeAttribute>(false);
            tag = attribute != null && !string.IsNullOrEmpty(attribute.Tag) ? attribute.Tag : type.Name;
            s_TagsByType[type] = tag;
            return tag;
        }

        /// <summary>
        /// Resolves a tag to a concrete type assignable to <paramref name="expectedBase"/>, or null.
        /// </summary>
        public static Type Resolve(ReadOnlySpan<char> tag, Type expectedBase)
        {
            var name = tag.ToString();
            var cacheKey = expectedBase == null ? name : string.Concat(expectedBase.FullName, "|", name);

            if (s_ResolvedByTagAndBase.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var resolved = ResolveUncached(name, expectedBase);
            if (resolved != null)
            {
                s_ResolvedByTagAndBase[cacheKey] = resolved;
            }

            return resolved;
        }

        private static Type ResolveUncached(string name, Type expectedBase)
        {
            EnsureScanned();

            if (s_TypesByTag.TryGetValue(name, out var registered))
            {
                foreach (var candidate in registered)
                {
                    if (IsCompatible(candidate, expectedBase))
                    {
                        return candidate;
                    }
                }
            }

            // A full or assembly-qualified name resolves directly.
            var direct = Type.GetType(name, false, true);
            if (direct != null && IsCompatible(direct, expectedBase))
            {
                return direct;
            }

            // Otherwise search for a concrete type with this short name that fits the slot.
            Type fallback = null;
            foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
            {
                foreach (var type in SafeGetTypes(assembly))
                {
                    if (!IsCompatible(type, expectedBase))
                    {
                        continue;
                    }

                    if (string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return type;
                    }

                    if (fallback == null && string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        fallback = type;
                    }
                }
            }

            return fallback;
        }

        private static bool IsCompatible(Type type, Type expectedBase)
        {
            if (type == null || type.IsAbstract || type.IsInterface)
            {
                return false;
            }

            return expectedBase == null || expectedBase == typeof(object) || expectedBase.IsAssignableFrom(type);
        }

        /// <summary>
        /// Indexes every <see cref="VslTypeAttribute"/> in the loaded assemblies, once, on the first
        /// polymorphic read. Explicit tags have to win over name matching, and there is no way to
        /// know they exist without looking.
        /// </summary>
        private static void EnsureScanned()
        {
            if (s_Scanned)
            {
                return;
            }

            lock (s_ScanLock)
            {
                if (s_Scanned)
                {
                    return;
                }

                foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
                {
                    foreach (var type in SafeGetTypes(assembly))
                    {
                        var attribute = type.GetCustomAttribute<VslTypeAttribute>(false);
                        if (attribute == null || string.IsNullOrEmpty(attribute.Tag))
                        {
                            continue;
                        }

                        AddCandidate(attribute.Tag, type);
                        s_TagsByType[type] = attribute.Tag;
                    }
                }

                s_Scanned = true;
            }
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // A partially loadable assembly still contributes the types that did load.
                var loaded = ex.Types;
                var count = 0;
                foreach (var type in loaded)
                {
                    if (type != null)
                    {
                        count++;
                    }
                }

                var result = new Type[count];
                var index = 0;
                foreach (var type in loaded)
                {
                    if (type != null)
                    {
                        result[index++] = type;
                    }
                }

                return result;
            }
            catch (Exception)
            {
                return Type.EmptyTypes;
            }
        }
    }
}
