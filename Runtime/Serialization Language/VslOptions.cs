using Unity.Scripting.LifecycleManagement;

namespace Vapor.Serialization
{
    /// <summary>
    /// Formatting and leniency settings for a VSL read or write.
    /// </summary>
    [NoAutoStaticsCleanup]
    public sealed class VslOptions
    {
        /// <summary>
        /// Shared default settings. Treat as immutable — call <see cref="Clone"/> before changing
        /// anything.
        /// </summary>
        public static VslOptions Default { get; } = new VslOptions();

        /// <summary>
        /// Shared settings that turn semantic mismatches into errors. Suits tests and validation
        /// tooling. Treat as immutable.
        /// </summary>
        public static VslOptions Validating { get; } = new VslOptions { Strict = true };

        /// <summary>Spaces per indent level.</summary>
        public int IndentWidth { get; set; } = 2;

        /// <summary>
        /// Column budget for writing a container on one line. A container holding no nested
        /// containers is written inline when it fits.
        /// </summary>
        public int InlineWidth { get; set; } = 60;

        /// <summary>
        /// Longest sequence of scalars still written on one line. Beyond this, one element per line
        /// — which is both easier to read and easier for a model to extend correctly.
        /// </summary>
        public int InlineSequenceLimit { get; set; } = 8;

        /// <summary>
        /// Most members an object may have and still be written on one line. Applies only when every
        /// member is a scalar.
        /// </summary>
        public int InlineMemberLimit { get; set; } = 3;

        /// <summary>Whether to emit the <c>@vsl 1</c> header.</summary>
        public bool EmitHeader { get; set; } = true;

        /// <summary>Whether to emit <c>#</c> comments declared by <see cref="VslCommentAttribute"/>.</summary>
        public bool EmitComments { get; set; } = true;

        /// <summary>
        /// When false (the default), an unknown member is skipped, a missing member keeps its
        /// existing value, and an unresolvable reference becomes null. When true, each of those
        /// throws <see cref="VslException"/>.
        /// </summary>
        /// <remarks>
        /// The lenient default is what makes partially-specified machine-authored input load.
        /// </remarks>
        public bool Strict { get; set; }

        /// <summary>Line ending used on write. Fixed to "\n" so output is byte-stable across platforms.</summary>
        public string NewLine { get; set; } = "\n";

        public VslOptions Clone() => (VslOptions)MemberwiseClone();
    }
}
