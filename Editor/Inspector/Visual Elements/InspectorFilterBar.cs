using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// A row of toggle chips over a member search, for an inspector that is long enough to need
    /// narrowing: one chip per part of what is on screen — the object itself, then each component or
    /// extension it carries — and a search that finds a field by name inside whatever the chips left
    /// showing.
    /// </summary>
    /// <remarks>
    /// The editor's chrome over <see cref="InspectorFilterModel"/>, which is where the actual filtering
    /// lives. The split is not ceremony: the same behaviour has to run in a player, where
    /// <see cref="ToolbarSearchField"/> and editor styling do not exist, so what is shared is the
    /// engine and what is not is exactly this file.
    /// </remarks>
    public sealed class InspectorFilterBar : VisualElement
    {
        private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.15f);
        private static readonly Color BarRule = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color AllChipAccent = new Color(1f, 1f, 1f, 0.5f);

        private const string SearchTooltip =
            "Find a field by name, anywhere in what the chips are showing. Fuzzy: \"entk\" finds Enabled Ticks. Ctrl+F to jump here, Esc to clear.";

        private const string ChipTooltip =
            "\n\nClick to show only this; click it again to show everything. Ctrl+click to add or remove it from what is shown.";

        private readonly InspectorFilterModel _model = new();
        private readonly ToolbarSearchField _search;
        private readonly VisualElement _chipRow;

        private Action _onAdd;
        private string _addTooltip;

        public InspectorFilterBar()
        {
            style.flexShrink = 0f;
            style.paddingLeft = 6;
            style.paddingRight = 6;
            style.paddingTop = 4;
            style.paddingBottom = 2;
            style.backgroundColor = BarBackground;
            style.borderBottomWidth = 1;
            style.borderBottomColor = BarRule;

            _search = new ToolbarSearchField
            {
                tooltip = SearchTooltip,
                style = { flexGrow = 1f, flexShrink = 1f, flexBasis = 0f, minWidth = 40, marginLeft = 0, marginRight = 0 },
            };
            _search.RegisterValueChangedCallback(evt => _model.Search(evt.newValue));

            var searchRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            searchRow.Add(_search);
            Add(searchRow);

            _chipRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 4 },
            };
            Add(_chipRow);

            _model.ChipsChanged += RefreshChips;
        }

        /// <summary>The filtering itself, for a host that wants at it directly.</summary>
        public InspectorFilterModel Model => _model;

        /// <inheritdoc cref="InspectorFilterModel.EmptyHint"/>
        public Label EmptyHint => _model.EmptyHint;

        /// <inheritdoc cref="InspectorFilterModel.GroupHeader"/>
        public VisualElement GroupHeader
        {
            get => _model.GroupHeader;
            set => _model.GroupHeader = value;
        }

        public IReadOnlyList<InspectorFilterModel.Part> Parts => _model.Parts;

        public string Query => _model.Query;

        /// <summary>Puts a + at the end of the chip row. Without one, the row ends at the last chip.</summary>
        public void SetAddAction(Action onAdd, string tooltip)
        {
            _onAdd = onAdd;
            _addTooltip = tooltip;
            RefreshChips();
        }

        /// <inheritdoc cref="InspectorFilterModel.SetParts"/>
        public void SetParts(IEnumerable<InspectorFilterModel.Part> parts) => _model.SetParts(parts);

        /// <inheritdoc cref="InspectorFilterModel.ShowEverything"/>
        public void ShowEverything() => _model.ShowEverything();

        /// <inheritdoc cref="InspectorFilterModel.Apply"/>
        public void Apply() => _model.Apply();

        /// <inheritdoc cref="InspectorFilterModel.ClickChip(int, bool)"/>
        public void ClickChip(int index, bool additive = false) => _model.ClickChip(index, additive);

        /// <inheritdoc cref="InspectorFilterModel.IsPartShown"/>
        public bool IsPartShown(int index) => _model.IsPartShown(index);

        /// <summary>Sets the search text as if it had been typed.</summary>
        public void Search(string query)
        {
            _search.SetValueWithoutNotify(query ?? string.Empty);
            _model.Search(query);
        }

        /// <summary>Back to the whole object: every chip on, no search.</summary>
        public void Reset()
        {
            _search.SetValueWithoutNotify(string.Empty);
            _model.Reset();
        }

        /// <summary>Ctrl+F focuses the search, Escape clears it. True when the key was one of those.</summary>
        public bool HandleShortcut(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return false;
            }

            if ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.F)
            {
                var input = _search.Q<TextField>();
                if (input != null)
                {
                    input.Focus();
                }
                else
                {
                    _search.Focus();
                }

                return true;
            }

            if (evt.keyCode != KeyCode.Escape)
            {
                return false;
            }

            Reset();
            return true;
        }

        #region Chips

        private void RefreshChips()
        {
            _chipRow.Clear();

            _chipRow.Add(Chip("All", "Show everything again.", AllChipAccent, _model.IsShowingEverything, _ => _model.ShowEverything()));

            foreach (var part in _model.Parts)
            {
                var captured = part;
                _chipRow.Add(Chip(part.Label, part.Tooltip + ChipTooltip, part.Accent, part.Visible,
                    additive => _model.ClickChip(captured, additive)));
            }

            if (_onAdd == null)
            {
                return;
            }

            _chipRow.Add(new Button(_onAdd)
            {
                text = "+",
                tooltip = _addTooltip,
                style =
                {
                    width = 22,
                    height = 18,
                    marginLeft = 2,
                    marginRight = 0,
                    marginTop = 0,
                    marginBottom = 3,
                    paddingLeft = 0,
                    paddingRight = 0,
                    flexShrink = 0f,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
            });
        }

        private static VisualElement Chip(string text, string tooltip, Color accent, bool on, Action<bool> onClick)
        {
            var border = on ? new Color(accent.r, accent.g, accent.b, 0.9f) : new Color(1f, 1f, 1f, 0.12f);

            var chip = new Label(text)
            {
                tooltip = tooltip,
                style =
                {
                    fontSize = 11,
                    height = 18,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    paddingLeft = 7,
                    paddingRight = 7,
                    marginRight = 4,
                    marginBottom = 3,
                    flexShrink = 0f,
                    opacity = on ? 1f : 0.55f,
                    backgroundColor = on ? new Color(accent.r, accent.g, accent.b, 0.30f) : new Color(1f, 1f, 1f, 0.03f),
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = border,
                    borderBottomColor = border,
                    borderLeftColor = border,
                    borderRightColor = border,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                },
            };

            chip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                onClick(evt.ctrlKey || evt.commandKey);
                evt.StopPropagation();
            });

            return chip;
        }

        #endregion

        #region Walking what is drawn

        /// <inheritdoc cref="InspectorFilterModel.CollectRows"/>
        public static void CollectRows(VisualElement container, List<VisualElement> rows) =>
            InspectorFilterModel.CollectRows(container, rows);

        /// <inheritdoc cref="InspectorFilterModel.CollectRowsExcept"/>
        public static VisualElement CollectRowsExcept(VisualElement container, VisualElement carveOut, List<VisualElement> rows) =>
            InspectorFilterModel.CollectRowsExcept(container, carveOut, rows);

        /// <inheritdoc cref="InspectorFilterModel.Matches"/>
        public static bool Matches(string candidate, string query) =>
            InspectorFilterModel.Matches(candidate, query);

        #endregion
    }
}
