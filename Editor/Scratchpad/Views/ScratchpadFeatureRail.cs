using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// The left rail: one row per feature folder, with what is still owed on it.
    /// </summary>
    /// <remarks>
    /// A feature has no file of its own — it is a directory — so this list is a directory listing with
    /// counts, and creating or renaming one creates or renames the folder. Both happen in place, on
    /// the row itself: the rail is narrow enough that a labelled field below it left almost no room
    /// for the text, and put the thing being named nowhere near the name.
    /// </remarks>
    internal sealed class ScratchpadFeatureRail : VisualElement
    {
        /// <summary>What a new feature is called before you have said what it is.</summary>
        private const string DefaultName = "New Feature";

        private readonly ScratchpadWindow _window;
        private readonly VisualElement _list;

        /// <summary>The feature whose row is currently a text field rather than a label.</summary>
        private ScratchpadFeature _renaming;

        public ScratchpadFeatureRail(ScratchpadWindow window)
        {
            _window = window;

            style.flexGrow = 1;
            style.backgroundColor = ScratchpadStyles.Panel;
            style.minWidth = 140;

            Add(BuildHeader());

            var scroll = new ScrollView { style = { flexGrow = 1 } };
            _list = new VisualElement();
            scroll.Add(_list);
            Add(scroll);

            Add(ScratchpadStyles.Separator());
            Add(BuildActions());
        }

        private VisualElement BuildHeader()
        {
            var header = ScratchpadStyles.Row();

            var caption = ScratchpadStyles.Caption("Features");
            caption.style.marginLeft = 4;
            caption.style.flexGrow = 1;
            header.Add(caption);

            // Next to the thing it adds to, rather than at the foot of the rail. Icon-only because the
            // header is not the place for a sentence, and the tooltip carries what it does.
            var add = ScratchpadStyles.IconOnlyButton(ScratchpadIcon.AddFeature,
                "Create a feature folder and name it. Handoffs for it go inside.",
                OnAddFeature, ScratchpadStyles.Dim, 12f);

            add.style.marginRight = 4;
            header.Add(add);
            return header;
        }

        private VisualElement BuildActions()
        {
            var actions = ScratchpadStyles.Column();
            actions.style.paddingBottom = 4;

            var addSession = ScratchpadStyles.IconButton(ScratchpadIcon.AddSession, "Empty session",
                "Somewhere to put notes before any handoff has landed for this feature.",
                OnAddSession);

            addSession.style.marginLeft = 4;
            addSession.style.marginRight = 4;
            actions.Add(addSession);

            return actions;
        }

        public void Rebuild()
        {
            _list.Clear();

            var features = _window.ShowArchive ? _window.Store.ArchivedFeatures : _window.Store.Features;

            if (features.Count == 0)
            {
                var empty = ScratchpadStyles.Body(
                    _window.ShowArchive ? "Nothing archived yet." : "No features yet.", dim: true);

                empty.style.marginLeft = 6;
                empty.style.marginTop = 4;
                empty.style.fontSize = 10;
                _list.Add(empty);
                return;
            }

            foreach (var feature in features)
            {
                _list.Add(ReferenceEquals(feature, _renaming) ? BuildEditRow(feature) : BuildRow(feature));
            }
        }

        private VisualElement BuildRow(ScratchpadFeature feature)
        {
            var row = ScratchpadStyles.Row();
            row.style.paddingLeft = 6;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;

            row.tooltip = _window.ShowArchive
                ? $"{feature.Sessions.Count} archived {(feature.Sessions.Count == 1 ? "session" : "sessions")}."
                : $"{feature.Sessions.Count} {(feature.Sessions.Count == 1 ? "session" : "sessions")}, " +
                  $"{feature.OpenIssues} open {(feature.OpenIssues == 1 ? "issue" : "issues")}, " +
                  $"{feature.OpenWork} outstanding. Right-click to rename or archive it.";

            var name = new Label(feature.Name)
            {
                style = { color = ScratchpadStyles.Text, flexGrow = 1, overflow = Overflow.Hidden },
            };

            row.Add(name);
            row.Add(ScratchpadStyles.Badge(feature.OpenIssues, ScratchpadStyles.Issue));
            row.Add(ScratchpadStyles.Badge(feature.OpenWork, ScratchpadStyles.Work));
            row.Add(new VisualElement { style = { width = 4, flexShrink = 0 } });

            ScratchpadStyles.MakeSelectable(row, () => ReferenceEquals(_window.Feature, feature));
            row.RegisterCallback<MouseDownEvent>(_ => _window.SelectFeature(feature));

            if (!_window.ShowArchive)
            {
                row.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction("Rename…", _ => BeginRename(feature));
                    evt.menu.AppendSeparator();

                    evt.menu.AppendAction("Archive feature…", _ => ArchiveFeature(feature),
                        feature.Sessions.Count > 0
                            ? DropdownMenuAction.Status.Normal
                            : DropdownMenuAction.Status.Disabled);

                    // Offered only when there is nothing to lose. A feature with sessions is archived,
                    // not deleted — the archive is how work leaves the list without leaving the disk.
                    evt.menu.AppendAction("Delete empty feature", _ => DeleteFeature(feature),
                        feature.Sessions.Count == 0
                            ? DropdownMenuAction.Status.Normal
                            : DropdownMenuAction.Status.Disabled);
                }));
            }

            return row;
        }

        /// <summary>The same row, with the name replaced by a field over it.</summary>
        private VisualElement BuildEditRow(ScratchpadFeature feature)
        {
            var row = ScratchpadStyles.Row();
            row.style.paddingLeft = 4;
            row.style.paddingTop = 1;
            row.style.paddingBottom = 1;

            var field = new TextField { value = feature.Name, style = { flexGrow = 1, marginRight = 4 } };
            field.RegisterCallback<KeyDownEvent>(evt => OnEditKey(evt, feature, field));

            // Committing on focus loss as well as on Return: clicking away from a half-named feature
            // should keep what was typed, not silently discard it.
            field.RegisterCallback<FocusOutEvent>(_ => Commit(feature, field.value));

            row.Add(field);

            // Focus and select-all on the next frame — the field has no text input to talk to until it
            // has been laid out once.
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            }).ExecuteLater(0);

            return row;
        }

        #region Creating and renaming

        /// <summary>
        /// Creates the folder straight away and drops into renaming it.
        /// </summary>
        /// <remarks>
        /// Rather than asking for a name first. The folder is cheap, an unnamed one is obvious in the
        /// list, and it means the create and rename paths are one path — which is the half of this
        /// that was worth fixing.
        /// </remarks>
        private void OnAddFeature()
        {
            if (_window.ShowArchive)
            {
                return;
            }

            var feature = _window.Store.CreateFeature(UniqueDefaultName());
            _window.SelectFeature(feature);
            _renaming = feature;
            _window.RebuildAll();
        }

        private string UniqueDefaultName()
        {
            if (_window.Store.FindFeature(DefaultName) == null)
            {
                return DefaultName;
            }

            for (var i = 2; i < 100; i++)
            {
                var candidate = $"{DefaultName} {i}";
                if (_window.Store.FindFeature(candidate) == null)
                {
                    return candidate;
                }
            }

            return DefaultName;
        }

        private void BeginRename(ScratchpadFeature feature)
        {
            if (_window.ShowArchive)
            {
                return;
            }

            _renaming = feature;
            Rebuild();
        }

        private void OnEditKey(KeyDownEvent evt, ScratchpadFeature feature, TextField field)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    evt.StopPropagation();
                    _renaming = null;
                    Rebuild();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    evt.StopPropagation();
                    Commit(feature, field.value);
                    break;
            }
        }

        private void Commit(ScratchpadFeature feature, string name)
        {
            if (_renaming == null)
            {
                return;
            }

            // Cleared first, so the focus-out this rebuild causes cannot come back round and commit a
            // second time against a feature that has already been renamed.
            _renaming = null;

            if (!string.IsNullOrWhiteSpace(name))
            {
                _window.Store.RenameFeature(feature, name);
                _window.SelectFeature(feature);
            }

            _window.RebuildAll();
        }

        /// <summary>
        /// Files a whole feature away, after asking.
        /// </summary>
        /// <remarks>
        /// Asked about because it is the only bulk file move in the tool and it is undone one session
        /// at a time — moving twelve sessions takes twelve unarchives to reverse. The count and the
        /// outstanding total are in the prompt because both change whether you meant to do it.
        /// </remarks>
        private void ArchiveFeature(ScratchpadFeature feature)
        {
            var sessions = feature.Sessions.Count;
            var outstanding = feature.OpenItems;

            var warning = outstanding > 0
                ? $"\n\n{outstanding} outstanding {(outstanding == 1 ? "note is" : "notes are")} still " +
                  "open on it. They will stop appearing in prompts."
                : string.Empty;

            var message = $"Archive all {sessions} {(sessions == 1 ? "session" : "sessions")} in " +
                          $"\"{feature.Name}\"?{warning}\n\nThey move to the Archive view and come back " +
                          "one at a time.";

            if (!EditorUtility.DisplayDialog("Archive feature", message, "Archive", "Cancel"))
            {
                return;
            }

            _window.Store.ArchiveFeature(feature);
            _window.Refresh();
        }

        /// <summary>Removes a feature folder that never got used. Not offered when it holds anything.</summary>
        private void DeleteFeature(ScratchpadFeature feature)
        {
            if (feature.Sessions.Count > 0)
            {
                return;
            }

            if (_window.Store.DeleteEmptyFeature(feature))
            {
                _window.Refresh();
            }
        }

        private void OnAddSession()
        {
            if (_window.ShowArchive || _window.Feature == null)
            {
                return;
            }

            var session = _window.Store.CreateEmptySession(_window.Feature);
            _window.SelectSession(session);
            _window.RebuildAll();
        }

        #endregion
    }
}
