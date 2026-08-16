using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;

namespace Vapor.GameplayTags
{
    public enum GameplayTagContainerChangeType
    {
        Add,
        Remove,
        Clear,
    }

    /// <summary>
    /// A set of tags with hierarchical matching. Crosses the wire through the container formatter
    /// that <c>Vapor.Networking</c> registers (a count and the keys); nothing here knows about the wire.
    /// </summary>
    [Serializable]
    public class GameplayTagContainer
    {
        public delegate void TagsChangedHandler(GameplayTagContainer container, List<GameplayTag> tags, GameplayTagContainerChangeType changeType);
        
        [SerializeField]
        private List<GameplayTag> _tags = new();
        public List<GameplayTag> Tags => _tags;

        [NonSerialized]
        private bool _isInit;
        [NonSerialized]
        private HashSet<GameplayTag> _exactValidTags;
        
        public event TagsChangedHandler OnTagsChanged;

        public GameplayTagContainer() { }

        public GameplayTagContainer(params GameplayTag[] tags)
        {
            _tags.AddRange(tags);
            EnsureInitialized();
        }

        public GameplayTagContainer(GameplayTagContainer tagContainer)
        {
            if (tagContainer != null)
            {
                _tags.AddRange(tagContainer.GetTags());
            }

            EnsureInitialized();
        }

        public bool HasTag(uint tagId, bool exactMatch)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return exactMatch ? _tags.Any(k => k.Key == tagId) : RecurseParentForValidTag(tagId);
            }
#endif

            EnsureInitialized();

            if (exactMatch)
            {
                return _exactValidTags.Contains(tagId);
            }

            foreach (var tag in _exactValidTags)
            {
                if (GameplayTagTree.HasParentTag(tag.Key, tagId))
                {
                    return true;
                }
            }

            return false;
        }
        public bool HasTag(GameplayTag tagId, bool exactMatch)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return exactMatch ? _tags.Any(k => k.Key == tagId.Key) : RecurseParentForValidTag(tagId);
            }
#endif

            EnsureInitialized();

            if (exactMatch)
            {
                return _exactValidTags.Contains(tagId);
            }

            foreach (var tag in _exactValidTags)
            {
                if (GameplayTagTree.HasParentTag(tag.Key, tagId.Key))
                {
                    return true;
                }
            }

            return false;
        }
        public bool HasTag(string tagName, bool exactMatch)
        {
            return HasTag(new GameplayTag(tagName), exactMatch);
        }

        public bool HasAny(IList<GameplayTag> tags, bool exactMatch)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return exactMatch ? tags.Any(t => _tags.Any(k => k.Key == t)) : tags.Any(RecurseParentForValidTag);
            }
