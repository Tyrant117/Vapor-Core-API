namespace VaporEditor.Scratchpad
{
    /// <summary>What a note is for.</summary>
    /// <remarks>
    /// The three kinds are not severities of the same thing — they ask for different responses, and
    /// the generated prompt groups by them for exactly that reason. A <see cref="Comment"/> wants
    /// acknowledgement, an <see cref="Issue"/> wants a fix, and <see cref="Work"/> wants scheduling.
    /// </remarks>
    public enum NoteKind
    {
        /// <summary>An observation on a change. Feedback, not a defect.</summary>
        Comment,

        /// <summary>Something wrong that needs fixing.</summary>
        Issue,

        /// <summary>New work to pick up on returning to this feature.</summary>
        Work,
    }

    /// <summary>Where a note is in its life.</summary>
    public enum NoteStatus
    {
        /// <summary>Written, not yet handed to anyone.</summary>
        Open,

        /// <summary>Included in a copied prompt. Still outstanding until something closes it.</summary>
        Sent,

        /// <summary>Closed, either by hand or by a later handoff naming its id.</summary>
        Resolved,

        /// <summary>Closed without being acted on.</summary>
        Dismissed,
    }

    /// <summary>How far you have got with reading a change.</summary>
    /// <remarks>
    /// Deliberately does not include a "has notes" member. Whether a change carries notes is a fact
    /// about the notes list, and storing it here as well would give two places to disagree.
    /// </remarks>
    public enum ReviewState
    {
        /// <summary>Not looked at yet.</summary>
        Unreviewed,

        /// <summary>Read and accepted, with nothing to say about it.</summary>
        Ok,

        /// <summary>Read, and something about it is suspicious. A bookmark for a second pass.</summary>
        CloserLook,
    }

    /// <summary>Where a note came from, so the prompt can say so when it matters.</summary>
    public enum NoteSource
    {
        /// <summary>Typed into the main window.</summary>
        Manual,

        /// <summary>Typed into the quick-capture popup.</summary>
        QuickCapture,

        /// <summary>Captured from a console entry, and carrying its message and stack.</summary>
        Console,

        /// <summary>Accepted from a follow-up the handoff proposed.</summary>
        ProposedFollowUp,
    }

    /// <summary>What you decided about a follow-up the handoff proposed.</summary>
    public enum FollowUpState
    {
        /// <summary>Offered by the handoff, not yet triaged.</summary>
        Proposed,

        /// <summary>Taken on. A Work note exists for it.</summary>
        Accepted,

        /// <summary>Declined. It stays visible as declined rather than vanishing.</summary>
        Dismissed,
    }
}
