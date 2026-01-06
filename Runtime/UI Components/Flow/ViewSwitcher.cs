using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace Vapor
{
    public class ViewSwitcher : ViewController
    {
        public int SelectedIndex { get; private set; }
        
        public event Action<int> ActiveViewIndexChanged;

        public List<VisualElement> Views { get; private set; } = new();

        public ViewSwitcher()
        {
            AddToClassList("view-switcher");
        }

        public override void OnInitialized(object initializer)
        {
            if (Views.IsValidIndex(0))
            {
                ActivateView(0);
            }
        }

        public void AddView(VisualElement view, bool activate = false, bool animate = true)
        {
            Views.Add(view);
            Add(view);
            if (activate)
            {
                ActivateView(Views.Count - 1, animate);
            }
            else
            {
                view.Hide();
            }
        }

        public void RemoveView(VisualElement view)
        {
            if (Views.Remove(view))
            {
                Remove(view);
            }
        }

        public void ClearViews()
        {
            Views.Clear();
            Clear();
        }

        public void ActivateView(int index, bool animate = true)
        {
            if (!Views.IsValidIndex(index))
            {
                return;
            }

            if (Views.IsValidIndex(SelectedIndex))
            {
                Views[SelectedIndex].RemoveFromClassList("view-switcher--switching");
                Views[SelectedIndex].Hide();
            }

            Views[index].Show();
            if (animate)
            {
                Views[SelectedIndex].AddToClassList("view-switcher--switching");
            }

            SelectedIndex = index;
            ActiveViewIndexChanged?.Invoke(index);
        }

        public void ActiveNextView()
        {
            if (SelectedIndex < Views.Count - 1)
            {
                ActivateView(SelectedIndex + 1);
            }
        }
        
        public void ActivePreviousView()
        {
            if (SelectedIndex > 0)
            {
                ActivateView(SelectedIndex - 1);
            }
        }
    }
}
