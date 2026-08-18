using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Draws a <c>Dictionary&lt;TKey, TValue&gt;</c> as a two-column table: a key, its value, and a
    /// button to drop the entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dictionary counterpart of <see cref="PaginatedList"/>, and deliberately the smaller of the
    /// two. A list has an order the user owns — reorder, duplicate, move to page — and a dictionary has
    /// none of that: an entry is identified by its key, so there is nothing to move it past. What is left
    /// is a header that counts the entries and pages through them, and one row apiece.
    /// </para>
    /// <para>
    /// Both columns are ordinary property fields, built from the entry properties the dictionary
    /// materialized. That is what makes this type-agnostic: a tag key draws with the tag picker, a
    /// <c>double</c> value draws as a number, and an authored class draws as the foldout it draws as
    /// anywhere else — none of which this file knows anything about.
    /// </para>
    /// </remarks>
    public class PaginatedDictionary : VisualElement
    {
        /// <inheritdoc cref="PaginatedList.Owner"/>
        public InspectorTreeElement Owner { get; }

        public InspectorTreeProperty Property { get; }

        // Elements
        public Foldout Foldout { get; private set; }
        public VisualElement Header { get; private set; }
        public Label Label { get; private set; }
        public Label PageNumber { get; private set; }
        public Label CountLabel { get; private set; }
        public VisualElement Container { get; private set; }
        public VisualElement Content { get; private set; }

        /// <summary>
        /// From [ListDrawer(editable:)], read the same way a list reads it: false means the entries are
        /// fixed, so no +/- and no per-row delete. The values themselves are still editable.
        /// </summary>
        private readonly bool _editable = true;

        /// <summary>Space between the key column and the value column, so the two read as two fields.</summary>
        private const int ColumnGap = 4;

        /// <summary>
        /// The row's own inset. Small on purpose: a dictionary row has no label to indent past, so the
        /// width belongs to the two fields.
        /// </summary>
        private const int RowInset = 3;

        private int _visibleMaxCount = 14;
        private int _currentPage = 1;

        public PaginatedDictionary(InspectorTreeElement owner, InspectorTreeProperty property, string header)
        {
            AddToClassList("unity-box");
            Owner = owner;
            Property = property;

            if (Property.TryGetAttribute<ListDrawerAttribute>(out var listDrawer))
            {
                _editable = listDrawer.Editable;
            }

            StyleBackground();
            DrawFoldout(header);
            DrawContent();
        }

        private void StyleBackground()
        {
            style.borderBottomColor = ContainerStyles.BorderColor;
            style.borderTopColor = ContainerStyles.BorderColor;
            style.borderRightColor = ContainerStyles.BorderColor;
            style.borderLeftColor = ContainerStyles.BorderColor;
            style.borderBottomLeftRadius = 3;
            style.borderBottomRightRadius = 3;
            style.borderTopLeftRadius = 3;
            style.borderTopRightRadius = 3;
            style.marginTop = 3;
            style.marginBottom = 3;
            style.marginLeft = 0;
            style.marginRight = 0;
            style.backgroundColor = ContainerStyles.BackgroundColor;
        }

        private void DrawFoldout(string header)
        {
            Foldout = new Foldout
            {
                name = "styled-foldout-foldout",
                viewDataKey = $"styled-paginated-dictionary__vdk_{header}"
            };

            var toggle = Foldout.Q<Toggle>();
            toggle.RegisterCallback<NavigationSubmitEvent>(evt => { evt.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            var toggleStyle = toggle.style;
            toggleStyle.marginTop = 0;
            toggleStyle.marginLeft = 0;
            toggleStyle.marginRight = 0;
            toggleStyle.marginBottom = 0;
            toggleStyle.backgroundColor = ContainerStyles.HeaderColor;

            var toggleContainerStyle = toggle.hierarchy[0].style;
            toggleContainerStyle.marginLeft = 3;
            toggleContainerStyle.marginTop = 3;
            toggleContainerStyle.marginBottom = 3;

            DrawHeader(header);
            toggle.hierarchy[0].Add(Header);

            Container = Foldout.Q<VisualElement>("unity-content");
            Container.style.marginTop = 0;
            Container.style.marginRight = 0;
            Container.style.marginBottom = 0;
            Container.style.marginLeft = 0;

            Foldout.value = false;
            Add(Foldout);
        }

        private void DrawHeader(string header)
        {
            Header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexGrow = 1f,
                }
            };

            Label = new Label(header)
            {
                name = "styled-header-box-label",
                style =
                {
                    marginLeft = 0,
                    marginRight = 0,
                    marginBottom = 0,
                    marginTop = 0,
                    paddingTop = 0,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };

            PageNumber = new Label($"{_currentPage}/{MaxPageCount()}")
            {
                style =
                {
                    minWidth = 31,
                    marginLeft = 0,
                    marginRight = 0,
                    marginBottom = 0,
                    marginTop = 0,
                    paddingTop = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };

            // A label rather than the list's editable count field: resizing a dictionary to a number is
            // meaningless when every entry has to be keyed by something.
            CountLabel = new Label(Property.DictionarySize.ToString())
            {
                tooltip = "Entries in this dictionary.",
                style =
                {
                    minWidth = 31,
                    marginRight = 7,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };

            Header.Add(Label);
            Header.Add(new VisualElement { style = { flexGrow = 1f } });
            Header.Add(HeaderButton("<", DecrementPage));
            Header.Add(PageNumber);
            Header.Add(HeaderButton(">", IncrementPage));
            if (_editable)
            {
                Header.Add(HeaderButton("-", RemoveLastEntry));
                Header.Add(HeaderButton("+", AddEntry));
            }

            Header.Add(CountLabel);
        }

        private static Button HeaderButton(string text, Action clicked)
        {
            return new Button(clicked)
            {
                text = text,
                style =
                {
                    minWidth = 20,
                    minHeight = 20,
                    paddingRight = 0,
                    paddingLeft = 0,
                    paddingBottom = 0,
                    paddingTop = 0,
                    marginLeft = 2,
                    marginRight = 2,
                    marginTop = 0,
                    marginBottom = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
        }

        private void DrawContent()
        {
            Assert.IsTrue(Property.IsDictionary, $"Trying to draw a dictionary for something that isn't one {Property.PropertyPath}");

            Content = new VisualElement();

            if (_currentPage > MaxPageCount())
            {
                _currentPage = MaxPageCount();
            }

            var rows = Property.DictionaryData;
            var indexStart = (_currentPage - 1) * _visibleMaxCount;
            var indexEnd = Mathf.Min(indexStart + _visibleMaxCount, rows.Count);
            Property.RequireRedraw = Redraw;

            for (var i = indexStart; i < indexEnd; i++)
            {
                Content.Add(DrawRow(rows[i], i));
            }

            Container.Add(Content);
            UpdateHeader();
        }

        /// <summary>
        /// One entry: its key, its value, and the button that drops it.
        /// </summary>
        /// <remarks>
        /// The two columns are given the same width rather than sized to their contents. A key is
        /// usually the longer of the two — a dotted tag name against a number — but a table whose
        /// columns move as its contents change is harder to read down than one that does not.
        /// </remarks>
        private VisualElement DrawRow(InspectorTreeProperty.DictionaryRow row, int index)
        {
            var rowElement = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexGrow = 1f,
                    alignItems = Align.Center,
                    paddingLeft = RowInset,
                    paddingRight = RowInset,
                    backgroundColor = index % 2 == 0 ? ContainerStyles.DarkInspectorBackgroundColor : ContainerStyles.InspectorBackgroundColor,
                }
            };

            var key = Column(row.Key);
            key.style.marginRight = ColumnGap;
            rowElement.Add(key);
            rowElement.Add(Column(row.Value));

            if (_editable)
            {
                var captured = index;
                rowElement.Add(new Button(() => Property.RemoveEntryAt(captured))
                {
                    text = "x",
                    style =
                    {
                        maxHeight = 31,
                        alignSelf = Align.Center,
                    }
                });
            }

            return rowElement;
        }

        /// <summary>
        /// One column, stretched to half the row.
        /// </summary>
        /// <remarks>
        /// Only the wrapper is sized. It is the row's direct child, so flex-basis on it is a width —
        /// while everything inside it sits in a column, where the same property is a <i>height</i> and
        /// setting it to zero flattens the row to a hairline. The elements below it already fill the
        /// wrapper's width, which is what a column lays its children out to do.
        /// </remarks>
        private VisualElement Column(InspectorTreeProperty property)
        {
            var element = new InspectorTreeRootElement(Owner, property);
            element.style.flexGrow = 1f;
            element.style.flexBasis = 0f;

            // Without this a column refuses to shrink below the width of its contents, and two of them
            // that will not shrink push the delete button off the end of the row.
            element.style.minWidth = 0f;

            var field = element.Q<InspectorTreeFieldElement>();
            if (field?.View != null)
            {
                field.View.style.flexGrow = 1f;
            }

            return element;
        }

        public void Redraw()
        {
            Content.RemoveFromHierarchy();
            DrawContent();
        }

        #region - Callbacks -
        private void IncrementPage()
        {
            if (_currentPage < MaxPageCount())
            {
                _currentPage++;
                Redraw();
            }
        }

        private void DecrementPage()
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                Redraw();
            }
        }

        private void AddEntry()
        {
            Property.AddEntry();

            // Jump to wherever the new entry landed, which is the end. An entry added onto a page the
            // user cannot see reads as nothing having happened.
            _currentPage = MaxPageCountAfter(Property.DictionarySize + 1);
        }

        private void RemoveLastEntry() => Property.RemoveLastEntry();
        #endregion

        #region - Helpers -
        private int MaxPageCount() => MaxPageCountAfter(Property.DictionarySize);

        private int MaxPageCountAfter(int count) => Mathf.Max(1, Mathf.CeilToInt(count * 1f / _visibleMaxCount));

        private void UpdateHeader()
        {
            PageNumber.text = $"{_currentPage}/{MaxPageCount()}";
            CountLabel.text = Property.DictionarySize.ToString();
        }
        #endregion
    }
}
