using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Vapor;
using Vapor.GameplayTags;
using Vapor.Inspector;
using VaporEditor.Inspector;

namespace VaporEditor.GameplayTags
{
    public class GameplayTagSearchModel : TagSearchModel<GameplayTagTreeNode>
    {
        private bool _canToggle = true;
        public GameplayTagSearchModel(string name, string tooltip, bool isInToggleGroup = false) : base(name, isInToggleGroup)
        {
            Tooltip = tooltip.EmptyOrNull() ? name : tooltip;
        }

        public void HideToggleIfChildren()
        {
            if (Children.Count > 0)
            {
                EntryElement.Toggle.Hide();
                _canToggle = false;
            }
        }

        public override bool CanToggle() => _canToggle;
        
        // public GameplayTagTreeNode Node { get; set; }
        // public bool IsToggled { get; private set; }
        // public bool IsMixed { get; private set; }
        //
        // public GameplayTagSearchModel Root { get; set; }
        // public GameplayTagSearchModel Parent { get; set; }
        // public List<GameplayTagSearchModel> Children { get; } = new();
        //
        // public GameplayTagSearchEntry EntryElement { get; }
        //
        // public GameplayTagSearchModel(string name) : base(name, null, name, false)
        // {
        //     EntryElement = new GameplayTagSearchEntry
        //     {
        //         Toggle =
        //         {
        //             showMixedValue = IsMixed
        //         }
        //     };
        //     EntryElement.Toggle.SetValueWithoutNotify(IsToggled);
        //     EntryElement.Toggle.RegisterValueChangedCallback(evt =>
        //     {
        //         IsToggled = evt.newValue;
        //         UpdateMixedState();
        //     });
        // }
        //
        // public void SetFromState(bool isToggled)
        // {
        //     IsToggled = isToggled;
        //     EntryElement.Toggle.SetValueWithoutNotify(isToggled);
        //     UpdateMixedState();
        // }
        //
        // public void SetToggle(bool value)
        // {
        //     IsToggled = value;
        //     EntryElement.Toggle.SetValueWithoutNotify(value);
        //     UpdateMixedState();
        // }
        //
        // private void UpdateMixedState()
        // {
        //     bool mixed = false;
        //     Visit(this, n =>
        //     {
        //         if (n.IsToggled)
        //         {
        //             mixed = true;
        //         }
        //     });
        //     
        //     IsMixed = mixed;
        //     EntryElement.Toggle.showMixedValue = mixed;
        //     if (IsMixed)
        //     {
        //         IsToggled = false;
        //         EntryElement.Toggle.SetValueWithoutNotify(IsToggled);
        //     }
        //     
        //     Parent?.UpdateMixedState();
        // }
        //
        // private static void Visit(GameplayTagSearchModel parent, Action<GameplayTagSearchModel> visitor)
        // {
        //     foreach (var child in parent.Children)
        //     {
        //         visitor(child);
        //         Visit(child, visitor);
        //     }
        // }
    }
}
