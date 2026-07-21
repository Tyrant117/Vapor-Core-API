using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    [UxmlElement]
    public partial class TileView : BindableElement, ISerializationCallbackReceiver
    {
        
        
        private static readonly BindingId s_ItemWidthProperty = (BindingId)nameof(ItemWidth);
        private static readonly BindingId s_ItemHeightProperty = (BindingId)nameof(ItemHeight);
        private static readonly BindingId s_HorizontalSpacingProperty = (BindingId)nameof(HorizontalSpacing);
        private static readonly BindingId s_VerticalSpacingProperty = (BindingId)nameof(VerticalSpacing);
        private static readonly BindingId s_AllowSelectionProperty = (BindingId)nameof(AllowSelection);
        private static readonly BindingId s_AllowDeselectionProperty = (BindingId)nameof(AllowDeselection);
        private static readonly BindingId s_FixedColumnCountProperty = (BindingId)nameof(FixedColumnCount);
        private static readonly BindingId s_ItemsSourceProperty = (BindingId)nameof(ItemsSource);
        private static readonly BindingId s_SelectedIndexProperty = (BindingId)nameof(SelectedIndex);
        private static readonly BindingId s_SelectedObjectProperty = (BindingId)nameof(SelectedObject);

        private IList _itemsSource;
        private Func<VisualElement> _makeItem;
        private Action<VisualElement, int> _bindItem;
        private ScrollView _scrollView;
        private VisualElement _contentContainer;
        private readonly List<RowData> _rows = new();
        private readonly Dictionary<int, VisualElement> _activeRows = new();
        private readonly Dictionary<int, VisualElement> _itemElements = new();
        private readonly Stack<VisualElement> _rowPool = new();

        private float _itemWidth = 200f;
        private float _itemHeight = 200f;
        private float _horizontalSpacing = 10f;
        private float _verticalSpacing = 10f;
        private bool _allowSelection = true;
        private bool _allowDeselection = true;
        private int _fixedColumnCount = -1;
        private int _cachedColumnsPerRow = 1;
        private int _firstVisibleRow = -1;
        private int _lastVisibleRow = -1;
        private int _selectedIndex = -1;
        private object _selectedObject;
        private VisualElement _selectedElement;

        public event Action<object, int> SelectionChanged;

        private class RowData
        {
            public int StartIndex;
            public int ItemCount;
        }

        [CreateProperty, UxmlAttribute]
        public float ItemWidth
        {
            get => _itemWidth;
            set
            {
                if (Mathf.Approximately(_itemWidth, value))
                    return;
                _itemWidth = value;
                NotifyPropertyChanged(in s_ItemWidthProperty);
                Rebuild();
            }
        }

        [CreateProperty, UxmlAttribute]
        public float ItemHeight
        {
            get => _itemHeight;
            set
            {
                if (Mathf.Approximately(_itemHeight, value))
                    return;
                _itemHeight = value;
                NotifyPropertyChanged(in s_ItemHeightProperty);
                Rebuild();
            }
        }

        [CreateProperty, UxmlAttribute]
        public float HorizontalSpacing
        {
            get => _horizontalSpacing;
            set
            {
                if (Mathf.Approximately(_horizontalSpacing, value))
                    return;
                _horizontalSpacing = value;
                NotifyPropertyChanged(in s_HorizontalSpacingProperty);
                Rebuild();
            }
        }

        [CreateProperty, UxmlAttribute]
        public float VerticalSpacing
        {
            get => _verticalSpacing;
            set
            {
                if (Mathf.Approximately(_verticalSpacing, value))
                    return;
                _verticalSpacing = value;
                NotifyPropertyChanged(in s_VerticalSpacingProperty);
                Rebuild();
            }
        }

        [CreateProperty, UxmlAttribute]
        public bool AllowSelection
        {
            get => _allowSelection;
            set
            {
                if (_allowSelection == value)
                    return;
                _allowSelection = value;
                NotifyPropertyChanged(in s_AllowSelectionProperty);
            }
        }

        [CreateProperty, UxmlAttribute]
        public bool AllowDeselection
        {
            get => _allowDeselection;
            set
            {
                if (_allowDeselection == value)
                    return;
                _allowDeselection = value;
                NotifyPropertyChanged(in s_AllowDeselectionProperty);
            }
        }
        
        [CreateProperty, UxmlAttribute]
        public int FixedColumnCount
        {
            get => _fixedColumnCount;
            set
            {
                if (_fixedColumnCount == value)
                    return;
                _fixedColumnCount = value;
                NotifyPropertyChanged(in s_FixedColumnCountProperty);
                UpdateWidthForFixedColumns();
                Rebuild();
            }
        }

        [CreateProperty]
        public IList ItemsSource
        {
            get => _itemsSource;
            set
            {
                if (Equals(_itemsSource, value))
                    return;

                if (_itemsSource is INotifyCollectionChanged oldCollection)
                {
                    oldCollection.CollectionChanged -= OnCollectionChanged;
                }

                _itemsSource = value;

                if (_itemsSource is INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += OnCollectionChanged;
                }

                NotifyPropertyChanged(in s_ItemsSourceProperty);
                Rebuild();
            }
        }

        [CreateProperty, UxmlAttribute]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (!AllowSelection)
                    return;
                
                if (_selectedIndex == value && !AllowDeselection)
                    return;

                if (value < -1 || (_itemsSource != null && value >= _itemsSource.Count))
                {
                    Debug.LogWarning($"SelectedIndex {value} is out of range. Valid range is -1 to {(_itemsSource?.Count - 1 ?? -1)}");
                    return;
                }

                if (_selectedIndex == value)
                {
                    // Deselect
                    _selectedIndex = -1;
                }
                else
                {
                    // Select
                    _selectedIndex = value;
                }
                
                UpdateSelectedObject();
                NotifyPropertyChanged(in s_SelectedIndexProperty);
                SelectionChanged?.Invoke(_selectedObject, _selectedIndex);
            }
        }

        [CreateProperty]
        public object SelectedObject
        {
            get => _selectedObject;
            set
            {
                if (_itemsSource == null)
                {
                    return;
                }
                
                if (value != null)
                {
                    int index = _itemsSource.IndexOf(value);
                    SelectedIndex = index;
                }
                else
                {
                    SelectedIndex = -1;
                }
            }
        }

        public VisualElement SelectedElement
        {
            get => _selectedElement;
            protected set
            {
                if (!AllowSelection)
                    return;
                
                if (_selectedElement != value)
                {
                    _selectedElement?.SetCheckedPseudoState(false);
                }

                _selectedElement = value;
                _selectedElement?.SetCheckedPseudoState(true);
            }
        }

        public Func<VisualElement> MakeItem
        {
            get => _makeItem;
            set
            {
                _makeItem = value;
                Rebuild();
            }
        }

        public Action<VisualElement, int> BindItem
        {
            get => _bindItem;
            set
            {
                _bindItem = value;
                Rebuild();
            }
        }

        public TileView()
        {
            AddToClassList("tile-view");
            _scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            _scrollView.StretchToParentSize();

            _contentContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1
                }
            };
            _scrollView.Add(_contentContainer);

            hierarchy.Add(_scrollView);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _scrollView.verticalScroller.valueChanged += OnScrollChanged;
        }

        public TileView(IList itemsSource, Func<VisualElement> makeItem, Action<VisualElement, int> bindItem, float itemWidth, float itemHeight, float horizontalSpacing,
            float verticalSpacing, int fixedColumnCount = -1) : this()
        {
            MakeItem = makeItem;
            BindItem = bindItem;
            ItemWidth = itemWidth;
            ItemHeight = itemHeight;
            HorizontalSpacing = horizontalSpacing;
            VerticalSpacing = verticalSpacing;
            _fixedColumnCount = fixedColumnCount;
            if (_fixedColumnCount != -1)
            {
                UpdateWidthForFixedColumns();
            }
            ItemsSource = itemsSource;
            Rebuild();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (!Mathf.Approximately(evt.oldRect.width, evt.newRect.width))
            {
                // int newColumnsPerRow = CalculateColumnsPerRow();
                // if (newColumnsPerRow != _cachedColumnsPerRow)
                // {
                //     _cachedColumnsPerRow = newColumnsPerRow;
                //     Rebuild();
                // }
                _cachedColumnsPerRow = CalculateColumnsPerRow();
                Rebuild();
            }
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Rebuild();
        }

        private int CalculateColumnsPerRow()
        {
            if (_fixedColumnCount != -1)
            {
                return Mathf.Max(1, _fixedColumnCount);
            }

            float availableWidth = resolvedStyle.width;
            if (availableWidth <= 0)
            {
                return 1;
            }

            int columns = Mathf.FloorToInt((availableWidth + _horizontalSpacing) / (_itemWidth + _horizontalSpacing));
            return Mathf.Max(1, columns);
        }

        private int CalculateTotalRows()
        {
            if (_itemsSource == null || _itemsSource.Count == 0)
                return 0;

            int columnsPerRow = _cachedColumnsPerRow;
            return Mathf.CeilToInt((float)_itemsSource.Count / columnsPerRow);
        }

        private void CalculateRows()
        {
            _rows.Clear();
            if (_itemsSource == null || _itemsSource.Count == 0)
                return;

            int columnsPerRow = _cachedColumnsPerRow;
            int itemCount = _itemsSource.Count;
            int currentIndex = 0;

            while (currentIndex < itemCount)
            {
                int itemsInThisRow = Mathf.Min(columnsPerRow, itemCount - currentIndex);
                _rows.Add(new RowData
                {
                    StartIndex = currentIndex,
                    ItemCount = itemsInThisRow
                });
                currentIndex += itemsInThisRow;
            }
        }

        private VisualElement MakeRow()
        {
            if (_rowPool.Count > 0)
            {
                var pooledRow = _rowPool.Pop();
                pooledRow.style.display = DisplayStyle.Flex;
                return pooledRow;
            }

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.NoWrap,
                    height = _itemHeight,
                    marginBottom = _verticalSpacing
                }
            };
            return row;
        }

        private void RecycleRow(VisualElement row)
        {
            row.Clear();
            row.style.display = DisplayStyle.None;
            if (!_rowPool.Contains(row))
            {
                _rowPool.Push(row);
            }
        }

        private void BindRow(VisualElement rowElement, int rowIndex)
        {
            if (_makeItem == null || _bindItem == null || rowIndex >= _rows.Count)
            {
                return;
            }

            rowElement.Clear();
            var rowData = _rows[rowIndex];

            for (int i = 0; i < rowData.ItemCount; i++)
            {
                int itemIndex = rowData.StartIndex + i;
                var item = _makeItem();
                if (item == null)
                {
                    continue;
                }

                item.style.width = _itemWidth;
                item.style.height = _itemHeight;
                item.style.marginBottom = 0;
                item.style.marginLeft = 0;
                item.style.marginTop = 0;
                item.style.marginRight = (i < rowData.ItemCount - 1) ? _horizontalSpacing : 0;

                if (AllowSelection)
                {
                    int capturedIndex = itemIndex;
                    if (itemIndex == _selectedIndex)
                    {
                        SelectedElement = item;
                    }
                    else
                    {
                        item.SetCheckedPseudoState(false);
                    }

                    item.RegisterCallback<ClickEvent>(evt =>
                    {
                        SelectedIndex = capturedIndex;
                        SelectedElement = SelectedIndex == capturedIndex ? item : null;
                        evt.StopPropagation();
                    });
                }

                _bindItem(item, itemIndex);
                rowElement.Add(item);
                _itemElements[itemIndex] = item;
            }
        }

        private void UpdateSelectedObject()
        {
            if (!AllowSelection)
                return;

            if (_itemsSource == null || _selectedIndex < 0 || _selectedIndex >= _itemsSource.Count)
            {
                if (_selectedObject != null)
                {
                    _selectedObject = null;
                    NotifyPropertyChanged(in s_SelectedObjectProperty);
                }

                return;
            }

            var newSelectedObject = _itemsSource[_selectedIndex];
            if (!Equals(_selectedObject, newSelectedObject))
            {
                _selectedObject = newSelectedObject;
                NotifyPropertyChanged(in s_SelectedObjectProperty);
            }
        }

        public void ClearSelection()
        {
            if (!AllowSelection)
            {
                return;
            }

            SelectedIndex = -1;
            SelectedElement = null;
        }

        public VisualElement GetVisualElementForIndex(int index)
        {
            if (index < 0 || _itemsSource == null || index >= _itemsSource.Count)
            {
                return null;
            }

            _itemElements.TryGetValue(index, out var element);
            return element;
        }

        private void CalculateVisibleRows()
        {
            if (_rows.Count == 0)
            {
                _firstVisibleRow = -1;
                _lastVisibleRow = -1;
                return;
            }

            float scrollOffset = _scrollView.scrollOffset.y;
            float viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
            float rowHeight = _itemHeight + _verticalSpacing;

            _firstVisibleRow = Mathf.Max(0, Mathf.FloorToInt(scrollOffset / rowHeight) - 1);
            _lastVisibleRow = Mathf.Min(_rows.Count - 1, Mathf.CeilToInt((scrollOffset + viewportHeight) / rowHeight) + 1);
        }

        private void UpdateVisibleRows()
        {
            CalculateVisibleRows();

            if (_firstVisibleRow == -1 || _lastVisibleRow == -1)
            {
                foreach (var kvp in _activeRows)
                {
                    RecycleRow(kvp.Value);
                }
                _activeRows.Clear();
                return;
            }

            var rowsToRemove = new List<int>();
            foreach (var kvp in _activeRows)
            {
                if (kvp.Key < _firstVisibleRow || kvp.Key > _lastVisibleRow)
                {
                    RecycleRow(kvp.Value);
                    rowsToRemove.Add(kvp.Key);
                }
            }

            foreach (var rowIndex in rowsToRemove)
            {
                _activeRows.Remove(rowIndex);
            }

            for (int i = _firstVisibleRow; i <= _lastVisibleRow; i++)
            {
                if (!_activeRows.ContainsKey(i))
                {
                    var row = MakeRow();
                    BindRow(row, i);

                    float rowHeight = _itemHeight + _verticalSpacing;
                    row.style.position = Position.Absolute;
                    row.style.top = i * rowHeight;
                    row.style.left = 0;
                    row.style.right = 0;

                    _contentContainer.Add(row);
                    _activeRows[i] = row;
                }
            }
        }

        private void OnScrollChanged(float newValue)
        {
            UpdateVisibleRows();
        }

        public void Rebuild()
        {
            foreach (var kvp in _activeRows)
            {
                RecycleRow(kvp.Value);
            }
            _activeRows.Clear();
            _itemElements.Clear();
            _contentContainer.Clear();

            if (_makeItem == null || _bindItem == null || _itemsSource == null)
            {
                _rows.Clear();
                if (_selectedIndex != -1)
                {
                    SelectedIndex = -1;
                }
                return;
            }

            if (_selectedIndex >= _itemsSource.Count)
            {
                SelectedIndex = -1;
            }
            else if (_selectedIndex >= 0)
            {
                UpdateSelectedObject();
            }

            _cachedColumnsPerRow = CalculateColumnsPerRow();
            CalculateRows();

            float totalHeight = _rows.Count * (_itemHeight + _verticalSpacing);
            _contentContainer.style.height = totalHeight;

            UpdateVisibleRows();
        }

        private void UpdateWidthForFixedColumns()
        {
            if (_fixedColumnCount <= 0)
            {
                style.width = StyleKeyword.Auto;
                return;
            }

            float calculatedWidth = (_fixedColumnCount * _itemWidth) + ((_fixedColumnCount - 1) * _horizontalSpacing);
            style.width = calculatedWidth;
        }

        #region - Serialization Callback -

        public void OnBeforeSerialize()
        {
            Rebuild();
        }
        public void OnAfterDeserialize()
        {
            
        }

        #endregion
    }
}
