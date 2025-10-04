using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class ToggleManipulatorGroup
    {
        public bool AllowToggleOff { get; set; }
        
        private readonly List<ToggleManipulator> _toggles = new(8);
        private ToggleManipulator _activeToggle;
        public VisualElement ActiveToggle => _activeToggle?.target;
        public event Action<VisualElement> Selected;

        public ToggleManipulatorGroup(bool allowToggleOff)
        {
            AllowToggleOff = allowToggleOff;
        }

        public void AddToggle(ToggleManipulator toggle)
        {
            toggle.Toggled += OnToggled;
            _toggles.Add(toggle);
        }

        internal void SetActiveToggle(ToggleManipulator toggle)
        {
            SetActiveToggleWithoutNotify(toggle);
            Selected?.Invoke(_activeToggle.target);
        }

        internal void SetActiveToggleWithoutNotify(ToggleManipulator toggle)
        {
            _activeToggle = toggle;
            foreach (var tog in _toggles)
            {
                if (tog != _activeToggle)
                {
                    tog.SetValueWithoutNotify(false);
                }
            }
        }

        private void OnToggled(EventBase sender, bool isOn)
        {
            if (isOn)
            {
                return;
            }

            if (_activeToggle == null)
            {
                return;
            }

            if (AllowToggleOff)
            {
                return;
            }

            if (_activeToggle.target == sender.target)
            {
                _activeToggle.IsOn = true;
            }
        }
    }
}