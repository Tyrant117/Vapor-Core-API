using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class TypeSearchWindow : EditorWindow
    {
        public class Descriptor
        {
            public Descriptor(TypeSearchModel searchModel, string match, string synonym) : this(searchModel.Name, searchModel.Category, searchModel, match, synonym)
            {
            }

            public Descriptor(string name, string category, TypeSearchModel searchModel = null, string match = null, string synonym = null)
            {
                Category = category;
                Name = name;
                SearchModel = searchModel;
                NameMatch = match;
                SynonymMatch = synonym;
                IsLeaf = SearchModel != null;
            }

            public string Category { get; }
            public string Name { get; }
            public TypeSearchModel SearchModel { get; }
            public string NameMatch { get; }
            public string SynonymMatch { get; }
            public float MatchingScore { get; set; }
            public bool IsLeaf { get; }

            public string GetDisplayName() => string.IsNullOrEmpty(NameMatch) ? Name : NameMatch;
            public string GetUniqueIdentifier() => $"{Category}/{Name}";
        }

        // This custom string comparer is used to sort path properly (Attribute should be listed before Attribute from Curve for instance)
        public class CategoryComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                // Hack to sort no category items at the end
                if (string.IsNullOrEmpty(x)) return 1;
                if (string.IsNullOrEmpty(y)) return -1;

                var xIndex = x.IndexOf('/');
                var yIndex = y.IndexOf('/');

                var comparison = 0;
                if (xIndex > 0 && yIndex < 0)
                {
                    comparison = string.Compare(x.Substring(0, xIndex), y, StringComparison.OrdinalIgnoreCase);
                }
                else if (xIndex < 0 && yIndex > 0)
                {
                    comparison = string.Compare(x, y.Substring(0, yIndex), StringComparison.OrdinalIgnoreCase);
                }
                else if (xIndex >= 0 && yIndex >= 0)
                {
                    comparison = string.Compare(x.Substring(0, xIndex), y.Substring(0, yIndex), StringComparison.OrdinalIgnoreCase);
                }

                if (comparison != 0)
                    return comparison;

                // Deeper categories are sorted at the end
                if (xIndex > 0 || yIndex > 0)
                {
                    var xDepth = x.Count(c => c == '/');
                    var yDepth = y.Count(c => c == '/');
                    if (xDepth < yDepth)
                    {
                        return -1;
                    }

                    if (xDepth > yDepth)
                    {
                        return 1;
                    }
                }

                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Serializable]
        internal struct Settings
        {
            [SerializeField] private List<string> _favorites;

            public bool IsFavorite(Descriptor descriptor) => _favorites?.Contains(descriptor.GetUniqueIdentifier()) == true;

            public void AddFavorite(Descriptor descriptor)
            {
                var path = descriptor.GetUniqueIdentifier();
                if (_favorites?.Contains(path) == false)
                {
                    _favorites.Add(path);
                }
                else if (_favorites == null)
                {
                    _favorites = new List<string> { path };
                }
            }

            public void RemoveFavorite(Descriptor descriptor) => _favorites?.Remove(descriptor.GetUniqueIdentifier());
        }

        // private static readonly ProfilerMarker s_GetMatchesPerfMarker = new("BlueprintSearchWindow.GetMatches");
        private static readonly char[] s_MatchingSeparators = { ' ', '|', '_' };
        private static readonly CategoryComparer s_CategoryComparer = new();
        private static readonly List<string> s_PatternMatches = new();


        private const float k_DefaultWindowWidth = 700;
        private const float k_DefaultPanelWidth = 350;
        private const float k_MinWidth = 400f;
        private const float k_MinHeight = 320f;

        private ISearchProvider<TypeSearchModel> _searchProvider;
        private bool _groupUncategorized;
        private bool _hideFavorites;
        private TreeView _treeView;
        private TreeView _variantTreeview;
        private readonly List<TreeViewItemData<Descriptor>> _treeViewData = new();
        private TreeViewItemData<Descriptor> _favoriteCategory;
        private string _searchPattern;
        private TwoPaneSplitView _splitPanel;
        private ToolbarSearchField _searchField;
        private string _currentTypeLabel;

        private float _leftPanelWidth;
        private bool _hideDetailsPanel;
        private Settings _settings;
        private bool _isResizing;
        private Rect _originalWindowPos;
        private Vector3 _originalMousePos;

        private bool HasSearch => !string.IsNullOrEmpty(GetSearchPattern());

        public static void Show(Vector2 graphPosition, Vector2 screenPosition, ISearchProvider<TypeSearchModel> searchProvider, bool groupUncategorized, bool hideFavorites, string typeLabel = null)
        {
            CreateInstance<TypeSearchWindow>().Init(graphPosition, screenPosition, searchProvider, groupUncategorized, hideFavorites, typeLabel);
        }

        private void Init(Vector2 graphPosition, Vector2 screenPosition, ISearchProvider<TypeSearchModel> searchProvider, bool groupUncategorized, bool hideFavorites, string typeLabel = null)
        {
            _searchProvider = searchProvider;
            _searchProvider.Position = graphPosition;
            _groupUncategorized = groupUncategorized;
            _hideFavorites = hideFavorites;
            _currentTypeLabel = typeLabel;

            RestoreSettings(screenPosition);

            ShowPopup();

            Focus();

            wantsMouseMove = true;
        }

        private void CreateGUI()
        {
            if (EditorGUIUtility.isProSkin)
            {
                rootVisualElement.AddToClassList("dark");
            }

            rootVisualElement.style.borderTopWidth = 1f;
            rootVisualElement.style.borderTopColor = new StyleColor(Color.black);
            rootVisualElement.style.borderBottomWidth = 1f;
            rootVisualElement.style.borderBottomColor = new StyleColor(Color.black);
            rootVisualElement.style.borderLeftWidth = 1f;
            rootVisualElement.style.borderLeftColor = new StyleColor(Color.black);
            rootVisualElement.style.borderRightWidth = 1f;
            rootVisualElement.style.borderRightColor = new StyleColor(Color.black);
            rootVisualElement.ConstructFromResourcePath("Styles/SearchWindow", "Styles/SearchWindow");
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallbackOnce<GeometryChangedEvent>(OnFirstDisplay);

            _searchField = rootVisualElement.Q<ToolbarSearchField>();
            _searchField.RegisterCallback<ChangeEvent<string>>(OnSearchChanged);
            _searchField.RegisterCallback<KeyDownEvent>(OnKeyDown);
            if (!_currentTypeLabel.EmptyOrNull())
            {
                _searchField.parent.style.flexDirection = FlexDirection.Column;
                _searchField.parent.Add(new Label(_currentTypeLabel)
                {
                    style =
                    {
                        flexGrow = 1f,
                        marginBottom = 3f,
                        unityTextAlign = TextAnchor.MiddleCenter,
                    }
                });
            }

            var searchTextField = _searchField.Q<TextField>();
            searchTextField.selectAllOnFocus = false;
            searchTextField.selectAllOnMouseUp = false;

            _splitPanel = rootVisualElement.Q<TwoPaneSplitView>("SplitPanel");
            _splitPanel.fixedPaneInitialDimension = _leftPanelWidth;

            rootVisualElement.Q<VisualElement>("DetailsPanel");

            _treeView = rootVisualElement.Q<TreeView>("ListOfNodes");
            _treeView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _treeView.makeItem += MakeItem;
            _treeView.bindItem += (element, index) => BindItem(_treeView, element, index);
            _treeView.unbindItem += UnbindItem;
            _treeView.selectionChanged += OnSelectionChanged;
            _treeView.viewDataKey = null;

            _variantTreeview = rootVisualElement.Q<TreeView>("ListOfVariants");
            _variantTreeview.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _variantTreeview.makeItem += MakeItem;
            _variantTreeview.bindItem += (element, index) => BindItem(_variantTreeview, element, index);
            _variantTreeview.unbindItem += UnbindItem;
            _variantTreeview.viewDataKey = null;

            UpdateTree(_searchProvider.GetDescriptors(), _treeViewData, true, _groupUncategorized);
            _treeView.SetRootItems(_treeViewData);
            _treeView.RefreshItems();
            _treeView.SetSelectionById(_favoriteCategory.id);

            var resizer = rootVisualElement.Q<VisualElement>("Resizer");
            resizer.RegisterCallback<PointerDownEvent>(OnStartResize);
            resizer.RegisterCallback<PointerMoveEvent>(OnResize);
            resizer.RegisterCallback<PointerUpEvent>(OnEndResize);

            _searchField.Focus();
        }

        protected void OnLostFocus()
        {
            Close();
        }

        private void OnDisable()
        {
            SaveSettings();
        }

        #region - Event Callbacks -

        private void OnFirstDisplay(GeometryChangedEvent geometryChangedEvent)
        {
            // UpdateDetailsPanelVisibility
            if (_hideDetailsPanel)
            {
                _splitPanel.CollapseChild(1);
            }
            else
            {
                _splitPanel.UnCollapse();
            }

            var splitter = rootVisualElement.Q<VisualElement>("unity-dragline-anchor");
            splitter.style.display = _hideDetailsPanel ? DisplayStyle.None : DisplayStyle.Flex;
            _treeView.parent.style.flexShrink = 1;
            _splitPanel.fixedPaneInitialDimension = _leftPanelWidth;
        }

        private static VisualElement MakeItem() => new();

        private void BindItem(TreeView treeview, VisualElement element, int index)
        {
            var item = treeview.GetItemDataForIndex<Descriptor>(index);
            element.AddToClassList("treenode");
            var parent = element.GetFirstAncestorWithClass<VisualElement>("unity-tree-view__item");

            List<Label> labels = null;

            labels = HighlightedMatches(item.GetDisplayName().SplitTextIntoLabels("setting")).ToList();

            if (item.SynonymMatch != null)
            {
                labels?.AddRange(HighlightedMatches(new[] { new Label($" ({item.SynonymMatch})") }));
            }

            if (item.SearchModel != null)
            {
                if (HasSearch)
                {
                    var category = item.SearchModel.Category?.Replace("Types/", string.Empty).Replace('/','.');
                    element.Add(new Label(category)
                    {
                        style =
                        {
                            fontSize = 10,
                            color = Color.grey,
                            width = 100,
                            maxWidth = 100f,
                            flexGrow = 0f,
                            flexShrink = 0f,
                            overflow = Overflow.Hidden,
                            textOverflow = TextOverflow.Ellipsis,
                            unityTextAlign = TextAnchor.MiddleRight,
                        },
                        tooltip = category
                    });
                }

                if (_settings.IsFavorite(item))
                {
                    parent.AddToClassList("favorite");
                }

                if (item.SearchModel.SupportFavorite)
                {
                    var favoriteButton = new Button
                    {
                        name = "favoriteButton", 
                        tooltip = "Click toggle favorite state",
                        userData = item, 
                        style =
                        {
                            flexGrow = 0f,
                            flexShrink = 0f,
                        }
                    };
                    favoriteButton.RegisterCallback<ClickEvent>(OnAddToFavorite);
                    element.Add(favoriteButton);
                }

                parent.AddToClassList("treeleaf");
                // This is to handle double click on variant
                parent.RegisterCallback<ClickEvent>(OnAddNode);
            }
            // This is a category
            else if (item.Name != null)
            {
                if (treeview == _treeView)
                {
                    if (treeview.GetIdForIndex(index) == _favoriteCategory.id && !_hideFavorites)
                    {
                        element.AddToClassList("favorite");
                    }
                }

                labels[0].name = "categoryLabel"; // So we can retrieve it for custom color
                labels[0].AddToClassList("category");

                // This is to handle expand collapse on the whole category line (not only the small arrow)
                parent.RegisterCallback<ClickEvent>(OnToggleCategory);
            }

            if (labels != null)
            {
                var i = 0;
                var ve = new VisualElement()
                {
                    style =
                    {
                        flexShrink = 1f,
                        flexGrow = 1f,
                        flexDirection = FlexDirection.Row,
                        flexBasis = 0f,
                        overflow = Overflow.Hidden,
                        textOverflow = TextOverflow.Ellipsis,
                    }
                };
                foreach (var label in labels)
                {
                    label.tooltip = item.Name.ToHumanReadable();
                    label.AddToClassList("node-name");
                    ve.Add(label);
                }
                element.Insert(i, ve);

                // var spacer = new VisualElement();
                // spacer.AddToClassList("nodes-label-spacer");
                // element.Insert(i, spacer);
            }
        }

        private static void UnbindItem(VisualElement element, int index)
        {
            element.Clear();
            element.ClearClassList();

            var parent = element.GetFirstAncestorWithClass<VisualElement>("unity-tree-view__item");
            parent.RemoveFromClassList("favorite");
            parent.RemoveFromClassList("treeleaf");
            parent.RemoveFromClassList("separator");
            parent.UnregisterCallback<ClickEvent>(OnToggleCategory);
            parent.visible = true;
        }

        private static void OnToggleCategory(ClickEvent evt)
        {
            // The test on localPosition is to toggle expand state only when clicking on the left of the treeview item label
            if (evt.target is VisualElement element and not Toggle && evt.localPosition.x < 30)
            {
                var parent = element.GetFirstAncestorWithClass<VisualElement>("unity-tree-view__item");
                if (parent != null)
                {
                    var toggle = parent.Q<Toggle>();
                    toggle.value = !toggle.value;
                }
            }
        }

        private void OnStartResize(PointerDownEvent evt)
        {
            if (evt.button == (int)MouseButton.LeftMouse)
            {
                _isResizing = true;
                evt.target.CaptureMouse();
                _originalWindowPos = position;
                _originalMousePos = evt.position;
            }
        }

        private void OnResize(PointerMoveEvent evt)
        {
            if (_isResizing)
            {
                var delta = evt.position - _originalMousePos;
                var minWidth = _hideDetailsPanel ? k_MinWidth / 2f : k_MinWidth;
                var size = new Vector2(
                    Math.Max(_originalWindowPos.size.x + delta.x, minWidth),
                    Math.Max(_originalWindowPos.size.y + delta.y, k_MinHeight));
                if (_hideDetailsPanel)
                {
                    _splitPanel.CollapseChild(1);
                    _splitPanel.fixedPane.style.width = size.x;
                }


                position = new Rect(position.position, size);
                Repaint();
            }
        }

        private void OnEndResize(PointerUpEvent evt)
        {
            if (_hideDetailsPanel)
            {
                _leftPanelWidth = _splitPanel.fixedPaneInitialDimension;
            }

            evt.target.ReleaseMouse();
            _isResizing = false;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    return;
                case KeyCode.DownArrow:
                    if (_searchField.IsFocused())
                    {
                        _treeView.Focus();
                        if (_treeView.selectedIndex == -1)
                        {
                            _treeView.SetSelection(0);
                        }
                        else
                        {
                            _treeView.SetSelection(_treeView.selectedIndex + 1);
                        }
                    }

                    break;
                case KeyCode.UpArrow:
                    if (!_searchField.IsFocused() && _treeView.selectedIndex == 0)
                    {
                        _searchField.Focus();
                    }
                    else if (_searchField.IsFocused() && _treeView.selectedIndex > 0)
                    {
                        _treeView.SetSelection(_treeView.selectedIndex - 1);
                    }

                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_treeView.selectedItem is Descriptor descriptor)
                    {
                        Select(descriptor);
                    }

                    break;
                case KeyCode.RightArrow:
                case KeyCode.LeftArrow:
                    break;
                default:
                    if (!_searchField.IsFocused() && evt.modifiers is EventModifiers.None or EventModifiers.Shift)
                    {
                        _searchField.Focus();
                    }

                    break;
            }
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            _searchPattern = evt.newValue.Trim().ToLower();
            UpdateSearchResult(false);
        }

        private void OnAddToFavorite(ClickEvent evt)
        {
            if (evt.target is Button { userData: Descriptor descriptor } button)
            {
                if (_settings.IsFavorite(descriptor))
                {
                    var parent = button.GetFirstAncestorWithClass<VisualElement>("unity-tree-view__item");
                    parent.RemoveFromClassList("favorite");
                    _settings.RemoveFavorite(descriptor);
                    var idToRemove = _favoriteCategory.children.SingleOrDefault(x => x.data.Name == descriptor.Name && x.data.Category == descriptor.Category).id;
                    if (idToRemove > 0)
                    {
                        _treeView.TryRemoveItem(idToRemove);
                    }
                }
                else
                {
                    var parent = button.GetFirstAncestorWithClass<VisualElement>("unity-tree-view__item");
                    parent.AddToClassList("favorite");
                    _settings.AddFavorite(descriptor);
                    var newId = _treeView.viewController.GetAllItemIds().Max() + 1;
                    _treeView.AddItem(new TreeViewItemData<Descriptor>(newId, descriptor), _favoriteCategory.id);
                }

                if (!_hideDetailsPanel)
                {
                    // Refresh details panel because if the state has changed from the main panel, we must update the details panel
                    OnSelectionChanged(null);
                }
            }
        }

        private void OnAddNode(ClickEvent evt)
        {
            if (evt.target is not Button)
            {
                var treeView = ((VisualElement)evt.target).GetFirstAncestorOfType<TreeView>();
                if (evt.button == (int)MouseButton.LeftMouse && evt.clickCount == 2)
                {
                    Select((Descriptor)treeView.selectedItem);
                }
            }
        }

        #endregion

        #region - Layout -

        private void Select(Descriptor descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            if (!descriptor.IsLeaf)
            {
                return;
            }

            _searchProvider.Select(descriptor.SearchModel);
            Close();
        }

        private void UpdateTree(IEnumerable<TypeSearchModel> modelDescriptors, List<TreeViewItemData<Descriptor>> treeViewData, bool isMainTree, bool groupUncategorized)
        {
            var favorites = isMainTree ? new List<TreeViewItemData<Descriptor>>() : null;
            treeViewData.Clear();
            var id = 0;

            if (HasSearch)
            {
                var searchPattern = GetSearchPattern();
                var patternTokens = searchPattern?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var max = new SearchScore();
                var finalResults = new List<Descriptor> { null };
                SearchMultithreaded(searchPattern, patternTokens, modelDescriptors.ToList().AsReadOnly(), max, finalResults);
                if (max.Descriptor != null)
                {
                    finalResults[0] = max.Descriptor;
                }
                else
                {
                    finalResults.RemoveAt(0);
                }

                treeViewData.AddRange(finalResults.Select(res => new TreeViewItemData<Descriptor>(id++, res)));

                if (HasSearch && isMainTree)
                {
                    foreach (var treeViewItemData in treeViewData)
                    {
                        SortSearchResult(treeViewItemData);
                    }
                }
            }
            else
            {
                foreach (var modelDescriptor in modelDescriptors
                             .OrderBy(x => x.Category, s_CategoryComparer)
                             .ThenBy(x => x.Name.ToHumanReadable()))
                {
                    var category = !string.IsNullOrEmpty(modelDescriptor.Category) ? modelDescriptor.Category : (groupUncategorized ? "Miscellaneous" : string.Empty);
                    var path = category.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var currentFolders = treeViewData;
                    var matchingDescriptor = new Descriptor(modelDescriptor, null, null) { MatchingScore = 1f };
                    
                    foreach (var p in path)
                    {
                        var containerName = p;
                        if (currentFolders.All(x => x.data.Name != containerName))
                        {
                            var newFolder = new TreeViewItemData<Descriptor>(id++, new Descriptor(containerName, containerName), new List<TreeViewItemData<Descriptor>>());
                            currentFolders.Add(newFolder);
                            currentFolders = (List<TreeViewItemData<Descriptor>>)newFolder.children;
                        }
                        else
                        {
                            currentFolders = (List<TreeViewItemData<Descriptor>>)currentFolders.Single(x => x.data.Name == containerName).children;
                        }
                    }

                    // When no search, only add main variant (which is the first one)
                    currentFolders.Add(new TreeViewItemData<Descriptor>(id++, matchingDescriptor));

                    // But add any matching variant, even sub-variants even when there's no search pattern
                    if (isMainTree && _settings.IsFavorite(matchingDescriptor))
                    {
                        favorites.Add(new TreeViewItemData<Descriptor>(id++, matchingDescriptor));
                    }
                }

                if (isMainTree && !_hideFavorites)
                {
                    _favoriteCategory = new TreeViewItemData<Descriptor>(id, new Descriptor("Favorites", string.Empty), favorites);
                    treeViewData.Insert(0, _favoriteCategory);
                }
            }
        }

        private class SearchScore
        {
            public Descriptor Descriptor;
            public float Score;
        }

        private void SearchMultithreaded(string searchPattern, string[] patternTokens, IReadOnlyList<TypeSearchModel> modelDescriptors, SearchScore max, List<Descriptor> finalResults)
        {
            var count = Environment.ProcessorCount;
            var tasks = new Task[count];
            var localResults = new SearchScore[count];
            var queue = new ConcurrentQueue<SearchScore>();
            var itemsPerTask = (int)Math.Ceiling(modelDescriptors.Count / (float)count);

            for (var i = 0; i < count; i++)
            {
                var i1 = i;
                localResults[i1] = new SearchScore();
                tasks[i] = Task.Run(() =>
                {
                    var result = localResults[i1];
                    for (var j = 0; j < itemsPerTask; j++)
                    {
                        var index = j + itemsPerTask * i1;
                        if (index >= modelDescriptors.Count)
                        {
                            break;
                        }

                        var matchingDescriptor = GetDescriptor(modelDescriptors[index], searchPattern, patternTokens);
                        if (searchPattern.Length != 0 && matchingDescriptor == null)
                        {
                            continue;
                        }

                        var score = matchingDescriptor.MatchingScore;
                        if (score > result.Score)
                        {
                            result.Descriptor = matchingDescriptor;
                            result.Score = score;
                        }

                        queue.Enqueue(new SearchScore { Descriptor = matchingDescriptor, Score = score });
                    }
                });
            }

            Task.WaitAll(tasks);

            for (var i = 0; i < count; i++)
            {
                if (!(localResults[i].Score > max.Score))
                {
                    continue;
                }

                max.Descriptor = localResults[i].Descriptor;
                max.Score = localResults[i].Score;
            }

            PostprocessResults(queue, finalResults, max);
        }

        private const float k_ScoreCutOff = 0.33f;

        private static void PostprocessResults(IEnumerable<SearchScore> results, ICollection<Descriptor> items, SearchScore max)
        {
            foreach (var result in results)
            {
                var normalizedScore = result.Score / max.Score;
                if (result.Descriptor != null && result.Descriptor != max.Descriptor && normalizedScore > k_ScoreCutOff)
                {
                    items.Add(result.Descriptor);
                }
            }
        }

        private void SelectFirstNode(string currentSelectedItem)
        {
            SelectFirstNodeRecurse(_treeViewData, currentSelectedItem);

            if (_treeView.selectedIndex == -1)
            {
                _treeView.SetSelection(0);
            }

            _treeView.ScrollToItem(_treeView.selectedIndex);
        }

        private bool SelectFirstNodeRecurse(IEnumerable<TreeViewItemData<Descriptor>> data, string previousSelectedVariant)
        {
            foreach (var itemData in data)
            {
                if (itemData.data != null)
                {
                    if (previousSelectedVariant == null || previousSelectedVariant == itemData.data.Name)
                    {
                        _treeView.SetSelectionById(itemData.id);
                        return true;
                    }
                }

                if (SelectFirstNodeRecurse(itemData.children, previousSelectedVariant))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region - Searching -

        private void UpdateSearchResult(bool keepSelection)
        {
            var currentSelectedItem = _treeView.selectedItem as Descriptor;
            UpdateTree(_searchProvider.GetDescriptors(), _treeViewData, true, _groupUncategorized);
            _treeView.SetRootItems(_treeViewData);
            _treeView.RefreshItems();
            if (HasSearch)
            {
                // Workaround because ExpandAll can change the selection without calling the callback
                _treeView.ExpandAll();
                // Call OnSelectionChanged even if it didn't change so that search matches highlight are properly updated
                if (currentSelectedItem != _treeView.selectedItem || (HasSearch && currentSelectedItem == null))
                {
                    OnSelectionChanged(null);
                }

                SelectFirstNode(keepSelection ? currentSelectedItem?.Name : null);
            }
        }

        private static void SortSearchResult(TreeViewItemData<Descriptor> treeViewItemData)
        {
            if (!treeViewItemData.hasChildren)
            {
                return;
            }

            var children = (List<TreeViewItemData<Descriptor>>)treeViewItemData.children;
            children.Sort((x, y) => y.data.MatchingScore.CompareTo(x.data.MatchingScore));
            foreach (var child in treeViewItemData.children)
            {
                SortSearchResult(child);
            }
        }

        private Descriptor GetDescriptor(TypeSearchModel searchModel, string pattern, string[] patternTokens)
        {
            var score = GetVariantMatchScore(searchModel, pattern, patternTokens, out var match, out var synonym);
            if (!(score > 0f))
            {
                return null;
            }

            var descriptor = new Descriptor(searchModel, match, synonym) { MatchingScore = score };
            return descriptor;
        }

        private float GetVariantMatchScore(TypeSearchModel searchModel, string pattern, string[] patternTokens, out string match, out string synonymMatch)
        {
            synonymMatch = match = null;
            if (!HasSearch)
            {
                return 1f;
            }

            var initialPatternLength = pattern.Length;
            var fixedPattern = pattern;
            var score = GetTextMatchScore(searchModel.Name, ref pattern, patternTokens, out match);
            if (pattern.Length > 0)
            {
                score += GetTextMatchScore(searchModel.Category, ref fixedPattern, patternTokens, out _);
            }

            if (pattern.Length > 0 && searchModel.Synonyms != null)
            {
                foreach (var synonym in searchModel.Synonyms)
                {
                    score += GetTextMatchScore(synonym, ref pattern, patternTokens, out synonymMatch);
                    if (pattern.Length == 0)
                    {
                        break;
                    }
                }
            }

            return initialPatternLength > 0 ? (pattern.Length == 0 ? score : 0) : 1f;
        }

        private float GetTextMatchScore(string text, ref string pattern, string[] patternTokens, out string matchHighlight)
        {
            var score = 0f;
            matchHighlight = null;
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            if (string.IsNullOrEmpty(pattern))
            {
                return 100f;
            }

            var start = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (start != -1)
            {
                matchHighlight = text.Insert(start + pattern.Length, "#").Insert(start, "#@");
                score = 10f + (float)pattern.Length / text.Length;
                pattern = string.Empty;
                return score;
            }

            // Match all pattern tokens with the source tokens
            var sourceTokens = text.Split(s_MatchingSeparators, StringSplitOptions.RemoveEmptyEntries).ToList();
            var patternMatches = new List<string>(patternTokens.Length * sourceTokens.Count);
            if (sourceTokens.Count >= patternTokens.Length)
            {
                // s_PatternMatches.Clear();
                foreach (var token in patternTokens)
                {
                    foreach (var sourceToken in sourceTokens)
                    {
                        if (sourceToken.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceTokens.Remove(sourceToken);
                            // s_PatternMatches.Add(token);
                            patternMatches.Add(token);
                            pattern = pattern.Replace(token, string.Empty).Trim();
                            score += (float)token.Length / sourceToken.Length;
                            break;
                        }
                    }
                }

                if ( /*s_PatternMatches.Count*/patternMatches.Count > 0)
                {
                    matchHighlight = text;
                    foreach (var match in /*s_PatternMatches*/patternMatches)
                    {
                        matchHighlight = matchHighlight.Replace(match, $"#@{match}#", StringComparison.OrdinalIgnoreCase);
                    }

                    return score / text.Length;
                }
            }

            // Consider pattern as initials and match with source first letters (ex: SPSC => Set Position Shape Cone)
            var initialIndex = 0;
            var matchingIndices = new List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == ' ' || c == '|' || i == 0)
                {
                    while (!char.IsLetterOrDigit(c) && i < text.Length - 1)
                    {
                        c = text[++i];
                    }

                    if (i == text.Length - 1 && !char.IsLetterOrDigit(c))
                    {
                        break;
                    }

                    if (initialIndex < pattern.Length)
                    {
                        if (char.ToLower(c) == pattern[initialIndex])
                        {
                            matchingIndices.Add(i);
                            initialIndex++;
                            if (initialIndex == pattern.Length)
                            {
                                matchHighlight = new string(text.SelectMany((x, k) => matchingIndices.Contains(k) ? new[] { '#', '@', x, '#' } : new[] { x }).ToArray());
                                pattern = string.Empty;
                                return 1f;
                            }
                        }
                    }
                }
            }

            return score;
        }

        private string GetSearchPattern() => _searchPattern;

        #endregion

        #region - Settings -

        private void RestoreSettings(Vector2 screenPosition)
        {
            _hideDetailsPanel = SessionState.GetBool($"{nameof(TypeSearchWindow)}.{nameof(_hideDetailsPanel)}", true);

            _leftPanelWidth = SessionState.GetFloat($"{nameof(TypeSearchWindow)}.{nameof(_leftPanelWidth)}", k_DefaultPanelWidth);
            var windowWidth = SessionState.GetFloat($"{nameof(TypeSearchWindow)}.WindowWidth", _hideDetailsPanel ? _leftPanelWidth : k_DefaultWindowWidth);
            var windowHeight = SessionState.GetFloat($"{nameof(TypeSearchWindow)}.WindowHeight", k_MinHeight);
            var topLeft = new Vector2(screenPosition.x - 24, screenPosition.y - 16);
            position = new Rect(topLeft, new Vector2(windowWidth, windowHeight));

            var settingsAsJson = EditorPrefs.GetString($"{nameof(TypeSearchWindow)}.{nameof(_settings)}", null);
            _settings = !string.IsNullOrEmpty(settingsAsJson) ? JsonUtility.FromJson<Settings>(settingsAsJson) : default;
        }

        private void SaveSettings()
        {
            _leftPanelWidth = _treeView.resolvedStyle.width;
            SessionState.SetFloat($"{nameof(TypeSearchWindow)}.{nameof(_leftPanelWidth)}", _leftPanelWidth);
            SessionState.SetBool($"{nameof(TypeSearchWindow)}.{nameof(_hideDetailsPanel)}", _hideDetailsPanel);
            SessionState.SetFloat($"{nameof(TypeSearchWindow)}.WindowWidth", position.width);
            SessionState.SetFloat($"{nameof(TypeSearchWindow)}.WindowHeight", position.height);
            var json = JsonUtility.ToJson(_settings);
            EditorPrefs.SetString($"{nameof(TypeSearchWindow)}.{nameof(_settings)}", json);
        }

        #endregion

        #region - Helpers -

        private static IEnumerable<Label> HighlightedMatches(IEnumerable<Label> labels)
        {
            foreach (var label in labels)
            {
                if (label.text.IndexOf('@') < 0)
                {
                    yield return label;
                    continue;
                }

                var tokens = label.text.Split('#', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i];
                    var isHighlighted = token.StartsWith('@');
                    var newLabel = label;
                    if (i > 0)
                    {
                        newLabel = new Label();
                        if (label.ClassListContains("setting"))
                            newLabel.AddToClassList("setting");
                    }

                    newLabel.text = isHighlighted
                        ? token.Substring(1, token.Length - 1)
                        : token;

                    if (isHighlighted)
                    {
                        newLabel.AddToClassList("highlighted");
                    }

                    // Use left, middle and right classes to properly join together text which is split across multiple labels
                    if (tokens.Length > 1)
                    {
                        if (i == 0)
                            newLabel.AddToClassList("left-part");
                        else if (i == tokens.Length - 1)
                            newLabel.AddToClassList("right-part");
                        else
                            newLabel.AddToClassList("middle-part");
                    }

                    yield return newLabel;
                }
            }
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
        }

        #endregion
    }
}
