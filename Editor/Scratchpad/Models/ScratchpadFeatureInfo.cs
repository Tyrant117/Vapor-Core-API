using System;
using Vapor.Serialization;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// What a feature is, before it has any handoffs to say so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored as <c>feature.vsl</c> beside the handoffs. A separate file from <c>tests.vsl</c> rather
    /// than a field on it, because a test log named to hold a description would be a misnomer that
    /// outlives whoever knew why — and two small honestly-named files cost less than one confusing
    /// one.
    /// </para>
    /// <para>
    /// The description exists for the kickoff prompt, which is used once at the start of a feature.
    /// It is stored rather than typed straight into the clipboard so that rewording it does not mean
    /// retyping it, and so a rebuild of the pane cannot eat it — the same reason every other draft in
    /// this window lives outside the field that shows it.
    /// </para>
    /// </remarks>
    [Serializable]
    [VslSerializable]
    public sealed partial class ScratchpadFeatureInfo
    {
        /// <summary>What the feature is meant to do, in the author's own words.</summary>
        public string Description = string.Empty;

        /// <summary>
        /// What to plan next, for a feature that is already under way.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="Description"/> because they answer different questions and both
        /// belong in the prompt: the description is what the feature is for, the draft is what is
        /// being added to it. Folding them into one field would mean planning the next piece of work
        /// overwrote the reason the feature exists.
        /// </remarks>
        public string PlanDraft = string.Empty;

        /// <summary>
        /// When <see cref="PlanDraft"/> was last copied as a prompt, or empty if it has not been.
        /// </summary>
        /// <remarks>
        /// Cleared whenever the draft is edited, which is what makes it mean "this exact text has
        /// been sent" rather than "something was sent once". A stamp that survived a rewrite would be
        /// worse than none: it would mark unsent work as sent.
        /// </remarks>
        public string PlanCopied = string.Empty;
    }
}
