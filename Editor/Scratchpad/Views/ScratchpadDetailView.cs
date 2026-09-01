using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// The right pane: one change (or one session) read in full, and everything said about it.
    /// </summary>
    /// <remarks>
    /// Reading and annotating happen in the same place on purpose. The rationale and the risk are
    /// what a comment is usually a reaction to, so the composer sits directly under them rather than
    /// in a dialog that would hide the thing being commented on.
    /// </remarks>
    internal sealed class ScratchpadDetailView : VisualElement
    {
        private readonly ScratchpadWindow _window;
        private readonly ScratchpadScrollMemory _scrollMemory;
        private readonly VisualElement _body;

        private NoteKind _composerKind = NoteKind.Comment;
        private string _pendingConsole;

        /// <summary>
        /// The note being typed, kept outside the field that shows it.
        /// </summary>
        /// <remarks>
        /// A <see cref="TextField"/> only holds text for as long as the element exists, and this pane
        /// rebuilds wholesale on every edit — including the ones you make while writing a note. The
        /// draft has to live at the same level as the console entry and the picked controls, which is
        /// to say with the note it is going to become.
        /// </remarks>
        private string _pendingBody = string.Empty;

        /// <summary>The reply being typed, for the same reason.</summary>
        private string _pendingReply = string.Empty;

        /// <summary>Controls picked for the note being written, in the order they were picked.</summary>
        private readonly List<string> _pendingContexts = new();

        public ScratchpadDetailView(ScratchpadWindow window)
        {
            _window = window;

            style.flexGrow = 1;
            style.backgroundColor = ScratchpadStyles.Panel;
            style.minWidth = 260;

            var scroll = new ScrollView { style = { flexGrow = 1 } };
            _body = new VisualElement();
            ScratchpadStyles.SetPadding(_body, 8);
            scroll.Add(_body);
            Add(scroll);

            _scrollMemory = new ScratchpadScrollMemory(scroll);

            // The picker can be turned off from the keyboard or by a right-click anywhere in the
            // editor, so the toggle cannot learn it has ended by watching its own clicks.
            ScratchpadElementPicker.PickingChanged += OnPickingChanged;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ScratchpadElementPicker.PickingChanged -= OnPickingChanged;
                ScratchpadElementPicker.Cancel();
            });
        }

        private void OnPickingChanged() => Rebuild();

        /// <summary>What the pane is showing, so a rebuild of the same thing can hold its scroll.</summary>
        private string ScrollKey => _window.Session == null
            ? string.Empty
            : $"{_window.Session.FeatureName}/{_window.Session.Stamp}/{_window.Change?.Id}";

        /// <summary>What the pane was showing last rebuild, so a move can clear the composer.</summary>
        private string _composerFor;

        /// <summary>The follow-up whose reply box is open, if any.</summary>
        private string _replyingTo;

        public void Rebuild()
        {
            var key = ScrollKey;

            // Anything half-written belongs to the thing it was written against. Moving to a different
            // change drops it; rebuilding the same one — which is what every edit does — keeps it.
            //
            // This used to be an unconditional clear at the top of the rebuild, which quietly made the
            // composer's attach-console button impossible to use: it set the pending entry and then
            // asked for the rebuild that threw it away.
            if (!string.Equals(key, _composerFor, StringComparison.Ordinal))
            {
                _pendingBody = string.Empty;
                _pendingReply = string.Empty;
                _pendingConsole = null;
                _pendingContexts.Clear();
                _replyingTo = null;
                _composerFor = key;
            }

            _scrollMemory.Around(key, RebuildCore);
        }

        private void RebuildCore()
        {
            _body.Clear();

            var session = _window.Session;
            if (session == null)
            {
                // A feature with nothing in it is not an empty selection — it is a feature waiting to
                // be started, and that is a different thing to offer.
                var feature = _window.Feature;
                if (feature is { Archived: false } && feature.Sessions.Count == 0)
                {
                    BuildNewFeature(feature);
                }
                else
                {
                    _body.Add(ScratchpadStyles.Body("Pick a session or a change on the left.", dim: true));
                }

                return;
            }

            if (_window.Change != null)
            {
                BuildChange(session, _window.Change);
            }
            else
            {
                BuildSession(session);
            }
        }

        #region New feature

        /// <summary>
        /// What a feature offers before it has any work in it: say what it is, and start it.
        /// </summary>
        /// <remarks>
        /// The kickoff prompt asks for a plan and questions rather than code, because that is what the
        /// start of a feature actually needs — and it carries the handoff contract along with it, so
        /// the chat that plans the work already knows how to report it.
        /// </remarks>
        private void BuildNewFeature(ScratchpadFeature feature)
        {
            var info = _window.Store.LoadFeatureInfo(feature);

            var title = ScratchpadStyles.Header(feature.Name);
            title.style.fontSize = 13;
            _body.Add(title);

            var blurb = ScratchpadStyles.Body(
                "Nothing here yet. Say what this feature should do, then copy the kickoff prompt into a " +
                "chat — it asks for a plan and questions first, and carries the handoff contract with it.",
                dim: true);

            blurb.style.fontSize = 10;
            _body.Add(blurb);

            _body.Add(ScratchpadStyles.Caption("What it should do"));

            var field = new TextField { multiline = true, value = info.Description };
            field.style.minHeight = 72;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.tooltip = "In your own words. This goes into the kickoff prompt, and is kept so you " +
                            "can reword it without retyping it.";

            // Saved on focus loss rather than per keystroke: this writes a file, and a write per
            // character typed is a reimport per character typed.
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (field.value == info.Description)
                {
                    return;
                }

                info.Description = field.value;
                _window.Store.SaveFeatureInfo(feature, info);
            });

            _body.Add(field);

            var row = ScratchpadStyles.Row();
            row.style.marginTop = 4;

            row.Add(ScratchpadStyles.IconButton(ScratchpadIcon.CopyContract, "Copy kickoff prompt",
                "Copy a prompt that starts this feature: what it should do, a request to plan it and " +
                "ask questions before building, and the handoff contract for everything after that.",
                () =>
                {
                    // The field may hold something typed and not yet committed by a focus change.
                    if (field.value != info.Description)
                    {
                        info.Description = field.value;
                        _window.Store.SaveFeatureInfo(feature, info);
                    }

                    _window.CopyKickoff(feature, info.Description);
                }));

            row.Add(ScratchpadStyles.IconButton(ScratchpadIcon.AddSession, "Empty session",
                "Somewhere to put notes before any handoff has landed.",
                () =>
                {
                    var created = _window.Store.CreateEmptySession(feature);
                    _window.SelectSession(created);
                    _window.RebuildAll();
                }));

            _body.Add(row);
        }

        #endregion

        #region Change

        private void BuildChange(ScratchpadSession session, ScratchpadChange change)
        {
            var title = ScratchpadStyles.Header(string.IsNullOrWhiteSpace(change.Title) ? change.Id : change.Title);
            title.style.fontSize = 13;
            _body.Add(title);

            var id = ScratchpadStyles.Body(change.Id, dim: true);
            id.style.fontSize = 9;
            id.tooltip = "The id the handoff gave this change. Notes attach to it.";
            _body.Add(id);

            _body.Add(BuildReviewRow(session, change));
            _body.Add(ScratchpadStyles.Separator());

            AppendField("Summary", change.Summary);
            AppendField("Rationale", change.Rationale);
            AppendField("Risk", change.Risk, ScratchpadStyles.CloserLook);

            if (change.Files.Count > 0)
            {
                _body.Add(ScratchpadStyles.Caption("Files"));
                foreach (var file in change.Files)
                {
                    _body.Add(BuildFileRow(file));
                }
            }

            BuildNotes(session, change.Id);
        }

        /// <summary>
        /// The three review states as one row of toggles.
        /// </summary>
        /// <remarks>
        /// Clicking the state a change is already in clears it back to unreviewed, so a mis-click is
        /// undone by repeating it rather than by hunting for a "clear" affordance.
        /// </remarks>
        private VisualElement BuildReviewRow(ScratchpadSession session, ScratchpadChange change)
        {
            var row = ScratchpadStyles.Row();
            row.style.marginTop = 4;

            var current = session.ReviewOf(change.Id);

            row.Add(ReviewButton(session, change, ReviewState.Unreviewed, "Unreviewed", current,
                "Mark this change as not yet read."));

            row.Add(ReviewButton(session, change, ReviewState.Ok, "OK", current,
                "Read and accepted, with nothing to say about it. Counts towards this session being archivable."));

            row.Add(ReviewButton(session, change, ReviewState.CloserLook, "Closer look", current,
                "Something here is suspicious. A bookmark for a second pass — click again to clear it."));

            return row;
        }

        private Button ReviewButton(ScratchpadSession session, ScratchpadChange change, ReviewState state,
            string text, ReviewState current, string tooltip)
        {
            var selected = current == state;

            var button = new Button(() =>
            {
                session.SetReview(change.Id, selected ? ReviewState.Unreviewed : state);
                _window.Store.SaveNotes(session);
                _window.RebuildAfterEdit();
            })
            {
                text = text,
                tooltip = tooltip,
                style = { marginRight = 2, fontSize = 10 },
            };

            if (selected)
            {
                button.style.backgroundColor = ScratchpadStyles.ReviewColor(state);
                button.style.color = Color.black;
            }

            button.SetEnabled(!_window.ShowArchive);
            return button;
        }

        private VisualElement BuildFileRow(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

            var label = new Label(path)
            {
                style =
                {
                    color = asset != null ? ScratchpadStyles.Work : ScratchpadStyles.Dim,
                    fontSize = 10,
                    whiteSpace = WhiteSpace.Normal,
                    marginLeft = 4,
                },
                tooltip = asset != null
                    ? "Click to show this file in the Project window."
                    : "The handoff named this file, but it is not in the project. It may have been moved or deleted.",
            };

            if (asset != null)
            {
                label.RegisterCallback<MouseDownEvent>(_ => EditorGUIUtility.PingObject(asset));
            }

            return label;
        }

        #endregion

        #region Session

        private void BuildSession(ScratchpadSession session)
        {
            var title = ScratchpadStyles.Header(session.DisplayTitle);
            title.style.fontSize = 13;
            _body.Add(title);

            var stamp = ScratchpadStyles.Body($"{session.FeatureName} · {session.DisplayStamp}", dim: true);
            stamp.style.fontSize = 9;
            _body.Add(stamp);

            if (session.ParseError != null)
            {
                var error = ScratchpadStyles.Body($"This handoff would not parse: {session.ParseError}");
                error.style.color = ScratchpadStyles.Issue;
                error.style.marginTop = 4;
                error.tooltip = "The file is still on disk. Fix the syntax and hit Refresh.";
                _body.Add(error);
            }

            if (session.Archived)
            {
                var archived = ScratchpadStyles.Row();
                archived.style.marginTop = 4;

                archived.Add(ScratchpadStyles.IconButton(ScratchpadIcon.Unarchive, "Unarchive",
                    "Move this session back out of Archive/ so it can be annotated again.",
                    () =>
                    {
                        if (_window.Store.Unarchive(session))
                        {
                            _window.Refresh();
                        }
                    }));

                _body.Add(archived);
            }

            _body.Add(ScratchpadStyles.Separator());

            AppendField("Summary", session.Handoff.Summary);

            if (session.Handoff.Resolved.Count > 0)
            {
                _body.Add(ScratchpadStyles.Caption("Closed by this handoff"));

                var resolved = ScratchpadStyles.Body(string.Join(", ", session.Handoff.Resolved), dim: true);
                resolved.style.color = ScratchpadStyles.Settled;
                resolved.tooltip = "Notes this handoff said it addressed. They were closed on load.";
                _body.Add(resolved);
            }

            BuildTests(session);
            BuildPlanNext(session);
            BuildFollowUps(session);
            BuildNotes(session, string.Empty);
        }

        /// <summary>
        /// The feature's tests: what to run, a button to run it, and what happened last time.
        /// </summary>
        /// <remarks>
        /// Shown on the session pane because that is where you land when you pick a feature, though
        /// the filter and the history belong to the feature rather than to this session. Failures file
        /// themselves as issues on the newest session, which is the one whose changes most likely
        /// caused them.
        /// </remarks>
        private void BuildTests(ScratchpadSession session)
        {
            var feature = session.Feature;
            if (feature == null || feature.Archived)
            {
                return;
            }

            var log = _window.Store.LoadTestLog(feature);
            var tests = _window.Store.TestsFor(feature, log);

            _body.Add(ScratchpadStyles.Caption("Tests"));

            if (tests.Count == 0)
            {
                var empty = ScratchpadStyles.Body(
                    "No tests linked. Handoffs name them in their `tests` list, or add one below.", dim: true);

                empty.style.fontSize = 10;
                _body.Add(empty);
            }

            foreach (var name in tests)
            {
                _body.Add(BuildTestRow(feature, log, name));
            }

            var row = ScratchpadStyles.Row();
            row.style.marginTop = 2;

            var running = ScratchpadTestRunner.IsRunning;

            var run = ScratchpadStyles.IconButton(ScratchpadIcon.Refresh,
                running ? "Running…" : $"Run {tests.Count} test{(tests.Count == 1 ? string.Empty : "s")}",
                "Run these in EditMode. Anything that fails is filed as an issue on the newest session, " +
                "with its message and stack. A failure already filed is not filed twice.",
                () => ScratchpadTestRunner.Run(_window.Store, feature, _ => _window.Refresh()));

            run.SetEnabled(!running && tests.Count > 0);
            row.Add(run);

            row.Add(ScratchpadStyles.IconOnlyButton(ScratchpadIcon.Link,
                "Add a test to this feature, picked from the ones that actually exist.",
                () => ShowTestPicker(feature, log), ScratchpadStyles.Dim, 12f));

            _body.Add(row);

            foreach (var entry in log.Runs.Take(5))
            {
                _body.Add(BuildTestRunRow(entry));
            }
        }

        /// <summary>
        /// One linked test, with where it came from and a way to drop it.
        /// </summary>
        /// <remarks>
        /// A test declared by a handoff is excluded rather than deleted, because the handoff is not
        /// ours to edit and would put it straight back on the next refresh.
        /// </remarks>
        private VisualElement BuildTestRow(ScratchpadFeature feature, ScratchpadTestLog log, string name)
        {
            var row = ScratchpadStyles.Row();
            var declared = _window.Store.IsDeclaredByHandoff(feature, name);

            var label = ScratchpadStyles.Body(name, dim: true);
            label.style.fontSize = 9;
            label.style.flexGrow = 1;
            label.tooltip = declared
                ? "Named by one of this feature's handoffs."
                : "Added here by hand.";

            row.Add(label);

            row.Add(ScratchpadStyles.TextButton("×",
                declared
                    ? "Stop running this one. The handoff still names it, so this is remembered as an exclusion."
                    : "Remove this test from the feature.",
                () =>
                {
                    if (declared)
                    {
                        log.Excluded.Add(name);
                    }
                    else
                    {
                        log.Extra.RemoveAll(e => string.Equals(e?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                    }

                    _window.Store.SaveTestLog(feature, log);
                    Rebuild();
                }));

            return row;
        }

        /// <summary>
        /// Offers the project's real EditMode tests, so a linked name always matches something.
        /// </summary>
        /// <remarks>
        /// Grouped into submenus by namespace and fixture, because the flat list runs to hundreds and
        /// a menu that long is unusable. Namespaces are offered too, so a whole fixture can be linked
        /// with one click rather than test by test.
        /// </remarks>
        private void ShowTestPicker(ScratchpadFeature feature, ScratchpadTestLog log)
        {
            ScratchpadTestRunner.ListTests(names =>
            {
                var menu = new GenericMenu();

                if (names.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent("No EditMode tests found"));
                    menu.ShowAsContext();
                    return;
                }

                var linked = new HashSet<string>(_window.Store.TestsFor(feature, log),
                    StringComparer.OrdinalIgnoreCase);

                // The fixtures first — one click to take a whole class — then the individual tests
                // underneath them.
                foreach (var fixture in names
                             .Select(Fixture)
                             .Where(f => f.Length > 0)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var captured = fixture;
                    menu.AddItem(new GUIContent($"Whole fixture/{captured}"), linked.Contains(captured),
                        () => Link(feature, log, captured));
                }

                menu.AddSeparator(string.Empty);

                foreach (var name in names)
                {
                    var captured = name;
                    var fixture = Fixture(name);
                    var leaf = fixture.Length > 0 && name.Length > fixture.Length
                        ? name[(fixture.Length + 1)..]
                        : name;

                    menu.AddItem(new GUIContent($"{fixture}/{leaf}".Replace('(', '[').Replace(')', ']')),
                        linked.Contains(captured), () => Link(feature, log, captured));
                }

                menu.ShowAsContext();
            });
        }

        private static string Fixture(string fullName)
        {
            var lastDot = fullName.LastIndexOf('.');
            return lastDot < 0 ? string.Empty : fullName[..lastDot];
        }

        private void Link(ScratchpadFeature feature, ScratchpadTestLog log, string name)
        {
            log.Excluded.RemoveAll(e => string.Equals(e?.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (!_window.Store.TestsFor(feature, log)
                    .Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
            {
                log.Extra.Add(name);
            }

            _window.Store.SaveTestLog(feature, log);
            Rebuild();
        }

        private VisualElement BuildTestRunRow(ScratchpadTestRun entry)
        {
            var row = ScratchpadStyles.Row();
            row.style.marginTop = 1;

            var status = new Label(entry.IsGreen ? "PASS" : "FAIL")
            {
                style =
                {
                    color = entry.IsGreen ? ScratchpadStyles.Live : ScratchpadStyles.Issue,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 9,
                    width = 34,
                    flexShrink = 0,
                },
            };

            row.Add(status);

            var text = ScratchpadStyles.Body($"{entry.Summary} · {entry.When}", dim: true);
            text.style.fontSize = 9;
            text.style.flexGrow = 1;

            if (entry.Failures.Count > 0)
            {
                text.tooltip = string.Join("\n", entry.Failures);
            }

            row.Add(text);
            return row;
        }

        /// <summary>
        /// Planning the next piece of work on a feature that is already under way.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Collapsed by default, because most visits to this pane are to read a session rather than to
        /// start something. It sits beside the Tests section because both are feature-level tools
        /// rather than anything to do with the session being read.
        /// </para>
        /// <para>
        /// The prompt carries the feature's description as well as the new ask, so a chat planning an
        /// addition knows what it is adding to — the handoffs say what was done, not what the feature
        /// is for.
        /// </para>
        /// </remarks>
        private void BuildPlanNext(ScratchpadSession session)
        {
            var feature = session.Feature;
            if (feature == null || feature.Archived)
            {
                return;
            }

            var info = _window.Store.LoadFeatureInfo(feature);

            var copied = DisplayTime(info.PlanCopied);

            var foldout = new Foldout
            {
                // On the header, so a folded section still says whether what is inside has been sent.
                text = copied == null ? "Plan new work" : $"Plan new work — copied {copied}",
                value = false,
                tooltip = "Write what you want to do next and copy a prompt that asks for a plan and " +
                          "questions before any code, with this feature's context attached.",
            };

            foldout.style.marginTop = 8;
            foldout.style.fontSize = 10;

            var field = new TextField { multiline = true, value = info.PlanDraft };
            field.style.minHeight = 60;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.tooltip = "What you want to add to this feature. Kept, so you can reword it across " +
                            "several visits without retyping it.";

            // Dimmed once sent. The text is still there to re-copy or build on, but it stops reading
            // as something outstanding — which is the whole complaint about a draft that lingers.
            field.style.opacity = copied == null ? 1f : 0.55f;

            // Saved on focus loss rather than per keystroke: this writes a file, and a write per
            // character typed is a reimport per character typed.
            field.RegisterCallback<FocusOutEvent>(_ => CommitPlanDraft(feature, info, field.value));
            foldout.Add(field);

            var stamp = ScratchpadStyles.Body($"Copied {copied}. Editing it clears the stamp.", dim: true);
            stamp.style.fontSize = 9;
            stamp.style.color = ScratchpadStyles.Settled;
            stamp.style.display = copied == null ? DisplayStyle.None : DisplayStyle.Flex;
            foldout.Add(stamp);

            // The display follows the keystroke; the file still waits for focus loss. Without this the
            // draft stayed dimmed and labelled copied while you typed into it, which reads as the
            // stamp being broken rather than merely late.
            //
            // Compared against the saved draft rather than latched on first edit, so typing something
            // and undoing it puts the stamp back — the text really is the text that was sent again.
            if (copied != null)
            {
                field.RegisterValueChangedCallback(evt =>
                {
                    var unchanged = string.Equals(evt.newValue, info.PlanDraft, StringComparison.Ordinal);

                    field.style.opacity = unchanged ? 0.55f : 1f;
                    stamp.style.display = unchanged ? DisplayStyle.Flex : DisplayStyle.None;
                    foldout.text = unchanged ? $"Plan new work — copied {copied}" : "Plan new work";
                });
            }

            var row = ScratchpadStyles.Row();
            row.style.marginTop = 2;

            row.Add(ScratchpadStyles.IconButton(ScratchpadIcon.CopyContract,
                copied == null ? "Copy plan prompt" : "Copy again",
                "Copy a plan-first prompt for this work: what the feature is for, what you want next, " +
                "a request to ask questions before building, and the handoff contract.",
                () =>
                {
                    CommitPlanDraft(feature, info, field.value);

                    info.PlanCopied = ScratchpadPaths.Timestamp(DateTime.Now);
                    _window.Store.SaveFeatureInfo(feature, info);

                    _window.CopyPlan(feature, info.Description, info.PlanDraft);
                    Rebuild();
                }));

            foldout.Add(row);

            if (!string.IsNullOrWhiteSpace(info.Description))
            {
                var context = ScratchpadStyles.Body($"The feature's description goes with it: {info.Description.Trim()}",
                    dim: true);

                context.style.fontSize = 9;
                context.style.marginTop = 2;
                foldout.Add(context);
            }

            _body.Add(foldout);
        }

        private void CommitPlanDraft(ScratchpadFeature feature, ScratchpadFeatureInfo info, string value)
        {
            if (value == info.PlanDraft)
            {
                return;
            }

            var wasStamped = !string.IsNullOrEmpty(info.PlanCopied);

            info.PlanDraft = value;

            // The stamp says "this exact text has been sent", so a rewrite retires it. Keeping it
            // would mark unsent work as sent, which is the one thing worse than no stamp at all.
            info.PlanCopied = string.Empty;
            _window.Store.SaveFeatureInfo(feature, info);

            // The header and the dimming both key off the stamp, so they have to be redrawn — but
            // only when there was one, since every other edit changes nothing on screen.
            if (wasStamped)
            {
                Rebuild();
            }
        }

        /// <summary>An ISO timestamp as something readable, or null if there isn't one.</summary>
        private static string DisplayTime(string stored) =>
            !string.IsNullOrWhiteSpace(stored) && DateTime.TryParse(stored, out var parsed)
                ? parsed.ToString("yyyy-MM-dd HH:mm")
                : null;

        /// <summary>
        /// The work the handoff said it left behind, offered rather than filed.
        /// </summary>
        /// <remarks>
        /// A proposal has to be accepted before it becomes a Work note, which keeps the outstanding
        /// list something you assembled rather than something that accumulated. Dismissed proposals
        /// stay on screen as dismissed — the offer was still made, and hiding it would invite the
        /// next session to make it again.
        /// </remarks>
        private void BuildFollowUps(ScratchpadSession session)
        {
            if (session.Handoff.FollowUps.Count == 0)
            {
                return;
            }

            _body.Add(ScratchpadStyles.Caption("Proposed follow-ups"));

            foreach (var followUp in session.Handoff.FollowUps)
            {
                var state = session.StateOf(followUp.Id);

                var card = ScratchpadStyles.Column();
                ScratchpadStyles.SetPadding(card, 6);
                card.style.backgroundColor = ScratchpadStyles.Raised;
                card.style.marginBottom = 4;
                // Only a dismissed proposal recedes. An accepted one is live work you can still reply
                // to, so dimming it would say the opposite of what the Reply button offers.
                card.style.opacity = state == FollowUpState.Dismissed ? 0.55f : 1f;

                card.tooltip = state switch
                {
                    FollowUpState.Accepted => "Accepted. There is a Work note for this.",
                    FollowUpState.Dismissed => "Dismissed. Kept visible so the same thing is not proposed again.",
                    _ => "Proposed by the handoff. It is not on your list until you accept it.",
                };

                var heading = ScratchpadStyles.Row();
                heading.Add(ScratchpadStyles.Pill(state.ToString(),
                    state == FollowUpState.Accepted ? ScratchpadStyles.Work : ScratchpadStyles.Dim));

                var titleLabel = new Label(followUp.Title)
                {
                    style =
                    {
                        color = ScratchpadStyles.Text,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        flexGrow = 1,
                        whiteSpace = WhiteSpace.Normal,
                    },
                };

                heading.Add(titleLabel);
                card.Add(heading);

                if (!string.IsNullOrWhiteSpace(followUp.Detail))
                {
                    var detail = ScratchpadStyles.Body(followUp.Detail);
                    detail.style.fontSize = 10;
                    detail.style.marginTop = 2;
                    card.Add(detail);
                }

                foreach (var note in session.NotesForFollowUp(followUp.Id))
                {
                    var reply = BuildNoteCard(session, note);
                    reply.style.marginLeft = 10;
                    reply.style.marginTop = 4;
                    card.Add(reply);
                }

                // Reply outlives the decision, accept and dismiss do not. Accepted work is the case
                // that still needs talking about — which route to take, what the constraint is — and
                // that conversation does not end the moment it goes on the list.
                var canReply = state != FollowUpState.Dismissed && !_window.ShowArchive;

                if (state == FollowUpState.Proposed && !_window.ShowArchive || canReply)
                {
                    var buttons = ScratchpadStyles.Row();
                    buttons.style.marginTop = 4;

                    if (state == FollowUpState.Proposed && !_window.ShowArchive)
                    {
                        buttons.Add(ScratchpadStyles.TextButton("Accept as Work",
                            "Create a Work note for this, so it goes into prompts and can be closed like any other note.",
                            () =>
                            {
                                _window.Store.AcceptFollowUp(session, followUp);
                                _window.RebuildAfterEdit();
                            }));

                        buttons.Add(ScratchpadStyles.TextButton("Dismiss",
                            "Decline it. The proposal stays visible as dismissed rather than disappearing.",
                            () =>
                            {
                                _window.Store.DismissFollowUp(session, followUp);
                                _window.RebuildAfterEdit();
                            }));
                    }

                    if (canReply)
                    {
                        buttons.Add(ScratchpadStyles.TextButton("Reply",
                            state == FollowUpState.Accepted
                                ? "Say something about the work you have taken on — which route, or what " +
                                  "you want it to do. It goes back quoting the proposal."
                                : "Answer this proposal. A follow-up that offers several routes needs you " +
                                  "to say which one, and that answer goes back with the prompt.",
                            () =>
                            {
                                _replyingTo = _replyingTo == followUp.Id ? null : followUp.Id;
                                _pendingReply = string.Empty;
                                Rebuild();
                            }));
                    }

                    card.Add(buttons);
                }

                if (_replyingTo == followUp.Id && canReply)
                {
                    card.Add(BuildReplyComposer(session, followUp));
                }

                _body.Add(card);
            }
        }

        /// <summary>
        /// A comment box on one proposed follow-up.
        /// </summary>
        /// <remarks>
        /// A proposal often offers two or three routes, and accept-or-dismiss cannot say which one you
        /// want. The reply is stored as an ordinary note rather than as a field on the proposal, which
        /// means it inherits everything notes already do: it goes into the prompt, it can be resolved
        /// by id from a later handoff, and it survives the proposal being accepted or dismissed.
        /// </remarks>
        private VisualElement BuildReplyComposer(ScratchpadSession session, ScratchpadFollowUp followUp)
        {
            var composer = ScratchpadStyles.Column();
            composer.style.marginTop = 4;
            composer.style.marginLeft = 10;

            var field = new TextField { multiline = true, value = _pendingReply };
            field.style.minHeight = 40;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.tooltip = "Your answer to this proposal — which route to take, or what would change " +
                            "your mind. It goes back quoting the proposal it is about.";

            field.RegisterValueChangedCallback(evt => _pendingReply = evt.newValue);
            composer.Add(field);

            var send = ScratchpadStyles.IconButton(ScratchpadIcon.AddNote, "Reply",
                "File this as a comment on the proposal.",
                () =>
                {
                    if (string.IsNullOrWhiteSpace(field.value))
                    {
                        return;
                    }

                    _window.Store.AddNote(session, string.Empty, NoteKind.Comment, field.value,
                        followUpId: followUp.Id);

                    _pendingReply = string.Empty;
                    _replyingTo = null;
                    _window.RebuildAfterEdit();
                });

            send.style.marginTop = 2;
            composer.Add(send);
            return composer;
        }

        #endregion

        #region Notes

        private void BuildNotes(ScratchpadSession session, string changeId)
        {
            var notes = session.NotesFor(changeId).ToList();

            _body.Add(ScratchpadStyles.Caption(changeId.Length > 0 ? "Notes" : "Notes on the session"));

            if (notes.Count == 0)
            {
                var empty = ScratchpadStyles.Body("Nothing yet.", dim: true);
                empty.style.fontSize = 10;
                _body.Add(empty);
            }

            foreach (var note in notes)
            {
                _body.Add(BuildNoteCard(session, note));
            }

            if (!_window.ShowArchive)
            {
                _body.Add(BuildComposer(session, changeId));
            }
        }

        private VisualElement BuildNoteCard(ScratchpadSession session, ScratchpadNote note)
        {
            var card = ScratchpadStyles.Column();
            ScratchpadStyles.SetPadding(card, 6);
            card.style.backgroundColor = ScratchpadStyles.Raised;
            card.style.marginBottom = 4;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = ScratchpadStyles.KindColor(note.Kind);
            card.style.opacity = note.IsOutstanding ? 1f : 0.55f;

            var heading = ScratchpadStyles.Row();

            var pill = ScratchpadStyles.Pill(note.Kind.ToString(), ScratchpadStyles.KindColor(note.Kind));
            pill.tooltip = note.Kind switch
            {
                NoteKind.Issue => "Something wrong that needs fixing. Issues go first in the prompt.",
                NoteKind.Work => "New work to pick up on returning to this feature.",
                _ => "An observation. Feedback, not a defect.",
            };

            heading.Add(pill);

            var id = new Label(note.Id)
            {
                style = { color = ScratchpadStyles.Dim, fontSize = 9 },
                tooltip = "Quote this id back in a handoff's resolved list to close the note.",
            };

            heading.Add(id);
            heading.Add(ScratchpadStyles.Spacer());

            // The one word on the card that changes meaning without changing length, so it carries
            // the colour: green live, gold handed over, grey done.
            var status = new Label(note.Status.ToString())
            {
                style =
                {
                    color = ScratchpadStyles.StatusColor(note.Status),
                    fontSize = 9,
                    unityFontStyleAndWeight = FontStyle.Bold,
                },
                tooltip = note.Status switch
                {
                    NoteStatus.Open => "Written, not yet handed to anyone.",
                    NoteStatus.Sent => $"Included in a prompt on {note.Sent}. Still outstanding until something closes it.",
                    NoteStatus.Resolved => "Closed — either by hand, or by a handoff naming its id.",
                    _ => "Closed without being acted on.",
                },
            };

            heading.Add(status);
            card.Add(heading);

            var body = ScratchpadStyles.Body(note.Body);
            body.style.marginTop = 2;
            card.Add(body);

            if (!string.IsNullOrWhiteSpace(note.Context))
            {
                var context = ScratchpadStyles.Body(note.Context, dim: true);
                context.style.fontSize = 9;
                context.tooltip = "What was selected in the editor when this note was captured.";
                card.Add(context);
            }

            if (!string.IsNullOrWhiteSpace(note.Console))
            {
                var foldout = new Foldout
                {
                    text = "Console",
                    value = false,
                    tooltip = "The console entry captured with this note. It goes into the prompt too.",
                };

                foldout.style.fontSize = 10;

                var console = ScratchpadStyles.Body(note.Console, dim: true);
                console.style.fontSize = 9;
                foldout.Add(console);
                card.Add(foldout);
            }

            if (!_window.ShowArchive)
            {
                card.Add(BuildNoteActions(session, note));
            }

            return card;
        }

        private VisualElement BuildNoteActions(ScratchpadSession session, ScratchpadNote note)
        {
            var row = ScratchpadStyles.Row();
            row.style.marginTop = 4;

            if (note.IsOutstanding)
            {
                row.Add(ScratchpadStyles.TextButton("Resolve",
                    "Close this note yourself, without waiting for a handoff to name its id.",
                    () =>
                    {
                        _window.Store.SetNoteStatus(session, note, NoteStatus.Resolved);
                        _window.RebuildAfterEdit();
                    }));

                row.Add(ScratchpadStyles.TextButton("Dismiss",
                    "Close it without acting on it. It stops going into prompts.",
                    () =>
                    {
                        _window.Store.SetNoteStatus(session, note, NoteStatus.Dismissed);
                        _window.RebuildAfterEdit();
                    }));
            }
            else
            {
                row.Add(ScratchpadStyles.TextButton("Reopen",
                    "Put it back on the outstanding list, so the next prompt carries it again.",
                    () =>
                    {
                        _window.Store.SetNoteStatus(session, note, NoteStatus.Open);
                        _window.RebuildAfterEdit();
                    }));
            }

            row.Add(BuildAttachToChangeButton(session, note));
            row.Add(ScratchpadStyles.Spacer());

            row.Add(ScratchpadStyles.TextButton("Delete",
                "Remove the note entirely. This is the one action here with no undo.",
                () =>
                {
                    // Asked about, because a deleted note is the one thing here with no undo: the
                    // file is rewritten immediately and nothing else remembers what it said.
                    if (EditorUtility.DisplayDialog("Delete note",
                            $"Delete {note.Id}? This cannot be undone.", "Delete", "Cancel"))
                    {
                        _window.Store.RemoveNote(session, note);
                        _window.RebuildAfterEdit();
                    }
                }));

            return row;
        }

        /// <summary>
        /// Moves a note onto one of the session's changes, or back off onto the session.
        /// </summary>
        /// <remarks>
        /// Quick capture can only file a loose note — a popup is the wrong place to render a session
        /// tree — so this is where one gets put in its proper place afterwards. Hidden on a session
        /// with no changes, where every possible destination is the one it is already on.
        /// </remarks>
        private VisualElement BuildAttachToChangeButton(ScratchpadSession session, ScratchpadNote note)
        {
            if (session.Changes.Count == 0)
            {
                return new VisualElement();
            }

            var button = ScratchpadStyles.IconOnlyButton(ScratchpadIcon.Link,
                "Attach this note to one of the session's changes, or move it back to the session.",
                () =>
                {
                    var menu = new GenericMenu();

                    menu.AddItem(new GUIContent("The session as a whole"),
                        string.IsNullOrEmpty(note.ChangeId),
                        () => Move(session, note, string.Empty));

                    menu.AddSeparator(string.Empty);

                    foreach (var change in session.Changes)
                    {
                        var captured = change;
                        var label = string.IsNullOrWhiteSpace(change.Title) ? change.Id : change.Title;

                        menu.AddItem(new GUIContent(label.Replace('/', '∕')),
                            note.ChangeId == change.Id,
                            () => Move(session, note, captured.Id));
                    }

                    menu.ShowAsContext();
                },
                ScratchpadStyles.Dim, 12f);

            return button;
        }

        private void Move(ScratchpadSession session, ScratchpadNote note, string changeId)
        {
            _window.Store.MoveNote(session, note, changeId);

            // Follow the note to wherever it went, so the move is visibly a move rather than the
            // note apparently vanishing off the pane you were looking at.
            _window.SelectChange(session, string.IsNullOrEmpty(changeId) ? null : session.FindChange(changeId));
            _window.RebuildAfterEdit();
        }

        #endregion

        #region Composer

        private VisualElement BuildComposer(ScratchpadSession session, string changeId)
        {
            var composer = ScratchpadStyles.Column();
            composer.style.marginTop = 6;

            var kinds = ScratchpadStyles.Row();
            foreach (NoteKind kind in Enum.GetValues(typeof(NoteKind)))
            {
                kinds.Add(KindButton(kind));
            }

            kinds.Add(ScratchpadStyles.Spacer());
            kinds.Add(BuildAttachConsoleButton());
            kinds.Add(BuildPickElementButton());
            composer.Add(kinds);

            // Seeded from the draft rather than starting empty. Every rebuild destroys this field —
            // and switching kind, picking an element and attaching a console entry all rebuild — so
            // without somewhere outside the field to keep the text, half a note is lost to a click on
            // the very buttons that are meant to describe it.
            var field = new TextField { multiline = true, value = _pendingBody };
            field.style.minHeight = 48;
            field.style.marginTop = 2;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.tooltip = changeId.Length > 0
                ? "What you want to say about this change. Goes into the next prompt with the change quoted around it."
                : "A note about the session rather than any one change.";

            field.RegisterValueChangedCallback(evt => _pendingBody = evt.newValue);
            composer.Add(field);

            if (_pendingConsole != null)
            {
                var attached = ScratchpadStyles.Body("A console entry is attached to this note.", dim: true);
                attached.style.fontSize = 9;
                attached.style.color = ScratchpadStyles.InFlight;
                composer.Add(attached);
            }

            foreach (var context in _pendingContexts.ToList())
            {
                composer.Add(BuildPickedRow(context));
            }

            if (ScratchpadElementPicker.IsPicking)
            {
                var hint = ScratchpadStyles.Body(
                    "Pick mode is on — click controls to add them, Escape or the crosshair to stop.", dim: true);

                hint.style.fontSize = 9;
                hint.style.color = ScratchpadStyles.InFlight;
                composer.Add(hint);
            }

            var add = ScratchpadStyles.IconButton(ScratchpadIcon.AddNote, $"Add {_composerKind}",
                $"File this as a {_composerKind} on " +
                (changeId.Length > 0 ? "this change." : "the session."),
                () =>
                {
                    if (string.IsNullOrWhiteSpace(field.value) && _pendingConsole == null &&
                        _pendingContexts.Count == 0)
                    {
                        return;
                    }

                    _window.Store.AddNote(session, changeId, _composerKind, field.value,
                        source: _pendingConsole != null ? NoteSource.Console : NoteSource.Manual,
                        context: string.Join("\n", _pendingContexts), console: _pendingConsole);

                    _pendingBody = string.Empty;
                    _pendingConsole = null;
                    _pendingContexts.Clear();
                    ScratchpadElementPicker.Cancel();
                    _window.RebuildAfterEdit();
                });

            add.style.marginTop = 2;
            composer.Add(add);
            return composer;
        }

        private Button KindButton(NoteKind kind)
        {
            var selected = _composerKind == kind;

            var button = new Button(() =>
            {
                _composerKind = kind;
                Rebuild();
            })
            {
                text = kind.ToString(),
                tooltip = kind switch
                {
                    NoteKind.Issue => "Something wrong that needs fixing. Issues lead the prompt.",
                    NoteKind.Work => "New work to pick up later. Neither a defect nor an opinion.",
                    _ => "An observation on the change. Feedback, not a defect.",
                },
                style = { fontSize = 10, marginRight = 2 },
            };

            if (selected)
            {
                button.style.backgroundColor = ScratchpadStyles.KindColor(kind);
                button.style.color = Color.black;
            }

            return button;
        }

        /// <summary>
        /// Point at a control anywhere in the editor and attach what it is to the note.
        /// </summary>
        /// <remarks>
        /// For the notes that are about a control rather than a file — a misaligned icon, a button in
        /// the wrong pane — where naming the thing in prose is both laborious and ambiguous.
        /// </remarks>
        private Button BuildPickElementButton()
        {
            var picking = ScratchpadElementPicker.IsPicking;

            var button = ScratchpadStyles.IconOnlyButton(ScratchpadIcon.Pick,
                picking
                    ? "Pick mode is on. Click controls to add them to this note; click here, press " +
                      "Escape, or right-click to stop."
                    : "Point at controls in any editor window to attach what they are to this note. " +
                      "Stays on so you can pick several.",
                () => ScratchpadElementPicker.Toggle(OnElementPicked),
                picking ? Color.black : ScratchpadStyles.Text, 12f);

            // Lit while it is on, which is the only signal that the editor is waiting for a click
            // somewhere other than where you are looking.
            if (picking)
            {
                button.style.backgroundColor = ScratchpadStyles.InFlight;
            }

            return button;
        }

        private void OnElementPicked(string description)
        {
            // Picking the same control twice is a mis-click, not an instruction to record it twice.
            if (!_pendingContexts.Contains(description))
            {
                _pendingContexts.Add(description);
            }

            Rebuild();
        }

        private VisualElement BuildPickedRow(string context)
        {
            var row = ScratchpadStyles.Row();
            row.style.marginTop = 1;

            var label = ScratchpadStyles.Body(context, dim: true);
            label.style.fontSize = 9;
            label.style.color = ScratchpadStyles.InFlight;
            label.style.flexGrow = 1;
            row.Add(label);

            row.Add(ScratchpadStyles.TextButton("×", "Remove this control from the note.", () =>
            {
                _pendingContexts.Remove(context);
                Rebuild();
            }));

            return row;
        }

        /// <summary>
        /// Offers the recent console entries, so an error can be quoted exactly rather than retyped.
        /// </summary>
        private Button BuildAttachConsoleButton()
        {
            var button = ScratchpadStyles.IconButton(ScratchpadIcon.Console,
                _pendingConsole == null ? "Attach console" : "Console attached",
                "Attach a recent console entry — message and stack — to the note you are writing.",
                () =>
                {
                    var menu = ScratchpadConsoleMenu.Build(detail =>
                    {
                        _pendingConsole = detail;
                        Rebuild();
                    }, _pendingConsole != null);

                    menu.ShowAsContext();
                },
                12f);

            return button;
        }

        private void AppendField(string caption, string value, Color? color = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var heading = ScratchpadStyles.Caption(caption);
            heading.tooltip = caption switch
            {
                "Rationale" => "Why the assistant did it this way, and what it rejected.",
                "Risk" => "What the assistant said is untested, uncertain, or deliberately cut.",
                _ => "What the assistant says changed.",
            };

            _body.Add(heading);

            var label = ScratchpadStyles.Body(value.Trim());
            if (color.HasValue)
            {
                label.style.color = color.Value;
            }

            _body.Add(label);
        }

        #endregion
    }
}
