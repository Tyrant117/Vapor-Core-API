using System;

namespace Vapor.Serialization
{
    /// <summary>
    /// Member-name matching shared by the reflection path and by generated formatters.
    /// </summary>
    /// <remarks>
    /// Public because generated formatters live in the consuming assembly and call into this.
    /// </remarks>
    public static class VslNames
    {
        /// <summary>
        /// Compares a member name from the document against an expected name, case-insensitively and
        /// ignoring a leading <c>_</c> or <c>m_</c>.
        /// </summary>
        /// <remarks>
        /// Deliberate leniency: <c>_hp</c>, <c>hp</c> and <c>m_Hp</c> all name the same member, so
        /// hand- and machine-authored documents bind without knowing the field's private naming
        /// convention.
        /// </remarks>
        public static bool Matches(ReadOnlySpan<char> name, string expected)
        {
            if (name.Equals(expected.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Normalize(name).Equals(Normalize(expected.AsSpan()), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Strips a leading <c>m_</c> or <c>_</c> prefix.</summary>
        public static ReadOnlySpan<char> Normalize(ReadOnlySpan<char> name)
        {
            if (name.Length >= 2 && (name[0] == 'm' || name[0] == 'M') && name[1] == '_')
            {
                return name.Slice(2);
            }

            if (name.Length >= 1 && name[0] == '_')
            {
                return name.Slice(1);
            }

            return name;
        }
    }
}
