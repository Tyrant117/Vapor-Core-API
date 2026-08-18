using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    /// <summary>
    /// The filter bar, drawn with controls a player has: a plain text field and a wrapping row of
    /// chips.
    /// </summary>
    /// <remarks>
    /// The editor's bar is the same <see cref="InspectorFilterModel"/> under a toolbar search field and
    /// editor styling. Everything about which chip means what, and what a query matches, is shared;
    /// this is only the drawing.
    /// </remarks>
    public sealed class InspectorFilterBarElement : VisualElement
    {
        public const string UssBar = "vapor-filter-bar";
        public const string UssSearch = "vapor-filter-bar__search";
        public const string UssChipRow = "vapor-filter-bar__chips";
        public const string UssChip = "vapor-filter-bar__chip";
        public const string UssChipOn = "vapor-filter-bar__chip--on";

        private const string ChipTooltip =
            "\n\nClick to show only this; click it again to show everything. Ctrl+click to add or remove it.";

        private readonly InspectorFilterModel _model = new();
        private readonly TextField _search;
        private readonly VisualElement _chipRow;

        public InspectorFilterBarElement()
        {
            AddToClassList(UssBar);
            style.flexShrink = 0f;

            _search = new TextField { isDelayed = false };
            _search.AddToClassList(UssSearch);
            _search.textEdition.placeholder = "Search fields…";
            _search.RegisterValueChangedCallback(evt => _model.Search(evt.newValue));
            Add(_search);

            _chipRow = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center },
            };
            _chipRow.AddToClassList(UssChipRow);
            Add(_chipRow);

            _model.ChipsChanged += RefreshChips;
        }

        public InspectorFilterModel Model => _model;

        public Label EmptyHint => _model.EmptyHint;

        public VisualElement GroupHeader
        {
            get => _model.GroupHeader;
            set => _model.GroupHeader = value;
        }

        public IReadOnlyList<InspectorFilterModel.Part> Parts => _model.Parts;

        public string Query => _model.Query;

        public void SetParts(IEnumerable<InspectorFilterModel.Part> parts) => _model.SetParts(parts);

        public void Apply() => _model.Apply();

        public void Search(string query)
        {
            _search.SetValueWithoutNotify(query ?? string.Empty);
            _model.Search(query);
        }

        public void Reset()
        {
            _search.SetValueWithoutNotify(string.Empty);
            _model.Reset();
        }

        /// <summary>Escape clears the search. True when the key was claimed.</summary>
        public bool HandleShortcut(KeyDownEvent evt)
        {
            if (evt == null || evt.keyCode != KeyCode.Escape || _model.Query.Length == 0)
            {
                return false;
            }

            Reset();
            return true;
        }

        private void RefreshChips()
        {
            _chipRow.Clear();
            _chipRow.Add(Chip("All", "Show everything again.", new Color(1f, 1f, 1f, 0.5f), _model.IsShowingEverything, _ => _model.ShowEverything()));

            foreach (var part in _model.Parts)
            {
                var captured = part;
                _chipRow.Add(Chip(part.Label, part.Tooltip + ChipTooltip, part.Accent, part.Visible,
                    additive => _model.ClickChip(captured, additive)));
            }
        }

        private static VisualElement Chip(string text, string tooltip, Color accent, bool on, Action<bool> onClick)
        {
            var border = on ? new Color(accent.r, accent.g, accent.b, 0.9f) : new Color(1f, 1f, 1f, 0.12f);

            var chip = new Label(text)
            {
                tooltip = tooltip,
                style =
                {
                    opacity = on ? 1f : 0.55f,
                    backgroundColor = on ? new Color(accent.r, accent.g, accent.b, 0.30f) : new Color(1f, 1f, 1f, 0.03f),
                    borderTopColor = border, borderBottomColor = border,
                    borderLeftColor = border, borderRightColor = border,
                },
            };

            chip.AddToClassList(UssChip);
            if (on)
            {
                chip.AddToClassList(UssChipOn);
            }

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
    }
}
