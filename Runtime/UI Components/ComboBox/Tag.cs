using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace Vapor.UIComponents
{
    public class Tag : VisualElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-tag-root";

        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        public IVaporUIComponent VaporComponent => this;

        public string TagName { get; private set; }

        public event Action<string> OnTagClicked;
        public event Action<string> OnTagRemoved;

        public Tag(string tagName, bool removeButton = true, string styleString = null)
        {
            TagName = tagName;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.alignSelf = Align.FlexStart;
            style.justifyContent = Justify.Center;
            this.WithPadding(6, 6, 2, 2);
            this.WithMargins(6, 6, 2, 2);
            this.WithBorder(1, 4, new Color(0.188f, 0.188f, 0.188f));
            style.maxHeight = 18;
            style.backgroundColor = new Color(0.345f, 0.345f, 0.345f);

            var btn = new ButtonManipulator("highlight").WithOnClick(ClickTypes.ClickOnDown, evt =>
                {
                    Debug.Log("OnTagClicked");
                    OnTagClicked?.Invoke(TagName);
                }).WithActivator<ButtonManipulator>(EventModifiers.None, MouseButton.LeftMouse)
                .WithHoverEntered<ButtonManipulator>(evt =>
                {
                    var hoverC = new Color(0.404f, 0.404f, 0.404f);
                    style.backgroundColor = hoverC;
                })
                .WithHoverExited<ButtonManipulator>(evt =>
                {
                    var hoverC = new Color(0.345f, 0.345f, 0.345f);
                    style.backgroundColor = hoverC;
                });
            this.WithManipulator(btn);
            
            // Label
            var label = new Text(tagName)
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                    flexGrow = 0,
                    flexShrink = 0,
                }
            };
            Add(label);

            // Remove button
            if (removeButton)
            {
                var x = new Text("x")
                {
                    style =
                    {
                        unityTextAlign = TextAnchor.MiddleCenter,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        flexGrow = 0,
                        flexShrink = 0,
                        marginLeft = 6,
                        translate = new StyleTranslate(new Translate(0, -1)),
                    }
                };
                var xBtn = new ButtonManipulator("highlight").WithOnClick(ClickTypes.ClickOnDown, evt =>
                    {
                        Debug.Log("OnTagRemoved");
                        OnTagRemoved?.Invoke(TagName);
                        parent.Remove(this);
                    }).WithActivator<ButtonManipulator>(EventModifiers.None, MouseButton.LeftMouse).WithHoverEntered<ButtonManipulator>(evt =>
                    {
                        var hoverC = new Color(1.0f, 1.0f, 1.0f);
                        x.style.color = hoverC;
                    })
                    .WithHoverExited<ButtonManipulator>(evt => { x.style.color = StyleKeyword.Null; });
                ;
                x.WithManipulator(xBtn);
                Add(x);
            }

            VaporComponent.SetStyle(styleString);
            VaporComponent.ApplyStyles();
        }

        public void ApplyCustomStyles() { }
    }
}