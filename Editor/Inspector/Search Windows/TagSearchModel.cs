using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class TagSearchModel<TNode> : SearchModelBase
    {
        public TNode Node { get; set; }
        public bool IsToggled { get; private set; }
        public bool IsMixed { get; private set; }
        public bool IsInToggleGroup { get; private set; }

        public TagSearchModel<TNode> Root { get; set; }
        public TagSearchModel<TNode> Parent { get; set; }
        public List<TagSearchModel<TNode>> Children { get; } = new();
        
        public TagSearchEntry EntryElement { get; }
        public event Action<TagSearchModel<TNode>> ToggleGroupUpdated;

        public TagSearchModel(string name, bool isInToggleGroup = false) : base(name, null, name, false)
        {
            IsInToggleGroup = isInToggleGroup;
            EntryElement = new TagSearchEntry
            {
                Toggle =
                {
                    showMixedValue = IsMixed
                }
            };
            EntryElement.Toggle.SetValueWithoutNotify(IsToggled);
            EntryElement.Toggle.RegisterValueChangedCallback(evt =>
            {
                IsToggled = evt.newValue;
                UpdateMixedState();
                UpdateToggleGroup();
            });
        }

        public virtual bool CanToggle() => true;
        
        public void SetToggle(bool value)
        {
            IsToggled = value;
            EntryElement.Toggle.SetValueWithoutNotify(value);
            UpdateMixedState();
            UpdateToggleGroup();
        }

        private void UpdateMixedState()
        {
            bool mixed = false;
            Visit(this, n =>
            {
                if (n.IsToggled)
                {
                    mixed = true;
                }
            });
            
            IsMixed = mixed;
            EntryElement.Toggle.showMixedValue = mixed;
            if (IsMixed)
            {
                IsToggled = false;
                EntryElement.Toggle.SetValueWithoutNotify(IsToggled);
            }
            
            Parent?.UpdateMixedState();
        }

        private void UpdateToggleGroup()
        {
            if (!IsToggled)
            {
                return;
            }

            ToggleGroupUpdated?.Invoke(this);
        }

        private static void Visit(TagSearchModel<TNode> parent, Action<TagSearchModel<TNode>> visitor)
        {
            foreach (var child in parent.Children)
            {
                visitor(child);
                Visit(child, visitor);
            }
        }
    }
}