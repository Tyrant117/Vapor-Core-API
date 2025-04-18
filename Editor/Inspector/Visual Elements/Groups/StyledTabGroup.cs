using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class StyledTabGroup : TabView
    {
        private readonly Dictionary<string, Tab> _tabs = new();

        public StyledTabGroup(TabGroupAttribute attribute) : base()
        {
            viewDataKey = $"styledtabgroup__vdk_{attribute.GroupName}";
            StyleBox();

            foreach (var tab in attribute.Tabs)
            {
                var t = new Tab(tab)
                {
                    viewDataKey = $"styledtabgroup__vdk_{attribute.GroupName}_{tab}"
                };
                t.Q<Label>().style.unityTextAlign = TextAnchor.UpperCenter;
                _tabs.Add(tab, t);
                Add(t);
            }            
        }

        protected void StyleBox()
        {
            style.borderBottomColor = ContainerStyles.BorderColor;
            style.borderTopColor = ContainerStyles.BorderColor;
            style.borderRightColor = ContainerStyles.BorderColor;
            style.borderLeftColor = ContainerStyles.BorderColor;
            style.borderBottomWidth = 1;
            style.borderTopWidth = 1;
            style.borderRightWidth = 1;
            style.borderLeftWidth = 1;
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

        public bool TryGetTab(string tabName, out Tab tab)
        {
            return _tabs.TryGetValue(tabName, out tab);
        }
    }
}
