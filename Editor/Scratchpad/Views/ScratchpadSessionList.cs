using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// The middle pane: a feature's sessions newest first, each listing the changes it delivered.
    /// </summary>
    /// <remarks>
    /// Only the newest few sessions open expanded. Older ones are almost always finished business,
    /// and a feature worked on for a month would otherwise open as several screens of scrolling
    /// before the thing you came to read.
    /// </remarks>
    internal sealed class ScratchpadSessionList : VisualElement
    {
        private readonly ScratchpadWindow _window;
        private readonly ScratchpadScrollMemory _scrollMemory;
        private readonly VisualElement _list;

        private readonly HashSet<string> _expanded = new();
        private string _expandedFor;
        private bool _showOlder;

        public ScratchpadSessionList(ScratchpadWindow window)
        {
            _window = window;

            style.flexGrow = 1;
            style.backgroundColor = ScratchpadStyles.Background;
            style.minWidth = 220;

            var scroll = new ScrollView { style = { flexGrow = 1 } };
            _list = new VisualElement { style = { paddingTop = 2 } };
            scroll.Add(_list);
            Add(scroll);

            _scrollMemory = new ScratchpadScrollMemory(scroll);
        }

        public void Rebuild() => _scrollMemory.Around(_window.Feature?.Name, RebuildCore);

        private void RebuildCore()
        {
            _list.Clear();

            var feature = _window.Feature;
            if (feature == null)
            {
                _list.Add(Placeholder("Pick a feature."));
                return;
            }

            SyncExpansion(feature);

            if (feature.Sessions.Count == 0)
            {
                _list.Add(Placeholder("No sessions yet. Copy the contract into a chat, or start an empty session."));
                return;
            }

            var cutoff = ScratchpadSettings.ExpandedSessions;

            for (var i = 0; i < feature.Sessions.Count; i++)
            {
                if (i >= cutoff && !_showOlder)
                {
                    _list.Add(BuildShowOlder(feature.Sessions.Count - i));
                    break;
                }

                _list.Add(BuildSession(feature.Sessions[i]));
            }
        }

        /// <summary>
        /// Opens the newest sessions when the feature changes, and leaves them alone after that.
        /// </summary>
        /// <remarks>
        /// Keyed on the feature so that switching away and back gives a predictable starting state,
        /// while a fold opened by hand survives every rebuild the window does in between.
        /// </remarks>
        private void SyncExpansion(ScratchpadFeature feature)
        {
            if (_expandedFor == feature.Name)
            {
                return;
            }

            _expandedFor = feature.Name;
            _showOlder = false;
            _expanded.Clear();

            foreach (var session in feature.Sessions.Take(ScratchpadSettings.ExpandedSessions))
            {
                _expanded.Add(session.Stamp);
            }
        }

        private VisualElement BuildSession(ScratchpadSession session)
        {
            var container = ScratchpadStyles.Column();
            container.style.marginBottom = 2;

            var open = _expanded.Contains(session.Stamp);
            container.Add(BuildSessionHeader(session, open));

            if (!open)
            {
                return container;
            }

            if (session.ParseError != null)
            {
                var error = ScratchpadStyles.Body($"This handoff would not parse: {session.ParseError}");
                error.style.color = ScratchpadStyles.Issue;
                error.style.marginLeft = 22;
                error.style.marginRight = 6;
                error.style.fontSize = 10;
                container.Add(error);
            }

            foreach (var change in session.Changes)
            {
                container.Add(BuildChangeRow(session, change));
            }

            var loose = session.LooseNotes.ToList();
            if (loose.Count > 0 || session.IsPlaceholder)
            {
                container.Add(BuildLooseRow(session, loose.Count));
            }

            return container;
        }

        private VisualElement BuildSessionHeader(ScratchpadSession session, bool open)
        {
            var row = ScratchpadStyles.Row();
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            row.style.paddingLeft = 4;

            var arrow = new Label(open ? "▾" : "▸")
            {
                style = { width = 14, color = ScratchpadStyles.Dim, flexShrink = 0 },
                tooltip = open ? "Collapse this session." : "Expand this session to list its changes.",
            };

            arrow.RegisterCallback<MouseDownEvent>(evt =>
            {
                Toggle(session.Stamp);
                evt.StopPropagation();
            });

            row.Add(arrow);

            var text = ScratchpadStyles.Column();
            text.style.flexGrow = 1;
            text.style.overflow = Overflow.Hidden;

            var title = new Label(session.DisplayTitle)
            {
                style = { color = ScratchpadStyles.Text, unityFontStyleAndWeight = FontStyle.Bold },
            };

            var stamp = new Label(session.DisplayStamp)
            {
                style = { color = ScratchpadStyles.Dim, fontSize = 9 },
            };

            text.Add(title);
            text.Add(stamp);
            row.Add(text);

            row.Add(ScratchpadStyles.Badge(session.OutstandingCount, ScratchpadStyles.Comment));

            // Only offered where it makes sense, which is the archive view. Everywhere else the
            // session is already live and the button would be a no-op with a confusing name.
            if (_window.ShowArchive)
            {
                row.Add(ScratchpadStyles.IconOnlyButton(ScratchpadIcon.Unarchive,
                    "Bring this session back out of the archive so it can be annotated again.",
                    () => Unarchive(session), ScratchpadStyles.Work, 12f));
            }

            row.Add(new VisualElement { style = { width = 4, flexShrink = 0 } });

            row.tooltip = session.ParseError != null
                ? "This handoff did not parse. Select it to see why."
                : $"{session.Changes.Count} {(session.Changes.Count == 1 ? "change" : "changes")}, " +
                  $"{session.OutstandingCount} outstanding. Click to read the session summary.";

            ScratchpadStyles.MakeSelectable(row,
                () => ReferenceEquals(_window.Session, session) && _window.Change == null);

            row.RegisterCallback<MouseDownEvent>(_ =>
            {
                if (!_expanded.Contains(session.Stamp))
                {
                    _expanded.Add(session.Stamp);
                }

                _window.SelectSession(session);
            });

            return row;
        }

        private VisualElement BuildChangeRow(ScratchpadSession session, ScratchpadChange change)
        {
            var row = ScratchpadStyles.Row();
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.paddingLeft = 20;

            row.Add(ScratchpadStyles.ReviewDot(session.ReviewOf(change.Id)));

            var title = new Label(string.IsNullOrWhiteSpace(change.Title) ? change.Id : change.Title)
            {
                style = { color = ScratchpadStyles.Text, flexGrow = 1, overflow = Overflow.Hidden },
            };

            row.Add(title);

            var outstanding = session.NotesFor(change.Id).Count(n => n.IsOutstanding);
            row.Add(ScratchpadStyles.Badge(outstanding, ScratchpadStyles.Comment));
            row.Add(new VisualElement { style = { width = 4, flexShrink = 0 } });

            var state = session.ReviewOf(change.Id);
            row.tooltip = state switch
            {
                ReviewState.Ok => "Marked OK. Click to read it again.",
                ReviewState.CloserLook => "Flagged for a closer look. Click to read it.",
                _ => "Not reviewed yet. Click to read it.",
            };

            ScratchpadStyles.MakeSelectable(row, () => ReferenceEquals(_window.Change, change));
            row.RegisterCallback<MouseDownEvent>(_ => _window.SelectChange(session, change));
            return row;
        }

        private VisualElement BuildLooseRow(ScratchpadSession session, int count)
        {
            var row = ScratchpadStyles.Row();
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.paddingLeft = 20;

            var label = new Label("Notes on the session")
            {
                style = { color = ScratchpadStyles.Dim, flexGrow = 1, unityFontStyleAndWeight = FontStyle.Italic },
            };

            row.Add(label);
            row.Add(ScratchpadStyles.Badge(count, ScratchpadStyles.Comment));
            row.Add(new VisualElement { style = { width = 4, flexShrink = 0 } });

            ScratchpadStyles.MakeSelectable(row,
                () => ReferenceEquals(_window.Session, session) && _window.Change == null);

            row.RegisterCallback<MouseDownEvent>(_ => _window.SelectSession(session));
            return row;
        }

        private void Unarchive(ScratchpadSession session)
        {
            if (!_window.Store.Unarchive(session))
            {
                return;
            }

            // The session has moved out of the list being browsed, so there is nothing here to select
            // any more. A refresh puts the window back on something that exists.
            _window.Refresh();
        }

        private VisualElement BuildShowOlder(int remaining)
        {
            var noun = remaining == 1 ? "session" : "sessions";

            var button = new Button(() =>
            {
                _showOlder = true;
                Rebuild();
            })
            {
                text = $"⋯ {remaining} older {noun}",
                tooltip = $"Show the {remaining} older {noun} in this feature. Only the newest " +
                          $"{ScratchpadSettings.ExpandedSessions} are listed by default.",
                style = { marginLeft = 18, marginRight = 6, marginTop = 2 },
            };

            return button;
        }

        private void Toggle(string stamp)
        {
            if (!_expanded.Remove(stamp))
            {
                _expanded.Add(stamp);
            }

            Rebuild();
        }

        private static Label Placeholder(string text)
        {
            var label = ScratchpadStyles.Body(text, dim: true);
            label.style.marginLeft = 8;
            label.style.marginRight = 8;
            label.style.marginTop = 6;
            label.style.fontSize = 10;
            return label;
        }
    }
}
