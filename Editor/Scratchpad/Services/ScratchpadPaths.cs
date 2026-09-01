using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Scripting.LifecycleManagement;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Every path and name rule the scratchpad uses, in one place.
    /// </summary>
    /// <remarks>
    /// Small enough to inline everywhere and dangerous enough not to. The naming rules are a contract
    /// with a program that is not this one — the assistant writes files into these folders by hand,
    /// following a spec — so the parsing has to stay tolerant of what a hand-written name looks like.
    /// </remarks>
    [NoAutoStaticsCleanup]
    internal static class ScratchpadPaths
    {
        public const string HandoffSuffix = ".handoff.vsl";
        public const string NotesSuffix = ".notes.vsl";
        public const string IndexFileName = "index.vsl";
        public const string ArchiveFolderName = "Archive";

        /// <summary>The stamp format: sortable as a plain string, and readable as a date.</summary>
        public const string StampFormat = "yyyy-MM-dd-HHmm";

        /// <summary>
        /// Points the whole scratchpad somewhere else. Null in every case but a test.
        /// </summary>
        /// <remarks>
        /// The alternative was threading a root through every path rule and every store method, which
        /// would put the seam in twenty signatures instead of one field. Tests set this to a temp
        /// directory so they can create, archive and delete real files without touching the project.
        /// </remarks>
        internal static string RootOverride;

        /// <summary>Project-relative root, e.g. <c>Assets/Vapor/Editor/Scratchpad</c>.</summary>
        public static string Root => RootOverride ?? $"Assets/{FolderSetupUtility.SCRATCHPAD_RELATIVE_PATH}";

        public static string ArchiveRoot => $"{Root}/{ArchiveFolderName}";

        public static string FeatureDirectory(string feature, bool archived = false) =>
            $"{(archived ? ArchiveRoot : Root)}/{feature}";

        public static string HandoffPath(string feature, string stamp, bool archived = false) =>
            $"{FeatureDirectory(feature, archived)}/{stamp}{HandoffSuffix}";

        public static string NotesPath(string feature, string stamp, bool archived = false) =>
            $"{FeatureDirectory(feature, archived)}/{stamp}{NotesSuffix}";

        public static string IndexPath => $"{Root}/{IndexFileName}";

        /// <summary>
        /// A feature's test filter and run history.
        /// </summary>
        /// <remarks>
        /// Named so it cannot collide with a session: sessions are always
        /// <c>&lt;stamp&gt;.handoff.vsl</c>, and the scan only ever looks for that suffix, so a file
        /// called <c>tests.vsl</c> sits in the same folder without being mistaken for one.
        /// </remarks>
        public static string TestLogPath(string feature, bool archived = false) =>
            $"{FeatureDirectory(feature, archived)}/tests.vsl";

        /// <summary>A feature's own description, for the kickoff prompt.</summary>
        public static string FeatureInfoPath(string feature, bool archived = false) =>
            $"{FeatureDirectory(feature, archived)}/feature.vsl";

        /// <summary>
        /// A feature name reduced to something that reads well inside a note id.
        /// </summary>
        /// <remarks>
        /// Runs of anything not alphanumeric collapse to a single dash, so "UV Editor" and
        /// "UV  Editor!" both slug to <c>uv-editor</c> and cannot hand out ids that collide with each
        /// other's while looking distinct.
        /// </remarks>
        public static string Slug(string feature)
        {
            if (string.IsNullOrWhiteSpace(feature))
            {
                return "unfiled";
            }

            var sb = new StringBuilder(feature.Length);
            var pendingDash = false;

            foreach (var c in feature)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (pendingDash && sb.Length > 0)
                    {
                        sb.Append('-');
                    }

                    pendingDash = false;
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    pendingDash = true;
                }
            }

            return sb.Length == 0 ? "unfiled" : sb.ToString();
        }

        /// <summary>Strips <c>.handoff.vsl</c> off a file name to get its session stamp.</summary>
        public static string StampFromHandoffFile(string path)
        {
            var name = Path.GetFileName(path);
            return name.EndsWith(HandoffSuffix, StringComparison.OrdinalIgnoreCase)
                ? name[..^HandoffSuffix.Length]
                : name;
        }

        /// <summary>
        /// A stamp for now, stepped past anything already in <paramref name="directory"/>.
        /// </summary>
        /// <remarks>
        /// Two sessions in the same minute are rare but not impossible, and the stamp is the session's
        /// identity — a collision would have one session's notes annotating the other's changes.
        /// </remarks>
        public static string NewStamp(DateTime now, string directory)
        {
            var stamp = now.ToString(StampFormat, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(directory) || !File.Exists($"{directory}/{stamp}{HandoffSuffix}"))
            {
                return stamp;
            }

            for (var i = 2; i < 100; i++)
            {
                var candidate = $"{stamp}-{i}";
                if (!File.Exists($"{directory}/{candidate}{HandoffSuffix}"))
                {
                    return candidate;
                }
            }

            return $"{stamp}-{now.Second:00}";
        }

        /// <summary>
        /// Recovers the time a stamp names, for a handoff that did not say when it was written.
        /// </summary>
        public static bool TryParseStamp(string stamp, out DateTime value)
        {
            value = default;
            if (string.IsNullOrEmpty(stamp) || stamp.Length < StampFormat.Length)
            {
                return false;
            }

            return DateTime.TryParseExact(stamp[..StampFormat.Length], StampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        /// <summary>The timestamp format written into every model that records one.</summary>
        public static string Timestamp(DateTime value) =>
            value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        public static bool TryParseTimestamp(string text, out DateTime value) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
