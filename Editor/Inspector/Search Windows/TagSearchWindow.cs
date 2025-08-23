using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class TagSearchWindow<TNode> : EditorWindow
    {
        public class Descriptor
        {
            public Descriptor(TagSearchModel<TNode> searchModel, string match, string synonym) : this(searchModel.Name, searchModel, match, synonym) { }

            public Descriptor(string name, TagSearchModel<TNode> searchModel = null, string match = null, string synonym = null)
            {
                Name = name;
                var splitIdx = name.LastIndexOf('.');
                DisplayName = splitIdx != -1 ? name[(splitIdx + 1)..] : name;
                SearchModel = searchModel;
                NameMatch = match;
                SynonymMatch = synonym;
                IsLeaf = SearchModel != null;
            }

            public void SetSearchModel(TagSearchModel<TNode> modelDescriptor)
            {
                Name = modelDescriptor.Name;
                SearchModel = modelDescriptor;
                NameMatch = null;
                SynonymMatch = null;
                IsLeaf = SearchModel != null;
            }

            public string Name { get; private set; }
            public string DisplayName { get; }
            public TagSearchModel<TNode> SearchModel { get; private set; }
            public string NameMatch { get; private set; }
            public string SynonymMatch { get; private set; }
            public float MatchingScore { get; set; }
            public bool IsLeaf { get; private set; }

            public string GetDisplayName() => string.IsNullOrEmpty(NameMatch) ? DisplayName : NameMatch;

            public string GetUniqueIdentifier() => Name;
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
        private readonly char[] _matchingSeparators = { ' ', '|', '_' };

        private const float k_DefaultPanelWidth = 350;
        private const float k_MinWidth = 400f;
        private const float k_MinHeight = 320f;

        private ISearchProvider<TagSearchModel<TNode>> _searchProvider;
        private TreeView _treeView;
        private readonly List<TreeViewItemData<Descriptor>> _treeViewData = new();
        private string _searchPattern;
        private ToolbarSearchField _searchField;

        private float _panelWidth;
        private Settings _settings;
        private bool _isResizing;
        private Rect _originalWindowPos;
        private Vector3 _originalMousePos;

        private bool HasSearch => !string.IsNullOrEmpty(GetSearchPattern());

        public static TagSearchWindow<TNode> Show<TWindow>(Vector2 graphPosition, Vector2 screenPosition, ISearchProvider<TagSearchModel<TNode>> searchProvider) where TWindow : TagSearchWindow<TNode>
        {
            var window = CreateInstance<TWindow>();
            window.Init(graphPosition, screenPosition, searchProvider);
            return window;
        }

        private void Init(Vector2 graphPosition, Vector2 screenPosition, ISearchProvider<TagSearchModel<TNode>> searchProvider)
        {
            _searchProvider = searchProvider;
            _searchProvider.Position = graphPosition;

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
            rootVisualElement.ConstructFromResourcePath("Styles/GameplayTagSearchWindow", "Styles/GameplayTagSearchWindow");
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            _searchField = rootVisualElement.Q<ToolbarSearchField>();
            _searchField.RegisterCallback<ChangeEvent<string>>(OnSearchChanged);
            _searchField.RegisterCallback<KeyDownEvent>(OnKeyDown);

            var searchTextField = _searchField.Q<TextField>();
            searchTextField.selectAllOnFocus = false;
            searchTextField.selectAllOnMouseUp = false;

            rootVisualElement.Q<VisualElement>("DetailsPanel");

            _treeView = rootVisualElement.Q<TreeView>("ListOfNodes");
            _treeView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _treeView.makeItem += MakeItem;
            _treeView.bindItem += (element, index) => BindItem(_treeView, element, index);
            _treeView.unbindItem += UnbindItem;
            _treeView.selectionChanged += OnSelectionChanged;
            _treeView.viewDataKey = null;

            if (_searchProvider.AllowMultiSelect)
            {
                rootVisualElement.Q<Button>("Add").clicked += () => { };
                rootVisualElement.Q<Button>("All").clicked += OnMultiSelectAll;
                rootVisualElement.Q<Button>("None").clicked += OnMultiSelectNone;
            }
            else
            {
                rootVisualElement.Q<Button>("Add").Hide();
                rootVisualElement.Q<Button>("All").Hide();
                rootVisualElement.Q<Button>("None").Hide();
            }

            rootVisualElement.Q<Button>("Confirm").clicked += OnConfirmMultiSelect;

            UpdateTree(_searchProvider.GetDescriptors(), _treeViewData, true);
            _treeView.SetRootItems(_treeViewData);
            _treeView.RefreshItems();

            var resizer = rootVisualElement.Q<VisualElement>("Resizer");
            resizer.RegisterCallback<PointerDownEvent>(OnStartResize);
            resizer.RegisterCallback<PointerMoveEvent>(OnResize);
            resizer.RegisterCallback<PointerUpEvent>(OnEndResize);

            _treeView.ExpandAll();
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
        private void OnMultiSelectAll()
        {
            foreach (var m in _searchProvider.GetDescriptors())
            {
                if (m.Name == "None")
                {
                    continue;
                }

                m.SetToggle(true);
            }

            UpdateSearchResult(true);
        }

        private void OnMultiSelectNone()
        {
            foreach (var m in _searchProvider.GetDescriptors())
            {
                m.SetToggle(false);
            }
            UpdateSearchResult(true);
        }

        private void OnConfirmMultiSelect()
        {
            if (_searchProvider.SelectMany(_searchProvider.GetDescriptors().Where(m => m.IsToggled && !m.IsMixed).ToArray()))
            {
                Close();
            }
        }

        private static VisualElement MakeItem() => new();

        private void BindItem(TreeView treeview, VisualElement element, int index)
        {
            var item = treeview.GetItemDataForIndex<Descriptor>(index);
            element.AddToClassList("treenode");
            var parent = element.GetFirstAncestorWithClass<VisualElement>("unity-tree-view__item");
            parent.RegisterCallback<ClickEvent>(OnDoubleClickEntry);

            var labels = HighlightedMatches(item.GetDisplayName().SplitTextIntoLabels("setting")).ToList();
            if (item.SynonymMatch != null)
            {
                labels.AddRange(HighlightedMatches(new[] { new Label($" ({item.SynonymMatch})") }));
            }
            
            item.SearchModel.EntryElement.LabelContainer.Clear();
            foreach (var label in labels)
            {
                label.tooltip = item.Name.ToHumanReadable();
                label.AddToClassList("node-name");
                item.SearchModel.EntryElement.LabelContainer.Add(label);
            }

            element.Add(item.SearchModel.EntryElement);
        }

        private static void UnbindItem(VisualElement element, int index)
        {
            element.Clear();
            element.ClearClassList();
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
                var minWidth = k_MinWidth / 2f;
                var size = new Vector2(
                    Math.Max(_originalWindowPos.size.x + delta.x, minWidth),
                    Math.Max(_originalWindowPos.size.y + delta.y, k_MinHeight));

                position = new Rect(position.position, size);
                Repaint();
            }
        }

        private void OnEndResize(PointerUpEvent evt)
        {
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

        private void OnDoubleClickEntry(ClickEvent evt)
        {
            if (evt.target is not Button)
            {
                var treeView = ((VisualElement)evt.target).GetFirstAncestorOfType<TreeView>();
                if (evt.button == (int)MouseButton.LeftMouse && evt.clickCount == 2)
                {
                    var item = (Descriptor)treeView.selectedItem;
                    if (item == null)
                    {
                        return;
                    }
                    
                    if(item.SearchModel.CanToggle())
                    {
                        Select(item);
                    }
                    else
                    {
                        if (treeView.IsExpanded(treeView.selectedIndex))
                        {
                            treeView.CollapseItem(treeView.selectedIndex, true);
                        }
                        else
                        {
                            treeView.ExpandItem(treeView.selectedIndex, true);
                        }
                    }
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

            descriptor.SearchModel.SetToggle(!descriptor.SearchModel.IsToggled);
        }

        private void UpdateTree(IEnumerable<TagSearchModel<TNode>> modelDescriptors, List<TreeViewItemData<Descriptor>> treeViewData, bool isMainTree)
        {
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
                var gameplayTagSearchModels = modelDescriptors as List<TagSearchModel<TNode>> ?? modelDescriptors.ToList();
                foreach (var modelDescriptor in gameplayTagSearchModels
                             .OrderBy(x => x.Name.ToHumanReadable()))
                {
                    Debug.Log(modelDescriptor.Name);
                    var path = modelDescriptor.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    var currentFolders = treeViewData;

                    StringBuilder containerCategory = new StringBuilder();
                    foreach (var p in path)
                    {
                        var containerName = containerCategory + p;
                        if (currentFolders.All(x => x.data.Name != containerName))
                        {
                            Debug.Log(containerName);
                            var newFolder = new TreeViewItemData<Descriptor>(id++, new Descriptor(containerName), new List<TreeViewItemData<Descriptor>>());
                            currentFolders.Add(newFolder);
                            currentFolders = (List<TreeViewItemData<Descriptor>>)newFolder.children;
                        }
                        else
                        {
                            currentFolders = (List<TreeViewItemData<Descriptor>>)currentFolders.Single(x => x.data.Name == containerName).children;
                        }
                        containerCategory.Append(p);
                        containerCategory.Append(".");
                    }
                }

                foreach (var modelDescriptor in gameplayTagSearchModels)
                {
                    Debug.Log($"Finding Descriptor: {modelDescriptor.Name}");
                    var ovrFolder = FindUniqueIdentifier(treeViewData, modelDescriptor.Name);
                    if (ovrFolder.data != null)
                    {
                        Debug.Log($"Setting Folder Model: {ovrFolder.data.GetUniqueIdentifier()}");
                        ovrFolder.data.SetSearchModel(modelDescriptor);
                    }
                }
            }
        }

        private static TreeViewItemData<Descriptor> FindUniqueIdentifier(List<TreeViewItemData<Descriptor>> treeViewData, string identifier)
        {
            foreach (var itemData in treeViewData)
            {
                if (itemData.data.GetUniqueIdentifier() == identifier)
                {
                    return itemData;
                }
            }

            foreach (var itemData in treeViewData)
            {
                if (itemData.hasChildren)
                {
                    var found =  FindUniqueIdentifier((List<TreeViewItemData<Descriptor>>)itemData.children, identifier);
                    if (found.data != null)
                    {
                        return found;
                    }
                }
            }

            return default;
        }

        private class SearchScore
        {
            public Descriptor Descriptor;
            public float Score;
        }

        private void SearchMultithreaded(string searchPattern, string[] patternTokens, IReadOnlyList<TagSearchModel<TNode>> modelDescriptors, SearchScore max, List<Descriptor> finalResults)
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

                        if (!matchingDescriptor.SearchModel.CanToggle())
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
            UpdateTree(_searchProvider.GetDescriptors(), _treeViewData, true);
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

        private Descriptor GetDescriptor(TagSearchModel<TNode> searchModel, string pattern, string[] patternTokens)
        {
            var score = GetVariantMatchScore(searchModel, pattern, patternTokens, out var match, out var synonym);
            if (!(score > 0f))
            {
                return null;
            }

            var descriptor = new Descriptor(searchModel, match, synonym) { MatchingScore = score };
            return descriptor;
        }

        private float GetVariantMatchScore(TagSearchModel<TNode> searchModel, string pattern, string[] patternTokens, out string match, out string synonymMatch)
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
            var sourceTokens = text.Split(_matchingSeparators, StringSplitOptions.RemoveEmptyEntries).ToList();
            var patternMatches = new List<string>(patternTokens.Length * sourceTokens.Count);
            if (sourceTokens.Count >= patternTokens.Length)
            {
                foreach (var token in patternTokens)
                {
                    foreach (var sourceToken in sourceTokens)
                    {
                        if (sourceToken.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceTokens.Remove(sourceToken);
                            patternMatches.Add(token);
                            pattern = pattern.Replace(token, string.Empty).Trim();
                            score += (float)token.Length / sourceToken.Length;
                            break;
                        }
                    }
                }

                if (patternMatches.Count > 0)
                {
                    matchHighlight = text;
                    foreach (var match in patternMatches)
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
            _panelWidth = SessionState.GetFloat($"{GetType().Name}.{nameof(_panelWidth)}", k_DefaultPanelWidth);
            var windowWidth = SessionState.GetFloat($"{GetType().Name}.WindowWidth", _panelWidth);
            var windowHeight = SessionState.GetFloat($"{GetType().Name}.WindowHeight", k_MinHeight);
            var topLeft = new Vector2(screenPosition.x - 24, screenPosition.y - 16);
            position = new Rect(topLeft, new Vector2(windowWidth, windowHeight));

            var settingsAsJson = EditorPrefs.GetString($"{GetType().Name}.{nameof(_settings)}", null);
            _settings = !string.IsNullOrEmpty(settingsAsJson) ? JsonUtility.FromJson<Settings>(settingsAsJson) : default;
        }

        private void SaveSettings()
        {
            _panelWidth = _treeView.resolvedStyle.width;
            SessionState.SetFloat($"{GetType().Name}.{nameof(_panelWidth)}", _panelWidth);
            SessionState.SetFloat($"{GetType().Name}.WindowWidth", position.width);
            SessionState.SetFloat($"{GetType().Name}.WindowHeight", position.height);
            var json = JsonUtility.ToJson(_settings);
            EditorPrefs.SetString($"{GetType().Name}.{nameof(_settings)}", json);
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