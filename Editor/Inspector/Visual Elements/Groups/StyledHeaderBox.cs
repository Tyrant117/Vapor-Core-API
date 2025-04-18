using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class StyledHeaderBox : Box, ILabeledGroup
    {
        public Label Label { get; private set; }

        public StyledHeaderBox(string header) : base()
        {
            StyleBox();
            StyleHeader(header);
            Add(Label);
        }

        protected virtual void StyleBox()
        {
            name = "styled-header-box";
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
            style.paddingLeft = 3;
            style.paddingBottom = 3;
            style.paddingRight = 4;
            style.backgroundColor = ContainerStyles.BackgroundColor;
        }

        protected virtual void StyleHeader(string header)
        {
            Label = new Label(header) { name = "styled-header-box-label" };
            Label.style.paddingLeft = 6;
            Label.style.paddingTop = 3;
            Label.style.paddingBottom = 3;
            Label.style.borderBottomWidth = 1f;
            Label.style.borderBottomColor = ContainerStyles.BorderColor;
            Label.style.marginBottom = 2;
            Label.style.marginLeft = -3;
            Label.style.marginRight = -4;
            Label.style.backgroundColor = ContainerStyles.HeaderColor;
        }
    }
}
