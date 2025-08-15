using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
using Vapor.Keys;
using VaporEditor.Inspector;

namespace VaporEditor.Keys
{
    [CustomPropertyDrawer(typeof(KeyDropdownValue))]
    public class KeyDropdownValueDrawer : VaporPropertyDrawer
    {
        public InspectorTreeProperty Property { get; private set; }
        public TreePropertyField Field { get; private set; }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            List<string> keys = new();
            List<KeyDropdownValue> values = new();
            var atr = fieldInfo.GetCustomAttribute<DropdownAttribute>();
            if (atr == null)
            {
                return new Label($"{property.displayName} must implement {TooltipMarkup.Class(nameof(DropdownAttribute))}");
            }
            IList convert = null;
            switch (atr.Filter)
            {
                case 0:
                    var mi = ReflectionUtility.GetMember(Property.ParentType, atr.Resolver);
                    if (!ReflectionUtility.TryResolveMemberValue<IList>(Property.GetParentObject(), mi, null, out convert))
                    {
                        Debug.LogError($"Could Not Resolve IEnumerable at Property: {Property.InspectorObject.Type.Name} Resolver: {atr.Resolver}");
                    }
                    break;
                case 1:
                    convert = KeyUtility.GetAllKeysFromCategory(atr.Resolver);
                    break;
                case 2:
                    convert = KeyUtility.GetAllKeysFromTypeName(atr.Resolver);
                    break;
            }

            string name = property.displayName;
            float? fixedWidth = null;
            if (fieldInfo.FieldType == typeof(List<KeyDropdownValue>))
            {
                int index = property.propertyPath.IndexOf(".Array");

                // If ".Array" is found, return the substring before it; otherwise, return the original string
                var propName = index >= 0 ? property.propertyPath.Substring(0, index) : fieldInfo.Name;

                var outerProp = property.serializedObject.FindProperty(propName);
                for (int i = 0; i < outerProp.arraySize; i++)
                {
                    if (property.propertyPath == outerProp.GetArrayElementAtIndex(i).propertyPath)
                    {
                        name = $"Element {i}";
                        fixedWidth = 120f;
                        break;
                    }
                }
                
            }

            ConvertToTupleList(keys, values, convert);
            var foldout = new StyledFoldoutProperty(name);
            var tooltip = "";
            if (fieldInfo.IsDefined(typeof(RichTextTooltipAttribute), true))
            {
                var rtAtr = fieldInfo.GetCustomAttribute<RichTextTooltipAttribute>();
                tooltip = rtAtr.Tooltip;
            }
            if (atr.Searchable)
            {
                var indexOfCurrent = values.IndexOf((KeyDropdownValue)property.boxedValue);
                string current = (indexOfCurrent < 0 || indexOfCurrent > keys.Count - 1) ? "None" : keys[indexOfCurrent];
                var dropdown = new SearchableDropdown<string>("", current)
                {
                    name = fieldInfo.Name,
                    userData = (property, values),
                    tooltip = tooltip,
                    style =
                    {
                        flexGrow = 1
                    }
                };
                if (fixedWidth.HasValue)
                {
                    dropdown.style.minWidth = fixedWidth.Value;
                }
                else
                {
                    dropdown.AddToClassList("unity-base-field__aligned");
                }
                dropdown.SetChoices(keys);
                dropdown.ValueChanged += OnSearchableDropdownChanged; 
                foldout.SetHeaderProperty(dropdown);
            }
            else
            {
                Debug.Log((KeyDropdownValue)property.boxedValue);
                var indexOfCurrent = values.IndexOf((KeyDropdownValue)property.boxedValue);
                int defaultInex = (indexOfCurrent < 0 || indexOfCurrent > keys.Count - 1) ? 0 : indexOfCurrent;
                var dropdown = new DropdownField("", keys, defaultInex)
                {
                    name = fieldInfo.Name,
                    tooltip = tooltip,
                    userData = (property, values),
                    style =
                    {
                        flexGrow = 1
                    }
                };
                if (fixedWidth.HasValue)
                {
                    dropdown.style.minWidth = fixedWidth.Value;
                }
                else
                {
                    dropdown.AddToClassList("unity-base-field__aligned");
                }
                dropdown.RegisterValueChangedCallback(OnDropdownChanged);
                foldout.SetHeaderProperty(dropdown);
            }

