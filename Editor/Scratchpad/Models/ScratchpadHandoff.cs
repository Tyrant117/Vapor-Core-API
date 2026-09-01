using System;
using System.Collections.Generic;
using Vapor.Serialization;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// One session of work, as written by the assistant that did it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assistant's half of the scratchpad and the editor never writes it. Everything the
    /// window knows about what changed comes from a file shaped like this, dropped into
    /// <c>Assets/Vapor/Editor/Scratchpad/&lt;Feature&gt;/</c>. There is no git integration and no
    /// asset scanning behind it — an unwritten handoff is an invisible session.
    /// </para>
    /// <para>
    /// Every member carries a <see cref="VslCommentAttribute"/> because those comments are the
    /// contract. <see cref="ScratchpadContractBuilder"/> serializes an empty instance of this type
    /// and hands the result to the assistant as the template to fill in, so a comment written here
    /// is instruction the next session actually reads.
    /// </para>
    /// <para>
    /// Only <see cref="Feature"/>, <see cref="Title"/> and <see cref="Changes"/> are worth anything.
    /// The reader is non-strict, so a handoff missing every other member still loads, and the views
    /// are all written to render an absent field as absent rather than as empty chrome.
    /// </para>
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadHandoff
    {
        [VslComment("Which feature this session belongs to. Must match the containing folder name.")]
        public string Feature = string.Empty;

        [VslComment("One line: what this session set out to do.")]
        public string Title = string.Empty;

        [VslComment("A few sentences of context for the whole session.")]
        public string Summary = string.Empty;

        [VslComment("ISO-8601 local timestamp this session was written, e.g. 2026-08-31T14:30:00.")]
        public string Written = string.Empty;

        [VslComment("Note ids from earlier sessions that this session addressed. The editor closes them.")]
        public List<string> Resolved = new();

        [VslComment("One entry per meaningful change. This is what gets commented on.")]
        public List<ScratchpadChange> Changes = new();

        [VslComment("Work this session deliberately left undone. Arrives as proposed, and is accepted or dismissed in the editor.")]
        public List<ScratchpadFollowUp> FollowUps = new();

        [VslComment("Tests covering this work, as namespaces, fixtures or single test names. The editor runs these on a button and files failures as issues.")]
        public List<string> Tests = new();

        [VslComment("Set by the editor on a session it created to hold notes before any real handoff landed. Do not write this.")]
        public bool Placeholder;
    }

    /// <summary>One change within a session — the unit a note attaches to.</summary>
    /// <remarks>
    /// <see cref="Rationale"/> and <see cref="Risk"/> are the fields the whole tool exists for. A
    /// list of edited files says what happened and invites nothing; a stated reason and a stated
    /// doubt are things a reader can agree or disagree with, which is what a review is.
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadChange
    {
        [VslComment("Short stable slug, unique within this file. Notes attach to it, so do not reuse a slug for a different change.")]
        public string Id = string.Empty;

        [VslComment("One line naming the change.")]
        public string Title = string.Empty;

        [VslComment("What actually changed, in a sentence or three.")]
        public string Summary = string.Empty;

        [VslComment("Why it was done this way, and what was considered and rejected.")]
        public string Rationale = string.Empty;

        [VslComment("What is uncertain, untested, or deliberately cut. Say so plainly - this field earns the most comments back.")]
        public string Risk = string.Empty;

        [VslComment("Project-relative paths touched. May be empty.")]
        public List<string> Files = new();
    }

    /// <summary>Work the session knew it was leaving behind.</summary>
    /// <remarks>
    /// Kept separate from <see cref="ScratchpadChange.Risk"/> prose so it can be triaged rather than
    /// merely read. A follow-up is a proposal until accepted: the outstanding-work list stays the
    /// user's, and nothing files itself into it.
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadFollowUp
    {
        [VslComment("Short stable slug, unique within this file.")]
        public string Id = string.Empty;

        [VslComment("One line naming the work.")]
        public string Title = string.Empty;

        [VslComment("Why it was left, and anything the next session would need to know to pick it up.")]
        public string Detail = string.Empty;
    }
}
