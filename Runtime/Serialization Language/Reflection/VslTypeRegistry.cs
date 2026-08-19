using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        /// <summary>
        /// The tag written for a type in a given slot, which is the mirror of
        /// <see cref="s_ResolvedByTagAndBase"/> for the writing side.
        /// </summary>
        /// <remarks>
        /// A type with no <see cref="VslTypeAttribute"/> earns its short name only if no other type in
        /// the slot shares it, and answering that means walking every type in every loaded assembly.
        /// Reading has always cached its half; writing did not, so serializing paid a full assembly
        /// scan per <c>!tag</c> - about 70 ms each in a project this size, which is most of what a
        /// document write cost. Keyed on the pair because the answer depends on the slot as well as
        /// the type.
        /// </remarks>
        private static readonly ConcurrentDictionary<(Type Type, Type Base), string> s_TagByTypeAndBase =
            new ConcurrentDictionary<(Type, Type), string>();

        private static bool s_Scanned;
        private static int s_ScannedAssemblyCount;
        private static readonly object s_ScanLock = new object();

        /// <summary>
        /// Every instantiable type by its short name, built once by the scan.
        /// </summary>
        /// <remarks>
        /// Both halves of tag resolution ask "which types are called this?", and both used to answer it
        /// by walking every type in every loaded assembly, once per tag. Holding the answer costs one
        /// entry per distinct short name and removes the scan from every path but the full-name last
        /// resort.
        /// </remarks>
        private static Dictionary<string, Type[]> s_TypesByShortName;

        /// <summary>How many distinct short names the index holds. Diagnostics only.</summary>
        internal static int IndexedNameCount
        {
            get
            {
                EnsureScanned();
                return s_TypesByShortName?.Count ?? 0;
            }
        }

        /// <summary>Registers the tag for a type, overriding any <see cref="VslTypeAttribute"/>.</summary>
        public static void Register(string tag, Type type)
        {
            if (!IsValidTag(tag))
            {
                throw new ArgumentException(
                    "A type tag must be a VSL identifier: a letter or underscore followed by letters, digits, underscores, or dots.",
                    nameof(tag));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            AddCandidate(tag, type);
            s_TagsByType[type] = tag;
            s_ResolvedByTagAndBase.Clear();

            // A new registration can change what any type's short name resolves to, so the written
            // side has to forget what it worked out just as the read side does.
            s_TagByTypeAndBase.Clear();
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

        /// <summary>The shortest unambiguous tag written for a type.</summary>
        public static string GetTag(Type type) => GetTag(type, null);

        /// <summary>
        /// The shortest tag that resolves uniquely to <paramref name="type"/> in a slot declared as
        /// <paramref name="expectedBase"/>.
        /// </summary>
        public static string GetTag(Type type, Type expectedBase)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var key = (type, expectedBase);
            if (s_TagByTypeAndBase.TryGetValue(key, out var cached))
            {
                return cached;
            }

            // Stored only on success, so an ambiguous registration goes on throwing every time it is
            // asked rather than being answered once and then silently forgotten.
            var resolved = GetTagUncached(type, expectedBase);
            s_TagByTypeAndBase[key] = resolved;
            return resolved;
        }

        private static string GetTagUncached(Type type, Type expectedBase)
        {
            EnsureScanned();

            if (s_TagsByType.TryGetValue(type, out var tag))
            {
                if (!IsValidTag(tag))
                {
                    throw new VslException(
                        $"'{tag}' is not a valid VSL type tag for {type}. Use letters, digits, underscores, and dots only.");
                }

                EnsureRegisteredTagIsUnique(tag, type, expectedBase);
                return tag;
            }

            tag = type.Name;
            if (IsValidTag(tag) && IsShortNameUnique(type, expectedBase))
            {
                return tag;
            }

            tag = type.FullName;
            if (IsValidTag(tag))
            {
                return tag;
            }

            throw new VslException(
                $"{type} needs a unique [VslType] tag because its short name is ambiguous and its full name is not a legal VSL identifier.");
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
                Type match = null;
                foreach (var candidate in registered)
                {
                    if (IsCompatible(candidate, expectedBase))
                    {
                        if (match != null && match != candidate)
                        {
                            throw new VslException(
                                $"'!{name}' is ambiguous for {expectedBase?.Name ?? "object"}; both {match} and {candidate} register it.");
                        }

                        match = candidate;
                    }
                }

                if (match != null)
                {
                    return match;
                }
            }

            // A full or assembly-qualified name resolves directly.
            var direct = Type.GetType(name, false, true);
            if (direct != null && IsCompatible(direct, expectedBase))
            {
                return direct;
            }

            // Otherwise a concrete type with this short name that fits the slot, from the index.
            Type shortMatch = null;
            foreach (var type in TypesNamed(name))
            {
                if (!IsCompatible(type, expectedBase))
                {
                    continue;
                }

                if (shortMatch != null && shortMatch != type)
                {
                    throw new VslException(
                        $"'!{name}' is ambiguous for {expectedBase?.Name ?? "object"}; use a [VslType] tag or a full type name.");
                }

                shortMatch = type;
            }

            if (shortMatch != null)
            {
                return shortMatch;
            }

            // A full name that is not assembly-qualified, which Type.GetType above could not resolve.
            // Rare enough to be worth a scan rather than a second index of every full name.
            foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
            {
                foreach (var type in SafeGetTypes(assembly))
                {
                    if (IsCompatible(type, expectedBase) &&
                        string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static void EnsureRegisteredTagIsUnique(string tag, Type type, Type expectedBase)
        {
            if (!s_TypesByTag.TryGetValue(tag, out var candidates))
            {
                return;
            }

            Type match = null;
            foreach (var candidate in candidates)
            {
                if (!IsCompatible(candidate, expectedBase))
                {
                    continue;
                }

                if (match != null && match != candidate)
                {
                    throw new VslException(
                        $"The VSL tag '!{tag}' is registered by both {match} and {candidate} for the same polymorphic slot.");
                }

                match = candidate;
            }

            if (match != null && match != type)
            {
                throw new VslException(
                    $"The VSL tag '!{tag}' resolves to {match}, not {type}.");
            }
        }

        private static bool IsShortNameUnique(Type type, Type expectedBase)
        {
            Type match = null;
            foreach (var candidate in TypesNamed(type.Name))
            {
                if (!IsCompatible(candidate, expectedBase))
                {
                    continue;
                }

                if (match != null && match != candidate)
                {
                    return false;
                }

                match = candidate;
            }

            return match == type;
        }

        /// <summary>
        /// The instantiable types with a given short name, from the index built by the scan.
        /// </summary>
        /// <remarks>
        /// Abstracts and interfaces are left out because <see cref="IsCompatible"/> refuses them
        /// anyway, so their absence cannot change an answer - it only keeps the index to the types
        /// that could ever be written or read.
        /// </remarks>
        private static Type[] TypesNamed(string name)
        {
            EnsureScanned();
            var index = s_TypesByShortName;
            return index != null && index.TryGetValue(name, out var types) ? types : Array.Empty<Type>();
        }

        private static bool IsValidTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            var first = tag[0];
            if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z') || first == '_'))
            {
                return false;
            }

            for (var i = 1; i < tag.Length; i++)
            {
                var c = tag[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '_' || c == '.'))
                {
                    return false;
                }
            }

            return true;
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
            // The index is a snapshot where the scan it replaced read the assembly list afresh every
            // time. An assembly loaded later - a test assembly, a runtime-loaded plugin - would
            // otherwise be invisible to tag resolution for the rest of the session, which is a wrong
            // answer rather than a slow one. Counting is cheap and only happens on a cache miss.
            var loaded = 0;
            foreach (var _ in CurrentAssemblies.GetLoadedAssemblies())
            {
                loaded++;
            }

            if (s_Scanned && loaded == s_ScannedAssemblyCount)
            {
                return;
            }

            lock (s_ScanLock)
            {
                if (s_Scanned && loaded == s_ScannedAssemblyCount)
                {
                    return;
                }

                // Rescanning can change what a short name resolves to, so what was worked out from the
                // previous set of assemblies cannot be trusted.
                s_TagByTypeAndBase.Clear();
                s_ResolvedByTagAndBase.Clear();

                var byName = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);

                foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
                {
                    foreach (var type in SafeGetTypes(assembly))
                    {
                        var attribute = type.GetCustomAttribute<VslTypeAttribute>(false);
                        if (attribute != null && !string.IsNullOrEmpty(attribute.Tag))
                        {
                            AddCandidate(attribute.Tag, type);
                            // A manual Register call made before the first scan overrides the attribute.
                            s_TagsByType.TryAdd(type, attribute.Tag);
                        }

                        // Indexed in the same pass, because both halves of tag resolution used to walk
                        // every type in every assembly again for every tag they handled. The scan was
                        // already here; only the index was missing.
                        if (type.IsAbstract || type.IsInterface)
                        {
                            continue;
                        }

                        if (!byName.TryGetValue(type.Name, out var named))
                        {
                            named = new List<Type>(1);
                            byName.Add(type.Name, named);
                        }

                        named.Add(type);
                    }
                }

                var index = new Dictionary<string, Type[]>(byName.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in byName)
                {
                    index.Add(pair.Key, pair.Value.ToArray());
                }

                // Published before the flag, so nothing can see s_Scanned true with no index behind it.
                s_TypesByShortName = index;
                s_ScannedAssemblyCount = loaded;
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
