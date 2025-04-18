using System;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public class BindableProperty<T> : INotifyBindablePropertyChanged
    {
        public static readonly BindingId ValuePropertyId = nameof(Value);

        private readonly Func<T> _getter;
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        BindableProperty(Func<T> getter, ref Action changed)
        {
            _getter = getter;
            changed += OnChanged;
        }

        [CreateProperty]
        public T Value => _getter();
        private void OnChanged()
        {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(Value)));
        }

        public void BindToText(TextElement text)
        {
            text.SetBinding(nameof(TextElement.text), new DataBinding()
            {
                dataSourcePath = new PropertyPath(ValuePropertyId),
                bindingMode = BindingMode.ToTarget,
                updateTrigger = BindingUpdateTrigger.OnSourceChanged
            });
        }

        public void BindToStyle(VisualElement element, BindingId styleName)
        {
            element.dataSource = this;
            element.SetBinding(styleName, new DataBinding()
            {
                dataSourcePath = new PropertyPath(ValuePropertyId),
                bindingMode = BindingMode.ToTarget,
                updateTrigger = BindingUpdateTrigger.OnSourceChanged
            });
        }

        public static BindableProperty<T> Bind(Func<T> getter, ref Action changed) => new(getter, ref changed);
    }
}
