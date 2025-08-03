using System;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace Vapor.UIComponents
{
    public class ActionIcon : VisualElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-action-icon-root";
        
        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        public IVaporUIComponent VaporComponent => this;

        public ButtonManipulator Manipulator { get; }
        public Image IconImage { get; }

        private event Action Clicked;

        public ActionIcon(Action callback, string style = null)
        {
            this.style.flexGrow = 0;
            this.style.flexShrink = 0;
            this.style.alignSelf = Align.FlexStart;

            IconImage = new Image();
            Add(IconImage);

            Manipulator = new ButtonManipulator(ROOT_SELECTOR)
                .WithActivator<ButtonManipulator>(EventModifiers.None, MouseButton.LeftMouse);
            Manipulator.Clicked += OnClicked;
            this.AddManipulator(Manipulator);
            Clicked += callback;

            VaporComponent.SetStyle(style);
            VaporComponent.ApplyStyles();
        }

        public ActionIcon WithIcon(Sprite icon)
        {
            IconImage.sprite = icon;
            return this;
        }

        public ActionIcon WithIcon(Texture texture)
        {
            IconImage.image = texture;
            return this;
        }

        private void OnClicked(EventBase obj)
        {
            Debug.Log("Clicked");
            Clicked?.Invoke();
        }
        
        public void ApplyCustomStyles()
        {
            
        }
    }
}