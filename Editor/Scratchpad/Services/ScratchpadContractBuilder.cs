using System;
using System.Linq;
using System.Text;
using Vapor.Serialization;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Builds the text that teaches an assistant to write a handoff this editor can read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The template it emits is not written out by hand here — it is an empty
    /// <see cref="ScratchpadHandoff"/> run through the VSL writer, which emits every
    /// <c>[VslComment]</c> alongside the member it documents. That is the property the format was
    /// built around, and using it means the contract cannot drift from the model: adding a field
    /// updates the instructions, and the two can never disagree about what a field is called.
    /// </para>
    /// <para>
    /// The full version is prefilled with the feature, the exact path to write to, and the ids of
    /// everything still open, so pasting it into a fresh chat is enough to start work — no reading
    /// the spec, no asking where the folder is.
    /// </para>
    /// </remarks>
    internal static class ScratchpadContractBuilder
    {
        public const string SpecPath = "Assets/Vapor Core/Editor/Scratchpad/HANDOFF-SPEC.md";

        /// <summary>
        /// The prompt that starts a feature: plan it first, then work under the handoff contract.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A feature with no sessions has nothing to hand off yet, so the ordinary contract is the
        /// wrong thing to paste — it explains how to report work that has not been discussed, let
        /// alone done. What is wanted at that moment is the opposite: questions before code.
        /// </para>
        /// <para>
        /// It carries the full handoff contract underneath anyway. The chat that plans the feature is
        /// the chat that will build it, and telling it the reporting format up front costs one paste
        /// and saves the format being explained mid-flight.
        /// </para>
        /// </remarks>
        public static string BuildKickoff(ScratchpadFeature feature, string description, DateTime now)
        {
            var featureName = feature?.Name ?? "Feature Name";

            var sb = new StringBuilder();

            sb.AppendLine($"I want to start a new feature: \"{featureName}\".");
            sb.AppendLine();

            sb.AppendLine("What it should do:");
            sb.AppendLine(string.IsNullOrWhiteSpace(description)
                ? "[describe the feature here]"
                : description.Trim());

            sb.AppendLine();
            AppendPlanning(sb, feature, now);
            return sb.ToString();
        }

        /// <summary>
        /// The same plan-first prompt, for the next piece of work on a feature already under way.
        /// </summary>
        /// <remarks>
        /// Carries the feature's description as well as the new ask. A chat planning an addition needs
        /// to know what it is adding to, and the description is the only place that is written down
        /// outside the handoffs — which describe what was done, not what the feature is for.
        /// </remarks>
        public static string BuildPlan(ScratchpadFeature feature, string description, string ask, DateTime now)
        {
            var featureName = feature?.Name ?? "Feature Name";

            var sb = new StringBuilder();

            sb.AppendLine($"I want to plan the next piece of work on an existing feature: \"{featureName}\".");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(description))
            {
                sb.AppendLine("What the feature is for:");
                sb.AppendLine(description.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("What I want to do next:");
            sb.AppendLine(string.IsNullOrWhiteSpace(ask)
                ? "[describe the work here]"
                : ask.Trim());

            sb.AppendLine();
            AppendPlanning(sb, feature, now);
            return sb.ToString();
        }

        /// <summary>The half both prompts share: plan first, then work under the contract.</summary>
        private static void AppendPlanning(StringBuilder sb, ScratchpadFeature feature, DateTime now)
        {
            sb.AppendLine("Before writing any code, plan it with me:");
            sb.AppendLine();
            sb.AppendLine("- Use your planning tool to work out the approach.");
            sb.AppendLine("- Ask me a lot of questions with your question tool. I would rather answer ten");
            sb.AppendLine("  questions now than correct ten assumptions later, so ask about anything where");
            sb.AppendLine("  two reasonable readings would lead to different work.");
            sb.AppendLine("- Come back with a plan I can approve before you build anything.");
            sb.AppendLine();
            sb.AppendLine("Once we have agreed the plan we will execute it. From then on this feature follows");
            sb.AppendLine("the handoff contract below, so that I can review each round of work in Unity.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(BuildFull(feature, now));
        }

        /// <summary>Everything needed to write a correct handoff without opening another file.</summary>
        public static string BuildFull(ScratchpadFeature feature, DateTime now)
        {
            var featureName = feature?.Name ?? "Feature Name";
            var directory = ScratchpadPaths.FeatureDirectory(featureName);
            var stamp = ScratchpadPaths.NewStamp(now, directory);

            var sb = new StringBuilder();

            sb.AppendLine("When you finish this piece of work, write a handoff file so I can review it in Unity.");
            sb.AppendLine();
            sb.AppendLine($"Write it to: {directory}/{stamp}{ScratchpadPaths.HandoffSuffix}");
            sb.AppendLine($"(Adjust the {ScratchpadPaths.StampFormat} stamp to when you actually finish. " +
                          "The folder name is the feature and must not change.)");
            sb.AppendLine();

            sb.AppendLine("Rules:");
            sb.AppendLine("- One entry in `changes` per meaningful change, not per edited file.");
            sb.AppendLine("- `rationale` and `risk` are the fields I actually read. Say why you chose this way, and");
            sb.AppendLine("  say plainly what is untested, uncertain, or deliberately cut. A blank `risk` reads as a");
            sb.AppendLine("  claim that there isn't one.");
            sb.AppendLine("- Give every change a short `id` slug. I attach comments to it, so keep it stable and");
            sb.AppendLine("  don't reuse one from an earlier session for a different change.");
            sb.AppendLine("- Put work you deliberately left undone in `followUps`, not in prose. I triage those.");
            sb.AppendLine("- Name the tests covering this work in `tests` — namespaces, fixtures or single");
            sb.AppendLine("  test names. I run them from a button here and failures file themselves as issues.");
            sb.AppendLine("- List the ids of any notes you addressed in `resolved`. That is what closes them for me.");
            sb.AppendLine("- Do not read or write the `.notes.vsl` file beside it. That one is mine.");
            sb.AppendLine();

            sb.AppendLine("The format is VSL. Commas are optional whitespace, members you omit keep their defaults,");
            sb.AppendLine($"and `\"\"\"` opens a multi-line block. Full spec: {SpecPath}");
            sb.AppendLine();

            sb.AppendLine("Template — the # lines document each field:");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(BuildTemplate(featureName, now).TrimEnd());
            sb.AppendLine("```");

            AppendOutstanding(sb, feature);
            return sb.ToString();
        }

        /// <summary>For a chat that already knows the format and just needs the target.</summary>
        public static string BuildShort(ScratchpadFeature feature, DateTime now)
        {
            var featureName = feature?.Name ?? "Feature Name";
            var directory = ScratchpadPaths.FeatureDirectory(featureName);
            var stamp = ScratchpadPaths.NewStamp(now, directory);

            var sb = new StringBuilder();
            sb.AppendLine("When you finish, write a scratchpad handoff to");
            sb.AppendLine($"{directory}/{stamp}{ScratchpadPaths.HandoffSuffix}");
            sb.AppendLine($"following {SpecPath}. Fill in `rationale` and `risk` honestly, put deferred work in");
            sb.AppendLine("`followUps`, and list any note ids you addressed in `resolved`.");

            AppendOutstanding(sb, feature);
            return sb.ToString();
        }

        /// <summary>
        /// A worked example, generated from the model so it can never name a field that does not exist.
        /// </summary>
        private static string BuildTemplate(string featureName, DateTime now)
        {
            var handoff = new ScratchpadHandoff
            {
                Feature = featureName,
                Title = "One line: what this session set out to do",
                Summary = "A few sentences of context for the whole session.",
                Written = ScratchpadPaths.Timestamp(now),
                Resolved = { "note-id-you-addressed" },
                Changes =
                {
                    new ScratchpadChange
                    {
                        Id = "short-slug",
                        Title = "One line naming the change",
                        Summary = "What actually changed.",
                        Rationale = "Why this way, and what was considered and rejected.",
                        Risk = "What is untested, uncertain, or deliberately cut.",
                        Files = { "Assets/Path/To/File.cs" },
                    },
                },
                FollowUps =
                {
                    new ScratchpadFollowUp
                    {
                        Id = "another-slug",
                        Title = "Work this session left undone",
                        Detail = "Why it was left, and what the next session needs to know.",
                    },
                },
                Tests = { "Namespace.Or.Fixture.Covering.This.Work" },
            };

            try
            {
                return Vsl.Serialize(handoff);
            }
            catch (Exception e)
            {
                // Never worth throwing out of a clipboard action. The rules above still stand on their
                // own, and the spec file is named right there in them.
                return $"# Could not generate the template ({e.Message}). See {SpecPath}.";
            }
        }

        /// <summary>
        /// Lists what is still open, so the chat starts holding the outstanding work.
        /// </summary>
        private static void AppendOutstanding(StringBuilder sb, ScratchpadFeature feature)
        {
            var open = feature?.AllNotes.Where(n => n.IsOutstanding).ToList();
            if (open == null || open.Count == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine($"Still open on \"{feature.Name}\" — quote an id in `resolved` if you deal with it:");

            foreach (var note in open.OrderBy(n => n.Kind switch { NoteKind.Issue => 0, NoteKind.Work => 1, _ => 2 })
                         .ThenBy(n => n.Created))
            {
                var firstLine = (note.Body ?? string.Empty)
                    .Split('\n')
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0) ?? "(no text)";

                if (firstLine.Length > 100)
                {
                    firstLine = firstLine[..97] + "...";
                }

                sb.AppendLine($"- [{note.Id}] {note.Kind}: {firstLine}");
            }
        }
    }
}
