using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class StyledHorizontalGroup : VisualElement
    {
        public Label Label { get; private set; }
        public VisualElement Content { get; private set; }

        public override VisualElement contentContainer => Content;

        public StyledHorizontalGroup(string label = null, StyleLength labelWidth = default) : base()
        {
            StyleGroup();
            StyleContent();

            if (label != null)
            {
                hierarchy.Add(StyleLabel(label, labelWidth));
                RegisterCallbackOnce<GeometryChangedEvent>(OnBuildWithLabel);
            }
            hierarchy.Add(Content);
        }

        private void OnBuildWithLabel(GeometryChangedEvent evt)
        {
            Content.Query<VisualElement>().Descendents<Label>().ForEach(l => l.style.display = DisplayStyle.None);
        }

        protected virtual void StyleGroup()
        {
            name = "styled-horizontal-group";
            style.flexDirection = FlexDirection.Row;
            style.marginTop = 1;
            style.marginBottom = 1;
            style.marginLeft = 0;
            style.marginRight = 0;
        }

        protected virtual VisualElement StyleLabel(string label, StyleLength labelWidth)
        {
            var wrapper = new VisualElement()
            {
                style =
                {
                    
                }
            };
            Label = new Label(label)
            {
                name = "styled-horizontal-group-label",
                style =
                {
                    paddingTop = 2,
                    paddingRight = 2,
                    marginRight = 2,
                    marginLeft = 1,

                    flexGrow = 1f,
                    flexShrink = 1f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    minWidth = new StyleLength(StyleKeyword.Auto),
                    width = labelWidth,
                    maxWidth = labelWidth,
                }
            };
            wrapper.Add(Label);
            return wrapper;
        }

        protected virtual void StyleContent()
        {
            Content = new VisualElement()
            {
                name = "styled-horizontal-group-content"
            };
            Content.style.flexDirection = FlexDirection.Row;
            Content.style.flexGrow = 1f;
        }
    }
}
