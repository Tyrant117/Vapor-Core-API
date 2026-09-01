using System;
using System.Collections.Generic;
using System.Linq;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// One feature folder and everything loaded out of it.
    /// </summary>
    /// <remarks>
    /// Not serialized. The feature has no file of its own — it is a folder, and this is what the
    /// store builds after reading one.
    /// </remarks>
    internal sealed class ScratchpadFeature
    {
        public string Name = string.Empty;
        public string Slug = string.Empty;

        /// <summary>True for the mirror of this feature living under <c>Archive/</c>.</summary>
        public bool Archived;

        /// <summary>Newest first, which is the order every view wants.</summary>
        public readonly List<ScratchpadSession> Sessions = new();

        /// <summary>Next number to hand out for a note id. Carried across refreshes by the index.</summary>
        public int NextNoteNumber = 1;

        public ScratchpadSession Newest => Sessions.Count > 0 ? Sessions[0] : null;

        public IEnumerable<ScratchpadNote> AllNotes => Sessions.SelectMany(s => s.Notes.Notes);

        public int OpenIssues => AllNotes.Count(n => n.Kind == NoteKind.Issue && n.IsOutstanding);

        public int OpenWork => AllNotes.Count(n => n.Kind == NoteKind.Work && n.IsOutstanding);

        public int OpenItems => AllNotes.Count(n => n.IsOutstanding);
    }

    /// <summary>
    /// A handoff file and its sibling notes file, held together.
    /// </summary>
    /// <remarks>
    /// The pairing is the whole design: <see cref="Handoff"/> is read-only and belongs to whoever
    /// wrote it, <see cref="Notes"/> is ours to write. Nothing on this class writes to the handoff,
    /// with the single exception noted on <see cref="ScratchpadStore"/> about backfilled change ids,
    /// which are patched into the in-memory copy only.
    /// </remarks>
    internal sealed class ScratchpadSession
    {
        public ScratchpadFeature Feature;
        public string Stamp = string.Empty;
        public bool Archived;

        public ScratchpadHandoff Handoff = new();
        public ScratchpadNotes Notes = new();

        /// <summary>Set when the handoff would not parse. The session still lists, showing this.</summary>
        public string ParseError;

        /// <summary>When the work happened, from the handoff if it said, from the stamp if not.</summary>
        public DateTime Written;

        /// <summary>Set by any mutation; cleared by <see cref="ScratchpadStore.SaveNotes"/>.</summary>
        public bool NotesDirty;

        public string FeatureName => Feature?.Name ?? Handoff.Feature;

        public string HandoffPath => ScratchpadPaths.HandoffPath(FeatureName, Stamp, Archived);

        public string NotesPath => ScratchpadPaths.NotesPath(FeatureName, Stamp, Archived);

        public bool IsPlaceholder => Handoff.Placeholder;

        public string DisplayTitle => !string.IsNullOrWhiteSpace(Handoff.Title)
            ? Handoff.Title
            : IsPlaceholder
                ? "Notes with no handoff yet"
                : "Untitled session";

        public string DisplayStamp => Written == default
            ? Stamp
            : Written.ToString("yyyy-MM-dd HH:mm");

        public List<ScratchpadChange> Changes => Handoff.Changes;

        /// <summary>
        /// Notes on one change, or — for an empty id — on the session itself.
        /// </summary>
        /// <remarks>
        /// Replies to a proposed follow-up are excluded from both. They carry no change id, so they
        /// would otherwise land in the session's own list as well as on the proposal they answer, and
        /// appear twice in the pane.
        /// </remarks>
        public IEnumerable<ScratchpadNote> NotesFor(string changeId) =>
            Notes.Notes.Where(n => n.ChangeId == changeId && !n.IsFollowUpReply);

        /// <summary>Notes that name no change: written before, or beside, the work they are about.</summary>
        public IEnumerable<ScratchpadNote> LooseNotes =>
            Notes.Notes.Where(n => string.IsNullOrEmpty(n.ChangeId) && !n.IsFollowUpReply);

        /// <summary>What you said back about one proposed follow-up.</summary>
        public IEnumerable<ScratchpadNote> NotesForFollowUp(string followUpId) =>
            Notes.Notes.Where(n => n.FollowUpId == followUpId && n.Kind == NoteKind.Comment);

        public ScratchpadFollowUp FindFollowUp(string followUpId) =>
            string.IsNullOrEmpty(followUpId)
                ? null
                : Handoff.FollowUps.FirstOrDefault(f => f.Id == followUpId);

        public int OutstandingCount => Notes.Notes.Count(n => n.IsOutstanding);

        public ReviewState ReviewOf(string changeId)
        {
            foreach (var review in Notes.Reviews)
            {
                if (review.ChangeId == changeId)
                {
                    return review.State;
                }
            }

            return ReviewState.Unreviewed;
        }

        public void SetReview(string changeId, ReviewState state)
        {
            foreach (var review in Notes.Reviews)
            {
                if (review.ChangeId != changeId)
                {
                    continue;
                }

                review.State = state;
                NotesDirty = true;
                return;
            }

            Notes.Reviews.Add(new ScratchpadReview { ChangeId = changeId, State = state });
            NotesDirty = true;
        }

        public FollowUpState StateOf(string followUpId)
        {
            foreach (var state in Notes.FollowUpStates)
            {
                if (state.FollowUpId == followUpId)
                {
                    return state.State;
                }
            }

            return FollowUpState.Proposed;
        }

        public void SetFollowUpState(string followUpId, FollowUpState value)
        {
            foreach (var state in Notes.FollowUpStates)
            {
                if (state.FollowUpId != followUpId)
                {
                    continue;
                }

                state.State = value;
                NotesDirty = true;
                return;
            }

            Notes.FollowUpStates.Add(new ScratchpadFollowUpState { FollowUpId = followUpId, State = value });
            NotesDirty = true;
        }

        /// <summary>Every change read, and every note closed. The precondition for archiving.</summary>
        public bool IsFullyClosed =>
            Changes.All(c => ReviewOf(c.Id) != ReviewState.Unreviewed) &&
            Notes.Notes.All(n => !n.IsOutstanding);

        public ScratchpadChange FindChange(string changeId) =>
            Changes.FirstOrDefault(c => c.Id == changeId);
    }
}
