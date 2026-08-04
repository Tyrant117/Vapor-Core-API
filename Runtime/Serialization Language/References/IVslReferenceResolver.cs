using System;
using Object = UnityEngine.Object;

namespace Vapor.Serialization
{
    /// <summary>
    /// Maps <see cref="Object"/> instances to and from the references written into a VSL document.
    /// </summary>
    /// <remarks>
    /// The default implementation, <see cref="VslObjectReferenceResolver"/>, writes an
    /// <c>EntityId</c> for the current session plus a durable asset locator where one exists, and
    /// resolves in that order. Replace it — or pre-seed a <see cref="VslReferenceTable"/> — to key
    /// references on something else entirely, such as your own network or save ids.
    /// </remarks>
    public interface IVslReferenceResolver
    {
        /// <summary>
        /// Produces the reference to write for <paramref name="obj"/>. Returning false writes
        /// <c>@null</c>.
        /// </summary>
        bool TryGetReference(Object obj, out VslObjectReference reference);

        /// <summary>
        /// Resolves a reference read from a document back to an instance. Returning false yields
        /// null, or throws when <see cref="VslOptions.Strict"/> is set.
        /// </summary>
        /// <param name="expectedType">The declared member type, for narrowing or validation.</param>
        bool TryResolve(in VslObjectReference reference, Type expectedType, out Object obj);
    }
}
