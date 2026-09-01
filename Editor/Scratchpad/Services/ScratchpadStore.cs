using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vapor.Serialization;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Reads the scratchpad folder, writes the notes files, and nothing else touches disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store loads everything on a refresh rather than paging sessions in as they are opened.
    /// Handoffs are small text files and there are tens of them, not thousands, and having the whole
    /// tree in hand is what makes the cross-session work trivial: applying a handoff's resolved list
    /// to notes written weeks earlier, recovering a note counter from files after the index was lost,
    /// deciding what is safe to archive.
    /// </para>
    /// <para>
    /// It reads with <see cref="File"/> rather than <c>AssetDatabase</c> on purpose. A handoff the
    /// assistant wrote thirty seconds ago has not been imported yet, and the whole point of the
    /// refresh button is that it finds that file. For the same reason nothing here calls
    /// <c>AssetDatabase.ImportAsset</c> after writing: Unity picks the notes file up on its next
    /// ordinary refresh, and forcing one on every state change would reimport a file per click.
    /// </para>
    /// </remarks>
    internal sealed class ScratchpadStore
    {
        /// <summary>Live features, alphabetical, each with its sessions newest first.</summary>
        public List<ScratchpadFeature> Features { get; } = new();

        /// <summary>The mirror under <c>Archive/</c>. Browsed, never annotated.</summary>
        public List<ScratchpadFeature> ArchivedFeatures { get; } = new();

        /// <summary>Sessions moved out by the last refresh, so the window can say what happened.</summary>
        public List<string> LastArchived { get; } = new();

        /// <summary>
        /// Sessions the last refresh would have archived, and did not, because you pulled them out.
        /// </summary>
        /// <remarks>
        /// Reported rather than silent because suppression creates its own confusion: an old, finished
        /// session sitting in the live list looks like the archive rule is broken, and the answer —
        /// "because you asked for it back" — is not visible anywhere else.
        /// </remarks>
        public List<string> LastKeptOut { get; } = new();

        /// <summary>
        /// Sessions unarchived by hand, as feature/stamp keys.
        /// </summary>
        /// <remarks>
        /// Held in <see cref="SessionState"/> rather than in a field, because a field dies with the
        /// domain: a recompile between unarchiving a session and the next refresh lost the exemption
        /// and archived it anyway, which made the button work only most of the time. Not
        /// <c>EditorPrefs</c> — an exemption that outlived the editor would keep sessions out of the
        /// archive for reasons nobody remembers.
        /// </remarks>
        /// <remarks>
        /// Scoped to the scratchpad root. In the editor there is only ever one, so this changes
        /// nothing; in the tests each case gets its own temp root, and without the scoping an
        /// exemption written by one test was still in <see cref="SessionState"/> for the next — which
        /// is exactly how it failed, silently and only from the second run onwards.
        /// </remarks>
        private static string UnarchivedKey =>
            $"Vapor.Scratchpad.UnarchivedByHand:{ScratchpadPaths.Root.GetHashCode():X8}";

        private static HashSet<string> LoadUnarchived()
        {
            var stored = SessionState.GetString(UnarchivedKey, string.Empty);

            return string.IsNullOrEmpty(stored)
                ? new HashSet<string>()
                : new HashSet<string>(stored.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        private static void RememberUnarchived(string key)
        {
            var set = LoadUnarchived();
            if (set.Add(key))
            {
                SessionState.SetString(UnarchivedKey, string.Join("\n", set));
            }
        }

        /// <summary>Wipes the exemptions for the current root, so one test cannot reach the next.</summary>
        internal static void ClearUnarchivedForTests() => SessionState.EraseString(UnarchivedKey);

        /// <summary>Drops an exemption, for a session archived on purpose.</summary>
        private static void ForgetUnarchived(string key)
        {
            var set = LoadUnarchived();
            if (set.Remove(key))
            {
                SessionState.SetString(UnarchivedKey, string.Join("\n", set));
            }
        }

        /// <summary>Now, injectable so the archive rule can be tested without waiting twelve hours.</summary>
        public Func<DateTime> Clock = () => DateTime.Now;

        #region Scanning

        /// <summary>
        /// Rebuilds everything from disk: load, repair, resolve, archive, reindex.
        /// </summary>
        /// <remarks>
        /// The order is load-and-repair, then close what the newest handoffs closed, then archive what
        /// that made eligible. Archiving last means a session closed by the handoff you just received
        /// can be filed away in the same pass rather than lingering until the next refresh.
        /// </remarks>
        /// <param name="allowArchive">
        /// False for the quick-capture popup, which loads the tree only to find a feature to write to.
        /// A popup that lives for two seconds should not be moving files around on disk — archiving is
        /// something you should be looking at the window to see happen.
        /// </param>
        public void Refresh(bool allowArchive = true)
        {
            Features.Clear();
            ArchivedFeatures.Clear();
            LastArchived.Clear();
            LastKeptOut.Clear();

            var index = LoadIndex();

            Directory.CreateDirectory(ScratchpadPaths.Root);

            ScanInto(Features, ScratchpadPaths.Root, archived: false);
            ScanInto(ArchivedFeatures, ScratchpadPaths.ArchiveRoot, archived: true);

            foreach (var feature in Features)
            {
                RestoreNoteCounter(feature, index);
                ApplyResolutions(feature);
            }

            foreach (var feature in ArchivedFeatures)
            {
                RestoreNoteCounter(feature, index);
            }

            if (allowArchive)
            {
                AutoArchive();
            }

            SaveDirty();
            SaveIndex();
        }

        private void ScanInto(List<ScratchpadFeature> into, string root, bool archived)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            var directories = Directory.GetDirectories(root)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);

                // The archive lives inside the root, so it would otherwise scan as a feature called
                // "Archive" holding folders that are really features.
                if (!archived && string.Equals(name, ScratchpadPaths.ArchiveFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var feature = new ScratchpadFeature
                {
                    Name = name,
                    Slug = ScratchpadPaths.Slug(name),
                    Archived = archived,
                };

                foreach (var file in Directory.GetFiles(directory, "*" + ScratchpadPaths.HandoffSuffix))
                {
                    feature.Sessions.Add(LoadSession(feature, ScratchpadPaths.StampFromHandoffFile(file), archived));
                }

                // Newest first. The stamp sorts correctly as text by construction, and a collision
                // suffix sorts after the stamp it disambiguates, which is also the order the two were
                // written in.
                feature.Sessions.Sort((a, b) => string.CompareOrdinal(b.Stamp, a.Stamp));
                into.Add(feature);
            }
        }

        private ScratchpadSession LoadSession(ScratchpadFeature feature, string stamp, bool archived)
        {
            var session = new ScratchpadSession
            {
                Feature = feature,
                Stamp = stamp,
                Archived = archived,
            };

            var handoffPath = ScratchpadPaths.HandoffPath(feature.Name, stamp, archived);

            try
            {
                session.Handoff = Vsl.Deserialize<ScratchpadHandoff>(File.ReadAllText(handoffPath)) ?? new ScratchpadHandoff();
            }
            catch (Exception e)
            {
                // A handoff that will not parse still gets a row. Dropping it would make a typo look
                // like the assistant never wrote anything.
                session.Handoff = new ScratchpadHandoff();
                session.ParseError = e.Message;
            }

            // The folder is the truth about which feature a session belongs to. A handoff that
            // disagrees was misfiled, and saying so is more use than quietly believing it.
            if (!string.IsNullOrWhiteSpace(session.Handoff.Feature) &&
                !string.Equals(session.Handoff.Feature, feature.Name, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[Scratchpad] {handoffPath} says its feature is \"{session.Handoff.Feature}\" " +
                                 $"but it sits in \"{feature.Name}\". Going with the folder.");
            }

            var notesPath = ScratchpadPaths.NotesPath(feature.Name, stamp, archived);
            if (File.Exists(notesPath))
            {
                try
                {
                    session.Notes = Vsl.Deserialize<ScratchpadNotes>(File.ReadAllText(notesPath)) ?? new ScratchpadNotes();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Scratchpad] Could not read {notesPath}: {e.Message}");
                    session.Notes = new ScratchpadNotes();
                }
            }

            session.Notes.Handoff = stamp;
            session.Written = ResolveWrittenTime(session, handoffPath);

            BackfillChangeIds(session);
            return session;
        }

        private static DateTime ResolveWrittenTime(ScratchpadSession session, string handoffPath)
        {
            if (ScratchpadPaths.TryParseTimestamp(session.Handoff.Written, out var stated))
            {
                return stated;
            }

            if (ScratchpadPaths.TryParseStamp(session.Stamp, out var fromStamp))
            {
                return fromStamp;
            }

            return File.Exists(handoffPath) ? File.GetLastWriteTime(handoffPath) : DateTime.Now;
        }

        #endregion

        #region Repair

        /// <summary>
        /// Gives every change an id, inventing one where the handoff left it empty.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the one place that writes to the in-memory handoff, and it never writes the file.
        /// Patching <see cref="ScratchpadChange.Id"/> here means everything downstream — notes,
        /// reviews, the prompt — can assume an id exists rather than each carrying its own fallback.
        /// </para>
        /// <para>
        /// The invented id is derived from the title and also persisted to the notes file. Either
        /// alone would do on a good day; both together mean a note stays attached whether the handoff
        /// is re-read, the notes file is lost, or the changes are reordered.
        /// </para>
        /// </remarks>
        private static void BackfillChangeIds(ScratchpadSession session)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < session.Changes.Count; i++)
            {
                var change = session.Changes[i];

                if (!string.IsNullOrWhiteSpace(change.Id) && seen.Add(change.Id))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(change.Id))
                {
                    Debug.LogWarning($"[Scratchpad] {session.Stamp} uses the change id \"{change.Id}\" more than " +
                                     "once. The later one is being given an id of its own.");
                }

                var recorded = session.Notes.Backfilled.FirstOrDefault(b => b.Ordinal == i);
                if (recorded != null)
                {
                    change.Id = recorded.Id;
                }
                else
                {
                    change.Id = InventChangeId(i, change.Title);
                    session.Notes.Backfilled.Add(new ScratchpadIdBackfill { Ordinal = i, Id = change.Id });
                    session.NotesDirty = true;
                }

                seen.Add(change.Id);
            }
        }

        private static string InventChangeId(int ordinal, string title)
        {
            // Derived from the title so a regenerated id matches the recorded one, which keeps a notes
            // file that failed to save from silently orphaning everything hanging off it.
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in title ?? string.Empty)
                {
                    hash = (hash ^ c) * 16777619u;
                }

                return $"chg-{ordinal}-{hash & 0xFFFF:x4}";
            }
        }

        /// <summary>
        /// Closes notes that a later handoff says it dealt with.
        /// </summary>
        /// <remarks>
        /// Scoped to the feature rather than the session, because the whole point is that a note
        /// written against Tuesday's session is closed by Thursday's handoff. Only outstanding notes
        /// move — naming an already-dismissed id does not reopen it.
        /// </remarks>
        private static void ApplyResolutions(ScratchpadFeature feature)
        {
            var resolved = new HashSet<string>(
                feature.Sessions.SelectMany(s => s.Handoff.Resolved).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            if (resolved.Count == 0)
            {
                return;
            }

            foreach (var session in feature.Sessions)
            {
                foreach (var note in session.Notes.Notes)
                {
                    if (!note.IsOutstanding || !resolved.Contains(note.Id))
                    {
                        continue;
                    }

                    note.Status = NoteStatus.Resolved;
                    session.NotesDirty = true;
                }
            }
        }

        /// <summary>
        /// Restores the note-id counter, preferring the index and falling back to the files.
        /// </summary>
        /// <remarks>
        /// Reissuing a number is the one genuinely damaging failure this tool has: an id already
        /// quoted in a chat would come back and close the wrong note. So the counter is always the
        /// higher of what the index remembers and what the files prove was used.
        /// </remarks>
        private static void RestoreNoteCounter(ScratchpadFeature feature, ScratchpadIndex index)
        {
            var next = index?.Features
                .FirstOrDefault(f => string.Equals(f.Name, feature.Name, StringComparison.OrdinalIgnoreCase))
                ?.NextNoteNumber ?? 1;

            var prefix = feature.Slug + "-";
            foreach (var note in feature.AllNotes)
            {
                if (note.Id == null || !note.Id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (int.TryParse(note.Id[prefix.Length..], out var number) && number >= next)
                {
                    next = number + 1;
                }
            }

            feature.NextNoteNumber = next < 1 ? 1 : next;
        }

        #endregion

        #region Mutation

        /// <summary>Hands out the next note id for a feature, e.g. <c>uv-editor-17</c>.</summary>
        public string AllocateNoteId(ScratchpadFeature feature)
        {
            var id = $"{feature.Slug}-{feature.NextNoteNumber}";
            feature.NextNoteNumber++;
            SaveIndex();
            return id;
        }

        public ScratchpadNote AddNote(ScratchpadSession session, string changeId, NoteKind kind, string body,
            NoteSource source = NoteSource.Manual, string context = null, string console = null,
            string followUpId = null)
        {
            var note = new ScratchpadNote
            {
                Id = AllocateNoteId(session.Feature),
                ChangeId = changeId ?? string.Empty,
                Kind = kind,
                Status = NoteStatus.Open,
                Body = body ?? string.Empty,
                Created = ScratchpadPaths.Timestamp(Clock()),
                Source = source,
                Context = context ?? string.Empty,
                Console = console ?? string.Empty,
                FollowUpId = followUpId ?? string.Empty,
            };

            session.Notes.Notes.Add(note);
            session.NotesDirty = true;
            SaveNotes(session);
            return note;
        }

        /// <summary>
        /// Moves a note onto a different change, or off every change and onto the session.
        /// </summary>
        /// <remarks>
        /// Quick capture can only ever file a loose note — a popup is the wrong place to render a
        /// session tree — so without this the note stays loose for good. Only the attachment moves;
        /// the id, the status and everything already quoted into a chat stay exactly as they were.
        /// </remarks>
        public void MoveNote(ScratchpadSession session, ScratchpadNote note, string changeId)
        {
            note.ChangeId = changeId ?? string.Empty;
            session.NotesDirty = true;
            SaveNotes(session);
        }

        public void RemoveNote(ScratchpadSession session, ScratchpadNote note)
        {
            session.Notes.Notes.Remove(note);
            session.NotesDirty = true;
            SaveNotes(session);
        }

        public void SetNoteStatus(ScratchpadSession session, ScratchpadNote note, NoteStatus status)
        {
            note.Status = status;
            session.NotesDirty = true;
            SaveNotes(session);
        }

        /// <summary>
        /// Takes on a follow-up the handoff proposed, as a Work note of your own.
        /// </summary>
        /// <remarks>
        /// Accepting creates a real note rather than treating the proposal as one, so an accepted
        /// follow-up flows into prompts, gets an id that can be quoted back, and closes the same way
        /// everything else does. The proposal keeps its own state so the offer stays visible as
        /// accepted rather than disappearing into the note list.
        /// </remarks>
        public ScratchpadNote AcceptFollowUp(ScratchpadSession session, ScratchpadFollowUp followUp)
        {
            session.SetFollowUpState(followUp.Id, FollowUpState.Accepted);

            var body = string.IsNullOrWhiteSpace(followUp.Detail)
                ? followUp.Title
                : $"{followUp.Title}\n\n{followUp.Detail}";

            var note = new ScratchpadNote
            {
                Id = AllocateNoteId(session.Feature),
                ChangeId = string.Empty,
                Kind = NoteKind.Work,
                Status = NoteStatus.Open,
                Body = body,
                Created = ScratchpadPaths.Timestamp(Clock()),
                Source = NoteSource.ProposedFollowUp,
                FollowUpId = followUp.Id,
            };

            session.Notes.Notes.Add(note);
            session.NotesDirty = true;
            SaveNotes(session);
            return note;
        }

        public void DismissFollowUp(ScratchpadSession session, ScratchpadFollowUp followUp)
        {
            session.SetFollowUpState(followUp.Id, FollowUpState.Dismissed);
            SaveNotes(session);
        }

        /// <summary>Creates a feature folder so notes have somewhere to go.</summary>
        public ScratchpadFeature CreateFeature(string name)
        {
            name = SanitizeFeatureName(name);

            var existing = FindFeature(name);
            if (existing != null)
            {
                return existing;
            }

            Directory.CreateDirectory(ScratchpadPaths.FeatureDirectory(name));

            var feature = new ScratchpadFeature { Name = name, Slug = ScratchpadPaths.Slug(name) };
            Features.Add(feature);
            Features.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            SaveIndex();
            return feature;
        }

        /// <summary>
        /// Renames a feature by renaming its folder, which is the only thing a feature is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Note ids already issued keep the old slug — <c>uv-editor-17</c> stays <c>uv-editor-17</c>
        /// after the feature becomes "UV Tools". That is deliberate: an id may already be quoted in a
        /// chat, and rewriting it would break the one promise the round trip makes. Ids are opaque
        /// once issued, so a feature carrying two prefixes is untidy rather than wrong.
        /// </para>
        /// <para>
        /// A useful consequence: because the new slug is a different prefix, the counter restarting at
        /// one cannot collide with anything already handed out.
        /// </para>
        /// </remarks>
        public bool RenameFeature(ScratchpadFeature feature, string newName)
        {
            if (feature == null || feature.Archived)
            {
                return false;
            }

            newName = SanitizeFeatureName(newName);
            if (string.Equals(newName, feature.Name, StringComparison.Ordinal))
            {
                return false;
            }

            var from = ScratchpadPaths.FeatureDirectory(feature.Name);
            var to = ScratchpadPaths.FeatureDirectory(newName);

            // A case-only rename is a move onto itself on Windows, which Directory.Move rejects.
            var caseOnly = string.Equals(newName, feature.Name, StringComparison.OrdinalIgnoreCase);

            if (!caseOnly && Directory.Exists(to))
            {
                Debug.LogError($"[Scratchpad] \"{newName}\" already exists. Merging two features is not " +
                               "something this can do safely, so nothing was moved.");
                return false;
            }

            try
            {
                if (caseOnly)
                {
                    var staging = to + "~renaming";
                    Directory.Move(from, staging);
                    Directory.Move(staging, to);
                }
                else
                {
                    Directory.Move(from, to);
                }

                // The folder's own .meta, which Directory.Move leaves behind pointing at nothing.
                MoveFile(from + ".meta", to + ".meta");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] Could not rename \"{feature.Name}\": {e.Message}");
                return false;
            }

            var oldName = feature.Name;
            feature.Name = newName;
            feature.Slug = ScratchpadPaths.Slug(newName);
            feature.NextNoteNumber = 1;

            if (string.Equals(ScratchpadSettings.LastFeature, oldName, StringComparison.OrdinalIgnoreCase))
            {
                ScratchpadSettings.LastFeature = newName;
            }

            Features.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            SaveIndex();

            Debug.Log($"[Scratchpad] Renamed \"{oldName}\" to \"{newName}\". Existing note ids keep the " +
                      $"{ScratchpadPaths.Slug(oldName)}- prefix; new ones will use {feature.Slug}-.");

            return true;
        }

        /// <summary>
        /// Writes an empty session so notes can be taken before any work has been handed off.
        /// </summary>
        /// <remarks>
        /// It is a real handoff file, flagged <see cref="ScratchpadHandoff.Placeholder"/>, rather than
        /// a special case inside the store. That keeps one code path for loading, one for saving and
        /// one shape on disk; the flag only changes what the row is called.
        /// </remarks>
        public ScratchpadSession CreateEmptySession(ScratchpadFeature feature)
        {
            var directory = ScratchpadPaths.FeatureDirectory(feature.Name);
            Directory.CreateDirectory(directory);

            var now = Clock();
            var stamp = ScratchpadPaths.NewStamp(now, directory);

            var session = new ScratchpadSession
            {
                Feature = feature,
                Stamp = stamp,
                Written = now,
                Handoff = new ScratchpadHandoff
                {
                    Feature = feature.Name,
                    Title = "Notes with no handoff yet",
                    Written = ScratchpadPaths.Timestamp(now),
                    Placeholder = true,
                },
            };

            session.Notes.Handoff = stamp;

            try
            {
                Vsl.WriteToFile(session.HandoffPath, session.Handoff);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] Could not write {session.HandoffPath}: {e.Message}");
            }

            session.NotesDirty = true;
            SaveNotes(session);

            feature.Sessions.Insert(0, session);
            return session;
        }

        /// <summary>Writes the sibling notes file. Never the handoff.</summary>
        public void SaveNotes(ScratchpadSession session)
        {
            if (session == null || session.Archived)
            {
                return;
            }

            try
            {
                Vsl.WriteToFile(session.NotesPath, session.Notes);
                session.NotesDirty = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] Could not write {session.NotesPath}: {e.Message}");
            }
        }

        private void SaveDirty()
        {
            foreach (var session in Features.SelectMany(f => f.Sessions).Where(s => s.NotesDirty).ToList())
            {
                SaveNotes(session);
            }
        }

        private static string SanitizeFeatureName(string name)
        {
            name = (name ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '-');
            }

            return string.IsNullOrWhiteSpace(name) ? "Unnamed Feature" : name;
        }

        #endregion

        #region Archive

        /// <summary>
        /// Whether a session may be filed away without hiding anything you have not dealt with.
        /// </summary>
        /// <remarks>
        /// Age is the last and weakest of the four conditions. The first two say the work has been
        /// read and everything it prompted has been closed; the third keeps a feature's most recent
        /// session on screen whatever its state, so opening a feature never shows an empty list. Age
        /// only decides how long a finished session lingers before it goes.
        /// </remarks>
        public bool IsArchivable(ScratchpadSession session)
        {
            if (session == null || session.Archived || session.Feature == null)
            {
                return false;
            }

            if (ReferenceEquals(session, session.Feature.Newest))
            {
                return false;
            }

            if (!session.IsFullyClosed)
            {
                return false;
            }

            return session.Written <= Clock().AddHours(-ScratchpadSettings.ArchiveHours);
        }

        /// <summary>
        /// Moves everything eligible into <c>Archive/</c>, saying so each time.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="Refresh"/> and so only when the window is opened or refreshed.
        /// Nothing here runs on a timer: a file move that happens while you are looking somewhere else
        /// is a file move you have to reconstruct later.
        /// </remarks>
        private void AutoArchive()
        {
            if (!ScratchpadSettings.AutoArchive)
            {
                return;
            }

            var exempt = LoadUnarchived();

            foreach (var feature in Features.ToList())
            {
                foreach (var session in feature.Sessions.Where(IsArchivable).ToList())
                {
                    var label = $"{feature.Name} / {session.DisplayStamp}";

                    // Unarchiving is an instruction, not a request. A session that qualifies on every
                    // other count still qualified a moment ago, when you took it back out — filing it
                    // again is the tool arguing with you.
                    if (exempt.Contains(SessionKey(feature.Name, session.Stamp)))
                    {
                        LastKeptOut.Add(label);
                        continue;
                    }

                    if (MoveSession(session, toArchive: true))
                    {
                        LastArchived.Add(label);
                    }
                }
            }

            if (LastArchived.Count > 0)
            {
                Debug.Log($"[Scratchpad] Archived {LastArchived.Count} finished " +
                          $"{(LastArchived.Count == 1 ? "session" : "sessions")}: {string.Join(", ", LastArchived)}");
            }

            if (LastKeptOut.Count > 0)
            {
                Debug.Log($"[Scratchpad] Kept {string.Join(", ", LastKeptOut)} out of the archive because " +
                          "you unarchived it. It will be filed again after a script reload.");
            }
        }

        #region Feature info

        /// <summary>Reads a feature's description, or an empty one if it has never had a kickoff.</summary>
        public ScratchpadFeatureInfo LoadFeatureInfo(ScratchpadFeature feature)
        {
            if (feature == null)
            {
                return new ScratchpadFeatureInfo();
            }

            var path = ScratchpadPaths.FeatureInfoPath(feature.Name, feature.Archived);

            try
            {
                if (File.Exists(path))
                {
                    return Vsl.Deserialize<ScratchpadFeatureInfo>(File.ReadAllText(path)) ?? new ScratchpadFeatureInfo();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Scratchpad] Could not read {path}: {e.Message}");
            }

            return new ScratchpadFeatureInfo();
        }

        public void SaveFeatureInfo(ScratchpadFeature feature, ScratchpadFeatureInfo info)
        {
            if (feature == null || info == null)
            {
                return;
            }

            var path = ScratchpadPaths.FeatureInfoPath(feature.Name, feature.Archived);

            try
            {
                Vsl.WriteToFile(path, info);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] Could not write {path}: {e.Message}");
            }
        }

        #endregion

        #region Test log

        /// <summary>
        /// Reads a feature's test filter and run history, or an empty one if it has never run any.
        /// </summary>
        /// <remarks>
        /// Loaded on demand rather than during the scan. Most refreshes never look at it, and it is
        /// the one file here that has nothing to do with what is owed on a session.
        /// </remarks>
        public ScratchpadTestLog LoadTestLog(ScratchpadFeature feature)
        {
            if (feature == null)
            {
                return new ScratchpadTestLog();
            }

            var path = ScratchpadPaths.TestLogPath(feature.Name, feature.Archived);

            try
            {
                if (File.Exists(path))
                {
                    return Vsl.Deserialize<ScratchpadTestLog>(File.ReadAllText(path)) ?? new ScratchpadTestLog();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Scratchpad] Could not read {path}: {e.Message}");
            }

            return new ScratchpadTestLog();
        }

        /// <summary>
        /// Everything that should run for a feature: what its handoffs declared, plus what you added,
        /// minus what you switched off.
        /// </summary>
        /// <remarks>
        /// Derived rather than stored, so a new handoff naming a new test brings it in without anybody
        /// maintaining a list. The assistant is the one that knows which tests it just wrote, and the
        /// handoff is where it already says everything else about the work.
        /// </remarks>
        public List<string> TestsFor(ScratchpadFeature feature, ScratchpadTestLog log = null)
        {
            var result = new List<string>();
            if (feature == null)
            {
                return result;
            }

            log ??= LoadTestLog(feature);
            var excluded = new HashSet<string>(log.Excluded, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Newest session first, so the tests most recently declared lead the list.
            foreach (var name in feature.Sessions.SelectMany(s => s.Handoff.Tests).Concat(log.Extra))
            {
                var trimmed = (name ?? string.Empty).Trim();

                if (trimmed.Length > 0 && !excluded.Contains(trimmed) && seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        /// <summary>Whether a test name came from a handoff rather than from the extras list.</summary>
        public bool IsDeclaredByHandoff(ScratchpadFeature feature, string name) =>
            feature != null && feature.Sessions.Any(s =>
                s.Handoff.Tests.Any(t => string.Equals(t?.Trim(), name, StringComparison.OrdinalIgnoreCase)));

        public void SaveTestLog(ScratchpadFeature feature, ScratchpadTestLog log)
        {
            if (feature == null || log == null)
            {
                return;
            }

            var path = ScratchpadPaths.TestLogPath(feature.Name, feature.Archived);

            try
            {
                Vsl.WriteToFile(path, log);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] Could not write {path}: {e.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Archives every session in a feature, whatever state they are in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deliberately ignores the archive predicate. That predicate exists to stop the tool filing
        /// away work you have not read; it has nothing to say about work you have explicitly told it
        /// to put away. Unreviewed sessions and outstanding notes go too.
        /// </para>
        /// <para>
        /// Any hand-unarchive exemptions for those sessions are dropped, since keeping them would
        /// mean a session you just archived on purpose being held out of the archive on the next
        /// refresh by an instruction you gave ten minutes earlier.
        /// </para>
        /// </remarks>
        public int ArchiveFeature(ScratchpadFeature feature)
        {
            if (feature == null || feature.Archived || feature.Sessions.Count == 0)
            {
                return 0;
            }

            var name = feature.Name;
            var moved = 0;

            foreach (var session in feature.Sessions.ToList())
            {
                var key = SessionKey(name, session.Stamp);

                if (MoveSession(session, toArchive: true))
                {
                    moved++;
                    ForgetUnarchived(key);
                }
            }

            if (moved > 0)
            {
                // The description and the test log describe the work that just moved, so they go with
                // it rather than being left to make the live folder look occupied.
                foreach (var side in SideFiles(name, archived: false))
                {
                    MoveWithMeta(side, Path.Combine(ScratchpadPaths.FeatureDirectory(name, archived: true),
                        Path.GetFileName(side)).Replace('\\', '/'));
                }

                // The live folder is empty now, and an empty one would sit in the rail as a feature
                // with nothing in it — indistinguishable from one you had just created.
                RemoveEmptyFeatureDirectory(name);

                Debug.Log($"[Scratchpad] Archived all of \"{name}\" — {moved} " +
                          $"{(moved == 1 ? "session" : "sessions")}. Unarchive them one at a time from " +
                          "the Archive view.");
            }

            SaveIndex();
            return moved;
        }

        /// <summary>
        /// Deletes a feature that has nothing in it.
        /// </summary>
        /// <remarks>
        /// Safe by construction: it refuses unless the folder is empty, so there is nothing to lose.
        /// Used both after archiving a feature — which empties it — and to clear up a feature that was
        /// created and never used.
        /// </remarks>
        public bool DeleteEmptyFeature(ScratchpadFeature feature)
        {
            if (feature == null || feature.Archived || feature.Sessions.Count > 0)
            {
                return false;
            }

            if (!RemoveEmptyFeatureDirectory(feature.Name))
            {
                return false;
            }

            Features.Remove(feature);
            SaveIndex();
            return true;
        }

        /// <summary>
        /// The files the window keeps beside a feature's sessions, which are not sessions themselves.
        /// </summary>
        /// <remarks>
        /// They have to be enumerated in one place because both of the operations that empty a feature
        /// have to deal with them: archiving moves them along with the work they describe, and deleting
        /// has to see past them to decide the folder is empty. A feature holding nothing but its own
        /// description is an empty feature.
        /// </remarks>
        private static IEnumerable<string> SideFiles(string name, bool archived) => new[]
        {
            ScratchpadPaths.FeatureInfoPath(name, archived),
            ScratchpadPaths.TestLogPath(name, archived),
        };

        private static bool RemoveEmptyFeatureDirectory(string name)
        {
            var directory = ScratchpadPaths.FeatureDirectory(name);

            try
            {
                if (!Directory.Exists(directory))
                {
                    return true;
                }

                foreach (var side in SideFiles(name, archived: false))
                {
                    DeleteFile(side);
                    DeleteFile(side + ".meta");
                }

                if (Directory.GetFileSystemEntries(directory).Length != 0)
                {
                    return false;
                }

                Directory.Delete(directory);

                // Unity treats a .meta with no asset beside it as an error worth logging on the next
                // import, so it goes with the folder it described.
                if (File.Exists(directory + ".meta"))
                {
                    File.Delete(directory + ".meta");
                }

                return true;
            }
            catch (Exception e)
            {
                // An empty folder left behind is untidy, not broken.
                Debug.LogWarning($"[Scratchpad] Could not remove the empty folder for \"{name}\": {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Brings a session back out of the archive, and remembers that you did.
        /// </summary>
        /// <remarks>
        /// Remembering is what lets the next refresh explain itself if the session qualifies for
        /// archiving again immediately. Kept in memory rather than on disk: it is about what you did a
        /// minute ago, and a fact that stale would be worse than one that is missing.
        /// </remarks>
        public bool Unarchive(ScratchpadSession session)
        {
            if (session?.Feature == null)
            {
                return false;
            }

            var key = SessionKey(session.Feature.Name, session.Stamp);

            if (!MoveSession(session, toArchive: false))
            {
                return false;
            }

            RememberUnarchived(key);
            return true;
        }

        private static string SessionKey(string feature, string stamp) => $"{feature}/{stamp}";

        private bool MoveSession(ScratchpadSession session, bool toArchive)
        {
            if (session?.Feature == null || session.Archived == toArchive)
            {
                return false;
            }

            var feature = session.Feature;
            var from = toArchive ? Features : ArchivedFeatures;
            var to = toArchive ? ArchivedFeatures : Features;

            // Flush before moving: the notes file about to move is the one holding the reviews and
            // resolutions that made the session eligible in the first place.
            if (session.NotesDirty)
            {
                SaveNotes(session);
            }

            try
            {
                Directory.CreateDirectory(ScratchpadPaths.FeatureDirectory(feature.Name, toArchive));

                MoveWithMeta(ScratchpadPaths.HandoffPath(feature.Name, session.Stamp, !toArchive),
                    ScratchpadPaths.HandoffPath(feature.Name, session.Stamp, toArchive));

                MoveWithMeta(ScratchpadPaths.NotesPath(feature.Name, session.Stamp, !toArchive),
                    ScratchpadPaths.NotesPath(feature.Name, session.Stamp, toArchive));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] Could not move {session.Stamp} for \"{feature.Name}\": {e.Message}");
                return false;
            }

            feature.Sessions.Remove(session);
            if (feature.Sessions.Count == 0)
            {
                from.Remove(feature);
            }

            var destination = to.FirstOrDefault(f =>
                string.Equals(f.Name, feature.Name, StringComparison.OrdinalIgnoreCase));

            if (destination == null)
            {
                destination = new ScratchpadFeature
                {
                    Name = feature.Name,
                    Slug = feature.Slug,
                    Archived = toArchive,
                    NextNoteNumber = feature.NextNoteNumber,
                };

                to.Add(destination);
                to.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }

            session.Feature = destination;
            session.Archived = toArchive;
            destination.Sessions.Add(session);
            destination.Sessions.Sort((a, b) => string.CompareOrdinal(b.Stamp, a.Stamp));
            return true;
        }

        /// <summary>
        /// Moves a file along with the <c>.meta</c> Unity made for it.
        /// </summary>
        /// <remarks>
        /// Leaving the meta behind would orphan it and cost the moved file its guid, which Unity then
        /// reports as a deleted asset and a new one.
        /// </remarks>
        private static void MoveWithMeta(string from, string to)
        {
            MoveFile(from, to);
            MoveFile(from + ".meta", to + ".meta");
        }

        /// <summary>Deletes one file if it is there.</summary>
        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Moves one file if it is there, overwriting whatever is in the way.</summary>
        private static void MoveFile(string from, string to)
        {
            if (!File.Exists(from))
            {
                return;
            }

            if (File.Exists(to))
            {
                File.Delete(to);
            }

            File.Move(from, to);
        }

        #endregion

        #region Index

        private static ScratchpadIndex LoadIndex()
        {
            try
            {
                return File.Exists(ScratchpadPaths.IndexPath)
                    ? Vsl.Deserialize<ScratchpadIndex>(File.ReadAllText(ScratchpadPaths.IndexPath))
                    : null;
            }
            catch
            {
                // A cache that will not parse is a cache to rebuild, not an error to report. Every
                // field in it is recoverable from the files it summarises.
                return null;
            }
        }

        public void SaveIndex()
        {
            var index = new ScratchpadIndex { Generated = ScratchpadPaths.Timestamp(Clock()) };

            foreach (var feature in Features.Concat(ArchivedFeatures))
            {
                var entry = index.Features.FirstOrDefault(f =>
                    string.Equals(f.Name, feature.Name, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    entry = new ScratchpadFeatureEntry { Name = feature.Name, Slug = feature.Slug };
                    index.Features.Add(entry);
                }

                // A feature can appear in both lists at once. The counter has to be the higher of the
                // two, or unarchiving would let a number out twice.
                entry.NextNoteNumber = Math.Max(entry.NextNoteNumber, feature.NextNoteNumber);
                entry.SessionCount += feature.Sessions.Count;
                entry.OpenIssues += feature.OpenIssues;
                entry.OpenWork += feature.OpenWork;

                if (!feature.Archived && feature.Newest != null)
                {
                    entry.LatestSession = feature.Newest.Stamp;
                }
            }

            try
            {
                Directory.CreateDirectory(ScratchpadPaths.Root);
                Vsl.WriteToFile(ScratchpadPaths.IndexPath, index);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] Could not write the index: {e.Message}");
            }
        }

        #endregion

        #region Lookup

        public ScratchpadFeature FindFeature(string name) =>
            Features.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The session a loose note should land on: the newest for the feature, made if there is none.
        /// </summary>
        public ScratchpadSession NewestOrCreate(ScratchpadFeature feature) =>
            feature.Newest ?? CreateEmptySession(feature);

        #endregion
    }
}
