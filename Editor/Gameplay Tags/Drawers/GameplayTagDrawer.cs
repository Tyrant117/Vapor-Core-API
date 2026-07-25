using System.Collections.Generic;
using System.Linq;
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
    [CustomPropertyDrawer(typeof(GameplayTag))]
    public class GameplayTagDrawer : VaporPropertyDrawer
    {
        private TreePropertyField _field;
        private VisualElement _tagContainer;
        private GameplayTagSearchProvider _searchProvider;
        // private GameplayTagDrawerAttribute _drawer;

        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            _field = field;
            bool hasDrawer = _field.Property.TryGetAttribute<GameplayTagDrawerAttribute>(out var drawer);
            List<TagSearchModel<GameplayTagTreeNode>> searchModels = new();
            GameplayTagTree.Traverse(n =>
            {
                if (hasDrawer && drawer.FilteredParents != null)
                {
                    // Debug.Log(n.Key + " " + n.Name);
                    foreach (var ap in drawer.FilteredParents)
                    {
                        if (!GameplayTagTree.HasParentTag(n.Key, ap.Hash32()))
                        {
                            continue;
                        }

                        var tooltip = GlobalDataRegistry.TryGet<GameplayTagData>(n.Key, out var tagData) && !string.IsNullOrEmpty(tagData.EditorTooltip) ? tagData.EditorTooltip : n.Name;
                        searchModels.Add(new GameplayTagSearchModel(n.Name, tooltip, true) { Node = n as GameplayTagTreeNode });
                        break;
                    }
                    
                }
                else
                {
                    var tooltip = GlobalDataRegistry.TryGet<GameplayTagData>(n.Key, out var tagData) && !string.IsNullOrEmpty(tagData.EditorTooltip) ? tagData.EditorTooltip : n.Name;
                    searchModels.Add(new GameplayTagSearchModel(n.Name, tooltip, true) { Node = n as GameplayTagTreeNode });
                }
            });
            BuildTree(drawer, searchModels);
            
            _searchProvider = new GameplayTagSearchProvider(OnSelect, searchModels, false);
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
            _tagContainer = new VisualElement
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
                .WithHoverEntered(_ => { _tagContainer.WithBorder(1, 6, new Color(0.227f, 0.475f, 0.733f)); })
                .WithHoverExited(_ => { _tagContainer.WithBorder(1, 6, new Color(0.13f, 0.13f, 0.13f)); }));

            var container = _field.Property.GetValue<GameplayTag>();
            if (container.IsNone())
            {
                container = GameplayTag.None();
                _field.MarkDirtyWithValue(container, container);
            }
            _searchProvider.SetModelToggled(container.GetName(), true);
            var tag = CreateTag(container);
            _tagContainer.Add(tag);

            group.AddChild(_tagContainer);
            return group;
        }

        private void OnSelect(TagSearchModel<GameplayTagTreeNode>[] tagsSelected)
        {
            // var tags = GameplayTagUtility.GetAllKeys();
            var container = GameplayTag.None();
            if (tagsSelected.Length > 1)
            {
                Debug.LogError($"{tagsSelected.Length} tags selected");
            }

            if (tagsSelected.Length > 0)
            {
                // var tagModel = tags.First(ot => ot.Name == tagsSelected[0].Name);
                // var key = (KeyDropdownValue)tagModel.Value;
                container = new GameplayTag { Key = tagsSelected[0].Node.Key };
            }

            _field.MarkDirtyWithValue(container, container);

            _tagContainer.Clear();

            _searchProvider.SetModelToggled(container.GetName(), true);
            var tag = CreateTag(container);
            _tagContainer.Add(tag);
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
                _searchProvider.SetModelToggled(tagName, false);
                var container = GameplayTag.None();
                _field.MarkDirtyWithValue(container, container);

                _searchProvider.SetModelToggled(container.GetName(), true);
                var newTag = CreateTag(container);
                _tagContainer.Add(newTag);
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

            List<TagSearchModel<GameplayTagTreeNode>> newNodes = new();
            
            foreach (var n in nodes)
            {
                if (n.Node.Parent != null)
                {
                    if (nodeLookup.TryGetValue(n.Node.Parent.Name, out var value))
                    {
                        n.Parent = value;
                    }
                    else
                    {
                        var externalNode = GameplayTagTree.TagMap[n.Node.Parent.Key];
                        var newSearchNode = new GameplayTagSearchModel(externalNode.Name, externalNode.Name, true) { Node = externalNode };
                        newNodes.Add(newSearchNode);
                        n.Parent = newSearchNode;
                    }
                }

                foreach (var c in n.Node.Children)
                {
                    n.Children.Add(nodeLookup[c.Name]);
                }
                
                if (drawer is { LeavesOnly: true } && n is GameplayTagSearchModel attributeTagSearchModel)
                {
                    attributeTagSearchModel.HideToggleIfChildren();
                }
            }

            foreach (var n in newNodes)
            {
                foreach (var c in n.Node.Children)
                {
                    if (!nodeLookup.TryGetValue(c.Name, out var value))
                    {
                        continue;
                    }
                    n.Children.Add(value);
                }
                
                if (drawer is { LeavesOnly: true } && n is GameplayTagSearchModel attributeTagSearchModel)
                {
                    attributeTagSearchModel.HideToggleIfChildren();
                }
            }
            
            nodes.AddRange(newNodes);
        }
    }
}