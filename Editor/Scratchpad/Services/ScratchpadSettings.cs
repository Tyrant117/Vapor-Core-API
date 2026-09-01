using UnityEditor;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// The handful of preferences the scratchpad exposes.
    /// </summary>
    /// <remarks>
    /// <see cref="EditorPrefs"/> rather than a file in the project, because every one of these is a
    /// statement about how one person likes to work rather than about the project. Putting them in
    /// the scratchpad folder would make them something to merge.
    /// </remarks>
    internal static class ScratchpadSettings
    {
        private const string Prefix = "Vapor.Scratchpad.";

        private const string AutoArchiveKey = Prefix + "AutoArchive";
        private const string ArchiveHoursKey = Prefix + "ArchiveHours";
        private const string ExpandedSessionsKey = Prefix + "ExpandedSessions";
        private const string LastFeatureKey = Prefix + "LastFeature";

        /// <summary>Whether fully-closed sessions move themselves into <c>Archive/</c>.</summary>
        public static bool AutoArchive
        {
            get => EditorPrefs.GetBool(AutoArchiveKey, true);
            set => EditorPrefs.SetBool(AutoArchiveKey, value);
        }

        /// <summary>
        /// How old a session must be before auto-archive will touch it.
        /// </summary>
        /// <remarks>
        /// Twelve hours is short because age is the weakest of the four conditions, not the point of
        /// them: a session has to be fully reviewed and fully closed before its age is even asked
        /// about. See <c>ScratchpadStore.IsArchivable</c>.
        /// </remarks>
        public static int ArchiveHours
        {
            get => EditorPrefs.GetInt(ArchiveHoursKey, 12);
            set => EditorPrefs.SetInt(ArchiveHoursKey, value < 1 ? 1 : value);
        }

        /// <summary>How many of a feature's newest sessions open expanded.</summary>
        public static int ExpandedSessions
        {
            get => EditorPrefs.GetInt(ExpandedSessionsKey, 3);
            set => EditorPrefs.SetInt(ExpandedSessionsKey, value < 1 ? 1 : value);
        }

        /// <summary>The feature the main window last had open, and the one quick capture files onto.</summary>
        public static string LastFeature
        {
            get => EditorPrefs.GetString(LastFeatureKey, string.Empty);
            set => EditorPrefs.SetString(LastFeatureKey, value ?? string.Empty);
        }
    }
}
