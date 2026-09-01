using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Reviews the work an assistant handed off, and turns what you say about it into the next prompt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three panes: the features on disk, that feature's sessions and the changes in them, and one
    /// change read in full with its notes. The loop it serves runs the other way round — the
    /// assistant writes a handoff file, you read and annotate it here, and <c>Copy Prompt</c> hands
    /// the annotations back with enough of the change quoted that a fresh chat can act on them.
    /// </para>
    /// <para>
    /// It only ever reads from disk when asked. There is no file watcher and no rescan on focus,
    /// which is the difference between a tool that can be left open mid-review and one that
    /// rearranges itself while you are typing into it.
    /// </para>
    /// </remarks>
    internal sealed class ScratchpadWindow : EditorWindow
    {
        private readonly ScratchpadStore _store = new();

        private ScratchpadFeatureRail _rail;
        private ScratchpadSessionList _sessions;
        private ScratchpadDetailView _detail;
        private Label _status;

        // Selection is held twice: as the live objects the views compare against, and as the keys
        // that survive a refresh, which throws every one of those objects away and rebuilds it.
        private string _featureKey;
        private string _sessionKey;
        private string _changeKey;

        public ScratchpadStore Store => _store;
        public ScratchpadFeature Feature { get; private set; }
        public ScratchpadSession Session { get; private set; }
        public ScratchpadChange Change { get; private set; }
        public bool ShowArchive { get; private set; }

        [MenuItem("Vapor/Scratchpad", priority = -1)]
        public static ScratchpadWindow Open()
        {
            var window = GetWindow<ScratchpadWindow>();
            window.titleContent = new GUIContent("Scratchpad");
            window.minSize = new Vector2(640, 380);
            return window;
        }

        public void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = ScratchpadStyles.Background;
            rootVisualElement.Add(BuildToolbar());

            _rail = new ScratchpadFeatureRail(this);
            _sessions = new ScratchpadSessionList(this);
            _detail = new ScratchpadDetailView(this);

            var inner = new TwoPaneSplitView(0, 320, TwoPaneSplitViewOrientation.Horizontal);
            inner.Add(_sessions);
            inner.Add(_detail);

            var outer = new TwoPaneSplitView(0, 180, TwoPaneSplitViewOrientation.Horizontal);
            outer.Add(_rail);
            outer.Add(inner);
            outer.style.flexGrow = 1;
            rootVisualElement.Add(outer);

            _status = new Label
            {
                style =
                {
                    color = ScratchpadStyles.Dim,
                    fontSize = 9,
                    paddingLeft = 6,
                    paddingTop = 2,
                    paddingBottom = 2,
                    backgroundColor = ScratchpadStyles.Panel,
                },
            };

            rootVisualElement.Add(_status);

            Refresh();
        }

        #region Toolbar

        private VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar();

            toolbar.Add(ToolbarIconButton(ScratchpadIcon.Refresh, "Refresh",
                "Re-read the scratchpad folder. Nothing is picked up until you do — there is no file " +
                "watcher, so the window never rearranges itself while you are reading.",
                Refresh));

            var prompt = ToolbarIconButton(ScratchpadIcon.CopyPrompt, "Copy Prompt",
                "Copy this session's outstanding notes, each with the change it is about quoted in " +
                "full, ready to paste into a chat. Right-click to widen it to the whole feature.",
                () => CopyPrompt(PromptScope.Session));

            prompt.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("This session", _ => CopyPrompt(PromptScope.Session));
                evt.menu.AppendAction("All open in this feature", _ => CopyPrompt(PromptScope.Feature));
            }));

            toolbar.Add(prompt);

            var contract = ToolbarIconButton(ScratchpadIcon.CopyContract, "Copy Contract",
                "Copy the instructions for writing a handoff — rules, a commented template, the exact " +
                "path, and everything still open here. Right-click for the short version.",
                () => CopyContract(full: true));

            contract.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Full (spec and template)", _ => CopyContract(full: true));
                evt.menu.AppendAction("Short reminder", _ => CopyContract(full: false));

                evt.menu.AppendAction(
                    Feature is { Sessions: { Count: 0 } } ? "Kickoff for a new feature" : "Plan the next piece of work",
                    _ => CopyPlanningPromptForSelected(),
                    Feature != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }));

            toolbar.Add(contract);

            toolbar.Add(new VisualElement { style = { flexGrow = 1 } });

            var archive = new ToolbarToggle
            {
                value = false,
                tooltip = "Browse sessions that have been archived. They are read-only until unarchived.",
            };

            ScratchpadStyles.CenterContent(archive);
            archive.Add(ScratchpadIcons.WithLabel(ScratchpadIcon.Archive, "Archive", ScratchpadStyles.Text, 13f));
            archive.RegisterValueChangedCallback(evt =>
            {
                ShowArchive = evt.newValue;
                Feature = null;
                Session = null;
                Change = null;
                _featureKey = _sessionKey = _changeKey = null;
                SelectFirstAvailable();
                RebuildAll();
            });

            toolbar.Add(archive);

            var settings = new ToolbarButton(ShowSettings)
            {
                tooltip = "Auto-archive, how long a finished session lingers, how many sessions open " +
                          "expanded, and a shortcut to the folder itself.",
            };

            ScratchpadStyles.CenterContent(settings);
            settings.Add(ScratchpadIcons.Create(ScratchpadIcon.Settings, ScratchpadStyles.Text, 13f));
            toolbar.Add(settings);
            return toolbar;
        }

        private static ToolbarButton ToolbarIconButton(ScratchpadIcon icon, string text, string tooltip,
            Action action)
        {
            var button = new ToolbarButton(action) { tooltip = tooltip };
            ScratchpadStyles.CenterContent(button);
            button.Add(ScratchpadIcons.WithLabel(icon, text, ScratchpadStyles.Text, 13f));
            return button;
        }

        private void ShowSettings()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Auto-archive finished sessions"), ScratchpadSettings.AutoArchive,
                () => ScratchpadSettings.AutoArchive = !ScratchpadSettings.AutoArchive);

            foreach (var hours in new[] { 6, 12, 24, 72 })
            {
                var value = hours;
                menu.AddItem(new GUIContent($"Archive after/{hours} hours"),
                    ScratchpadSettings.ArchiveHours == hours,
                    () => ScratchpadSettings.ArchiveHours = value);
            }

            foreach (var count in new[] { 1, 3, 5, 10 })
            {
                var value = count;
                menu.AddItem(new GUIContent($"Sessions expanded/{count}"),
                    ScratchpadSettings.ExpandedSessions == count,
                    () =>
                    {
                        ScratchpadSettings.ExpandedSessions = value;
                        RebuildAll();
                    });
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Show the scratchpad folder"), false, () =>
            {
                Directory.CreateDirectory(ScratchpadPaths.Root);
                EditorUtility.RevealInFinder(ScratchpadPaths.Root);
            });

            menu.ShowAsContext();
        }

        #endregion

        #region Refresh and selection

        public void Refresh()
        {
            _store.Refresh();
            ReselectFromKeys();
            RebuildAll();

            // This goes first because it is the one line here about something you just did rather than
            // about the state of the folder — and an old finished session sitting in the live list
            // otherwise looks like the archive rule is broken.
            var keptOut = _store.LastKeptOut;
            if (keptOut.Count > 0)
            {
                SetStatus($"Kept {string.Join(", ", keptOut)} out of the archive because you unarchived " +
                          "it. It will be filed again after a script reload.",
                    ScratchpadStyles.InFlight);
            }
            else if (_store.LastArchived.Count > 0)
            {
                SetStatus($"Archived {_store.LastArchived.Count} finished " +
                          $"{(_store.LastArchived.Count == 1 ? "session" : "sessions")}.");
            }
            else
            {
                SetStatus($"{_store.Features.Count} " +
                          $"{(_store.Features.Count == 1 ? "feature" : "features")}, " +
                          $"{_store.Features.Sum(f => f.OpenItems)} open.");
            }
        }

        /// <summary>
        /// Finds what was selected again after a refresh replaced every object with a new one.
        /// </summary>
        /// <remarks>
        /// Falling back down the chain — change to session to feature to whatever is first — means a
        /// refresh that arrives with a new handoff, or one that archives what you were reading, still
        /// lands somewhere sensible instead of on an empty pane.
        /// </remarks>
        private void ReselectFromKeys()
        {
            var features = ShowArchive ? _store.ArchivedFeatures : _store.Features;

            Feature = features.FirstOrDefault(f =>
                string.Equals(f.Name, _featureKey, StringComparison.OrdinalIgnoreCase));

            if (Feature == null)
            {
                SelectFirstAvailable();
                return;
            }

            Session = Feature.Sessions.FirstOrDefault(s => s.Stamp == _sessionKey) ?? Feature.Newest;
            Change = _changeKey == null ? null : Session?.FindChange(_changeKey);
            WriteKeys();
        }

        private void SelectFirstAvailable()
        {
            var features = ShowArchive ? _store.ArchivedFeatures : _store.Features;

            // The feature the window was last on is the one quick capture files onto, so preferring it
            // keeps the two halves of the tool pointing at the same place.
            Feature = features.FirstOrDefault(f =>
                          string.Equals(f.Name, ScratchpadSettings.LastFeature, StringComparison.OrdinalIgnoreCase))
                      ?? features.FirstOrDefault();

            Session = Feature?.Newest;
            Change = null;
            WriteKeys();
        }

        public void SelectFeature(ScratchpadFeature feature)
        {
            Feature = feature;
            Session = feature?.Newest;
            Change = null;
            WriteKeys();

            if (feature != null && !ShowArchive)
            {
                ScratchpadSettings.LastFeature = feature.Name;
            }

            RebuildAll();
        }

        public void SelectSession(ScratchpadSession session)
        {
            Session = session;
            Feature = session?.Feature ?? Feature;
            Change = null;
            WriteKeys();
            RebuildSelection();
        }

        public void SelectChange(ScratchpadSession session, ScratchpadChange change)
        {
            Session = session;
            Feature = session?.Feature ?? Feature;
            Change = change;
            WriteKeys();
            RebuildSelection();
        }

        private void WriteKeys()
        {
            _featureKey = Feature?.Name;
            _sessionKey = Session?.Stamp;
            _changeKey = Change?.Id;
        }

        #endregion

        #region Rebuilding

        /// <summary>
        /// Rebuilds the panes wholesale rather than patching the rows that changed.
        /// </summary>
        /// <remarks>
        /// These lists are tens of rows, not thousands, and a full rebuild cannot leave a row showing
        /// a count or a state that has moved on underneath it. Incremental updates would buy nothing
        /// here except a class of bug that is unpleasant to find.
        /// </remarks>
        public void RebuildAll()
        {
            _rail?.Rebuild();
            _sessions?.Rebuild();
            _detail?.Rebuild();
        }

        private void RebuildSelection()
        {
            _sessions?.Rebuild();
            _detail?.Rebuild();
        }

        /// <summary>After a note or state change: counts move, so the rail has to move with them.</summary>
        public void RebuildAfterEdit() => RebuildAll();


        private void SetStatus(string text, Color? color = null)
        {
            if (_status == null)
            {
                return;
            }

            _status.text = text;
            _status.style.color = color ?? ScratchpadStyles.Dim;
        }

        #endregion

        #region Clipboard

        private void CopyPrompt(PromptScope scope)
        {
            if (Feature == null)
            {
                SetStatus("Nothing selected.");
                return;
            }

            var notes = ScratchpadPromptBuilder.Collect(Feature, Session, scope);
            if (notes.Count == 0)
            {
                SetStatus("Nothing outstanding to copy.");
                return;
            }

            EditorGUIUtility.systemCopyBuffer = ScratchpadPromptBuilder.Build(Feature, Session, scope);

            // Stamped only once the text is actually on the clipboard, so a failed copy does not leave
            // notes claiming to have been handed over.
            var now = ScratchpadPaths.Timestamp(DateTime.Now);
            var stamped = new HashSet<ScratchpadNote>(notes.Where(n => n.Status == NoteStatus.Open));

            foreach (var note in stamped)
            {
                note.Status = NoteStatus.Sent;
                note.Sent = now;
            }

            foreach (var session in Feature.Sessions.Where(s => s.Notes.Notes.Any(stamped.Contains)))
            {
                session.NotesDirty = true;
                _store.SaveNotes(session);
            }

            var description = ScratchpadPromptBuilder.Describe(Feature, Session, scope);
            Debug.Log(description);
            SetStatus(description.Replace("[Scratchpad] ", string.Empty));
            RebuildAll();
        }

        private void CopyContract(bool full)
        {
            var now = DateTime.Now;

            EditorGUIUtility.systemCopyBuffer = full
                ? ScratchpadContractBuilder.BuildFull(Feature, now)
                : ScratchpadContractBuilder.BuildShort(Feature, now);

            var where = Feature != null ? $"\"{Feature.Name}\"" : "a new feature";
            SetStatus($"Copied the {(full ? "full" : "short")} handoff contract for {where}.");
        }

        /// <summary>
        /// The planning prompt for whatever is selected, in whichever of its two shapes fits.
        /// </summary>
        /// <remarks>
        /// A feature with work in it is not being started, it is being added to, and the two prompts
        /// say different things. Choosing here rather than offering both keeps the menu one item and
        /// makes it impossible to paste the wrong one.
        /// </remarks>
        private void CopyPlanningPromptForSelected()
        {
            if (Feature == null)
            {
                return;
            }

            var info = _store.LoadFeatureInfo(Feature);

            if (Feature.Sessions.Count == 0)
            {
                CopyKickoff(Feature, info.Description);
            }
            else
            {
                CopyPlan(Feature, info.Description, info.PlanDraft);
            }
        }

        /// <summary>Copies the plan-first prompt for the next piece of work on a live feature.</summary>
        public void CopyPlan(ScratchpadFeature feature, string description, string ask)
        {
            feature ??= Feature;
            if (feature == null)
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer =
                ScratchpadContractBuilder.BuildPlan(feature, description, ask, DateTime.Now);

            SetStatus(string.IsNullOrWhiteSpace(ask)
                    ? $"Copied a plan prompt for \"{feature.Name}\" — fill in the work before sending."
                    : $"Copied a plan prompt for \"{feature.Name}\".",
                string.IsNullOrWhiteSpace(ask) ? ScratchpadStyles.InFlight : null);
        }

        /// <summary>
        /// Copies the prompt that starts a feature: plan and questions first, contract underneath.
        /// </summary>
        public void CopyKickoff(ScratchpadFeature feature, string description)
        {
            feature ??= Feature;
            if (feature == null)
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer =
                ScratchpadContractBuilder.BuildKickoff(feature, description, DateTime.Now);

            SetStatus(string.IsNullOrWhiteSpace(description)
                ? $"Copied the kickoff prompt for \"{feature.Name}\" — fill in what it should do before sending."
                : $"Copied the kickoff prompt for \"{feature.Name}\".",
                string.IsNullOrWhiteSpace(description) ? ScratchpadStyles.InFlight : null);
        }

        #endregion
    }
}
