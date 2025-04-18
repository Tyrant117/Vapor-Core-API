using System.Collections;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class StyledFoldoutProperty : StyledFoldout
    {
        public VisualElement Header { get; }

        public StyledFoldoutProperty(string header) : base(header)
        {
            Header = Label.parent;
            Header.style.marginTop = 1;
            Header.style.marginBottom = 1;
            Label.style.marginLeft = 0;
        }

        public void SetHeaderProperty(VisualElement headerProperty)
        {
            if (headerProperty is VaporPropertyField prop)
            {
                prop.label = "";
                prop.style.flexGrow = 1;
                prop.style.marginRight = 3;
                prop.style.marginLeft = 25;
                prop.RegisterCallback<GeometryChangedEvent>(OnPropertyBuilt);
            }
            Header.Add(headerProperty);
        }

        private void OnPropertyBuilt(GeometryChangedEvent evt)
        {
            if (evt.target is VaporPropertyField propertyField && propertyField.childCount > 0)
            {
                propertyField.UnregisterCallback<GeometryChangedEvent>(OnPropertyBuilt);
                propertyField.Q<TextElement>().style.marginLeft = 0;
            }
        }

        protected override void StyleBox()
        {
            base.StyleBox();
            style.marginTop = 0;
            style.marginBottom = 0;
            style.backgroundColor = ContainerStyles.DarkInspectorBackgroundColor;
        }

        protected override void StyleFoldout(string header)
        {
            base.StyleFoldout(header);
            var togStyle = Foldout.Q<Toggle>().style;
            togStyle.backgroundColor = ContainerStyles.InspectorBackgroundColor;
            Label.AddToClassList("unity-base-field__label");
        }
    }
}
