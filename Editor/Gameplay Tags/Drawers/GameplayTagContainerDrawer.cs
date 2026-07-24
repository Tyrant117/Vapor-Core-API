using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.GameplayTags;
using Vapor.Inspector;
using Vapor.Keys;
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

        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            _field = field;
            bool hasDrawer = _field.Property.TryGetAttribute<GameplayTagDrawerAttribute>(out var drawer);
            var tagList = RuntimeAssetDatabaseUtility.FindAssetsByType<GameplayTagSo>();
            List<TagSearchModel<GameplayTagTreeNode>> searchModels = new();
            TagTree<GameplayTagTreeNode>.Traverse(n =>
            {
                // Debug.Log($"Gameplay Tag Drawer found: {n.Name} - {n.Key}");
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

                        var tagSo = tagList.Find(t => t.Key == n.Key);
                        searchModels.Add(new GameplayTagSearchModel(n.Name, tagSo.OrNull()?.EditorTooltip ?? n.Name, true) { Node = n as GameplayTagTreeNode });
                        break;
                    }
                }
                else
                {
                    var tagSo = tagList.Find(t => t.Key == n.Key);
                    searchModels.Add(new GameplayTagSearchModel(n.Name, tagSo.OrNull()?.EditorTooltip ?? n.Name) { Node = n as GameplayTagTreeNode });
                }
            });
            BuildTree(drawer, searchModels);

            var container = _field.Property.GetValue<GameplayTagContainer>();
            _searchProvider = new GameplayTagSearchProvider(OnSelect, searchModels, true);
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
                    TagSearchWindow<GameplayTagTreeNode>.Show<GameplayTagSearchWindow>(pos, pos, _searchProvider);
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
                //     _searchProvider.SetModelToggled(updatedTag.GetName(), true);
                //     var tag = CreateTag(updatedTag);
                //     _tagContainer.Add(tag);
                // }
                // else
                {

                    _searchProvider.SetModelToggled(activeTag.GetName(), true);
                    var tag = CreateTag(activeTag);
                    _tagContainer.Add(tag);
                }
            }

            group.AddChild(_tagContainer);
            return group;
        }

        private void OnSelect(TagSearchModel<GameplayTagTreeNode>[] tagsSelected)
        {
            var tags = GameplayTagUtility.GetAllKeys();
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
                _searchProvider.SetModelToggled(activeTag.GetName(), true);
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
                TagSearchWindow<GameplayTagTreeNode>.Show<GameplayTagSearchWindow>(pos, pos, _searchProvider);
            };
            tag.OnTagRemoved += tagName =>
            {
                var container = _field.Property.GetValue<GameplayTagContainer>();
                var idx = container.Tags.FindIndex(kdv => kdv.GetName() == tagName);
                if (idx != -1)
                {
                    container.Tags.RemoveAt(idx);
                    _searchProvider.SetModelToggled(tagName, false);
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
