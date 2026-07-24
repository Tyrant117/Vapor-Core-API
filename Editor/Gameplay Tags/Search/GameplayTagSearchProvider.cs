using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vapor.GameplayTags;
using VaporEditor.Inspector;

namespace VaporEditor.GameplayTags
{
    public class GameplayTagSearchProvider : TagSearchProvider<GameplayTagTreeNode>
    {
        
        public GameplayTagSearchProvider(Action<TagSearchModel<GameplayTagTreeNode>[]> onSelect, List<TagSearchModel<GameplayTagTreeNode>> descriptors, bool allowMultiSelect) : base(onSelect,
            descriptors, allowMultiSelect)
        {
            if(!allowMultiSelect)
            {
                foreach (var model in descriptors)
                {
                    model.ToggleGroupUpdated += OnToggleGroupUpdated;
                }
            }
        }

        private void OnToggleGroupUpdated(TagSearchModel<GameplayTagTreeNode> activeModel)
        {
            foreach (var model in CachedDescriptors.Where(model => model != activeModel))
            {
                model.SetToggle(false);
            }
        }
        // public Vector2 Position { get; set; }
        // public bool AllowMultiSelect { get; set; }
        //
        // private readonly Action<GameplayTagSearchModel[]> _onSelect;
        // private readonly List<GameplayTagSearchModel> _cachedDescriptors;
        //
        // public GameplayTagSearchProvider(Action<GameplayTagSearchModel[]> onSelect, List<GameplayTagSearchModel> descriptors)
        // {
        //     _onSelect = onSelect;
        //     _cachedDescriptors = descriptors;
        // }
        //
        // public IEnumerable<GameplayTagSearchModel> GetDescriptors()
        // {
        //     return _cachedDescriptors;
        // }
        //
        // public void SetModelToggled(string tagName, bool value)
        // {
        //     var model = _cachedDescriptors.FirstOrDefault(sm => sm.Name == tagName);
        //     model?.SetFromState(value);
        // }
        //
        // public bool Select(GameplayTagSearchModel searchModel)
        // {
        //     _onSelect?.Invoke(new[] { searchModel });
        //     return true;
        // }
        //
        // public bool SelectMany(GameplayTagSearchModel[] searchModels)
        // {
        //     _onSelect?.Invoke(searchModels);
        //     return true;
        // }
    }
}