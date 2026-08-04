namespace Vapor.Serialization
{
    /// <summary>
    /// Per-operation state for a VSL read or write: the options in force, the reference resolver, and
    /// the recursion guard.
    /// </summary>
    public sealed class VslContext
    {
        /// <summary>
        /// Shared context using <see cref="VslOptions.Default"/> and
        /// <see cref="VslObjectReferenceResolver"/>.
        /// </summary>
        public static VslContext Default { get; } = new VslContext();

        /// <summary>Formatting and leniency settings.</summary>
        public VslOptions Options { get; }

        /// <summary>Maps <c>UnityEngine.Object</c> instances to and from <c>@id</c> tokens.</summary>
        public IVslReferenceResolver References { get; set; } = VslObjectReferenceResolver.Instance;

        /// <summary>
        /// Nesting limit, which is what stops a reference cycle from becoming a stack overflow. VSL
        /// has no back-reference syntax, so a cycle is a authoring error rather than something to
        /// encode.
        /// </summary>
        public int MaxDepth { get; set; } = 64;

        private int _depth;

        public VslContext() : this(null)
        {
        }

        public VslContext(VslOptions options)
        {
            Options = options ?? VslOptions.Default;
        }

        /// <summary>
        /// Enters one level of nesting, throwing past <see cref="MaxDepth"/>.
        /// </summary>
        /// <remarks>
        /// Public because generated formatters live in the consuming assembly and bracket their
        /// object bodies with this pair.
        /// </remarks>
        public void EnterDepth()
        {
            if (++_depth > MaxDepth)
            {
                _depth = 0;
                throw new VslException(
                    $"Exceeded the maximum nesting depth of {MaxDepth}. This usually means the object graph contains a reference cycle.");
            }
        }

        /// <inheritdoc cref="EnterDepth"/>
        public void ExitDepth() => _depth--;

        internal void ResetDepth() => _depth = 0;
    }
}
