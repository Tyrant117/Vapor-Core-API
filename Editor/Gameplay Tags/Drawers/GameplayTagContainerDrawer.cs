using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.GameplayTags;
using Vapor.Inspector;
using Vapor.UIComponents;
using Vapor.Unsafe;
using VaporEditor.Inspector;

namespace VaporEditor.GameplayTags
{
    [CustomPropertyDrawer(typeof(GameplayTagContainer))]
    public class GameplayTagContainerDrawer : VaporPropertyDrawer
    {
        private TreePropertyField _field;
        private VisualElement _tagContainer;
        private GameplayTagSearchProvider _searchProvider;

        /// <summary>Tags to show as chosen once the picker is built.</summary>
        private readonly List<string> _toggledNames = new();

        /// <summary>
        /// The picker's model, built the first time the picker is opened.
        /// </summary>
        /// <remarks>
        /// Building it walks every tag in the project, looks each one up in the registry and allocates
        /// a model for it. Doing that while drawing meant a field that may never be clicked paid for
        /// the whole tag tree — and a list of entries each holding a container paid for it once per
        /// row, on every redraw.
        /// </remarks>
        private GameplayTagSearchProvider SearchProvider
        {
            get
            {
                if (_searchProvider != null)
                {
                    return _searchProvider;
                }

                bool hasDrawer = _field.Property.TryGetAttribute<GameplayTagDrawerAttribute>(out var drawer);
                List<TagSearchModel<GameplayTagTreeNode>> searchModels = new();
                GameplayTagTree.Traverse(n =>
                {
                    if (n.Key == 0)
                    {
                        return;
                    }

                    if (hasDrawer && drawer.FilteredParents != null)
                    {
                        foreach (var ap in drawer.FilteredParents)
                        {
                            if (!GameplayTagTree.HasParentTag(n.Key, ap.Hash32()))
                            {
                                continue;
                            }

                            var filteredTooltip = GlobalDataRegistry.TryGet<GameplayTagData>(n.Key, out var filteredData) && !string.IsNullOrEmpty(filteredData.EditorTooltip) ? filteredData.EditorTooltip : n.Name;
                            searchModels.Add(new GameplayTagSearchModel(n.Name, filteredTooltip, true) { Node = n });
                            break;
                        }

                        return;
                    }

                    var tooltip = GlobalDataRegistry.TryGet<GameplayTagData>(n.Key, out var tagData) && !string.IsNullOrEmpty(tagData.EditorTooltip) ? tagData.EditorTooltip : n.Name;
                    searchModels.Add(new GameplayTagSearchModel(n.Name, tooltip) { Node = n });
                });

                BuildTree(drawer, searchModels);

                _searchProvider = new GameplayTagSearchProvider(OnSelect, searchModels, true);

                // Straight to the provider, not through SetToggled - that writes to the list being
                // iterated here.
                foreach (var name in _toggledNames)
                {
                    _searchProvider.SetModelToggled(name, true);
                }

                return _searchProvider;
            }
        }

        /// <summary>Records a chosen tag, and tells the picker only if it has been built.</summary>
        private void SetToggled(string name, bool toggled)
        {
            if (toggled)
            {
                if (!_toggledNames.Contains(name))
                {
                    _toggledNames.Add(name);
                }
            }
            else
            {
                _toggledNames.Remove(name);
            }

            _searchProvider?.SetModelToggled(name, toggled);
        }

        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            _field = field;
            _searchProvider = null;
            _toggledNames.Clear();

