using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vapor.Serialization;
using VaporEditor.Scratchpad;

namespace Vapor.Tests.Scratchpad
{
    /// <summary>
    /// The rules the scratchpad cannot get wrong without costing you work.
    /// </summary>
    /// <remarks>
    /// Deliberately a short suite. Most of this tool is a window, and a window is verified by opening
    /// it; what is worth pinning down is the handful of rules where a bug is silent — a note quietly
    /// coming unattached from its change, an id handed out twice, a session archived before it was
    /// read. Each test below is one of those.
    /// </remarks>
    public class ScratchpadTests
    {
        private string _root;
        private int _archiveHours;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "VaporScratchpadTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            ScratchpadPaths.RootOverride = _root.Replace('\\', '/');

            // Pinned rather than merely restored: the setting lives in EditorPrefs, so without this
            // every archive test's arithmetic depends on whatever the machine happens to have.
            _archiveHours = ScratchpadSettings.ArchiveHours;
            ScratchpadSettings.ArchiveHours = 12;
        }

        [TearDown]
        public void TearDown()
        {
            // Before the root is cleared, because the exemptions are keyed by it. SessionState
            // outlives a test — and outlives the whole run — so an exemption left behind here is one
            // the next case inherits.
            ScratchpadStore.ClearUnarchivedForTests();

            ScratchpadPaths.RootOverride = null;
            ScratchpadSettings.ArchiveHours = _archiveHours;

            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a green test over.
            }
        }

        #region Helpers

        private static ScratchpadStore StoreAt(DateTime now)
        {
            var store = new ScratchpadStore { Clock = () => now };
            store.Refresh();
            return store;
        }

        private static void WriteHandoff(string feature, string stamp, ScratchpadHandoff handoff)
        {
            Vsl.WriteToFile(ScratchpadPaths.HandoffPath(feature, stamp), handoff);
        }

        private static ScratchpadHandoff Handoff(string feature, params ScratchpadChange[] changes)
        {
            var handoff = new ScratchpadHandoff { Feature = feature, Title = "A session" };
            handoff.Changes.AddRange(changes);
            return handoff;
        }

        private static ScratchpadChange Change(string id, string title) =>
            new()
            {
                Id = id,
                Title = title,
                Summary = $"{title} summary",
                Rationale = $"{title} rationale",
                Risk = $"{title} risk",
                Files = { "Assets/Some/File.cs" },
            };

        #endregion

        /// <summary>The template the contract button hands out is a document this reader accepts.</summary>
        /// <remarks>
        /// The button's whole promise is "paste this and you cannot get it wrong". If the writer ever
        /// emitted something the reader rejected, the tool would be teaching a format it cannot load —
        /// and the failure would land on the assistant, which has no way to know better.
        /// </remarks>
        [Test]
        public void Contract_TemplateParsesBackAsAHandoff()
        {
            var contract = ScratchpadContractBuilder.BuildFull(null, new DateTime(2026, 8, 31, 16, 15, 0));

            var start = contract.IndexOf("```", StringComparison.Ordinal) + 3;
            var end = contract.LastIndexOf("```", StringComparison.Ordinal);
            var template = contract[start..end];

            var handoff = Vsl.Deserialize<ScratchpadHandoff>(template);

            Assert.AreEqual(1, handoff.Changes.Count);
            Assert.AreEqual("short-slug", handoff.Changes[0].Id);
            Assert.IsNotEmpty(handoff.Changes[0].Rationale);
            Assert.IsNotEmpty(handoff.Changes[0].Risk);
            Assert.AreEqual(1, handoff.FollowUps.Count);
            Assert.AreEqual(1, handoff.Resolved.Count);
        }

        /// <summary>A handoff written by the template builder reads back as the same handoff.</summary>
        /// <remarks>
        /// The contract button hands an assistant a serialized empty instance as the thing to copy. If
        /// what the writer emits is not what the reader accepts, the tool teaches a format it cannot
        /// itself load.
        /// </remarks>
        [Test]
        public void Handoff_RoundTripsThroughVsl()
        {
            var original = Handoff("UV Editor", Change("pin-roundtrip", "Pins survive an extrude"));
            original.Summary = "Fixed pins.\nAlso the SLIM toggle.";
            original.Written = "2026-08-31T14:30:00";
            original.Resolved.Add("uv-editor-4");
            original.FollowUps.Add(new ScratchpadFollowUp { Id = "split", Title = "Split the file", Detail = "It is long." });

            var restored = Vsl.Deserialize<ScratchpadHandoff>(Vsl.Serialize(original));

            Assert.AreEqual(original.Feature, restored.Feature);
            Assert.AreEqual(original.Summary, restored.Summary);
            Assert.AreEqual(original.Written, restored.Written);
            CollectionAssert.AreEqual(original.Resolved, restored.Resolved);
            Assert.AreEqual(1, restored.Changes.Count);
            Assert.AreEqual("pin-roundtrip", restored.Changes[0].Id);
            Assert.AreEqual("Pins survive an extrude risk", restored.Changes[0].Risk);
            CollectionAssert.AreEqual(original.Changes[0].Files, restored.Changes[0].Files);
            Assert.AreEqual(1, restored.FollowUps.Count);
            Assert.AreEqual("split", restored.FollowUps[0].Id);
        }

        /// <summary>A handoff that is wrong in the ordinary ways still loads.</summary>
        /// <remarks>
        /// Every input to this tool is hand-written by a program that is not this one. A stray member,
        /// an omitted field and an empty list are the three things that will actually happen, and any
        /// of them throwing would make the window blank at exactly the wrong moment.
        /// </remarks>
        [Test]
        public void Handoff_ToleratesUnknownAndMissingMembers()
        {
            const string text = @"@vsl 1
{
  feature: ""UV Editor""
  title: ""A session""
  somethingNobodyDeclared: 42
  changes: [
    { id: ""a""  title: ""Only a title"" }
  ]
}";

            var handoff = Vsl.Deserialize<ScratchpadHandoff>(text);

            Assert.AreEqual("UV Editor", handoff.Feature);
            Assert.AreEqual(1, handoff.Changes.Count);
            Assert.AreEqual("a", handoff.Changes[0].Id);
            Assert.IsEmpty(handoff.Changes[0].Rationale);
            Assert.IsEmpty(handoff.FollowUps);
            Assert.IsEmpty(handoff.Resolved);
        }

        /// <summary>An id-less change gets one, and gets the same one next time.</summary>
        /// <remarks>
        /// This is what keeps a note attached to the change it was written about. An id regenerated
        /// differently on the second load would leave every note on that change pointing at nothing,
        /// silently — the note would still exist, just no longer anywhere you would look for it.
        /// </remarks>
        [Test]
        public void ChangeIds_AreBackfilledAndStable()
        {
            var handoff = Handoff("UV Editor",
                new ScratchpadChange { Title = "No id at all" },
                new ScratchpadChange { Title = "Also no id" });

            WriteHandoff("UV Editor", "2026-08-31-1430", handoff);

            var first = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var firstIds = first.Features[0].Sessions[0].Changes.Select(c => c.Id).ToList();

            Assert.That(firstIds, Has.All.Not.Empty);
            Assert.AreEqual(2, firstIds.Distinct().Count(), "Two changes must not share an id.");

            var second = StoreAt(new DateTime(2026, 8, 31, 16, 0, 0));
            var secondIds = second.Features[0].Sessions[0].Changes.Select(c => c.Id).ToList();

            CollectionAssert.AreEqual(firstIds, secondIds);
        }

        /// <summary>A note id is never handed out twice, even after the index is lost.</summary>
        /// <remarks>
        /// The index is a cache and is allowed to vanish. What is not allowed is a reissued number:
        /// the id gets quoted into a chat, and a duplicate would have the assistant's reply close a
        /// different note than the one it answered.
        /// </remarks>
        [Test]
        public void NoteIds_AreUniqueAndSurviveALostIndex()
        {
            WriteHandoff("UV Editor", "2026-08-31-1430", Handoff("UV Editor", Change("a", "A change")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var session = store.Features[0].Sessions[0];

            var issued = new[]
            {
                store.AddNote(session, "a", NoteKind.Issue, "one").Id,
                store.AddNote(session, "a", NoteKind.Work, "two").Id,
                store.AddNote(session, "a", NoteKind.Comment, "three").Id,
            };

            Assert.AreEqual("uv-editor-1", issued[0]);
            Assert.AreEqual(3, issued.Distinct().Count());

            File.Delete(ScratchpadPaths.IndexPath);

            var reopened = StoreAt(new DateTime(2026, 8, 31, 16, 0, 0));
            var next = reopened.AllocateNoteId(reopened.Features[0]);

            CollectionAssert.DoesNotContain(issued, next);
            Assert.AreEqual("uv-editor-4", next);
        }

        /// <summary>A later handoff naming a note id closes that note and only that note.</summary>
        /// <remarks>
        /// The whole round trip rests on this one rule. It has to reach across sessions, because the
        /// handoff that fixes something is by definition not the one that broke it.
        /// </remarks>
        [Test]
        public void Resolved_ClosesNamedNotesAcrossSessions()
        {
            WriteHandoff("UV Editor", "2026-08-31-1430", Handoff("UV Editor", Change("a", "A change")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var first = store.Features[0].Sessions[0];

            var fixedNote = store.AddNote(first, "a", NoteKind.Issue, "Broken");
            var openNote = store.AddNote(first, "a", NoteKind.Issue, "Also broken");

            var later = Handoff("UV Editor", Change("b", "The fix"));
            later.Resolved.Add(fixedNote.Id);
            WriteHandoff("UV Editor", "2026-09-01-0900", later);

            var reopened = StoreAt(new DateTime(2026, 9, 1, 10, 0, 0));
            var notes = reopened.Features[0].AllNotes.ToList();

            Assert.AreEqual(NoteStatus.Resolved, notes.Single(n => n.Id == fixedNote.Id).Status);
            Assert.AreEqual(NoteStatus.Open, notes.Single(n => n.Id == openNote.Id).Status);
        }

        /// <summary>The prompt carries the change, in order, and names its ids at the end.</summary>
        /// <remarks>
        /// Three things make a prompt usable in a chat that has never seen this work: the quoted
        /// change, the ordering that puts defects before opinions, and the closing list of ids that
        /// asks for the round trip back.
        /// </remarks>
        [Test]
        public void Prompt_QuotesTheChangeAndOrdersByKind()
        {
            WriteHandoff("UV Editor", "2026-08-31-1430",
                Handoff("UV Editor", Change("pin-roundtrip", "Pins survive an extrude")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var feature = store.Features[0];
            var session = feature.Sessions[0];

            store.AddNote(session, "pin-roundtrip", NoteKind.Comment, "Reads well.");
            store.AddNote(session, "pin-roundtrip", NoteKind.Issue, "Dissolve still drops it.");
            store.AddNote(session, "pin-roundtrip", NoteKind.Work, "Add a test for it.");

            var prompt = ScratchpadPromptBuilder.Build(feature, session, PromptScope.Session);

            StringAssert.Contains("Pins survive an extrude summary", prompt);
            StringAssert.Contains("Pins survive an extrude rationale", prompt);
            StringAssert.Contains("Pins survive an extrude risk", prompt);
            StringAssert.Contains("Dissolve still drops it.", prompt);

            Assert.Less(prompt.IndexOf("## Issues", StringComparison.Ordinal),
                prompt.IndexOf("## Work", StringComparison.Ordinal));

            Assert.Less(prompt.IndexOf("## Work", StringComparison.Ordinal),
                prompt.IndexOf("## Comments", StringComparison.Ordinal));

            foreach (var note in feature.AllNotes)
            {
                StringAssert.Contains(note.Id, prompt);
            }

            StringAssert.Contains("resolved:", prompt);
        }

        /// <summary>A session pulled back out by hand stays out, and the refresh says why.</summary>
        /// <remarks>
        /// Unarchiving something still reviewed, closed and past the archive window re-qualifies it
        /// immediately, so without this the next refresh files it straight back and the button looks
        /// broken. Unarchiving is an instruction rather than a request: the session qualified a moment
        /// ago too, when you took it out.
        /// </remarks>
        [Test]
        public void Unarchive_KeepsTheSessionOutAndSaysSo()
        {
            var handoff = Handoff("UV Editor", Change("a", "A change"));
            handoff.Written = "2026-08-30T09:00:00";
            WriteHandoff("UV Editor", "2026-08-30-0900", handoff);

            var later = Handoff("UV Editor", Change("b", "Newer"));
            later.Written = "2026-08-31T09:00:00";
            WriteHandoff("UV Editor", "2026-08-31-0900", later);

            // Read and closed, so the only thing keeping it out of the archive is the clock.
            var store = StoreAt(new DateTime(2026, 8, 30, 10, 0, 0));
            var session = store.FindFeature("UV Editor").Sessions.Single(s => s.Stamp == "2026-08-30-0900");
            session.SetReview("a", ReviewState.Ok);
            store.SaveNotes(session);

            var archiving = StoreAt(new DateTime(2026, 8, 31, 12, 0, 0));
            Assert.AreEqual(1, archiving.LastArchived.Count, "The old session should have been archived.");
            Assert.IsEmpty(archiving.LastKeptOut, "Nothing was unarchived by hand yet.");

            var archived = archiving.ArchivedFeatures.Single().Sessions.Single();
            Assert.IsTrue(archiving.Unarchive(archived));

            // Same store instance, because remembering the unarchive is what is being tested.
            archiving.Refresh();

            Assert.IsEmpty(archiving.LastArchived, "A session unarchived by hand must not be re-filed.");
            Assert.AreEqual(1, archiving.LastKeptOut.Count, "Keeping it out has to be reported.");
            StringAssert.Contains("2026-08-30", archiving.LastKeptOut[0]);

            Assert.IsTrue(File.Exists(ScratchpadPaths.HandoffPath("UV Editor", "2026-08-30-0900")),
                "The session should still be live on disk.");

            // The exemption lives in SessionState, so it outlives the store that recorded it — which
            // is the whole point: a script recompile used to lose it and archive the session anyway.
            var reloaded = StoreAt(new DateTime(2026, 8, 31, 13, 0, 0));
            Assert.IsEmpty(reloaded.LastArchived, "The exemption has to survive a new store.");
            Assert.AreEqual(1, reloaded.LastKeptOut.Count);
        }

        /// <summary>A feature's test log round-trips, and is not mistaken for a session.</summary>
        /// <remarks>
        /// The second half is the reason this exists. <c>tests.vsl</c> is a new kind of file living in
        /// the same folder as the handoffs, and a scanner that picked it up would show it as a session
        /// with no changes — which looks exactly like a legitimately empty one.
        /// </remarks>
        [Test]
        public void TestLog_RoundTripsAndIsNotASession()
        {
            WriteHandoff("UV Editor", "2026-08-31-1430", Handoff("UV Editor", Change("a", "A change")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var feature = store.FindFeature("UV Editor");

            var log = store.LoadTestLog(feature);
            Assert.IsEmpty(log.Extra, "A feature that has never run tests starts with nothing added.");

            log.Extra.Add("Vapor.Tests.Scratchpad");
            log.Runs.Add(new ScratchpadTestRun
            {
                When = "2026-08-31T15:05:00",
                Passed = 16,
                Failed = 1,
                Duration = 0.42f,
                Failures = { "ScratchpadTests.Archive_OnlyTakesReviewed" },
            });

            store.SaveTestLog(feature, log);

            var reopened = StoreAt(new DateTime(2026, 8, 31, 15, 10, 0));
            var reloaded = reopened.LoadTestLog(reopened.FindFeature("UV Editor"));

            CollectionAssert.AreEqual(new[] { "Vapor.Tests.Scratchpad" }, reloaded.Extra);
            Assert.AreEqual(1, reloaded.Runs.Count);
            Assert.AreEqual(16, reloaded.Runs[0].Passed);
            Assert.IsFalse(reloaded.Runs[0].IsGreen);
            CollectionAssert.AreEqual(log.Runs[0].Failures, reloaded.Runs[0].Failures);

            Assert.AreEqual(1, reopened.FindFeature("UV Editor").Sessions.Count,
                "tests.vsl must not scan as a session.");
        }

        /// <summary>The kickoff prompt asks for a plan, and still teaches the handoff format.</summary>
        /// <remarks>
        /// Both halves matter. Without the planning ask it is just the ordinary contract pasted at a
        /// feature that has nothing to report yet; without the contract underneath, the chat that
        /// plans the work has to be taught the format again once it starts.
        /// </remarks>
        [Test]
        public void Kickoff_AsksForAPlanAndCarriesTheContract()
        {
            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var feature = store.CreateFeature("Loot Tables");

            var kickoff = ScratchpadContractBuilder.BuildKickoff(feature, "Weighted drops per enemy tier.",
                new DateTime(2026, 8, 31, 15, 0, 0));

            StringAssert.Contains("Loot Tables", kickoff);
            StringAssert.Contains("Weighted drops per enemy tier.", kickoff);
            StringAssert.Contains("planning tool", kickoff);
            StringAssert.Contains("question tool", kickoff);
            StringAssert.Contains("before you build anything", kickoff);

            // The contract half, so a fresh chat needs nothing else.
            StringAssert.Contains(ScratchpadContractBuilder.SpecPath, kickoff);
            StringAssert.Contains("rationale", kickoff);
            StringAssert.Contains("risk", kickoff);

            // Empty description leaves something obviously unfilled rather than an empty heading.
            var blank = ScratchpadContractBuilder.BuildKickoff(feature, string.Empty,
                new DateTime(2026, 8, 31, 15, 0, 0));

            StringAssert.Contains("[describe the feature here]", blank);
        }

        /// <summary>The plan prompt carries both the feature's purpose and the new ask.</summary>
        /// <remarks>
        /// A chat planning an addition needs to know what it is adding to, and the description is the
        /// only place that is written down — the handoffs say what was done, not what the feature is
        /// for. Kept as two fields for the same reason: planning the next thing must not overwrite the
        /// reason the feature exists.
        /// </remarks>
        [Test]
        public void Plan_CarriesTheDescriptionAndTheAskSeparately()
        {
            WriteHandoff("Loot Tables", "2026-08-31-1430", Handoff("Loot Tables", Change("a", "First pass")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var feature = store.FindFeature("Loot Tables");

            store.SaveFeatureInfo(feature, new ScratchpadFeatureInfo
            {
                Description = "Weighted drops per enemy tier.",
                PlanDraft = "Add pity timers so a long dry streak raises the odds.",
            });

            var info = store.LoadFeatureInfo(feature);
            var plan = ScratchpadContractBuilder.BuildPlan(feature, info.Description, info.PlanDraft,
                new DateTime(2026, 8, 31, 15, 0, 0));

            StringAssert.Contains("existing feature", plan);
            StringAssert.Contains("Weighted drops per enemy tier.", plan);
            StringAssert.Contains("Add pity timers", plan);
            StringAssert.Contains("planning tool", plan);
            StringAssert.Contains("question tool", plan);
            StringAssert.Contains(ScratchpadContractBuilder.SpecPath, plan);

            // The two prompts must not be the same text with a different heading.
            var kickoff = ScratchpadContractBuilder.BuildKickoff(feature, info.Description,
                new DateTime(2026, 8, 31, 15, 0, 0));

            StringAssert.Contains("start a new feature", kickoff);
            StringAssert.DoesNotContain("Add pity timers", kickoff);
        }

        /// <summary>The copied stamp survives a reload and does not survive a rewrite.</summary>
        /// <remarks>
        /// The stamp means "this exact text has been sent". A stamp that outlived an edit would mark
        /// unsent work as sent, which is worse than not stamping at all — you would skip over the one
        /// draft that still needed sending.
        /// </remarks>
        [Test]
        public void PlanDraft_StampSurvivesAReloadButNotAnEdit()
        {
            WriteHandoff("Loot Tables", "2026-08-31-1430", Handoff("Loot Tables", Change("a", "First pass")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var feature = store.FindFeature("Loot Tables");

            store.SaveFeatureInfo(feature, new ScratchpadFeatureInfo
            {
                PlanDraft = "Add pity timers.",
                PlanCopied = "2026-08-31T15:05:00",
            });

            var reopened = StoreAt(new DateTime(2026, 8, 31, 15, 30, 0));
            var info = reopened.LoadFeatureInfo(reopened.FindFeature("Loot Tables"));

            Assert.AreEqual("Add pity timers.", info.PlanDraft);
            Assert.AreEqual("2026-08-31T15:05:00", info.PlanCopied, "A stamp has to survive a reload.");

            // What the view does on an edit, which is the rule being pinned.
            info.PlanDraft = "Add pity timers, and a cap.";
            info.PlanCopied = string.Empty;
            reopened.SaveFeatureInfo(reopened.FindFeature("Loot Tables"), info);

            var afterEdit = StoreAt(new DateTime(2026, 8, 31, 15, 40, 0));
            var edited = afterEdit.LoadFeatureInfo(afterEdit.FindFeature("Loot Tables"));

            Assert.AreEqual("Add pity timers, and a cap.", edited.PlanDraft);
            Assert.IsEmpty(edited.PlanCopied, "A rewritten draft must not still read as sent.");
        }

        /// <summary>A feature holding only its own description still counts as empty.</summary>
        /// <remarks>
        /// The description and test log are files the window puts in the feature folder, so a naive
        /// emptiness check sees them and refuses to delete a feature that has no work in it — which
        /// is exactly the lingering-empty-feature complaint that prompted the delete action.
        /// </remarks>
        [Test]
        public void DeleteEmptyFeature_SeesPastTheWindowsOwnSideFiles()
        {
            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var feature = store.CreateFeature("Abandoned");

            store.SaveFeatureInfo(feature, new ScratchpadFeatureInfo { Description = "Never started." });
            Assert.IsTrue(File.Exists(ScratchpadPaths.FeatureInfoPath("Abandoned")));

            Assert.IsTrue(store.DeleteEmptyFeature(feature));
            Assert.IsFalse(Directory.Exists(ScratchpadPaths.FeatureDirectory("Abandoned")));
            CollectionAssert.DoesNotContain(store.Features, feature);
        }

        /// <summary>A feature's test set comes from its handoffs, plus and minus what you said.</summary>
        /// <remarks>
        /// Derived rather than stored so a new handoff naming a new test brings it in with nobody
        /// maintaining a list. The exclusion half matters just as much: a handoff is not ours to edit,
        /// so switching one of its tests off has to be recorded on our side or the next refresh puts
        /// it straight back.
        /// </remarks>
        [Test]
        public void TestsFor_UnionsHandoffsWithExtrasAndHonoursExclusions()
        {
            var older = Handoff("UV Editor", Change("a", "A change"));
            older.Tests.Add("Vapor.Tests.Uv.PinTests");
            WriteHandoff("UV Editor", "2026-08-30-0900", older);

            var newer = Handoff("UV Editor", Change("b", "Newer"));
            newer.Tests.Add("Vapor.Tests.Uv.SlimTests");

            // Also named by the older handoff — the union must not list it twice.
            newer.Tests.Add("Vapor.Tests.Uv.PinTests");
            WriteHandoff("UV Editor", "2026-08-31-0900", newer);

            var store = StoreAt(new DateTime(2026, 8, 31, 10, 0, 0));
            var feature = store.FindFeature("UV Editor");

            CollectionAssert.AreEquivalent(
                new[] { "Vapor.Tests.Uv.PinTests", "Vapor.Tests.Uv.SlimTests" },
                store.TestsFor(feature));

            var log = store.LoadTestLog(feature);
            log.Extra.Add("Vapor.Tests.Uv.SeamTests");
            log.Excluded.Add("Vapor.Tests.Uv.PinTests");
            store.SaveTestLog(feature, log);

            CollectionAssert.AreEquivalent(
                new[] { "Vapor.Tests.Uv.SlimTests", "Vapor.Tests.Uv.SeamTests" },
                store.TestsFor(feature));

            Assert.IsTrue(store.IsDeclaredByHandoff(feature, "Vapor.Tests.Uv.SlimTests"));
            Assert.IsFalse(store.IsDeclaredByHandoff(feature, "Vapor.Tests.Uv.SeamTests"),
                "An added test is ours to delete, not to exclude.");
        }

        /// <summary>Archiving a feature takes everything, including work you never read.</summary>
        /// <remarks>
        /// The archive predicate exists to stop the tool filing away work you have not looked at. It
        /// has nothing to say about work you have explicitly told it to put away, so this path ignores
        /// it — and that is worth pinning, because a silent "archived 2 of 5" would look like success.
        /// </remarks>
        [Test]
        public void ArchiveFeature_TakesEverySessionWhateverItsState()
        {
            var first = Handoff("UV Editor", Change("a", "A change"));
            first.Written = "2026-08-31T09:00:00";
            WriteHandoff("UV Editor", "2026-08-31-0900", first);

            var second = Handoff("UV Editor", Change("b", "Newer"));
            second.Written = "2026-08-31T10:00:00";
            WriteHandoff("UV Editor", "2026-08-31-1000", second);

            var store = StoreAt(new DateTime(2026, 8, 31, 10, 30, 0));
            var feature = store.FindFeature("UV Editor");

            // Unreviewed, outstanding, and far too new — none of which should matter.
            store.AddNote(feature.Sessions[0], "b", NoteKind.Issue, "Still open");
            Assert.IsFalse(store.IsArchivable(feature.Sessions[0]));

            Assert.AreEqual(2, store.ArchiveFeature(feature));

            Assert.IsEmpty(store.Features, "The emptied feature should be gone from the live list.");
            Assert.IsFalse(Directory.Exists(ScratchpadPaths.FeatureDirectory("UV Editor")),
                "The empty live folder should have been removed.");

            var reopened = StoreAt(new DateTime(2026, 8, 31, 11, 0, 0));
            Assert.IsEmpty(reopened.Features);
            Assert.AreEqual(2, reopened.ArchivedFeatures.Single().Sessions.Count);

            // The note went with its session rather than being left behind.
            Assert.AreEqual("Still open", reopened.ArchivedFeatures.Single().AllNotes.Single().Body);
        }

        /// <summary>A reply to a proposal goes back quoting the proposal, not the session.</summary>
        /// <remarks>
        /// A follow-up that offers two routes needs an answer naming one, and that answer is useless
        /// beside the wrong question. Modelled as an ordinary note so it inherits sending, resolving
        /// and prompt inclusion rather than needing its own of each.
        /// </remarks>
        [Test]
        public void FollowUpReply_QuotesTheProposalItAnswers()
        {
            var handoff = Handoff("UV Editor", Change("a", "A change"));
            handoff.FollowUps.Add(new ScratchpadFollowUp
            {
                Id = "picker-cursor",
                Title = "Pick mode gives no sign it is active",
                Detail = "A tint on the overlay, or a line in the status bar, would fix it.",
            });

            WriteHandoff("UV Editor", "2026-08-31-1430", handoff);

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var session = store.Features[0].Sessions[0];

            var reply = store.AddNote(session, string.Empty, NoteKind.Comment,
                "Make the button a toggle that highlights while it is on.", followUpId: "picker-cursor");

            // A reply belongs to the proposal, so it must not also show up as a note on the session.
            Assert.IsTrue(reply.IsFollowUpReply);
            CollectionAssert.DoesNotContain(session.LooseNotes.ToList(), reply);
            CollectionAssert.Contains(session.NotesForFollowUp("picker-cursor").ToList(), reply);

            var prompt = ScratchpadPromptBuilder.Build(store.Features[0], session, PromptScope.Session);

            StringAssert.Contains("answering your proposed follow-up", prompt);
            StringAssert.Contains("Pick mode gives no sign it is active", prompt);
            StringAssert.Contains("A tint on the overlay", prompt);
            StringAssert.Contains("Make the button a toggle", prompt);
        }

        /// <summary>The console buffer comes back after a domain reload.</summary>
        /// <remarks>
        /// A static list dies with the domain but the Console window's contents do not, so without the
        /// stash the buffer disagreed with the console every time a script was touched — and the
        /// entries missing were the older ones, which is to say the ones worth going to look for.
        /// </remarks>
        [Test]
        public void ConsoleBuffer_SurvivesADomainReload()
        {
            Debug.unityLogger.logHandler.LogFormat(LogType.Warning, null, "{0}", "a warning worth keeping");

            var before = ScratchpadConsoleBuffer.RecentOfKind(e => e.Type == LogType.Warning, 5);
            Assert.IsNotEmpty(before, "The buffer should have taken the warning.");

            // Stand in for the reload: stash, wipe, restore.
            ScratchpadConsoleBuffer.StashForTests();
            ScratchpadConsoleBuffer.ClearForTests();
            Assert.AreEqual(0, ScratchpadConsoleBuffer.Count);

            ScratchpadConsoleBuffer.RestoreForTests();

            var after = ScratchpadConsoleBuffer.RecentOfKind(e => e.Type == LogType.Warning, 5);
            Assert.IsNotEmpty(after, "The warning should have come back.");
            Assert.AreEqual(before[0].Message, after[0].Message);
        }

        /// <summary>Renaming a feature moves the folder and leaves issued ids alone.</summary>
        /// <remarks>
        /// An id may already be quoted in a chat, so rewriting one would break the only promise the
        /// round trip makes. The counter restarting under the new slug is safe precisely because it
        /// is a different prefix — it cannot reissue anything already handed out.
        /// </remarks>
        [Test]
        public void Rename_MovesTheFolderAndKeepsIssuedIds()
        {
            WriteHandoff("UV Editor", "2026-08-31-1430", Handoff("UV Editor", Change("a", "A change")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var feature = store.Features[0];
            var oldId = store.AddNote(feature.Sessions[0], "a", NoteKind.Issue, "Broken").Id;

            Assert.AreEqual("uv-editor-1", oldId);
            Assert.IsTrue(store.RenameFeature(feature, "UV Tools"));

            Assert.IsFalse(Directory.Exists(ScratchpadPaths.FeatureDirectory("UV Editor")));
            Assert.IsTrue(File.Exists(ScratchpadPaths.HandoffPath("UV Tools", "2026-08-31-1430")));

            var reopened = StoreAt(new DateTime(2026, 8, 31, 16, 0, 0));
            var renamed = reopened.Features.Single();

            Assert.AreEqual("UV Tools", renamed.Name);
            Assert.AreEqual(oldId, renamed.AllNotes.Single().Id, "An issued id must survive a rename.");
            Assert.AreEqual("uv-tools-1", reopened.AllocateNoteId(renamed));
        }

        /// <summary>Renaming onto a name already in use refuses rather than merging.</summary>
        [Test]
        public void Rename_RefusesToCollideWithAnExistingFeature()
        {
            WriteHandoff("UV Editor", "2026-08-31-1430", Handoff("UV Editor", Change("a", "A change")));
            WriteHandoff("Networking", "2026-08-31-1500", Handoff("Networking", Change("b", "Another")));

            var store = StoreAt(new DateTime(2026, 8, 31, 16, 0, 0));
            var feature = store.FindFeature("UV Editor");

            LogAssert.Expect(LogType.Error, new Regex("already exists"));
            Assert.IsFalse(store.RenameFeature(feature, "Networking"));

            Assert.IsTrue(File.Exists(ScratchpadPaths.HandoffPath("UV Editor", "2026-08-31-1430")));
            Assert.IsTrue(File.Exists(ScratchpadPaths.HandoffPath("Networking", "2026-08-31-1500")));
        }

        /// <summary>A loose note can be attached to a change, and taken off again.</summary>
        /// <remarks>
        /// Quick capture can only ever file loose, so without this a note captured mid-test stays
        /// unattached for good. Only the attachment moves — the id and status are what a chat may
        /// already be holding.
        /// </remarks>
        [Test]
        public void MoveNote_ReattachesWithoutDisturbingTheNote()
        {
            WriteHandoff("UV Editor", "2026-08-31-1430", Handoff("UV Editor", Change("a", "A change")));

            var store = StoreAt(new DateTime(2026, 8, 31, 15, 0, 0));
            var session = store.Features[0].Sessions[0];
            var note = store.AddNote(session, string.Empty, NoteKind.Issue, "Spotted while testing");

            Assert.AreEqual(1, session.LooseNotes.Count());

            store.MoveNote(session, note, "a");
            Assert.AreEqual(0, session.LooseNotes.Count());
            Assert.AreEqual(note, session.NotesFor("a").Single());

            store.MoveNote(session, note, string.Empty);
            Assert.AreEqual(1, session.LooseNotes.Count());

            // Survives a reload, and nothing else about the note moved with it.
            store.MoveNote(session, note, "a");
            var reopened = StoreAt(new DateTime(2026, 8, 31, 16, 0, 0));
            var reloaded = reopened.Features[0].Sessions[0].NotesFor("a").Single();

            Assert.AreEqual(note.Id, reloaded.Id);
            Assert.AreEqual(NoteStatus.Open, reloaded.Status);
            Assert.AreEqual("Spotted while testing", reloaded.Body);
        }

        /// <summary>Only work that has been read and closed, and is not the latest, gets filed away.</summary>
        /// <remarks>
        /// Age is the condition people notice; the other three are the ones that matter. Together they
        /// mean auto-archive can never hide a change you have not looked at, and a feature always
        /// opens showing something.
        /// </remarks>
        [Test]
        public void Archive_OnlyTakesReviewedClosedAndSupersededSessions()
        {
            ScratchpadSettings.ArchiveHours = 12;

            var old = Handoff("UV Editor", Change("a", "Old change"));
            old.Written = "2026-08-30T09:00:00";
            WriteHandoff("UV Editor", "2026-08-30-0900", old);

            var newest = Handoff("UV Editor", Change("b", "Newest change"));
            newest.Written = "2026-08-31T14:30:00";
            WriteHandoff("UV Editor", "2026-08-31-1430", newest);

            var now = new DateTime(2026, 9, 1, 12, 0, 0);

            var store = StoreAt(now);
            var oldSession = store.Features[0].Sessions.Single(s => s.Stamp == "2026-08-30-0900");
            var newSession = store.Features[0].Sessions.Single(s => s.Stamp == "2026-08-31-1430");

            Assert.IsFalse(store.IsArchivable(oldSession), "An unreviewed session must never be archived.");

            oldSession.SetReview("a", ReviewState.Ok);
            var note = store.AddNote(oldSession, "a", NoteKind.Issue, "Still open");
            Assert.IsFalse(store.IsArchivable(oldSession), "An open note must hold a session back.");

            store.SetNoteStatus(oldSession, note, NoteStatus.Resolved);
            Assert.IsTrue(store.IsArchivable(oldSession));

            newSession.SetReview("b", ReviewState.Ok);
            Assert.IsFalse(store.IsArchivable(newSession),
                "The newest session for a feature is never archived, whatever its state.");

            // And the move actually happens on the next refresh, files and all.
            var reopened = StoreAt(now);
            Assert.AreEqual(1, reopened.Features[0].Sessions.Count);
            Assert.AreEqual("2026-08-31-1430", reopened.Features[0].Sessions[0].Stamp);
            Assert.IsTrue(File.Exists(ScratchpadPaths.HandoffPath("UV Editor", "2026-08-30-0900", archived: true)));
        }
    }
}
