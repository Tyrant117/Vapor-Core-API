using System;
using System.Collections.Generic;
using Vapor.Serialization;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// A cache of what the scratchpad folder contains, so the window can open without reading every
    /// session on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never the truth. The folders are the truth, and this file is rebuilt from them on every
    /// refresh. A missing, stale or corrupt index costs nothing: it is regenerated without a word.
    /// That is deliberate — an index that had to be trusted would be one more thing to keep correct
    /// across a hand-dropped file, and hand-dropped files are the entire input to this tool.
    /// </para>
    /// <para>
    /// The one thing worth preserving across a rebuild is <see cref="ScratchpadFeatureEntry.NextNoteNumber"/>,
    /// since reissuing a note id already quoted in a chat would attach the reply to the wrong note.
    /// When the index is gone, the counter is recovered by taking the highest id in the feature's
    /// notes files and adding one.
    /// </para>
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadIndex
    {
        public string Generated = string.Empty;
        public List<ScratchpadFeatureEntry> Features = new();
    }

    /// <summary>One feature folder, summarised.</summary>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadFeatureEntry
    {
        public string Name = string.Empty;
        public string Slug = string.Empty;

        /// <summary>The next number to hand out for a note id in this feature.</summary>
        public int NextNoteNumber = 1;

        public string LatestSession = string.Empty;
        public int SessionCount;
        public int OpenIssues;
        public int OpenWork;
    }
}