            var guidBox = new StyledHorizontalGroup();
            var guidField = new PropertyField(property.FindPropertyRelative("Guid"))
            {
                style = { flexGrow = 1}
            };
            guidBox.Add(guidField);
            guidField.SetEnabled(false);
            guidBox.Add(new Button(() => OnSelectClicked(property))
            {
                text = "Select"
            });

            var keyBox = new StyledHorizontalGroup();
            var keyField = new PropertyField(property.FindPropertyRelative("Key"))
            {
                style = { flexGrow = 1}
            };
            keyField.SetEnabled(false);
            keyBox.Add(keyField);
            keyBox.Add(new Button(() => OnRemapClicked(property))
            {
                text = "Re-Map",
            });

            foldout.Add(guidBox);
            foldout.Add(keyBox);
            return foldout;
        }

        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            Field = field;
            Property = field.Property;
            string displayName = Property.DisplayName;
            List<string> keys = new();
            List<object> values = new();

            // Debug.Log(Property.PropertyPath);
            // Debug.Log(Property.PropertyInfoType);
            Property.TryGetAttribute<DropdownAttribute>(out var dropdownAttribute);
            switch (dropdownAttribute.Filter)
            {
                case 0:
                    var mi = ReflectionUtility.GetMember(Property.ParentType, dropdownAttribute.Resolver);
                    if (ReflectionUtility.TryResolveMemberValue<IEnumerable<DropdownModel>>(Property.GetParentObject(), mi, null, out var convert))
                    {
                        SplitTupleToDropdown(keys, values, convert);
                    }
                    else
                    {
                        Debug.LogError($"Could Not Resolve IEnumerable at Property: {Property.InspectorObject.Type.Name} Resolver: {dropdownAttribute.Resolver}");
                        return null;
                    }
                    break;
                case 1:
                    SplitTupleToDropdown(keys, values, KeyUtility.GetAllKeysFromCategory(dropdownAttribute.Resolver));
                    break;
                case 2:
                    SplitTupleToDropdown(keys, values, KeyUtility.GetAllKeysFromTypeName(dropdownAttribute.Resolver));
                    break;
            }                

