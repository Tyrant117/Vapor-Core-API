using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public class TagSearchProvider<TNode> : ISearchProvider<TagSearchModel<TNode>>
    {
        public Vector2 Position { get; set; }
        public bool AllowMultiSelect { get; set; }

        private readonly Action<TagSearchModel<TNode>[]> _onSelect;
        protected readonly List<TagSearchModel<TNode>> CachedDescriptors;

        public TagSearchProvider(Action<TagSearchModel<TNode>[]> onSelect, List<TagSearchModel<TNode>> descriptors, bool allowMultiSelect)
        {
            _onSelect = onSelect;
            CachedDescriptors = descriptors;
            AllowMultiSelect = allowMultiSelect;
        }

        public IEnumerable<TagSearchModel<TNode>> GetDescriptors()
        {
            return CachedDescriptors;
        }

        public void SetModelToggled(string tagName, bool value)
        {
            var model = CachedDescriptors.FirstOrDefault(sm => sm.Name == tagName);
            model?.SetToggle(value);
        }

        public bool Select(TagSearchModel<TNode> searchModel)
        {
            _onSelect?.Invoke(new[] { searchModel });
            return true;
        }

        public bool SelectMany(TagSearchModel<TNode>[] searchModels)
        {
            _onSelect?.Invoke(searchModels);
            return true;
        }
    }
}