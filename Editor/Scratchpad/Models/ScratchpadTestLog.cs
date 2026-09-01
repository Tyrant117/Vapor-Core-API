using System;
using System.Collections.Generic;
using Vapor.Serialization;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// A feature's test filter and the history of what it has done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored as <c>tests.vsl</c> in the feature folder, beside the handoffs rather than inside any
    /// one of them. Which tests cover a feature is a fact about the feature and outlives every
    /// session in it; hanging it off the newest handoff would lose it the next time one landed.
    /// </para>
    /// <para>
    /// The window owns this file, like the notes files. Nothing in the assistant-facing contract
    /// mentions it, so the members carry no <c>[VslComment]</c>.
    /// </para>
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadTestLog
    {
        /// <summary>
        /// Tests added by hand, on top of whatever the handoffs declare.
        /// </summary>
        /// <remarks>
        /// The handoffs are the main source — the assistant knows what it wrote and says so in the
        /// handoff's <c>tests</c> list — and this is the escape hatch for anything it did not think
        /// to name. Picked from the real test tree rather than typed, so a name here always matches
        /// something that exists.
        /// </remarks>
        public List<string> Extra = new();

        /// <summary>Tests named by a handoff that you have switched off for this feature.</summary>
        public List<string> Excluded = new();

        /// <summary>Newest first, capped so the file does not grow without bound.</summary>
        public List<ScratchpadTestRun> Runs = new();
    }

    /// <summary>One execution of a feature's tests.</summary>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadTestRun
    {
        public string When = string.Empty;
        public int Passed;
        public int Failed;
        public int Skipped;

        /// <summary>Seconds, as the runner reported them.</summary>
        public float Duration;

        /// <summary>The failing test names, so a run reads as more than a count.</summary>
        public List<string> Failures = new();

        public bool IsGreen => Failed == 0;

        public string Summary => Failed == 0
            ? $"{Passed} passed"
            : $"{Failed} failed, {Passed} passed";
    }
}
