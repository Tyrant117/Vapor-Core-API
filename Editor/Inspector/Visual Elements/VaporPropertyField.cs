using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class VaporPropertyField : PropertyField
    {
        private bool _alreadyBuilt = false;

        public VaporPropertyField() : base() { }
        public VaporPropertyField(SerializedProperty property) : this(property, null) { }
        public VaporPropertyField(SerializedProperty property, string label) : base(property, label) { }

        protected override void HandleEventBubbleUp(EventBase evt)
        {
            var type = evt.GetType();
            if (type.Name == "SerializedPropertyBindEvent" && !bindingPath.EmptyOrNull())
            {
                if (_alreadyBuilt)
                {
                    evt.StopPropagation();
                    return;
                }
                else
                {
                    _alreadyBuilt = true;
                    base.HandleEventBubbleUp(evt);
                }
            }            
        }
    }
}
