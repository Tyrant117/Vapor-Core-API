using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public interface IVaporUIComponent
    {
        ComponentStyleProps StyleProperties { get; }
        StyleOverride StyleOverride { get; }
        IVaporUIComponent VaporComponent { get; }

        void SetStyle(string style)
        {
            StyleProperties.SetStyle(style);
        }

        void ApplyStyles()
        {
            // Apply Base Styles
            StyleHelper.ApplyStyleProps(this as VisualElement, StyleProperties);

            // Apply Custom Styles
            ApplyCustomStyles();
            
            // Apply Style Overrides
            StyleOverride.ApplyTo(this as VisualElement);
        }

        void ApplyCustomStyles();

        T WithChildren<T>(Action<T> childFactory) where T : VisualElement, IVaporUIComponent
        {
            Debug.Assert(this is T, $"{this} must be of type {typeof(T)} to be valid, but it is {GetType()}");
            var thisT = (T)this;
            childFactory(thisT);
            return thisT;
        }

        void AddChild(VisualElement child)
        {
            if(this is VisualElement element)
            {
                element.Add(child);
            }
        }
    }
}