#endif

            EnsureInitialized();

            if (exactMatch)
            {
                foreach (var t in tags)
                {
                    if (_exactValidTags.Contains(t))
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (var tag in _exactValidTags)
            {
                foreach (var searchTag in tags)
                {
                    if (GameplayTagTree.HasParentTag(tag.Key, searchTag.Key))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        public bool HasAny(IList<string> tagNames, bool exactMatch)
        {
            return HasAny(tagNames.Select(s => new GameplayTag(s)).ToList(), exactMatch);
        }
        public bool HasAny(GameplayTagContainer tagContainer, bool exactMatch)
        {
            return HasAny(tagContainer.GetTags(), exactMatch);
        }

        public bool HasAll(IList<GameplayTag> tags, bool exactMatch)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return exactMatch ? tags.All(t => _tags.Any(k => k.Key == t)) : tags.All(RecurseParentForValidTag);
            }
#endif
            EnsureInitialized();

            if (exactMatch)
            {
                foreach (var t in tags)
                {
                    if (!_exactValidTags.Contains(t))
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var tag in _exactValidTags)
            {
                foreach (var searchTag in tags)
                {
                    if (!GameplayTagTree.HasParentTag(tag.Key, searchTag.Key))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        public bool HasAll(IList<string> tagNames, bool exactMatch)
        {
            return HasAll(tagNames.Select(s => new GameplayTag(s)).ToList(), exactMatch);
        }
        public bool HasAll(GameplayTagContainer tagContainer, bool exactMatch)
        {
            return HasAll(tagContainer.GetTags(), exactMatch);
        }


        public bool AddTag(GameplayTag tagId)
        {
            EnsureInitialized();
            if (!_exactValidTags.Add(tagId))
            {
                return false;
            }

            _tags.Add(tagId);
            var list = ListPool<GameplayTag>.Get();
            list.Add(tagId);
            OnTagsChanged?.Invoke(this, list, GameplayTagContainerChangeType.Add);
            ListPool<GameplayTag>.Release(list);
            return true;
        }

        public bool AddTag(string tagName)
        {
            return AddTag(new GameplayTag(tagName));
        }

        public void AddTags(params GameplayTag[] tagIds)
        {
            EnsureInitialized();
            var list = ListPool<GameplayTag>.Get();
            foreach (var tagId in tagIds)
            {
                if (!_exactValidTags.Add(tagId))
                {
                    continue;
                }

                _tags.Add(tagId);
                list.Add(tagId);
            }
            OnTagsChanged?.Invoke(this, list, GameplayTagContainerChangeType.Add);
            ListPool<GameplayTag>.Release(list);
        }

        public bool RemoveTag(GameplayTag tagId)
        {
            EnsureInitialized();
            if (!_exactValidTags.Remove(tagId))
            {
                return false;
            }

            _tags.Remove(tagId);
            var list = ListPool<GameplayTag>.Get();
            list.Add(tagId);
            OnTagsChanged?.Invoke(this, list, GameplayTagContainerChangeType.Remove);
            ListPool<GameplayTag>.Release(list);
            return true;
        }
        public bool RemoveTag(string tagName)
        {
            return RemoveTag(new GameplayTag(tagName));
        }

        public void RemoveTags(params GameplayTag[] tagIds)
        {
            EnsureInitialized();
            var list = ListPool<GameplayTag>.Get();
            
            foreach (var tagId in tagIds)
            {
                if (!_exactValidTags.Remove(tagId))
                {
                    continue;
                }

                list.Add(tagId);
                _tags.Remove(tagId);
            }
            
            OnTagsChanged?.Invoke(this, list, GameplayTagContainerChangeType.Remove);
            ListPool<GameplayTag>.Release(list);
        }

        public void ClearTags()
        {
            EnsureInitialized();
            var list = ListPool<GameplayTag>.Get();

            list.AddRange(_exactValidTags);
            _exactValidTags.Clear();
            _tags.Clear();

            OnTagsChanged?.Invoke(this, list, GameplayTagContainerChangeType.Clear);
            ListPool<GameplayTag>.Release(list);
        }

        public bool IsEmpty() => _exactValidTags.Count == 0;
        

        public IList<GameplayTag> GetTags()
        {
            EnsureInitialized();
            return _tags;
        }

        public IReadOnlyList<GameplayTag> GetAllChildren(uint parentTagId)
        {
            EnsureInitialized();
            return _tags.Where(t => GameplayTagTree.HasParentTag(parentTagId, t.Key)).ToList();
        }

        public GameplayTag GetFirstChild(uint parentTagId)
        {
            EnsureInitialized();
            return _tags.FirstOrDefault(t => GameplayTagTree.HasParentTag(parentTagId, t.Key));
        }

        private void EnsureInitialized()
        {
            if (_isInit)
            {
                return;
            }

            _exactValidTags = new HashSet<GameplayTag>(_tags);
            _isInit = true;
        }

        private bool RecurseParentForValidTag(GameplayTag tagId)
        {
            foreach (var tag in _tags)
            {
                if (GameplayTagTree.HasParentTag(tag.Key, tagId.Key))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
