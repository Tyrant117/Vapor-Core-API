using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class StyledFoldout : Box, ILabeledGroup
    {
        public Foldout Foldout { get; private set; }
        public Label Label { get; private set; }
        public VisualElement Content { get; private set; }

        public override VisualElement contentContainer => Foldout;

        public StyledFoldout(string header) : base()
        {
            StyleBox();
            StyleFoldout(header);

            hierarchy.Add(Foldout);
        }

        protected virtual void StyleBox()
        {
            name = "styled-foldout-box";
            style.borderBottomColor = ContainerStyles.BorderColor;
            style.borderTopColor = ContainerStyles.BorderColor;
            style.borderRightColor = ContainerStyles.BorderColor;
            style.borderLeftColor = ContainerStyles.BorderColor;
            style.borderBottomLeftRadius = 3;
            style.borderBottomRightRadius = 3;
            style.borderTopLeftRadius = 3;
            style.borderTopRightRadius = 3;
            style.marginTop = 3;
            style.marginBottom = 3;
            style.marginLeft = 0;
            style.marginRight = 0;
            style.backgroundColor = ContainerStyles.BackgroundColor;
        }

        protected virtual void StyleFoldout(string header)
        {
            Foldout = new Foldout()
            {
                text = header,
                name = "styled-foldout-foldout",
                viewDataKey = $"styled-foldout-foldout__vdk_{header}"
            };

            var togStyle = Foldout.Q<Toggle>().style;
            togStyle.marginTop = 0;
            togStyle.marginLeft = 0;
            togStyle.marginRight = 0;
            togStyle.marginBottom = 0;
            togStyle.backgroundColor = ContainerStyles.HeaderColor;

            var togContainerStyle = Foldout.Q<Toggle>().hierarchy[0].style;
            togContainerStyle.marginLeft = 3;
            togContainerStyle.marginTop = 3;
            togContainerStyle.marginBottom = 3;

            // Label
            Label = Foldout.Q<Toggle>().Q<Label>();

            // Content
            Content = Foldout.Q<VisualElement>("unity-content");
            Content.style.marginTop = 2;
            Content.style.marginRight = 4;
            Content.style.marginBottom = 3;
            Content.style.marginLeft = 3;


            Foldout.value = false;
        }
    }
}
