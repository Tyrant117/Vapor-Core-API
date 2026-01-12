using System;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace Vapor.UIComponents
{
    public enum ButtonVariants
    {
        Filled,
        Light,
        Outline,
        Subtle,
        Transparent,
    }
    
    public class Button : VisualElement
    {
        private const string k_DefaultHoverClass = "button--hover";
        private const string k_DefaultPressedClass = "button--pressed";
        private const string k_DefaultSelectedClass = "button--selected";
        private const string k_DefaultDisabledClass = "button--disabled";
        
        public VisualElement Background { get; }
        
        private string _hoverClass;
        private string _pressedClass;
        private string _selectedClass;
        private string _disabledClass;
        
        private readonly ButtonManipulator _buttonManipulator;
        private readonly Image _leftIcon;
        private readonly TextElement _text;
        private readonly Image _rightIcon;
        private VisualElement _notify;

        public event Action<Button> Clicked;

        public Button()
        {
            _hoverClass = k_DefaultHoverClass;
            _pressedClass = k_DefaultPressedClass;
            _selectedClass = k_DefaultSelectedClass;
            _disabledClass = k_DefaultDisabledClass;

            AddToClassList("button");

            Background = new VisualElement().AddClasses("button-background");
            Add(Background);

            _leftIcon = new Image().AddClasses("button-icon-left");
            Add(_leftIcon);

            _text = new TextElement().AddClasses("text-primary", "button-text");
            Add(_text);

            _rightIcon = new Image().AddClasses("button-icon-right");
            Add(_rightIcon);
        }

        public Button(Action<Button> clicked) : this()
        {
            _buttonManipulator = new ButtonManipulator("button")
                .WithOnClick(ClickTypes.ClickOnUp, OnClicked)
                .WithActivator<ButtonManipulator>(EventModifiers.None, MouseButton.LeftMouse)
                .WithHoverEntered<ButtonManipulator>(OnHoverEntered)
                .WithHoverExited<ButtonManipulator>(OnHoverExited)
                .WithOnPress<ButtonManipulator>(OnPress)
                .WithOnRelease<ButtonManipulator>(OnRelease);
            this.AddManipulator(_buttonManipulator);
            
            Clicked += clicked;
        }

        public Button(Action<Button> clicked, string hoverClass, string pressedClass, string disabledClass = null) : this(clicked)
        {
            _hoverClass = hoverClass;
            _pressedClass = pressedClass;
            _disabledClass = disabledClass;
        }

        public Button WithText(string text, string textClass = null)
        {
            if (!textClass.EmptyOrNull())
            {
                _text.AddToClassList(textClass);
            }

            _text.text = text;
            _text.Show();
            return this;
        }
        
        public Button WithIconLeft(Sprite icon, string iconClass = null)
        {
            if (!iconClass.EmptyOrNull())
            {
                _leftIcon.AddToClassList(iconClass);
            }
            _leftIcon.sprite = icon;
            _leftIcon.Show();
            return this;
        }

        public Button WithIconRight(Sprite icon, string iconClass = null)
        {
            if (!iconClass.EmptyOrNull())
            {
                _rightIcon.AddToClassList(iconClass);
            }
            _rightIcon.sprite = icon;
            _rightIcon.Show();
            return this;
        }

        public Button WithBackgroundContent(VisualElement content)
        {
            Background.Add(content);
            return this;
        }

        public Button WithVariant(ButtonVariants variant)
        {
            switch (variant)
            {
                case ButtonVariants.Filled:
                    AddToClassList("button-filled");
                    _hoverClass = "button-filled--hover";
                    break;
                case ButtonVariants.Light:
                    AddToClassList("button-light");
                    _hoverClass = "button-light--hover";
                    break;
                case ButtonVariants.Outline:
                    AddToClassList("button-outline");
                    _hoverClass = "button-outline--hover";
                    break;
                case ButtonVariants.Subtle:
                    AddToClassList("button-subtle");
                    _hoverClass = "button-subtle--hover";
                    break;
                case ButtonVariants.Transparent:
                    AddToClassList("button-transparent");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
            }
            
            return this;
        }

        public Button WithSelectable(string selectedClass)
        {
            _selectedClass = selectedClass;
            return this;
        }

        public Button WithNotify()
        {
            _notify = new VisualElement().AddClasses("button-notify");
            Add(_notify);
            return this;
        }
        
        public void SetActive(bool active)
        {
            if (active)
            {
                pickingMode = PickingMode.Ignore;
                RemoveFromClassList(_disabledClass);
            }
            else
            {
                pickingMode = PickingMode.Position;
                AddToClassList(_disabledClass);
            }
        }

        public void Select()
        {
            AddToClassList(_selectedClass);
        }
        
        public void Deselect()
        {
            RemoveFromClassList(_selectedClass);
        }

        private void OnHoverEntered(EventBase obj)
        {
            AddToClassList(_hoverClass);
        }

        private void OnHoverExited(EventBase obj)
        {
            RemoveFromClassList(_hoverClass);
            RemoveFromClassList(_pressedClass);
        }

        private void OnPress(EventBase obj)
        {
            AddToClassList(_pressedClass);
        }

        private void OnRelease(EventBase obj)
        {
            RemoveFromClassList(_pressedClass);
            if (_buttonManipulator.IsHovering)
            {
                AddToClassList(_hoverClass);
            }
            else
            {
                RemoveFromClassList(_hoverClass);
            }
        }

        private void OnClicked(EventBase obj)
        {
            Clicked?.Invoke(this);
        }
    }
}