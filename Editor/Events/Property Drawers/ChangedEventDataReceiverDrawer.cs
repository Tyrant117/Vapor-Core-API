using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Events;
using Vapor.Keys;
using Vapor.Inspector;
using VaporEditor.Inspector;

namespace VaporEditor.Events
{
    public abstract class BaseChangedEventDataReceiverDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            List<string> keys = new();
            List<KeyDropdownValue> values = new();
            var key = property.FindPropertyRelative("_key");

            _ConvertToTupleList(keys, values, EventKeyUtility.GetAllEventKeyValues());
            Debug.Log(key.boxedValue.GetType());

            var indexOfCurrent = values.IndexOf((KeyDropdownValue)key.boxedValue);
            var dropdown = new ComboBox<KeyDropdownValue>(property.displayName, indexOfCurrent, keys, values, false)
            {
                name = fieldInfo.Name,
                userData = key,
            }.AddClasses("unity-base-field__aligned");
            dropdown.SelectionChanged += OnSearchableDropdownChanged;

            return dropdown;

            static void _ConvertToTupleList(List<string> keys, List<KeyDropdownValue> values, IEnumerable<DropdownModel> toConvert)
            {
                if (toConvert == null)
                {
                    return;
                }

                foreach (var model in toConvert)
                {
                    var category = model.Category;
                    var name = model.Name;
                    var value = (KeyDropdownValue)model.Value;

                    if (name == null)
                    {
                        continue;
                    }

                    if (category != null)
                    {
                        name = $"{category}/{name}";
                    }

                    keys.Add(name);
                    values.Add(value);
                }
            }
        }

        private static void OnSearchableDropdownChanged(ComboBox<KeyDropdownValue> comboBox, List<int> selectedIndices)
        {
            if (comboBox.userData is not SerializedProperty sp || !selectedIndices.IsValidIndex(0))
            {
                return;
            }

            sp.boxedValue = comboBox.Values[selectedIndices[0]];
            sp.serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomPropertyDrawer(typeof(ChangedEventDataReceiver))]
    public class ChangedEventDataReceiverDrawer : BaseChangedEventDataReceiverDrawer
    {
        
    }
    
    [CustomPropertyDrawer(typeof(ChangedEventDataReceiver<>))]
    public class ChangedEventDataReceiverDrawerOne : BaseChangedEventDataReceiverDrawer
    {
        
    }

    [CustomPropertyDrawer(typeof(ChangedEventDataReceiver<,>))]
    public class ChangedEventDataReceiverDrawerTwo : BaseChangedEventDataReceiverDrawer
    {
        
    }
    
    [CustomPropertyDrawer(typeof(ChangedEventDataReceiver<,,>))]
    public class ChangedEventDataReceiverDrawerThree : BaseChangedEventDataReceiverDrawer
    {
        
    }
    
    [CustomPropertyDrawer(typeof(ChangedEventDataReceiver<,,,>))]
    public class ChangedEventDataReceiverDrawerFour : BaseChangedEventDataReceiverDrawer
    {
        
    }
}