            //Debug.Log($"Building Property: {Property.PropertyName} | IsArray: {Property.IsArray} | Options: {keys.Count}");
            if (Property.IsArray)
            {
                if (!dropdownAttribute.MultiSelectArray)
                {
                    return null;
                }

                var comboBox = new ComboBox<object>(displayName, -1, keys, values, null, true, categorySplitCharacter: dropdownAttribute.CategorySplitCharacter);
                List<int> selectedIdx = new();
                foreach (var elem in Property.ArrayData)
                {
                    int idx = values.IndexOf(elem.GetValue());
                    if (idx != -1)
                    {
                        selectedIdx.Add(idx);
                    }
                }
                List<string> selectedNames = new(selectedIdx.Count);
                foreach (var idx in selectedIdx)
                {
                    string c = dropdownAttribute.CategorySplitCharacter.EmptyOrNull() ? keys[idx] : keys[idx].Replace(dropdownAttribute.CategorySplitCharacter[0], '/');
                    selectedNames.Add(c);
                }

                comboBox.Select(selectedNames);
                comboBox.SelectionChanged += Field.OnComboBoxSelectionChanged;
                return comboBox;
            }
            else
            {
                var horizontal = new StyledHorizontalGroup();
                var current = Property.GetValue();
                var cIdx = Mathf.Max(0, values.IndexOf(current));
                var comboBox = new ComboBox<object>(displayName, cIdx, keys, values, null, false, categorySplitCharacter: dropdownAttribute.CategorySplitCharacter);

                comboBox.SelectionChanged += Field.OnComboBoxSelectionChanged;
                horizontal.Add(comboBox);

                var select = new Button(() => OnSelectClicked(Property))
                {
                    style =
                    {
                        paddingLeft = 3,
                        paddingRight = 3,
                    }
                };
                var image = new Image
                {                    
                    image = EditorGUIUtility.IconContent("d_scenepicking_pickable_hover").image,
                    scaleMode = ScaleMode.ScaleToFit,
                };
                select.Add(image);

                horizontal.Add(select);

                var remap = new Button(() => OnRemapClicked(Property))
                {
                    style =
                    {
                        paddingLeft = 3,
                        paddingRight = 3,
                    }
                };
                var image2 = new Image
                {
                    image = EditorGUIUtility.IconContent("d_Refresh").image,
                    scaleMode = ScaleMode.ScaleToFit,
                };
                remap.Add(image2);

                horizontal.Add(remap);
                return horizontal;
            }
        }        

        private static void OnSelectClicked(SerializedProperty property)
        {
            if (property is { boxedValue: KeyDropdownValue key })
            {
                key.Select();
            }
        }

        private static void OnRemapClicked(SerializedProperty property)
        {
            if (property is { boxedValue: KeyDropdownValue key })
            {
                key.Remap();
            }
        }

        private static void OnSelectClicked(InspectorTreeProperty property)
        {
            property.GetValue<KeyDropdownValue>().Select();
        }

        private static void OnRemapClicked(InspectorTreeProperty property)
        {
            property.GetValue<KeyDropdownValue>().Remap();
        }

        private static void OnSearchableDropdownChanged(VisualElement visualElement, string oldValue, string newValue)
        {
            if (visualElement is SearchableDropdown<string> dropdown)
            {
                var tuple = ((SerializedProperty, List<KeyDropdownValue>))dropdown.userData;
                var newVal = tuple.Item2[dropdown.Index];
                Debug.Log("Applied " + newVal);
                tuple.Item1.boxedValue = newVal;
                tuple.Item1.serializedObject.ApplyModifiedProperties();
            }
        }

        private static void OnDropdownChanged(ChangeEvent<string> evt)
        {
            if (evt.target is DropdownField dropdown)
            {
                var tuple = ((SerializedProperty, List<KeyDropdownValue>))dropdown.userData;
                var newVal = tuple.Item2[dropdown.index];
                Debug.Log("Applied " + newVal);
                tuple.Item1.boxedValue = newVal;
                tuple.Item1.serializedObject.ApplyModifiedProperties();
            }
        }

        private static void SplitTupleToDropdown(List<string> keys, List<object> values, IEnumerable<DropdownModel> toConvert)
        {
            if (toConvert == null)
            {
                return;
            }

            foreach (var model in toConvert)
            {
                var category = model.Category;
                var name = model.Name;
                var value = model.Value;

                if (name == null || value == null)
                {
                    name = model.ToString();
                    value = model;
                }

                if (category != null)
                {
                    name = $"{category}/{name}";
                }

                keys.Add(name);
                values.Add(value);
            }
        }

        private static void ConvertToTupleList(List<string> keys, List<KeyDropdownValue> values, IList convert)
        {
            foreach (var obj in convert)
            {
                var item1 = (string)obj.GetType().GetField("Item1", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(obj);
                var item2 = obj.GetType().GetField("Item2", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(obj);
                if (item1 == null || item2 == null)
                {
                    continue;
                }

                keys.Add(item1);
                values.Add((KeyDropdownValue)item2);
            }
        }

        private static IList GetKeysField(Type type, string valuesName)
        {
            var fieldInfo = type.GetField(valuesName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (fieldInfo == null)
            {
                var allTypes = ReflectionUtility.GetSelfAndBaseTypes(type);
                foreach (var t in allTypes)
                {
                    fieldInfo = t.GetField(valuesName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (fieldInfo != null)
                    {
                        break;
                    }
                }
            }

            if (fieldInfo == null) return null;

            var keys = fieldInfo.GetValue(null);
            if (keys is IList keyList)
            {
                return keyList;
            }

            return null;
        }

        
    }
}
