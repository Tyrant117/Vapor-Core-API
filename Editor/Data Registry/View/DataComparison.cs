using System;
using System.Collections.Generic;
using UnityEngine;
using Vapor.Serialization;
using VaporEditor.Inspector;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Marks the fields that disagree across a comparison.
    /// </summary>
    /// <remarks>
    /// Works on groups of already-drawn fields, where a group is the same member across every entry
    /// being compared. The grid knows how to build those groups whichever way round it is laid out,
    /// so nothing here has to care about rows, columns, or which axis the entries are on. Reading the
    /// values back through <see cref="InspectorTreeProperty"/> also means the comparison sees exactly
    /// what the editor shows — no second opinion about what a member is or how to reach it.
    /// </remarks>
    internal static class DataComparison
    {
        private static readonly Color HighlightBackground = new Color(0.90f, 0.60f, 0.20f, 0.14f);
        private static readonly Color HighlightBorder = new Color(0.90f, 0.60f, 0.20f, 0.85f);

        private const string Tooltip = "This value differs between the entries being compared.";

        /// <summary>
        /// Highlights every field in a group whose values are not all the same.
        /// </summary>
        public static void FlagDifferences(IEnumerable<IReadOnlyList<TreePropertyField>> groups)
        {
            if (groups == null)
            {
                return;
            }

            foreach (var group in groups)
            {
                if (group == null || group.Count < 2 || AllEqual(group))
                {
                    continue;
                }

                foreach (var field in group)
                {
                    Highlight(field);
                }
            }
        }

        private static bool AllEqual(IReadOnlyList<TreePropertyField> fields)
        {
            var first = SafeGet(fields[0]);
            var type = fields[0].Property.PropertyType;

            for (var i = 1; i < fields.Count; i++)
            {
                if (!SameValue(first, SafeGet(fields[i]), type))
                {
                    return false;
                }
            }

            return true;
        }

        private static object SafeGet(TreePropertyField field)
        {
            try
            {
                return field.Property.GetValue();
            }
            catch (Exception)
            {
                // A member that throws on read cannot be compared, and an editor decoration is not
                // worth failing over. Treated as null, which compares equal to other unreadable ones.
                return null;
            }
        }

        /// <summary>
        /// Compares two member values, falling back to their serialized form.
        /// </summary>
        /// <remarks>
        /// <see cref="object.Equals(object)"/> alone is not enough. A list, a nested object or a
        /// <c>LocalizedString</c> compares by reference, so two entries carrying identical data would
        /// be flagged as different. Serializing both answers the question the user is actually
        /// asking — would these two write the same thing to the document — using the format that
        /// defines it.
        /// </remarks>
        private static bool SameValue(object a, object b, Type type)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (a.Equals(b))
            {
                return true;
            }

            try
            {
                return string.Equals(Vsl.Serialize(a, type), Vsl.Serialize(b, type), StringComparison.Ordinal);
            }
            catch (Exception)
            {
                // Not serializable, and Equals already said no.
                return false;
            }
        }

        private static void Highlight(TreePropertyField field)
        {
            field.style.backgroundColor = HighlightBackground;
            field.style.borderLeftWidth = 2;
            field.style.borderLeftColor = HighlightBorder;
            field.style.paddingLeft = 4;

            if (string.IsNullOrEmpty(field.tooltip))
            {
                field.tooltip = Tooltip;
            }
        }
    }
}
