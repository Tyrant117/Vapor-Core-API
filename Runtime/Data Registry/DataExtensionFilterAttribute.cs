using System;

namespace Vapor
{
    /// <summary>
    /// Narrows which <see cref="IDataExtension"/> implementations the editor offers for a list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list's element type already sets a floor — a <c>List&lt;IItemDataExtension&gt;</c> can only
    /// hold item extensions. This is for narrowing further than that: a member that should only accept
    /// part of the family says so here rather than accepting anything that compiles and rejecting it
    /// later.
    /// </para>
    /// <para>
    /// With no types given the filter is just the element type, which is the common case.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
    public class DataExtensionFilterAttribute : Attribute
    {
        /// <summary>Bases or interfaces an offered extension has to derive from. Any one is enough.</summary>
        public Type[] AllowedTypes { get; }

        public DataExtensionFilterAttribute(params Type[] allowedTypes)
        {
            AllowedTypes = allowedTypes ?? Array.Empty<Type>();
        }

        /// <summary>Whether a concrete extension type passes this filter.</summary>
        public bool Allows(Type candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (AllowedTypes.Length == 0)
            {
                return true;
            }

            foreach (var allowed in AllowedTypes)
            {
                if (allowed != null && allowed.IsAssignableFrom(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