            var container = _field.Property.GetValue<GameplayTagContainer>();
            var group = new Group("my=4px")
            {
                Align = Align.Stretch
            };
            var labelContainer = new StyledElement(StyleHelper.GetInspectorLabelStyle() + " mr=2 pr=2")
            {
                style =
                {
                    alignItems = Align.Center,
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexStart
                }
            };
            var label = new Text(field.Property.DisplayName, "mr=6 fg=1 ov=hidden tt=ellipsis ta=middleleft");
            var helpUrl = new HelpUrlView(field.Property.TryGetAttribute<RichTextTooltipAttribute>(out var tooltipAtr) ? tooltipAtr.Tooltip : null);
            labelContainer.AddChild(label);
            labelContainer.AddChild(helpUrl);
            group.Add(labelContainer);
            _tagContainer = new VisualElement()
            {
                style =
                {
                    minHeight = 24,
                    flexGrow = 1f,
                    backgroundColor = new Color(0.165f, 0.165f, 0.165f),
                }
            };
            _tagContainer.WithBorder(1, 6, new Color(0.13f, 0.13f, 0.13f));
            _tagContainer.WithManipulator(new ButtonManipulator()
                .WithOnClick(ClickTypes.ClickOnDown, _ =>
                {
                    var worldRect = GUIUtility.GUIToScreenRect(_tagContainer.worldBound);
                    var pos = new Vector2(worldRect.position.x + 24, worldRect.position.y + _tagContainer.worldBound.height + 16);
                    TagSearchWindow<GameplayTagTreeNode>.Show<GameplayTagSearchWindow>(pos, pos, SearchProvider);
                })
                .WithActivator(EventModifiers.None, MouseButton.LeftMouse)
                .WithHoverEntered(_ =>
                {
                    _tagContainer.WithBorder(1, 6, new Color(0.227f, 0.475f, 0.733f));
                })
                .WithHoverExited(_ =>
                {
                    _tagContainer.WithBorder(1, 6, new Color(0.13f, 0.13f, 0.13f));
                }));
            foreach (var activeTag in container.Tags)
            {
                // if (GameplayTagUtility.TryUpdateIfMissing(activeTag, out var updatedTag))
                // {
                //     SetToggled(updatedTag.GetName(), true);
                //     var tag = CreateTag(updatedTag);
                //     _tagContainer.Add(tag);
                // }
                // else
                {

                    SetToggled(activeTag.GetName(), true);
                    var tag = CreateTag(activeTag);
                    _tagContainer.Add(tag);
                }
            }

            group.AddChild(_tagContainer);
            return group;
        }

        private void OnSelect(TagSearchModel<GameplayTagTreeNode>[] tagsSelected)
        {
            var container = _field.Property.GetValue<GameplayTagContainer>();
            container.Tags.Clear();
            foreach (var t in tagsSelected)
            {
                // var dropdownModel = tags.First(ot => ot.Name == t.Name);
                // var key = (KeyDropdownValue)dropdownModel.Value;
                var tag = new GameplayTag { Key = t.Node.Key };
                container.Tags.Add(tag);
            }

            _field.MarkDirtyWithValue(container, container);
            
            _tagContainer.Clear();
            foreach (var activeTag in container.Tags)
            {
                SetToggled(activeTag.GetName(), true);
                var tag = CreateTag(activeTag);
                _tagContainer.Add(tag);
            }
        }
        
        private Tag CreateTag(GameplayTag activeTag)
        {
            var tag = new Tag(activeTag.GetName());
            tag.OnTagClicked += _ =>
            {
                var worldRect = GUIUtility.GUIToScreenRect(tag.worldBound);
                var pos = new Vector2(worldRect.position.x + 24, worldRect.position.y + tag.worldBound.height + 16);
                TagSearchWindow<GameplayTagTreeNode>.Show<GameplayTagSearchWindow>(pos, pos, SearchProvider);
            };
            tag.OnTagRemoved += tagName =>
            {
                var container = _field.Property.GetValue<GameplayTagContainer>();
                var idx = container.Tags.FindIndex(kdv => kdv.GetName() == tagName);
                if (idx != -1)
                {
                    container.Tags.RemoveAt(idx);
                    SetToggled(tagName, false);
                }
                _field.MarkDirtyWithValue(container, container);
            };
            return tag;
        }

        private static void BuildTree(GameplayTagDrawerAttribute drawer, List<TagSearchModel<GameplayTagTreeNode>> nodes)
        {
            var nodeLookup = new Dictionary<string, TagSearchModel<GameplayTagTreeNode>>();

            foreach (var n in nodes)
            {
                nodeLookup.TryAdd(n.Node.Name, n);
            }

            foreach (var n in nodes)
            {
                if (n.Node.Parent != null)
                {
                    n.Parent = nodeLookup[n.Node.Parent.Name];
                }
                // else
                // {
                //     _rootNodes.Add(n);
                // }

                foreach (var c in n.Node.Children)
                {
                    n.Children.Add(nodeLookup[c.Name]);
                }
                
                if (drawer is { LeavesOnly: true } && n is GameplayTagSearchModel attributeTagSearchModel)
                {
                    attributeTagSearchModel.HideToggleIfChildren();
                }
            }
        }
    }
}
