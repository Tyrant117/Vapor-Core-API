using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// How a type reads in a picker: what it is called, and what it does.
    /// </summary>
    /// <remarks>
    /// A type picker over a closed set is a different problem from a type browser over the project. A
    /// browser has to disambiguate thousands of names, so it shows them raw and files them by namespace.
    /// A picker offers a handful of siblings, and every one of them shares the same namespace and the
    /// same suffix — so the parts that would disambiguate are exactly the parts carrying no information.
    /// This strips them.
    /// </remarks>
    internal static class TypeDisplayNames
    {
        /// <summary>
        /// Shortest stem a name may be reduced to. Below this the suffix was carrying the meaning, so
        /// nothing is stripped from anything and the full names stay.
        /// </summary>
        private const int MinimumStem = 2;

        /// <summary>
        /// The description shown under a type's name, or null.
        /// </summary>
        /// <remarks>
        /// Read from <see cref="DropdownTooltipAttribute"/>, which already existed for exactly this and
        /// which the reference dropdown already honoured as a hover tooltip. A type without one falls
        /// back to its name alone, so annotating is worth doing but never required.
        /// </remarks>
        public static string Describe(Type type) =>
            type?.GetCustomAttribute<DropdownTooltipAttribute>(false)?.Tooltip;

        /// <summary>
        /// The name a picker shows for a type, with <paramref name="sharedSuffix"/> removed and the
        /// remainder spaced out.
        /// </summary>
        public static string GetDisplayName(Type type, string sharedSuffix = null)
        {
            if (type == null)
            {
                return string.Empty;
            }

            var name = type.Name;

            // Generics arrive as 'Foo`1'; the arity is noise in a list.
            var arity = name.IndexOf('`');
            if (arity > 0)
            {
                name = name.Substring(0, arity);
            }

            if (!string.IsNullOrEmpty(sharedSuffix)
                && name.Length - sharedSuffix.Length >= MinimumStem
                && name.EndsWith(sharedSuffix, StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - sharedSuffix.Length);
            }

            return ObjectNames.NicifyVariableName(name);
        }

        /// <summary>
        /// The longest trailing word every candidate shares, or null when there is nothing worth cutting.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Derived from the candidates rather than from the interface they implement, because the two
        /// rarely line up — every <c>IEffectDataExtension</c> ends in <c>EffectExtension</c>, not
        /// <c>EffectDataExtension</c>. Taking it from the set means it also works for a family that
        /// shares no base type name at all.
        /// </para>
        /// <para>
        /// Snapped to an upper-case letter so the cut lands on a word: the common tail of
        /// <c>StackingEffectExtension</c> and <c>GrantTagsEffectExtension</c> is <c>EffectExtension</c>,
        /// and a naive longest-common-suffix would have been happy to cut in the middle of a word for a
        /// set whose names happened to rhyme.
        /// </para>
        /// </remarks>
        public static string FindSharedSuffix(IReadOnlyList<Type> candidates)
        {
            if (candidates == null || candidates.Count < 2)
            {
                return null;
            }

            var first = candidates[0].Name;
            var shared = first.Length;

            for (int i = 1; i < candidates.Count && shared > 0; i++)
            {
                var other = candidates[i].Name;
                var matched = 0;
                while (matched < shared
                       && matched < other.Length
                       && first[first.Length - 1 - matched] == other[other.Length - 1 - matched])
                {
                    matched++;
                }

                shared = matched;
            }

            // Back off to a word boundary.
            while (shared > 0 && !char.IsUpper(first[first.Length - shared]))
            {
                shared--;
            }

            if (shared == 0)
            {
                return null;
            }

            var suffix = first.Substring(first.Length - shared);

            // All or nothing: a set where one name would be gutted keeps its full names throughout,
            // rather than reading as a mix of long and cryptic.
            foreach (var candidate in candidates)
            {
                if (candidate.Name.Length - suffix.Length < MinimumStem)
                {
                    return null;
                }
            }

            return suffix;
        }
    }
}
