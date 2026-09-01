using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// A one-field popup for writing a note without leaving what you were doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It files onto the newest session of the last feature the main window was on, as a note with no
    /// change attached. Picking a specific change would mean rendering the session tree inside a
    /// popup, which is the opposite of the point — attach it properly later, in the window, if it
    /// turns out to belong to one change rather than the session.
    /// </para>
    /// <para>
    /// If the feature has no session at all it makes an empty one, so the note has somewhere to live
    /// before any handoff has landed. That is the case this is most useful in: noticing something
    /// while testing, before the assistant has written anything down.
    /// </para>
    /// </remarks>
    internal sealed class ScratchpadQuickCaptureWindow : EditorWindow
    {
        private readonly ScratchpadStore _store = new();

        private List<string> _features;
        private string _feature;
        private NoteKind _kind = NoteKind.Comment;

        private DropdownField _featureField;
        private TextField _newFeatureField;
        private TextField _bodyField;
        private Label _attachment;

        private string _console;
        private string _context;

        /// <summary>Controls picked for this note, in the order they were picked.</summary>
        private readonly List<string> _picked = new();

        private Button _pickButton;
        private VisualElement _pickedList;
        private ScrollView _pickedScroll;

        [Shortcut("Vapor/Scratchpad/Quick Capture", KeyCode.S, ShortcutModifiers.Action | ShortcutModifiers.Alt)]
        public static void Open()
        {
            var window = CreateInstance<ScratchpadQuickCaptureWindow>();
            window.titleContent = new GUIContent("Scratchpad note");

            var size = new Vector2(420, 250);
            var mouse = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
            window.position = new Rect(mouse.x - size.x * 0.5f, mouse.y, size.x, size.y);
            window.minSize = size;

            window.ShowUtility();
        }

        public void CreateGUI()
        {
            _store.Refresh(allowArchive: false);

            _features = _store.Features.Select(f => f.Name).ToList();
            _feature = _features.FirstOrDefault(f =>
                           string.Equals(f, ScratchpadSettings.LastFeature, System.StringComparison.OrdinalIgnoreCase))
                       ?? _features.FirstOrDefault();

            _context = DescribeSelection();

            var root = rootVisualElement;
            root.style.backgroundColor = ScratchpadStyles.Panel;
            ScratchpadStyles.SetPadding(root, 8);

            root.Add(BuildFeatureRow());
            root.Add(BuildKindRow());

            _bodyField = new TextField { multiline = true };
            _bodyField.style.flexGrow = 1;
            _bodyField.style.marginTop = 4;
            _bodyField.style.whiteSpace = WhiteSpace.Normal;
            _bodyField.tooltip = "The note. It files onto the session rather than a specific change — " +
                                 "attach it to one later from the main window if it belongs to just one.";

            root.Add(_bodyField);

            // Capped rather than free-growing. This window is a fixed 250px and the buttons live under
            // this list, so an uncapped list pushes Add off the bottom of a window that does not
            // scroll — the picks are the one part of it with no upper bound.
            _pickedScroll = new ScrollView
            {
                style =
                {
                    maxHeight = 78,
                    flexShrink = 0,
                    display = DisplayStyle.None,
                },
            };

            _pickedList = new VisualElement();
            _pickedScroll.Add(_pickedList);
            root.Add(_pickedScroll);

            _attachment = ScratchpadStyles.Body(string.Empty, dim: true);
            _attachment.style.fontSize = 9;
            root.Add(_attachment);
            UpdateAttachmentLabel();

            root.Add(BuildActions());

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // The picker can be ended from the keyboard or by a right-click anywhere in the editor,
            // so the toggle cannot learn it has stopped by watching its own clicks.
            ScratchpadElementPicker.PickingChanged += OnPickingChanged;

            // Focus has to wait for the first layout, or it lands on nothing and the popup opens with
            // the caret outside the field it exists to fill in.
            root.schedule.Execute(() => _bodyField.Q("unity-text-input")?.Focus()).ExecuteLater(50);
        }

        #region Layout

        private VisualElement BuildFeatureRow()
        {
            var row = ScratchpadStyles.Row();

            _featureField = new DropdownField
            {
                style = { flexGrow = 1, marginRight = 4 },
                tooltip = "Which feature this note belongs to. Defaults to the one the main window " +
                          "was last on.",
            };
            _featureField.choices = new List<string>(_features) { NewFeatureChoice };
            _featureField.SetValueWithoutNotify(_feature ?? NewFeatureChoice);
            _featureField.RegisterValueChangedCallback(evt =>
            {
                var creating = evt.newValue == NewFeatureChoice;
                _newFeatureField.style.display = creating ? DisplayStyle.Flex : DisplayStyle.None;
                _feature = creating ? null : evt.newValue;
            });

            row.Add(_featureField);

            _newFeatureField = new TextField
            {
                style = { flexGrow = 1, display = _feature == null ? DisplayStyle.Flex : DisplayStyle.None },
            };

            _newFeatureField.textEdition.placeholder = "New feature name";

            var column = ScratchpadStyles.Column();
            column.Add(row);
            column.Add(_newFeatureField);
            return column;
        }

        private VisualElement BuildKindRow()
        {
            var row = ScratchpadStyles.Row();
            row.style.marginTop = 4;

            foreach (NoteKind kind in System.Enum.GetValues(typeof(NoteKind)))
            {
                var captured = kind;

                var button = new Button
                {
                    text = kind.ToString(),
                    tooltip = kind switch
                    {
                        NoteKind.Issue => "Something wrong that needs fixing. Issues lead the prompt.",
                        NoteKind.Work => "New work to pick up later. Neither a defect nor an opinion.",
                        _ => "An observation. Feedback, not a defect.",
                    },
                    style = { fontSize = 10, marginRight = 2 },
                };

                button.clicked += () =>
                {
                    _kind = captured;
                    PaintKindButtons(row);
                };

                row.Add(button);
            }

            row.Add(ScratchpadStyles.Spacer());
            row.Add(BuildAttachButton());
            row.Add(BuildPickButton());

            PaintKindButtons(row);
            return row;
        }

        private void PaintKindButtons(VisualElement row)
        {
            var index = 0;
            foreach (var child in row.Children().OfType<Button>())
            {
                if (index >= System.Enum.GetValues(typeof(NoteKind)).Length)
                {
                    break;
                }

                var kind = (NoteKind)index;
                var selected = kind == _kind;
                child.style.backgroundColor = selected
                    ? ScratchpadStyles.KindColor(kind)
                    : StyleKeyword.Null;

                child.style.color = selected ? Color.black : StyleKeyword.Null;
                index++;
            }
        }

        /// <summary>
        /// Point at controls and attach them to the note being written here.
        /// </summary>
        /// <remarks>
        /// This used to close the popup and hand the pick to the main window, on the belief that a
        /// utility window gives up focus as soon as you click into another one. It does not —
        /// <c>ShowUtility</c> keeps it open — so the detour was throwing away a half-written note to
        /// solve a problem that was not there.
        /// </remarks>
        private Button BuildPickButton()
        {
            _pickButton = ScratchpadStyles.IconOnlyButton(ScratchpadIcon.Pick,
                "Point at controls in any editor window to attach what they are to this note. Stays " +
                "on so you can pick several; Escape or right-click stops.",
                () => ScratchpadElementPicker.Toggle(OnElementPicked),
                ScratchpadStyles.Text, 12f);

            return _pickButton;
        }

        private void OnElementPicked(string description)
        {
            // Picking the same control twice is a mis-click, not an instruction to record it twice.
            if (!_picked.Contains(description))
            {
                _picked.Add(description);
            }

            RebuildPickedList();
            UpdateAttachmentLabel();
        }

        private void OnPickingChanged()
        {
            if (_pickButton == null)
            {
                return;
            }

            var picking = ScratchpadElementPicker.IsPicking;

            // Lit while it is on, which is the only sign the editor is waiting for a click somewhere
            // other than where you are looking.
            _pickButton.style.backgroundColor = picking ? ScratchpadStyles.InFlight : StyleKeyword.Null;
        }

        private void RebuildPickedList()
        {
            _pickedList.Clear();

            // Hidden rather than empty when there is nothing picked, so it takes no height from the
            // body field in the ordinary case where nobody picks anything at all.
            _pickedScroll.style.display = _picked.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            foreach (var context in _picked.ToList())
            {
                var row = ScratchpadStyles.Row();

                var label = ScratchpadStyles.Body(context, dim: true);
                label.style.fontSize = 9;
                label.style.color = ScratchpadStyles.InFlight;
                label.style.flexGrow = 1;
                row.Add(label);

                row.Add(ScratchpadStyles.TextButton("×", "Remove this control from the note.", () =>
                {
                    _picked.Remove(context);
                    RebuildPickedList();
                    UpdateAttachmentLabel();
                }));

                _pickedList.Add(row);
            }
        }

        private void OnDisable()
        {
            ScratchpadElementPicker.PickingChanged -= OnPickingChanged;

            // Closing the note while pointing at something would otherwise leave every window wearing
            // an overlay with nothing left to receive the pick.
            if (ScratchpadElementPicker.IsPicking)
            {
                ScratchpadElementPicker.Cancel();
            }
        }

        private Button BuildAttachButton()
        {
            // Fixed label: the button is built once, and what is currently attached is reported by
            // the line under the body field, which does refresh.
            return ScratchpadStyles.IconButton(ScratchpadIcon.Console, "Attach console",
                "Attach a recent console entry — message and stack — to this note. The buffer keeps " +
                "the last fifty, so an entry stays attachable after the console has been cleared.",
                () =>
            {
                var menu = ScratchpadConsoleMenu.Build(detail =>
                {
                    _console = detail;
                    UpdateAttachmentLabel();
                }, _console != null);

                menu.ShowAsContext();
            }, 12f);
        }

        private VisualElement BuildActions()
        {
            var row = ScratchpadStyles.Row();
            row.style.marginTop = 4;

            var hint = ScratchpadStyles.Body("Ctrl+Enter to add, Esc to cancel", dim: true);
            hint.style.fontSize = 9;
            row.Add(hint);
            row.Add(ScratchpadStyles.Spacer());

            row.Add(new Button(Close) { text = "Cancel", tooltip = "Close without saving the note." });

            row.Add(new Button(Submit)
            {
                text = "Add",
                tooltip = "File the note onto the newest session for the chosen feature. If that " +
                          "feature has no session yet, an empty one is created to hold it.",
            });

            return row;
        }

        private void UpdateAttachmentLabel()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(_context))
            {
                parts.Add($"selection: {_context}");
            }

            if (_picked.Count > 0)
            {
                parts.Add($"{_picked.Count} picked control{(_picked.Count == 1 ? string.Empty : "s")}");
            }

            if (_console != null)
            {
                parts.Add("a console entry");
            }

            _attachment.text = parts.Count == 0 ? string.Empty : "Attaching " + string.Join(", ", parts);
        }

        #endregion

        #region Submitting

        private const string NewFeatureChoice = "＋ New feature…";

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                Close();
                evt.StopPropagation();
                return;
            }

            // Ctrl+Enter rather than plain Enter: the body is a multi-line field, and a note worth
            // writing is often worth writing on two lines.
            if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter && (evt.ctrlKey || evt.commandKey))
            {
                Submit();
                evt.StopPropagation();
            }
        }

        private void Submit()
        {
            var name = _feature ?? _newFeatureField.value;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowNotification(new GUIContent("Pick a feature first."));
                return;
            }

            if (string.IsNullOrWhiteSpace(_bodyField.value) && _console == null && _picked.Count == 0)
            {
                ShowNotification(new GUIContent("Nothing to add."));
                return;
            }

            var feature = _store.FindFeature(name) ?? _store.CreateFeature(name);
            var session = _store.NewestOrCreate(feature);

            // Picked controls replace the guessed selection when there are any: pointing at something
            // is a deliberate answer to "what is this about", where the selection is only a guess.
            var context = _picked.Count > 0 ? string.Join("\n", _picked) : _context;

            _store.AddNote(session, string.Empty, _kind, _bodyField.value,
                _console != null ? NoteSource.Console : NoteSource.QuickCapture,
                context, _console);

            ScratchpadSettings.LastFeature = feature.Name;

            // The main window reads from disk and has no idea this happened, so tell it. Only if it is
            // already open — capturing a note is not a reason to put a window on screen.
            if (HasOpenInstances<ScratchpadWindow>())
            {
                GetWindow<ScratchpadWindow>(false, "Scratchpad", false).Refresh();
            }

            Close();
        }

        /// <summary>
        /// Whatever is selected, named the way you would recognise it later.
        /// </summary>
        /// <remarks>
        /// An asset gets its project path; a scene object gets its hierarchy path, which is the only
        /// thing about it that survives being written down.
        /// </remarks>
        private static string DescribeSelection()
        {
            var active = Selection.activeObject;
            if (active == null)
            {
                return string.Empty;
            }

            var path = AssetDatabase.GetAssetPath(active);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            return active is GameObject go ? HierarchyPath(go) : active.name;
        }

        private static string HierarchyPath(GameObject go)
        {
            var path = go.name;
            for (var parent = go.transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        #endregion
    }
}
