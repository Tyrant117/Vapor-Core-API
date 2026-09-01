using System;
using System.Collections.Generic;
using Vapor.Serialization;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Everything the editor knows about one session that the assistant did not write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored beside its handoff as <c>&lt;stamp&gt;.notes.vsl</c>. The split is the point: the
    /// assistant owns the handoff and never reads this file, the editor owns this file and never
    /// writes the handoff, so neither can lose the other's work. It also means a handoff can be
    /// re-dropped or hand-edited without costing you a single note.
    /// </para>
    /// <para>
    /// Unlike <see cref="ScratchpadHandoff"/>, nothing here is part of the assistant-facing contract,
    /// so the members carry no <c>[VslComment]</c>. It is written and read by one program.
    /// </para>
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadNotes
    {
        /// <summary>The session stamp this file annotates, e.g. <c>2026-08-31-1430</c>.</summary>
        public string Handoff = string.Empty;

        /// <summary>Ids the editor assigned to changes whose handoff left <c>id</c> empty.</summary>
        public List<ScratchpadIdBackfill> Backfilled = new();

        /// <summary>How far reading has got, per change. A change with no entry is unreviewed.</summary>
        public List<ScratchpadReview> Reviews = new();

        /// <summary>Every note on this session, attached and loose alike.</summary>
        public List<ScratchpadNote> Notes = new();

        /// <summary>What was decided about each follow-up the handoff proposed.</summary>
        public List<ScratchpadFollowUpState> FollowUpStates = new();
    }

    /// <summary>
    /// A change id the editor invented because the handoff omitted one.
    /// </summary>
    /// <remarks>
    /// Keyed by ordinal because that is all an id-less change has. Persisting the assignment is what
    /// makes it stable: without this the id would be regenerated on every load and every note
    /// hanging off it would come unstuck.
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadIdBackfill
    {
        public int Ordinal;
        public string Id = string.Empty;
    }

    /// <summary>Review state for one change.</summary>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadReview
    {
        public string ChangeId = string.Empty;
        public ReviewState State = ReviewState.Unreviewed;
    }

    /// <summary>One thing you had to say.</summary>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadNote
    {
        /// <summary>Globally unique and short enough to quote back, e.g. <c>uv-editor-17</c>.</summary>
        public string Id = string.Empty;

        /// <summary>The change this is about. Empty means a loose note on the feature.</summary>
        public string ChangeId = string.Empty;

        public NoteKind Kind = NoteKind.Comment;
        public NoteStatus Status = NoteStatus.Open;

        public string Body = string.Empty;

        /// <summary>ISO-8601 local timestamps. Empty <see cref="Sent"/> means never included in a prompt.</summary>
        public string Created = string.Empty;
        public string Sent = string.Empty;

        public NoteSource Source = NoteSource.Manual;

        /// <summary>
        /// Set when this note came from a proposed follow-up — either accepting one, or replying to it.
        /// </summary>
        /// <remarks>
        /// The kind is what separates the two: accepting produces <see cref="NoteKind.Work"/>, replying
        /// produces a <see cref="NoteKind.Comment"/>. See <see cref="IsFollowUpReply"/>.
        /// </remarks>
        public string FollowUpId = string.Empty;

        /// <summary>Whatever was selected when the note was captured, as an asset or scene path.</summary>
        public string Context = string.Empty;

        /// <summary>A captured console entry, message and stack together.</summary>
        public string Console = string.Empty;

        /// <summary>
        /// True when this note is an answer to a proposal rather than a note in its own right.
        /// </summary>
        /// <remarks>
        /// A Work note created by accepting a proposal is not a reply — it is the work itself, and it
        /// belongs in the session's list like any other.
        /// </remarks>
        public bool IsFollowUpReply => !string.IsNullOrEmpty(FollowUpId) && Kind == NoteKind.Comment;

        /// <summary>
        /// Still owed something: written or handed over, but not closed either way.
        /// </summary>
        /// <remarks>
        /// A property rather than a field, which is also how it stays out of the file — the schema
        /// only picks up properties that ask for it with <c>[VslSerialize]</c>. Storing it would give
        /// two things to keep in step with <see cref="Status"/>.
        /// </remarks>
        public bool IsOutstanding => Status is NoteStatus.Open or NoteStatus.Sent;
    }

    /// <summary>Your decision on one proposed follow-up.</summary>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadFollowUpState
    {
        public string FollowUpId = string.Empty;
        public FollowUpState State = FollowUpState.Proposed;
    }
}
