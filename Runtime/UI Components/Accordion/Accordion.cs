using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Vapor.Inspector;

namespace Vapor.UIComponents
{
    public class Accordion : VisualElement
    {
        public enum ExpansionModes
        {
            Single,
            Multiple
        }
        
        private readonly List<AccordionItem> _items;
        public IReadOnlyList<AccordionItem> Items => _items;
        public ExpansionModes ExpansionMode { get; set; } = ExpansionModes.Single;
        public bool Collapsable { get; set; } = true;

        public Accordion()
        {
            _items = new List<AccordionItem>();
            AddToClassList("accordion");
        }

        public void AddItem(string header, VisualElement content)
        {
            var item = new AccordionItem(header, content, this);
            _items.Add(item);
            Add(item);
        }

        public void RemoveItem(AccordionItem item)
        {
            if (_items.Remove(item))
            {
                Remove(item);
            }
        }
    }
    
    public class AccordionItem : VisualElement
    {
        public string Header { get; }
        public VisualElement Content { get; }
        public bool IsExpanded { get; private set; }

        private readonly VisualElement _contentContainer;

        private readonly Accordion _accordion;
        private float _desiredHeight;

        public AccordionItem(string headerText, VisualElement content, Accordion accordion)
        {
            Header = headerText;
            Content = content;
            _accordion = accordion;

            AddToClassList("accordion-item");

            VisualElement header = new Group();
            header.AddToClassList("accordion-item__header");
            var headerText1 = new TextElement { text = Header }.AddClasses("accordion-item__header-text");
            
            header.Add(headerText1);
            header.Add(new Image().AddClasses("accordion-item__header-icon"));
            Add(header);

            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("accordion-item__content");
            _contentContainer.style.height = StyleKeyword.Auto;
            _contentContainer.RegisterCallbackOnce<GeometryChangedEvent>(evt =>
            {
                _desiredHeight = evt.newRect.height;
                _contentContainer.style.height = StyleKeyword.Null;
            });
            
            _contentContainer.Add(content);
            
            Add(_contentContainer);
            
            var buttonManipulator = new ButtonManipulator("accordion-item")
                .WithActivator<ButtonManipulator>(EventModifiers.None, MouseButton.LeftMouse)
                .WithOnClick(ClickTypes.ClickOnUp, ToggleExpand);
            this.AddManipulator(buttonManipulator);
        }

        public void ToggleExpand(EventBase eventBase)
        {
            if (_accordion is { Collapsable: false } && IsExpanded)
            {
                return;
            }

            if (_accordion?.ExpansionMode == Accordion.ExpansionModes.Single)
            {
                foreach (var item in _accordion.Items)
                {
                    if (item != this && item.IsExpanded)
                    {
                        item.Collapse();
                    }
                }
            }

            IsExpanded = !IsExpanded;

            if (IsExpanded)
            {
                _contentContainer.AddToClassList("accordion-item__expanded");
                _contentContainer.style.height = _desiredHeight;
            }
            else
            {
                _contentContainer.style.height = StyleKeyword.Null;
                _contentContainer.RemoveFromClassList("accordion-item__expanded");
            }
        }

        public void Expand()
        {
            if (!IsExpanded)
            {
                IsExpanded = true;
                _contentContainer.AddToClassList("accordion-item__expanded");
                _contentContainer.style.height = _desiredHeight;
            }
        }

        public void Collapse()
        {
            if (IsExpanded)
            {
                IsExpanded = false;
                _contentContainer.style.height = StyleKeyword.Null;
                _contentContainer.RemoveFromClassList("accordion-item__expanded");
            }
        }
    }
}
