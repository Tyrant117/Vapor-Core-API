using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Serialization;
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

        private DataTypeRail _typeRail;
        private ListView _entryList;
        private ToolbarSearchField _search;
        private VisualElement _inspectorPane;
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

        /// <summary>How many of the leading <see cref="_filtered"/> rows are pinned selection.</summary>
        private int _pinnedCount;

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
            }
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

            return toolbar;
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
            var name = DataDocument.GetName(entry);

            var nameLabel = element.Q<Label>("Name");
            nameLabel.text = string.IsNullOrEmpty(name) ? "<unnamed>" : name;
            nameLabel.style.opacity = string.IsNullOrEmpty(name) ? 0.6f : 1f;

            // A pinned row is one the search would otherwise have hidden. Saying so explains why it is
            // still there, and the rule stays out of the way when the search is empty.
            var pinned = index < _pinnedCount && !string.IsNullOrEmpty(_search?.value);
            nameLabel.tooltip = pinned ? "Kept in view because it is selected." : name;

            var separator = element.Q<VisualElement>("Separator");
            separator.style.display = index == _pinnedCount - 1 && _pinnedCount < _filtered.Count
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            var warning = element.Q<Label>("Warning");
            var problem = DescribeProblem(entry, name);
            warning.text = problem == null ? string.Empty : "!";
            warning.tooltip = problem ?? string.Empty;
            warning.style.color = new Color(0.9f, 0.6f, 0.2f);
        }

        /// <summary>The reason this entry will not register cleanly, or null when it will.</summary>
        private string DescribeProblem(IData entry, string name)
        {
            if (string.IsNullOrEmpty(name))
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
            _pinnedCount = 0;

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
                        _pinnedCount++;
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
            var scroll = new ScrollView
            {
                style = { flexGrow = 1f, paddingLeft = 6, paddingRight = 6, paddingTop = 4 },
            };

            var body = BuildEntryInspector(_selection[0]);
            if (body != null)
            {
                scroll.Add(body);
            }

            return scroll;
        }

        /// <summary>
        /// Builds one entry's editor. Name is drawn like any other <c>[VslSerialize]</c> member, so
        /// there is no separate field for it — but editing it changes the row's label and its key,
        /// which is why every change refreshes the row rather than only marking the document dirty.
        /// </summary>
        private VisualElement BuildEntryInspector(IData entry) =>
            DataAuthoringViewFactory
                .Resolve(_document.DataType)
                // Handed the document the entry actually lives in, not the merged selection, so a
                // custom view sees the file it would be writing to.
                .CreateInspector(_document, entry, () =>
                {
                    MarkDirty(entry);
                    RefreshRow(entry);
                });

        private void RefreshRow(IData entry)
        {
            var index = _filtered.IndexOf(entry);
            if (index >= 0)
            {
                _entryList.RefreshItem(index);
            }
        }

        /// <summary>Marks only the file the edited entry belongs to, so saving leaves the rest alone.</summary>
        private void MarkDirty(IData entry)
        {
            _document?.SetDirty();
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
            if (_document == null || !_document.Save())
            {
                return;
            }

            RefreshExternalKeys();
            RefreshEntryList();
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

            foreach (var (_, _, entries) in VslDataStore.ReadAllFromDisk())
            {
                foreach (var entry in entries)
                {
                    _externalKeys.Remove(entry.Key);
                }
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
            _statusLabel.text = $"{_document.DisplayName}{dirty}  —  {_document.Entries.Count} entries{comparing}  —  {_document.AssetPath}";
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
