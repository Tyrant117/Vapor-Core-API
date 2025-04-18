using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class SwitchButton : VisualElement
    {
        public Button Left { get; private set; }
        public Button Right { get; private set; }

        public int Current { get; private set; }

        public event Action<int> Toggled;

        public SwitchButton(string leftName, string rightName, int defaultIndex = 0)
        {
            name = "SwitchButton";
            styleSheets.Add(Resources.Load<StyleSheet>("Styles/SwitchButtonGroup"));

            Left = new Button(OnSwitchLeftClicked)
            {
                name = "SwitchLeft",
                text = leftName
            };
            Right = new Button(OnSwitchRightClicked)
            {
                name = "SwitchRight",
                text = rightName
            };
            contentContainer.Add(Left);
            contentContainer.Add(Right);
            if (defaultIndex == 0)
            {
                OnSwitchLeftClicked();
            }
            else
            {
                OnSwitchRightClicked();
            }
        }

        private void OnSwitchLeftClicked()
        {
            Right.RemoveFromClassList("switch-selected");
            Left.AddToClassList("switch-selected");
            Current = 0;
            Toggled?.Invoke(Current);
        }

        private void OnSwitchRightClicked()
        {
            Left.RemoveFromClassList("switch-selected");
            Right.AddToClassList("switch-selected");
            Current = 1;
            Toggled?.Invoke(Current);
        }        
    }
}
