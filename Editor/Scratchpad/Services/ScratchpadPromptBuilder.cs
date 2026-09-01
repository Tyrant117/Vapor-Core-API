using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VaporEditor.Scratchpad
{
    /// <summary>How much of the scratchpad a copied prompt covers.</summary>
    internal enum PromptScope
    {
        /// <summary>Just the session on screen.</summary>
        Session,

        /// <summary>Everything still open across the feature, whatever session raised it.</summary>
        Feature,
    }

    /// <summary>
    /// Builds the block of text the <c>Copy Prompt</c> button puts on the clipboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prompt quotes the change each note is about in full — summary, reasoning, stated risk,
    /// files — rather than pointing at it. That is what makes it work in a brand-new chat: the
    /// session it is reviewing may have happened days ago in a conversation that is gone, and a note
    /// reading "this is still wrong" is worthless without the thing it is about sitting next to it.
    /// </para>
    /// <para>
    /// It closes by naming the ids it contained and asking for them back in the next handoff's
    /// <c>resolved:</c> list. That single line is the whole round trip: without it the editor has no
    /// way to know a note was dealt with, and every review would end in manual bookkeeping.
    /// </para>
    /// </remarks>
    internal static class ScratchpadPromptBuilder
    {
        private const string SpecPath = "Assets/Vapor Core/Editor/Scratchpad/HANDOFF-SPEC.md";

        /// <summary>The notes a prompt of this scope would carry, in the order it would carry them.</summary>
        /// <remarks>
        /// Shared with the window so the button can show a count and disable itself when there is
        /// nothing to send, without the two disagreeing about what counts.
        /// </remarks>
        public static List<ScratchpadNote> Collect(ScratchpadFeature feature, ScratchpadSession session, PromptScope scope)
        {
            var sessions = scope == PromptScope.Feature
                ? feature?.Sessions ?? new List<ScratchpadSession>()
                : session != null
                    ? new List<ScratchpadSession> { session }
                    : new List<ScratchpadSession>();

            return sessions
                .SelectMany(s => s.Notes.Notes)
                .Where(n => n.IsOutstanding)
                .OrderBy(n => n.Kind switch { NoteKind.Issue => 0, NoteKind.Work => 1, _ => 2 })
                .ThenBy(n => n.Created)
                .ToList();
        }

        public static string Build(ScratchpadFeature feature, ScratchpadSession session, PromptScope scope)
        {
            var notes = Collect(feature, session, scope);
            if (notes.Count == 0)
            {
                return string.Empty;
            }

            var owner = ByNote(feature, session, scope);
            var sb = new StringBuilder();

            WriteHeader(sb, feature, session, scope, notes);
            WriteGroup(sb, "Issues", notes.Where(n => n.Kind == NoteKind.Issue), owner, scope);
            WriteGroup(sb, "Work", notes.Where(n => n.Kind == NoteKind.Work), owner, scope);
            WriteGroup(sb, "Comments", notes.Where(n => n.Kind == NoteKind.Comment), owner, scope);
            WriteFooter(sb, feature, notes);

            return sb.ToString();
        }

        /// <summary>Which session each note came from, so a feature-wide prompt can say.</summary>
        private static Dictionary<ScratchpadNote, ScratchpadSession> ByNote(
            ScratchpadFeature feature, ScratchpadSession session, PromptScope scope)
        {
            var map = new Dictionary<ScratchpadNote, ScratchpadSession>();
            var sessions = scope == PromptScope.Feature
                ? feature?.Sessions ?? new List<ScratchpadSession>()
                : session != null
                    ? new List<ScratchpadSession> { session }
                    : new List<ScratchpadSession>();

            foreach (var s in sessions)
            {
                foreach (var note in s.Notes.Notes)
                {
                    map[note] = s;
                }
            }

            return map;
        }

        private static void WriteHeader(StringBuilder sb, ScratchpadFeature feature, ScratchpadSession session,
            PromptScope scope, List<ScratchpadNote> notes)
        {
            var featureName = feature?.Name ?? session?.FeatureName ?? "this feature";

            if (scope == PromptScope.Session && session != null)
            {
                sb.AppendLine($"I'm reviewing the changes you delivered in the \"{featureName}\" session " +
                              $"({session.DisplayStamp}).");
                sb.AppendLine($"Handoff: {session.HandoffPath}");

                if (!string.IsNullOrWhiteSpace(session.Handoff.Summary))
                {
                    sb.AppendLine();
                    sb.AppendLine("Session summary:");
                    sb.AppendLine(session.Handoff.Summary.Trim());
                }
            }
            else
            {
                sb.AppendLine($"These are my outstanding notes on \"{featureName}\", gathered from every session " +
                              "I haven't closed out yet.");
                sb.AppendLine($"Handoffs live in {ScratchpadPaths.FeatureDirectory(featureName)}/");
            }

            sb.AppendLine();
            sb.AppendLine($"Below {(notes.Count == 1 ? "is" : "are")} {Describe(notes)}. " +
                          "Each quotes the change it is about.");

            return;
        }

        private static void WriteGroup(StringBuilder sb, string heading, IEnumerable<ScratchpadNote> notes,
            IReadOnlyDictionary<ScratchpadNote, ScratchpadSession> owner, PromptScope scope)
        {
            var list = notes.ToList();
            if (list.Count == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine($"## {heading}");

            foreach (var note in list)
            {
                owner.TryGetValue(note, out var session);
                WriteNote(sb, note, session, scope);
            }
        }

        private static void WriteNote(StringBuilder sb, ScratchpadNote note, ScratchpadSession session, PromptScope scope)
        {
            var change = session?.FindChange(note.ChangeId);

            // A reply to a proposal is about the proposal, not about the session it arrived in.
            // Quoting the proposal is the whole point of one: it is usually an answer to a question
            // the proposal asked, and the answer is meaningless beside the wrong question.
            var followUp = note.IsFollowUpReply ? session?.FindFollowUp(note.FollowUpId) : null;

            sb.AppendLine();

            if (followUp != null)
            {
                sb.AppendLine($"### {note.Id} — answering your proposed follow-up \"{followUp.Title}\" ({followUp.Id})");
            }
            else if (change != null)
            {
                sb.AppendLine($"### {note.Id} — on \"{change.Title}\" ({change.Id})");
            }
            else if (session != null && session.IsPlaceholder)
            {
                sb.AppendLine($"### {note.Id} — not about a specific change");
            }
            else
            {
                sb.AppendLine($"### {note.Id} — on the session as a whole");
            }

            // Only worth saying when the prompt spans more than one session; inside a single session
            // it would repeat the header on every note.
            if (scope == PromptScope.Feature && session != null && !session.IsPlaceholder)
            {
                sb.AppendLine($"From: {session.DisplayStamp} — {session.DisplayTitle}");
            }

            if (followUp != null)
            {
                AppendField(sb, "You proposed", followUp.Detail);
            }
            else if (change != null)
            {
                if (change.Files.Count > 0)
                {
                    sb.AppendLine($"Files: {string.Join(", ", change.Files)}");
                }

                AppendField(sb, "What you did", change.Summary);
                AppendField(sb, "Why", change.Rationale);
                AppendField(sb, "You flagged", change.Risk);
            }

            if (!string.IsNullOrWhiteSpace(note.Context))
            {
                sb.AppendLine($"Selected at the time: {note.Context}");
            }

            sb.AppendLine();
            sb.AppendLine("My note:");
            sb.AppendLine(note.Body.Trim());

            if (string.IsNullOrWhiteSpace(note.Console))
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine("Console:");
            sb.AppendLine("```");
            sb.AppendLine(note.Console.Trim());
            sb.AppendLine("```");
        }

        private static void AppendField(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            sb.AppendLine($"{label}: {Flatten(value)}");
        }

        /// <summary>
        /// Collapses a multi-line field onto one logical line.
        /// </summary>
        /// <remarks>
        /// These are quoted context, not the point of the message. Left as written they out-measure
        /// the note itself and the actual request gets lost in the middle of the block.
        /// </remarks>
        private static string Flatten(string value) =>
            string.Join(" ", value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

        private static void WriteFooter(StringBuilder sb, ScratchpadFeature feature, List<ScratchpadNote> notes)
        {
            var featureName = feature?.Name ?? "the feature";

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("When you're done, write the next handoff into");
            sb.AppendLine($"{ScratchpadPaths.FeatureDirectory(featureName)}/ (format: {SpecPath})");
            sb.AppendLine("and list the note ids you addressed in its `resolved:` field — that closes them on my side.");
            sb.AppendLine($"Ids in this prompt: {string.Join(", ", notes.Select(n => n.Id))}");
        }

        /// <summary>A one-line summary of what was copied, for the console.</summary>
        public static string Describe(ScratchpadFeature feature, ScratchpadSession session, PromptScope scope)
        {
            var notes = Collect(feature, session, scope);
            if (notes.Count == 0)
            {
                return "Nothing outstanding to copy.";
            }

            var where = scope == PromptScope.Feature
                ? $"\"{feature?.Name}\""
                : $"\"{session?.FeatureName}\" / {session?.DisplayStamp}";

            return $"[Scratchpad] Copied {Describe(notes)} on {where} to the clipboard.";
        }

        /// <summary>"2 issues, 1 piece of new work and 3 comments", skipping the empty ones.</summary>
        private static string Describe(IReadOnlyCollection<ScratchpadNote> notes)
        {
            var parts = new List<string>();

            var issues = notes.Count(n => n.Kind == NoteKind.Issue);
            if (issues > 0)
            {
                parts.Add($"{issues} {(issues == 1 ? "issue" : "issues")}");
            }

            var work = notes.Count(n => n.Kind == NoteKind.Work);
            if (work > 0)
            {
                parts.Add(work == 1 ? "1 piece of new work" : $"{work} pieces of new work");
            }

            var comments = notes.Count(n => n.Kind == NoteKind.Comment);
            if (comments > 0)
            {
                parts.Add($"{comments} {(comments == 1 ? "comment" : "comments")}");
            }

            return parts.Count switch
            {
                0 => "nothing",
                1 => parts[0],
                _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}",
            };
        }
    }
}
