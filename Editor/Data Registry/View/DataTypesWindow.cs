using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Serialization;
using VaporEditor.Inspector;
using Object = UnityEngine.Object;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Authors every <see cref="IData"/> type marked <see cref="DataAuthoringAttribute"/>: types on
    /// the left, that type's entries in the middle, the selected entry on the right.
    /// </summary>
    /// <remarks>
    /// Entirely reflection-driven. A new authored data type appears here as soon as it carries the
    /// attribute, with no editor code of its own, which is the whole point of moving authoring out of
    /// hand-written <see cref="IDataRegistry"/> classes.
    /// </remarks>
    public class DataTypesWindow : EditorWindow
    {
        private const int TypeRailWidth = 220;
        private const int TypeRailMinWidth = 140;
        private const int EntryListWidth = 280;
        private const int EntryListMinWidth = 200;
        private const int InspectorMinWidth = 240;

        [MenuItem("Vapor Data/Data Types", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<DataTypesWindow>();
            window.titleContent = new GUIContent("Data Types");
            window.minSize = new Vector2(720, 420);
        }

        /// <summary>Opens the window on one entry, with its type already selected.</summary>
        /// <remarks>
        /// <para>
        /// So that anything holding an <see cref="IData"/> can hand the user the editor for it. Every
        /// tool that shows authored content eventually wants "edit this", and without it each one grows
        /// its own inspector window instead — a second place the same fields are drawn, and a second
        /// place they fall out of date.
        /// </para>
        /// <para>
        /// The type rail and the selection are both private, and deliberately: opening at an entry has
        /// to select the type, load its document and select the row in one step, or the window comes up
        /// on the right type with nothing chosen.
        /// </para>
        /// <para>
        /// <b>Matched by key, not by reference, and that is the whole difficulty.</b> A caller holds
        /// the registry's instance; selecting the type makes <see cref="OpenType"/> load the document
        /// fresh, which builds its OWN instances from the file. They are equal in every way that
        /// matters and identical in none — so selecting the caller's object put a row in the list that
        /// <see cref="RefreshEntryList"/> then dropped, because it intersects the selection with what
        /// the document holds. The window opened on the right type with nothing selected.
        /// </para>
        /// </remarks>
        public static void Open(IData entry)
        {
            // A type with a window of its own is edited there. Routed before this window is even
            // shown, so "edit this actor" lands in the Actors window and not on an empty pane here.
            if (entry != null && DataAuthoringWindows.HasWindow(entry.GetType()))
            {
                DataAuthoringWindows.Open(entry.GetType(), entry);
                return;
            }

            Open();

            if (entry == null)
            {
                return;
            }

            var window = GetWindow<DataTypesWindow>();

            // A window opening for the first time has not run CreateGUI yet, so there is no rail to
            // select on and no list to highlight. Deferred a frame rather than half-applied, or the
            // first use after a domain reload is the one that silently does nothing.
            if (window._typeRail == null || window._entryList == null)
            {
                EditorApplication.delayCall += () => Open(entry);
                return;
            }

            // By type and key rather than by the caller's object; see the remarks above.
            window.ShowEntry(entry.GetType(), entry.Key);
        }

        private DataTypeRail _typeRail;
        private ListView _entryList;
        private ToolbarSearchField _search;
        private VisualElement _inspectorPane;

        /// <summary>The filter bar of whatever is in the inspector, when it has one. Ctrl+F and Escape go here.</summary>
        private InspectorFilterBar _filterBar;

        private Label _statusLabel;
        private ToolbarButton _saveButton;
        private ToolbarButton _revertButton;

        private DataDocument _document;

        /// <summary>
        /// The entries being edited. More than one puts the inspector into side-by-side comparison.
        /// </summary>
        private readonly List<IData> _selection = new();

        /// <summary>
        /// What the entry list is showing: the current selection, then everything matching the search.
        /// </summary>
        /// <remarks>
        /// The selection is pinned to the top rather than filtered away with everything else. A search
        /// that excluded the open entry would drop it out of the list, and the list is the only thing
        /// that can put it back — so the entry became unreachable until the search was cleared and
        /// re-clicked.
        /// </remarks>
        private readonly List<IData> _filtered = new();

        /// <summary>How many of the leading <see cref="_filtered"/> rows are there because they are selected.</summary>
        private int _stickyCount;

        /// <summary>Entries the user keeps a click away, in the order they were pinned. Any type, not only the open one.</summary>
        private readonly List<DataPin> _pinned = new();

        /// <summary>The last pin list written to prefs, so a rename does not write on every keystroke.</summary>
        private string _storedPins = string.Empty;

        private VisualElement _pinRail;

        /// <summary>The name on the open entry's header, kept in step with a rename as it is typed.</summary>
        private Label _entryHeaderName;

        /// <summary>
        /// Members ticked on the sheet's field axis. Empty — the default — means <c>Copy Prompt</c>
        /// covers whole entries.
        /// </summary>
        private readonly HashSet<string> _focusedFields = new();

        /// <summary>
        /// Keys owned by something other than a VSL document — a code registry, or an addressable
        /// asset. An entry landing on one of these is rejected by
        /// <see cref="GlobalDataRegistry.Register"/> at load, so it is worth flagging before the save
        /// rather than after.
        /// </summary>
        private readonly HashSet<uint> _externalKeys = new();

        private ToolbarToggle _flagDifferencesToggle;
        private ToolbarToggle _orientationToggle;

        private static string OrientationLabel(bool fieldsAsRows) =>
            fieldsAsRows ? "Fields: Rows" : "Fields: Columns";
        private VisualElement _entryPane;
        private bool _pointerOverEntries;

        /// <summary>Persisted so the choices survive a domain reload, like any other editor preference.</summary>
        private const string FlagDifferencesPref = "Vapor.DataTypes.FlagDifferences";

        private const string FieldsAsRowsPref = "Vapor.DataTypes.FieldsAsRows";

        private const string PinnedPrefsKey = "Vapor.DataTypes.Pinned";

        /// <summary>Separators for the stored pins. Control characters, so no name can contain one.</summary>
        private const char RecordSeparator = (char)0x1E;

        private const char FieldSeparator = (char)0x1F;

        private static readonly Color PinRailBackground = new Color(0f, 0f, 0f, 0.15f);
        private static readonly Color PinRailRule = new Color(1f, 1f, 1f, 0.12f);

        private static bool FlagDifferences
        {
            get => EditorPrefs.GetBool(FlagDifferencesPref, true);
            set => EditorPrefs.SetBool(FlagDifferencesPref, value);
        }

        /// <summary>
        /// Which way round the comparison sheet runs: field names down the side, or across the top.
        /// </summary>
        private static bool FieldsAsRows
        {
            get => EditorPrefs.GetBool(FieldsAsRowsPref, true);
            set => EditorPrefs.SetBool(FieldsAsRowsPref, value);
        }

        public void CreateGUI()
        {
            rootVisualElement.Add(BuildToolbar());
            rootVisualElement.Add(BuildPinRail());

            // Every pane goes into a plain container with a flex grow and a minimum width. A split view
            // sizes its panes from what the child reports, and a TreeView or ScrollView handed to it
            // directly can report nothing and collapse the pane to zero.
            var outer = new TwoPaneSplitView(0, TypeRailWidth, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1f },
            };
            outer.Add(Pane(BuildTypeRail(), TypeRailMinWidth));

            var inner = new TwoPaneSplitView(0, EntryListWidth, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1f },
            };
            inner.Add(Pane(BuildEntryPane(), EntryListMinWidth));
            inner.Add(Pane(BuildInspectorPane(), InspectorMinWidth));
            outer.Add(inner);

            rootVisualElement.Add(outer);

            // Handled at the root rather than on the list, because a hovered element receives no key
            // events of its own. Trickling down so these win over the list's own key handling; the
            // text-editing guard is what keeps it from stealing keystrokes meant for a field.
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnEntryShortcut, TrickleDown.TrickleDown);

            // Before the rail picks a type: opening one draws the pins, and there would be none yet.
            RestorePins();

            RefreshExternalKeys();
            RefreshTypeRail();
            UpdateStatus();
        }

        private static VisualElement Pane(VisualElement content, int minWidth)
        {
            var pane = new VisualElement
            {
                style =
                {
                    flexGrow = 1f,
                    flexShrink = 1f,
                    minWidth = minWidth,
                    overflow = Overflow.Hidden,
                },
            };
            pane.Add(content);
            return pane;
        }

        private void OnDestroy()
        {
            if (_document is not { IsDirty: true })
            {
                return;
            }

            // Two options, not three: the window is already closing, so there is nothing for a cancel
            // to return to.
            if (EditorUtility.DisplayDialog("Unsaved data",
                    $"{_document.DisplayName} has unsaved changes.", "Save", "Discard"))
            {
                _document.Save();
                return;
            }

            // Discarded rather than simply abandoned: a saved document leaves its entries in the
            // registry, so unsaved edits to them have to be read back off disk or the rest of the
            // editor spends the session looking at changes the file does not have.
            _document.Revert();
        }

        #region Chrome

        private VisualElement BuildToolbar()
        {
            var toolbar = new Toolbar();

            _saveButton = new ToolbarButton(SaveCurrent)
            {
                text = "Save",
                tooltip = "Write this type's entries to its .vsl document and rebuild the registry.",
                style = { flexShrink = 0f },
            };
            _revertButton = new ToolbarButton(RevertCurrent)
            {
                text = "Revert",
                tooltip = "Discard unsaved changes and reload the document from disk.",
                style = { flexShrink = 0f },
            };
            toolbar.Add(_saveButton);
            toolbar.Add(_revertButton);

            // Takes the slack so the buttons on either side keep their size, and clips rather than
            // pushing them off the edge when the window is narrow.
            _statusLabel = new Label
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginLeft = 8,
                    flexGrow = 1f,
                    flexShrink = 1f,
                    flexBasis = 0f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                },
            };
            toolbar.Add(_statusLabel);

            _orientationToggle = new ToolbarToggle
            {
                tooltip = "Which way round the comparison sheet runs.",
                value = FieldsAsRows,
                style = { flexShrink = 0f },
            };
            _orientationToggle.text = OrientationLabel(FieldsAsRows);
            _orientationToggle.RegisterValueChangedCallback(evt =>
            {
                FieldsAsRows = evt.newValue;
                _orientationToggle.text = OrientationLabel(evt.newValue);
                RefreshInspector();
            });
            toolbar.Add(_orientationToggle);

            _flagDifferencesToggle = new ToolbarToggle
            {
                text = "Flag Differences",
                tooltip = "When comparing several entries, highlight the fields whose values are not the same across all of them.",
                value = FlagDifferences,
                style = { flexShrink = 0f },
            };
            _flagDifferencesToggle.RegisterValueChangedCallback(evt =>
            {
                FlagDifferences = evt.newValue;
                RefreshInspector();
            });
            toolbar.Add(_flagDifferencesToggle);

            toolbar.Add(new ToolbarButton(CopyPrompt)
            {
                text = "Copy Prompt",
                tooltip = "Copy a block describing this data - its type, its file paths, the format spec and the "
                          + "selected entries as VSL - ready to paste into an AI prompt.",
                style = { flexShrink = 0f },
            });

            toolbar.Add(new ToolbarButton(() =>
            {
                GlobalDataRegistry.Initialize();
                RefreshExternalKeys();
                RefreshTypeRail();
                RefreshEntryList();
            })
            {
                text = "Rebuild Registry",
                tooltip = "Reload every data source - code registries, addressable assets and .vsl documents - and rebuild the global registry.",
                style = { flexShrink = 0f },
            });

            var menu = new ToolbarMenu { tooltip = "More actions.", style = { flexShrink = 0f, width = 28 } };
            menu.menu.AppendAction("Rebalance Files", _ => RebalanceCurrent(),
                _ => _document == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.menu.AppendSeparator();
            menu.menu.AppendAction("Log Save Timings", _ => VslSaveDiagnostics.Enabled = !VslSaveDiagnostics.Enabled,
                _ => VslSaveDiagnostics.Enabled ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.menu.AppendAction("Log Registry Rebuilds", _ => VslSaveDiagnostics.Verbose = !VslSaveDiagnostics.Verbose,
                _ => VslSaveDiagnostics.Verbose ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            toolbar.Add(menu);

            return toolbar;
        }

        /// <summary>Re-packs the open document's files, then saves.</summary>
        private void RebalanceCurrent()
        {
            if (_document == null)
            {
                return;
            }

            var before = _document.ShardCount;
            if (!_document.Rebalance())
            {
                return;
            }

            Debug.Log($"Rebalanced {_document.DisplayName} from {before} {(before == 1 ? "file" : "files")} to {_document.ShardCount}.");
            RefreshExternalKeys();
            RefreshEntryList();
            UpdateStatus();
        }

        /// <summary>
        /// A toolbar button showing a built-in editor icon, falling back to text when the icon is not
        /// available so the control is never invisible.
        /// </summary>
        private static ToolbarButton IconButton(string iconName, string fallbackText, string tooltip, Action onClick)
        {
            var button = new ToolbarButton(onClick)
            {
                tooltip = tooltip,
                style =
                {
                    flexShrink = 0f,
                    width = 26,
                    paddingLeft = 2,
                    paddingRight = 2,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                },
            };

            var icon = EditorGUIUtility.IconContent(iconName)?.image as Texture2D;
            if (icon == null)
            {
                button.text = fallbackText;
                return button;
            }

            button.Add(new VisualElement
            {
                style =
                {
                    backgroundImage = new StyleBackground(icon),
                    width = 16,
                    height = 16,
                    flexShrink = 0f,
                },
            });
            return button;
        }

        private VisualElement BuildTypeRail()
        {
            _typeRail = new DataTypeRail
            {
                // The rail asks before it changes, so a cancelled prompt leaves the current selection
                // where it was rather than having to be undone afterwards.
                ConfirmChange = ConfirmDiscardOrSave,
            };

            _typeRail.SelectionChanged += OpenType;
            _typeRail.TypeFilterChanged += _ =>
            {
                // Only the list narrows; the open document is the family's either way.
                RefreshEntryList();
            };

            return _typeRail;
        }

        private VisualElement BuildEntryPane()
        {
            var pane = new VisualElement { style = { flexGrow = 1f } };
            _entryPane = pane;

            // Hover as well as focus, so the shortcuts work while pointing at the list without having
            // clicked into it first.
            pane.RegisterCallback<PointerEnterEvent>(_ => _pointerOverEntries = true);
            pane.RegisterCallback<PointerLeaveEvent>(_ => _pointerOverEntries = false);

            var toolbar = new Toolbar();

            // The search field takes whatever is left after the buttons. Giving it a zero flex basis
            // and the buttons a zero shrink is what stops it growing to the full width and squeezing
            // them down to nothing.
            _search = new ToolbarSearchField
            {
                tooltip = "Filter entries by name.",
                style =
                {
                    flexGrow = 1f,
                    flexShrink = 1f,
                    flexBasis = 0f,
                    minWidth = 40,
                },
            };
            _search.RegisterValueChangedCallback(_ => RefreshEntryList());
            toolbar.Add(_search);

            toolbar.Add(IconButton("Toolbar Plus", "+", "Add a new entry to this document.", AddEntry));
            toolbar.Add(IconButton("TreeEditor.Duplicate", "Copy", "Duplicate the selected entries.", DuplicateSelected));
            toolbar.Add(IconButton("Toolbar Minus", "-", "Delete the selected entries.", RemoveSelected));

            var menu = new ToolbarMenu
            {
                tooltip = "More actions for this document.",
                style = { flexShrink = 0f, width = 28 },
            };
            menu.menu.AppendAction("Import Registered Instances", _ => ImportRegistered(),
                _ => _document == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.menu.AppendAction("Reveal Document", _ => RevealDocument(),
                _ => _document == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            toolbar.Add(menu);

            pane.Add(toolbar);

            _entryList = new ListView
            {
                // Multiple so several entries can be opened side by side and compared.
                selectionType = SelectionType.Multiple,
                fixedItemHeight = 22,
                itemsSource = _filtered,
                style = { flexGrow = 1f },
            };

            _entryList.makeItem = MakeEntryRow;
            _entryList.bindItem = BindEntryRow;
            _entryList.selectionChanged += selection =>
            {
                _selection.Clear();
                _selection.AddRange(selection.OfType<IData>());
                RefreshInspector();
            };

            pane.Add(_entryList);
            return pane;
        }

        /// <summary>
        /// Host for the inspector. Left as a plain container because what goes in it differs: one
        /// entry scrolls vertically, several scroll sideways as columns.
        /// </summary>
        private VisualElement BuildInspectorPane()
        {
            _inspectorPane = new VisualElement { style = { flexGrow = 1f } };

            // Trickled down so the search field's own text handling does not swallow Escape first.
            _inspectorPane.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (_filterBar != null && _filterBar.HandleShortcut(evt))
                {
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            return _inspectorPane;
        }

        private static VisualElement MakeEntryRow()
        {
            var row = new VisualElement { style = { flexGrow = 1f } };

            var line = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    flexGrow = 1f,
                },
            };

            line.Add(new Label { name = "Name", style = { flexGrow = 1f, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } });
            line.Add(new Label { name = "Warning", style = { flexShrink = 0, marginRight = 4 } });
            row.Add(line);

            // Drawn under the last pinned row, so the kept-in-view entries read as a group rather than
            // as search results that do not match the search.
            row.Add(new VisualElement
            {
                name = "Separator",
                style =
                {
                    height = 1,
                    backgroundColor = new Color(1f, 1f, 1f, 0.15f),
                    display = DisplayStyle.None,
                },
            });

            return row;
        }

        private void BindEntryRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _filtered.Count)
            {
                return;
            }

            var entry = _filtered[index];
            var entryName = DataDocument.GetName(entry);

            var nameLabel = element.Q<Label>("Name");
            nameLabel.text = string.IsNullOrEmpty(entryName) ? "<unnamed>" : entryName;
            nameLabel.style.opacity = string.IsNullOrEmpty(entryName) ? 0.6f : 1f;

            // A pinned row is one the search would otherwise have hidden. Saying so explains why it is
            // still there, and the rule stays out of the way when the search is empty.
            var pinned = index < _stickyCount && !string.IsNullOrEmpty(_search?.value);
            nameLabel.tooltip = pinned ? "Kept in view because it is selected." : entryName;

            var separator = element.Q<VisualElement>("Separator");
            separator.style.display = index == _stickyCount - 1 && _stickyCount < _filtered.Count
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            var warning = element.Q<Label>("Warning");
            var problem = DescribeProblem(entry, entryName);
            warning.text = problem == null ? string.Empty : "!";
            warning.tooltip = problem ?? string.Empty;
            warning.style.color = new Color(0.9f, 0.6f, 0.2f);
        }

        /// <summary>The reason this entry will not register cleanly, or null when it will.</summary>
        private string DescribeProblem(IData entry, string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
            {
                return "This entry has no name, so it has no key and cannot be registered.";
            }

            if (_document.Entries.Count(e => e.Key == entry.Key) > 1)
            {
                return "Another entry in this document has the same key.";
            }

            return _externalKeys.Contains(entry.Key)
                ? "A code registry or addressable asset already registers this key. One of the two has to go."
                : null;
        }

        #endregion

        #region Pinned entries

        /// <summary>
        /// One pinned entry.
        /// </summary>
        /// <remarks>
        /// Held two ways because only one type's document is open at a time. While its type is the
        /// open one there is a real entry to point at, and a rename moves the pin with it; while it is
        /// not, all that is left is the type, the key and the name it had when we last saw it - which
        /// is enough to draw a chip and to find the entry again when the type is opened.
        /// </remarks>
        private sealed class DataPin
        {
            public Type Type;
            public uint Key;
            public string Label;
            public IData Entry;
        }

        /// <summary>
        /// The strip of pinned entries under the toolbar: the ones being worked on together, each a
        /// click away whatever type the rail is on.
        /// </summary>
        /// <remarks>
        /// Hidden outright while nothing is pinned rather than left as an empty band, so a window
        /// nobody pins anything in looks exactly as it did before.
        /// </remarks>
        private VisualElement BuildPinRail()
        {
            _pinRail = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    alignItems = Align.Center,
                    flexShrink = 0f,
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 4,
                    paddingBottom = 1,
                    backgroundColor = PinRailBackground,
                    borderBottomWidth = 1,
                    borderBottomColor = PinRailRule,
                    display = DisplayStyle.None,
                },
            };

            return _pinRail;
        }

        private void RefreshPinRail()
        {
            if (_pinRail == null)
            {
                return;
            }

            RebindPins();
            StorePins();

            _pinRail.Clear();
            _pinRail.style.display = _pinned.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            foreach (var pin in _pinned)
            {
                _pinRail.Add(PinChip(pin));
            }
        }

        /// <summary>
        /// Points every pin of the open type at the entry it means, and drops the ones that are gone.
        /// </summary>
        /// <remarks>
        /// A revert - or any other reload - builds the document's entries again, so a pin holding one
        /// of the old objects is holding something the document has already thrown away. The key is
        /// what survives that, and the entry is what survives a rename, so each is used where it works.
        /// </remarks>
        private void RebindPins()
        {
            for (int i = _pinned.Count - 1; i >= 0; i--)
            {
                var pin = _pinned[i];

                if (_document == null || pin.Type != _document.DataType)
                {
                    pin.Entry = null;
                    continue;
                }

                if (pin.Entry != null && _document.Entries != null && !_document.Entries.Contains(pin.Entry))
                {
                    pin.Entry = null;
                }

                pin.Entry ??= _document.Entries?.FirstOrDefault(entry => entry != null && entry.Key == pin.Key);

                if (pin.Entry == null)
                {
                    _pinned.RemoveAt(i);
                    continue;
                }

                // A rename moves both, and the pin follows because it is holding the object rather
                // than the name it used to have.
                pin.Key = pin.Entry.Key;
                pin.Label = EntryLabel(pin.Entry);
            }
        }

        private VisualElement PinChip(DataPin pin)
        {
            bool showing = pin.Entry != null && _selection.Count > 0 && _selection[0] == pin.Entry;
            bool open = _document != null && pin.Type == _document.DataType;

            var chip = InspectorFilterBar.CreateChip(
                pin.Label,
                $"{pin.Type.Name}   ·   key 0x{pin.Key:X8}\n" +
                (open
                    ? "Click to open it. Right-click to unpin."
                    : "Click to switch to this type and open it. Right-click to unpin."),
                VslDataStore.GetColor(pin.Type),
                showing,
                () => ShowEntry(pin.Type, pin.Key));

            chip.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Unpin", _ => Unpin(pin));
                evt.menu.AppendAction("Unpin All", _ =>
                {
                    _pinned.Clear();
                    RefreshPinRail();
                });
            }));

            return chip;
        }

        private static string EntryLabel(IData entry) =>
            entry == null || string.IsNullOrEmpty(entry.Name) ? "<unnamed>" : entry.Name;

        /// <summary>
        /// The pin for an entry, if it has one.
        /// </summary>
        /// <remarks>
        /// A pin holds the document's type, not the entry's own. One document covers a whole family —
        /// the concrete subtype is a filter on the list, not a document of its own — so a pin recorded
        /// against the subtype would name a type the rail cannot open.
        /// </remarks>
        private DataPin FindPin(IData entry)
        {
            if (entry == null)
            {
                return null;
            }

            return _pinned.FirstOrDefault(pin => pin.Entry == entry ||
                (_document != null && pin.Type == _document.DataType && pin.Key == entry.Key));
        }

        private bool IsPinned(IData entry) => FindPin(entry) != null;

        private void TogglePin(IData entry)
        {
            if (entry == null || _document == null)
            {
                return;
            }

            var existing = FindPin(entry);
            if (existing != null)
            {
                _pinned.Remove(existing);
            }
            else
            {
                _pinned.Add(new DataPin
                {
                    Type = _document.DataType,
                    Key = entry.Key,
                    Label = EntryLabel(entry),
                    Entry = entry,
                });
            }

            RefreshPinRail();
        }

        private void Unpin(DataPin pin)
        {
            if (_pinned.Remove(pin))
            {
                RefreshPinRail();
            }
        }

        /// <summary>
        /// Selects an entry by type and key, switching the rail to that type first if it is elsewhere.
        /// </summary>
        /// <remarks>
        /// The rail asks about unsaved changes on the way, and a cancelled prompt leaves it where it
        /// was — so what got opened is checked afterwards rather than assumed.
        /// </remarks>
        private void ShowEntry(Type type, uint key)
        {
            _typeRail.Select(type);

            // AGAINST THE DOCUMENT'S OWNER, NOT THE ENTRY'S OWN TYPE. A family shares one document —
            // every terrain brush lives in TerrainStampData.vsl whatever subclass it is — and the rail
            // resolves a member to that owner, so a document opened for one is never opened AS one.
            // Comparing against the concrete type therefore returned here for every subclassed entry:
            // the rail moved, the family opened, and nothing was selected — which is precisely the
            // failure this method exists to prevent. It only bites the types that have a family, which
            // is why it survived.
            var owner = VslDataStore.GetDocumentOwner(type) ?? type;

            if (_document == null || _document.DataType != owner)
            {
                return;
            }

            var opened = _document.Entries?.FirstOrDefault(entry => entry != null && entry.Key == key);
            if (opened == null)
            {
                return;
            }

            SelectOnly(opened);
            RefreshEntryList();

            // The list draws from _filtered, which RefreshEntryList has just rebuilt with the selected
            // row at the top. Telling the ListView about it is what highlights the row; RefreshInspector
            // is what puts the fields on screen. Neither follows from setting _selection alone.
            _entryList?.SetSelectionWithoutNotify(new[] { 0 });
            RefreshInspector();
            UpdateStatus();
        }

        /// <summary>
        /// Reads the pins back. Stored as type, key and last known name, one pin per record.
        /// </summary>
        /// <remarks>
        /// The name is stored as well as the key because a pin whose type is not the open one has no
        /// entry to read a name off, and a rail of keys is a rail nobody can use. It is refreshed from
        /// the entry itself whenever that type is open.
        /// </remarks>
        private void RestorePins()
        {
            _pinned.Clear();
            _storedPins = EditorPrefs.GetString(PinnedPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(_storedPins))
            {
                return;
            }

            var known = VslDataStore.GetAuthoredTypes().ToList();

            foreach (var record in _storedPins.Split(RecordSeparator))
            {
                var parts = record.Split(FieldSeparator);
                if (parts.Length != 3 || !uint.TryParse(parts[1], out uint key))
                {
                    continue;
                }

                var type = known.FirstOrDefault(candidate => candidate.FullName == parts[0]);

                // A type that has gained a window of its own since it was pinned is not this window's
                // to open, and a chip that does nothing is worse than no chip.
                if (type == null || DataAuthoringWindows.HasWindow(type))
                {
                    continue;
                }

                _pinned.Add(new DataPin { Type = type, Key = key, Label = parts[2] });
            }
        }

        private void StorePins()
        {
            string text = string.Join(RecordSeparator.ToString(), _pinned.Select(pin =>
                string.Join(FieldSeparator.ToString(), pin.Type.FullName, pin.Key.ToString(), pin.Label)));

            if (text == _storedPins)
            {
                return;
            }

            _storedPins = text;
            EditorPrefs.SetString(PinnedPrefsKey, text);
        }

        #endregion

        #region Selection

        private void RefreshTypeRail()
        {
            _typeRail.Refresh();

            if (_document == null && _typeRail.SelectedType == null)
            {
                _typeRail.SelectFirst();
            }
        }

        /// <summary>
        /// Asks what to do about unsaved changes. Returns false when the user backed out.
        /// </summary>
        private bool ConfirmDiscardOrSave()
        {
            if (_document is not { IsDirty: true })
            {
                return true;
            }

            var choice = EditorUtility.DisplayDialogComplex("Unsaved data",
                $"{_document.DisplayName} has unsaved changes.", "Save", "Cancel", "Discard");

            switch (choice)
            {
                case 0:
                    return _document.Save();
                case 2:
                    return true;
                default:
                    return false;
            }
        }

        private void OpenType(Type dataType)
        {
            _document = dataType != null ? DataDocument.Load(dataType) : null;
            _selection.Clear();

            // Field names are per type, so a focus carried into another type would be meaningless.
            _focusedFields.Clear();
            RefreshExternalKeys();
            RefreshEntryList();
            UpdateStatus();
        }

        #endregion

        #region Entries

        private void RefreshEntryList()
        {
            _filtered.Clear();
            _stickyCount = 0;

            if (_document != null)
            {
                // Anything selected is dropped by the document if it was deleted elsewhere, so the
                // pinned set is intersected with what the document still holds.
                _selection.RemoveAll(e => !_document.Entries.Contains(e));

                foreach (var entry in _document.Entries)
                {
                    if (_selection.Contains(entry))
                    {
                        _filtered.Add(entry);
                        _stickyCount++;
                    }
                }

                var query = _search?.value;
                var typeFilter = _typeRail?.TypeFilter;

                foreach (var entry in _document.Entries)
                {
                    if (_selection.Contains(entry))
                    {
                        continue;
                    }

                    // The rail's type filter narrows a family's shared document to one of its concrete
                    // types. Pinned entries are exempt, for the same reason they are exempt from search.
                    if (typeFilter != null && entry.GetType() != typeFilter)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(query) ||
                        DataDocument.GetName(entry).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _filtered.Add(entry);
                    }
                }
            }
            else
            {
                _selection.Clear();
            }

            _entryList.itemsSource = _filtered;
            _entryList.Rebuild();

            // Written back explicitly, including when it is empty. Leaving a stale index behind is
            // what made a row unclickable: the list still believed it was selected, so clicking it
            // changed nothing and raised no event.
            var indices = new List<int>(_selection.Count);
            for (var i = 0; i < _filtered.Count; i++)
            {
                if (_selection.Contains(_filtered[i]))
                {
                    indices.Add(i);
                }
            }

            _entryList.SetSelectionWithoutNotify(indices);

            RefreshInspector();
            UpdateStatus();
        }

        private void RefreshInspector()
        {
            _inspectorPane.Clear();

            // Whatever is built next hands one back if it has one; the comparison sheet does not.
            _filterBar = null;
            _entryHeaderName = null;

            // The rail marks whichever pin the inspector is showing, so it is drawn from here rather
            // than from the selection: every path that changes what is inspected comes through.
            RefreshPinRail();

            if (_document == null || _selection.Count == 0)
            {
                _inspectorPane.Add(new Label(_document == null
                    ? "Select a data type."
                    : "Select an entry. Select several to compare them side by side.")
                {
                    style = { opacity = 0.6f, marginTop = 8, marginLeft = 6, whiteSpace = WhiteSpace.Normal },
                });
                UpdateStatus();
                return;
            }

            // One entry is just an inspector. Several is a comparison, and a comparison is a sheet —
            // the only question is which way round it runs.
            if (_selection.Count == 1 || !SupportsGrid())
            {
                _inspectorPane.Add(BuildSingleInspector());
                UpdateStatus();
                return;
            }

            _inspectorPane.Add(DataGridView.Build(new DataGridView.Context
            {
                Entries = _selection,
                FieldsAsRows = FieldsAsRows,
                FlagDifferences = FlagDifferences,
                EntryChanged = OnGridEntryChanged,
                FocusedFields = _focusedFields,
                FieldCheckedChanged = OnFieldChecked,
                AllFieldsCheckedChanged = OnAllFieldsChecked,
            }));

            UpdateStatus();
        }

        /// <summary>
        /// Ticking a field narrows what <c>Copy Prompt</c> writes. Deliberately does not rebuild the
        /// sheet — nothing on screen changes, and rebuilding would throw away the focus of whatever
        /// field was being edited.
        /// </summary>
        private void OnFieldChecked(string path, bool focused)
        {
            if (focused)
            {
                _focusedFields.Add(path);
            }
            else
            {
                _focusedFields.Remove(path);
            }

            UpdateStatus();
        }

        private void OnAllFieldsChecked(bool focused)
        {
            _focusedFields.Clear();

            if (focused)
            {
                foreach (var member in VslTypeSchema.Get(_document.DataType).Members)
                {
                    _focusedFields.Add(member.Name);
                }
            }

            // Rebuilt because every field's checkbox has to redraw in its new state.
            RefreshInspector();
        }

        /// <summary>
        /// Whether this type can be shown as a sheet.
        /// </summary>
        /// <remarks>
        /// A type with its own authoring view cannot: the grid works by moving individual member
        /// elements into cells, and a custom view is free to draw whatever it likes with no members to
        /// take apart. Those fall back to the stacked view rather than being shown a broken sheet.
        /// </remarks>
        private bool SupportsGrid() => VslDataStore.GetAuthoring(_document.DataType)?.View == null;

        private void OnGridEntryChanged(IData entry)
        {
            MarkDirty(entry);
            RefreshRow(entry);
        }

        private VisualElement BuildSingleInspector()
        {
            var root = new VisualElement { style = { flexGrow = 1f } };
            var body = BuildEntryInspector(_selection[0], out var header);

            root.Add(BuildEntryHeader(_selection[0]));

            // Outside the scroll: a filter bar that scrolled away with the fields it filters would be
            // gone exactly when it is wanted.
            if (header != null)
            {
                root.Add(header);
            }

            var scroll = new ScrollView
            {
                style = { flexGrow = 1f, paddingLeft = 6, paddingRight = 6, paddingTop = 4 },
            };

            if (body != null)
            {
                scroll.Add(body);
            }

            root.Add(scroll);
            return root;
        }

        /// <summary>
        /// The line above the open entry: what it is, and the pin that puts it in the rail.
        /// </summary>
        /// <remarks>
        /// The window had no header of its own before this - the entry's name is drawn as an ordinary
        /// member further down, and the type is on the rail. It exists because the pin needs somewhere
        /// to live that belongs to the entry being looked at rather than to the list or the document,
        /// and it earns the space by naming what the fields underneath belong to.
        /// </remarks>
        private VisualElement BuildEntryHeader(IData entry)
        {
            var color = VslDataStore.GetColor(_document.DataType);

            var header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    flexShrink = 0f,
                    paddingLeft = 8,
                    paddingRight = 6,
                    paddingTop = 4,
                    paddingBottom = 4,
                    borderLeftWidth = 3,
                    borderLeftColor = color,
                    borderBottomWidth = 1,
                    borderBottomColor = PinRailRule,
                    backgroundColor = new Color(color.r, color.g, color.b, 0.10f),
                },
            };

            _entryHeaderName = new Label(EntryLabel(entry))
            {
                tooltip = $"{_document.DataType.FullName}   ·   key 0x{entry.Key:X8}",
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    flexGrow = 1f,
                    flexShrink = 1f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                },
            };
            header.Add(_entryHeaderName);

            // The entry's own class rather than the document's: one document covers a family, and the
            // class worth opening is the one this entry actually is.
            header.Add(TypeScriptLocator.CreateButton(entry.GetType()));
            header.Add(BuildPinButton(entry, color));

            return header;
        }

        /// <summary>
        /// The pin, on the header of whatever is being inspected: it puts this entry in the rail under
        /// the toolbar, and takes it out again.
        /// </summary>
        /// <remarks>
        /// The state is drawn onto the button rather than announced anywhere, because the rail below
        /// is the announcement - what the button has to say is whether clicking it adds or removes.
        /// </remarks>
        private VisualElement BuildPinButton(IData entry, Color color)
        {
            var button = new Button
            {
                style =
                {
                    height = 18,
                    minWidth = 22,
                    marginLeft = 6,
                    marginTop = 0,
                    marginBottom = 0,
                    marginRight = 0,
                    paddingLeft = 4,
                    paddingRight = 4,
                    flexShrink = 0f,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                },
            };

            var icon = PinIcon();
            if (icon != null)
            {
                button.Add(new VisualElement
                {
                    style = { backgroundImage = new StyleBackground(icon), width = 14, height = 14, flexShrink = 0f },
                });
            }

            void Apply()
            {
                bool on = IsPinned(entry);

                button.tooltip = on
                    ? "Unpin. It leaves the rail under the toolbar."
                    : "Pin to the rail under the toolbar, a click away whatever type the rail is on.";

                button.style.opacity = on ? 1f : 0.6f;
                button.style.backgroundColor = on ? new Color(color.r, color.g, color.b, 0.30f) : StyleKeyword.Null;

                if (icon == null)
                {
                    button.text = on ? "Unpin" : "Pin";
                }
            }

            button.clicked += () =>
            {
                TogglePin(entry);
                Apply();
            };

            Apply();
            return button;
        }

        /// <summary>
        /// The first built-in icon that exists in this Unity version, or null to fall back to text.
        /// </summary>
        /// <remarks>
        /// Looked up by name and cached, because <c>IconContent</c> is happy to throw on a name a
        /// version does not have rather than returning nothing.
        /// </remarks>
        private Texture2D PinIcon()
        {
            if (_pinIconResolved)
            {
                return _pinIcon;
            }

            _pinIconResolved = true;

            foreach (var pinName in new[] { "pin", "pinned", "d_Favorite Icon", "Favorite Icon", "d_Favorite", "Favorite" })
            {
                try
                {
                    _pinIcon = EditorGUIUtility.IconContent(pinName)?.image as Texture2D;
                }
                catch (Exception)
                {
                    _pinIcon = null;
                }

                if (_pinIcon)
                {
                    return _pinIcon;
                }
            }

            return null;
        }

        private Texture2D _pinIcon;
        private bool _pinIconResolved;

        /// <summary>
        /// Builds one entry's editor, and whatever chrome the view wants pinned over it. Name is drawn
        /// like any other <c>[VslSerialize]</c> member, so there is no separate field for it — but
        /// editing it changes the row's label and its key, which is why every change refreshes the row
        /// rather than only marking the document dirty.
        /// </summary>
        private VisualElement BuildEntryInspector(IData entry, out VisualElement pinnedHeader)
        {
            // Handed the document the entry actually lives in, not the merged selection, so a custom
            // view sees the file it would be writing to.
            var view = DataAuthoringViewFactory.Resolve(_document.DataType);
            var body = view.CreateInspector(_document, entry, () =>
            {
                MarkDirty(entry);
                RefreshRow(entry);
            });

            // Read after the build, not before: a header is usually made out of what was drawn.
            pinnedHeader = (view as IDataAuthoringHeader)?.PinnedHeader;
            _filterBar = pinnedHeader as InspectorFilterBar;
            return body;
        }

        private void RefreshRow(IData entry)
        {
            var index = _filtered.IndexOf(entry);
            if (index >= 0)
            {
                _entryList.RefreshItem(index);
            }

            if (_entryHeaderName != null && _selection.Count == 1 && _selection[0] == entry)
            {
                _entryHeaderName.text = EntryLabel(entry);
            }

            // A rename moves the chip's label with it, and moves the key the pin is stored under.
            if (IsPinned(entry))
            {
                RefreshPinRail();
            }
        }

        /// <summary>Marks only the file the edited entry belongs to, so saving leaves the rest alone.</summary>
        private void MarkDirty(IData entry)
        {
            _document?.SetDirty(entry);
            UpdateStatus();
        }

        private void AddEntry()
        {
            if (_document == null)
            {
                return;
            }

            // The rail's filter already names a concrete type, so use it rather than asking again.
            var chosen = _typeRail?.TypeFilter;
            if (chosen == null)
            {
                var concrete = VslDataStore.GetConcreteTypes(_document.DataType);
                switch (concrete.Count)
                {
                    case 0:
                        Debug.LogError($"{_document.DataType.Name} has no concrete type to create.");
                        return;
                    case 1:
                        chosen = concrete[0];
                        break;
                    default:
                        // A family can hold several, and which one is not something to guess at.
                        ShowAddMenu(concrete);
                        return;
                }
            }

            AddEntryOfType(chosen);
        }

        private void ShowAddMenu(List<Type> concrete)
        {
            var menu = new GenericMenu();
            foreach (var type in concrete)
            {
                var captured = type;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.Name)), false, () => AddEntryOfType(captured));
            }

            menu.ShowAsContext();
        }

        private void AddEntryOfType(Type concreteType)
        {
            var entry = _document.Add(_document.NextNewName(concreteType), concreteType);
            if (entry == null)
            {
                return;
            }

            SelectOnly(entry);
            _search.value = string.Empty;
            RefreshEntryList();
            _entryList.ScrollToItem(_filtered.IndexOf(entry));
        }

        private void DuplicateSelected()
        {
            if (_document == null || _selection.Count == 0)
            {
                return;
            }

            // Copied from a snapshot: Duplicate appends to the same list the selection is drawn from.
            var copies = new List<IData>(_selection.Count);
            foreach (var entry in _selection.ToList())
            {
                var copy = _document.Duplicate(entry);
                if (copy != null)
                {
                    copies.Add(copy);
                }
            }

            if (copies.Count == 0)
            {
                return;
            }

            _selection.Clear();
            _selection.AddRange(copies);
            RefreshEntryList();
            _entryList.ScrollToItem(_filtered.IndexOf(copies[0]));
        }

        private void RemoveSelected()
        {
            if (_document == null || _selection.Count == 0)
            {
                return;
            }

            var prompt = _selection.Count == 1
                ? $"Delete '{DataDocument.GetName(_selection[0])}'?"
                : $"Delete these {_selection.Count} entries?";

            if (!EditorUtility.DisplayDialog("Delete entries", prompt, "Delete", "Cancel"))
            {
                return;
            }

            foreach (var entry in _selection.ToList())
            {
                _document.Remove(entry);
            }

            _selection.Clear();
            RefreshEntryList();
        }

        private void SelectOnly(IData entry)
        {
            _selection.Clear();
            if (entry != null)
            {
                _selection.Add(entry);
            }
        }

        #endregion

        #region Shortcuts

        /// <summary>
        /// Ctrl+A select all / none, Ctrl+C copy, Ctrl+V paste, Ctrl+D duplicate, D or Delete to
        /// delete.
        /// </summary>
        private void OnEntryShortcut(KeyDownEvent evt)
        {
            if (!CanUseEntryShortcuts(evt))
            {
                return;
            }

            var ctrl = evt.ctrlKey || evt.commandKey;

            switch (evt.keyCode)
            {
                case KeyCode.A when ctrl:
                    ToggleSelectAll();
                    break;
                case KeyCode.C when ctrl:
                    CopySelected();
                    break;
                case KeyCode.V when ctrl:
                    PasteEntries();
                    break;
                case KeyCode.D when ctrl:
                    DuplicateSelected();
                    break;
                case KeyCode.D when !evt.altKey && !evt.shiftKey:
                case KeyCode.Delete:
                    RemoveSelected();
                    break;
                default:
                    return;
            }

            evt.StopPropagation();
        }

        /// <summary>
        /// Whether a keystroke belongs to the entry list.
        /// </summary>
        /// <remarks>
        /// Text editing is excluded first and unconditionally. The search field lives in this pane, so
        /// without that guard typing a 'd' into it would delete the selection.
        /// </remarks>
        private bool CanUseEntryShortcuts(KeyDownEvent evt)
        {
            if (_document == null || _entryPane == null)
            {
                return false;
            }

            if (IsTextInput(evt.target as VisualElement) ||
                IsTextInput(rootVisualElement.panel?.focusController?.focusedElement as VisualElement))
            {
                return false;
            }

            return _pointerOverEntries ||
                   (evt.target is VisualElement target && (target == _entryPane || _entryPane.Contains(target)));
        }

        private static bool IsTextInput(VisualElement element) =>
            element != null && (element is TextField || element.GetFirstAncestorOfType<TextField>() != null);

        private void ToggleSelectAll()
        {
            // Scoped to what is on screen, which is what a select-all in a filtered list is expected to
            // mean. Everything already selected toggles back to nothing.
            var all = _filtered.Count > 0 && _selection.Count >= _filtered.Count;

            _selection.Clear();
            if (!all)
            {
                _selection.AddRange(_filtered);
            }

            RefreshEntryList();
        }

        private void CopySelected()
        {
            if (_selection.Count == 0)
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer = DataDocument.Copy(_selection);
        }

        private void PasteEntries()
        {
            var added = _document.Paste(EditorGUIUtility.systemCopyBuffer);
            if (added.Count == 0)
            {
                return;
            }

            _selection.Clear();
            _selection.AddRange(added);
            _search.value = string.Empty;
            RefreshEntryList();
            _entryList.ScrollToItem(_filtered.IndexOf(added[0]));
        }

        private void ImportRegistered()
        {
            if (_document == null)
            {
                return;
            }

            var imported = _document.ImportRegistered();
            Debug.Log(imported == 0
                ? $"No unclaimed {_document.DisplayName} instances were registered."
                : $"Imported {imported} {_document.DisplayName} {(imported == 1 ? "entry" : "entries")}. " +
                  "Delete the code registry that built them before saving, or both will claim the same keys.");

            RefreshEntryList();
        }

        /// <summary>
        /// Puts a description of what is open on the clipboard, ready to paste into an AI prompt.
        /// </summary>
        private void CopyPrompt()
        {
            if (_document == null)
            {
                return;
            }

            var text = DataPromptBuilder.Build(_document, _selection, _focusedFields);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log(DataPromptBuilder.Describe(_document, _selection));
        }

        private void RevealDocument()
        {
            if (_document == null)
            {
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<Object>(_document.AssetPath);
            if (asset == null)
            {
                Debug.Log($"{_document.AssetPath} does not exist yet. Save to create it.");
                return;
            }

            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        #endregion

        #region Saving

        private void SaveCurrent()
        {
            if (_document == null)
            {
                return;
            }

            // Bracketed here rather than in the document so the report covers the window's own refresh
            // too. Nested operations fold into the outermost one, so the document's bracket inside this
            // one still reports as a single line.
            VslSaveDiagnostics.Begin($"Save {_document.DisplayName}");
            try
            {
                if (!_document.Save())
                {
                    return;
                }

                using (VslSaveDiagnostics.Measure("ui"))
                {
                    RefreshExternalKeys();
                    RefreshEntryList();
                }
            }
            finally
            {
                VslSaveDiagnostics.End();
            }
        }

        private void RevertCurrent()
        {
            if (_document == null)
            {
                return;
            }

            if (_document.IsDirty &&
                !EditorUtility.DisplayDialog("Revert", $"Discard changes to {_document.DisplayName}?", "Revert", "Cancel"))
            {
                return;
            }

            _document.Revert();
            _selection.Clear();
            RefreshEntryList();
        }

        /// <summary>
        /// Works out which registered keys come from somewhere other than a VSL document, by taking
        /// everything the registry holds and removing everything the documents account for.
        /// </summary>
        private void RefreshExternalKeys()
        {
            _externalKeys.Clear();

            foreach (var data in GlobalDataRegistry.GetAll())
            {
                if (data != null)
                {
                    _externalKeys.Add(data.Key);
                }
            }

            // Asked of the registry rather than worked out by re-reading the data folder. The registry
            // already records which keys came from a document, and parsing every file again to rederive
            // that made a save read the whole corpus twice.
            foreach (var key in GlobalDataRegistry.GetDocumentKeys())
            {
                _externalKeys.Remove(key);
            }
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }

            if (_document == null)
            {
                _statusLabel.text = string.Empty;
                SetChromeEnabled(false);
                return;
            }

            var dirty = _document.IsDirty ? "*" : string.Empty;
            var comparing = _selection.Count > 1 ? $"  —  comparing {_selection.Count}" : string.Empty;

            // The path is only the whole story while the document fits in one file, so say how many
            // there are once it does not. Otherwise the status line quietly names a fraction of the data.
            var shards = _document.ShardCount;
            var location = shards > 1 ? $"{_document.AssetPath} +{shards - 1} more" : _document.AssetPath;

            _statusLabel.text = $"{_document.DisplayName}{dirty}  —  {_document.Entries.Count} entries{comparing}  —  {location}";
            SetChromeEnabled(true);
        }

        private void SetChromeEnabled(bool enabled)
        {
            _saveButton?.SetEnabled(enabled);
            _revertButton?.SetEnabled(enabled);
        }

        #endregion
    }
}
