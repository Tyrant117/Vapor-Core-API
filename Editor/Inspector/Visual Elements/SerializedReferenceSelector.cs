using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class SerializedReferenceSelector : VisualElement
    {
        public SerializedReferenceSelector(string label, string indexName, List<Type> types, Action<Type> setterCallback)
        {
            List<string> keys = new();
            List<object> values = new();
            _ConvertToTupleList(keys, values, types);

            var foldoutProp = new StyledFoldoutProperty(label)
            {
                tooltip = tooltip,
            };

            var indexOfCurrent = keys.IndexOf(indexName);
            var currentNameValue = indexOfCurrent >= 0 ? keys[indexOfCurrent] : "null";
            var field = new SearchableDropdown<string>(null, currentNameValue)
            {
                userData = (setterCallback, values),
                style =
                {
                    flexGrow = 1f,
                }
            };
            field.AddToClassList("unity-base-field__aligned");
            field.SetChoices(keys);
            field.ValueChanged += OnSearchableDropdownChanged;

            static void _ConvertToTupleList(List<string> keys, List<object> values, List<Type> convert)
            {
                if (convert == null)
                {
                    return;
                }

                foreach (var obj in convert)
                {
                    var item1 = obj.Name;
                    var item2 = obj;
                    if (item1 == null || item2 == null)
                    {
                        continue;
                    }

                    keys.Add(item1);
                    values.Add(item2);
                }
            }
        }

        private static void OnSearchableDropdownChanged(VisualElement visualElement, string oldValue, string newValue)
        {
            if (visualElement is SearchableDropdown<string> dropdown)
            {
                var tuple = ((Action<Type>, List<Type>))dropdown.userData;
                var newVal = tuple.Item2[dropdown.Index];
                tuple.Item1.Invoke(newVal);
            }
        }
    }
}
