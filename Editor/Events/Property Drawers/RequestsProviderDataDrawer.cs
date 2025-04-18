using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;
using Vapor.Events;
using Vapor.Inspector;
using Vapor.Keys;

namespace VaporEditor.Events
{
    public abstract class BaseRequestsProviderDataDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            List<string> keys = new();
            List<KeyDropdownValue> values = new();
            var key = property.FindPropertyRelative("_key");

            _ConvertToTupleList(keys, values, EventKeyUtility.GetAllProviderKeyValues());

            var indexOfCurrent = values.IndexOf((KeyDropdownValue)key.boxedValue);
            var currentNameValue = indexOfCurrent >= 0 ? keys[indexOfCurrent] : "None";
            var dropdown = new SearchableDropdown<string>(property.displayName, currentNameValue)
            {
                name = fieldInfo.Name,
                userData = (key, values),
            };
            dropdown.AddToClassList("unity-base-field__aligned");
            dropdown.SetChoices(keys);
            dropdown.ValueChanged += OnSearchableDropdownChanged;
            
            return dropdown;
            
            static void _ConvertToTupleList(List<string> keys, List<KeyDropdownValue> values, IList convert)
            {
                if (convert == null)
                {
                    return;
                }
                
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
        }

        private static void OnSearchableDropdownChanged(VisualElement visualElement, string oldValue, string newValue)
        {
            if (visualElement is not SearchableDropdown<string> dropdown) return;

            var tuple = ((SerializedProperty, List<KeyDropdownValue>))dropdown.userData;
            var newVal = tuple.Item2[dropdown.Index];
            tuple.Item1.boxedValue = newVal;
            tuple.Item1.serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomPropertyDrawer(typeof(RequestsCachedProviderData<>))]
    public class RequestsCachedProviderDataDrawerOne : BaseRequestsProviderDataDrawer
    {
        
    }
    
    [CustomPropertyDrawer(typeof(RequestsCachedProviderData<,>))]
    public class RequestsCachedProviderDataDrawerTwo : BaseRequestsProviderDataDrawer
    {
        
    }
    
    [CustomPropertyDrawer(typeof(RequestsCachedProviderData<,,>))]
    public class RequestsCachedProviderDataDrawerThree : BaseRequestsProviderDataDrawer
    {
        
    }
    
    [CustomPropertyDrawer(typeof(RequestsCachedProviderData<,,,>))]
    public class RequestsCachedProviderDataDrawerFour : BaseRequestsProviderDataDrawer
    {
        
    }
    
    [CustomPropertyDrawer(typeof(RequestsCachedProviderData<,,,,>))]
    public class RequestsCachedProviderDataDrawerFive : BaseRequestsProviderDataDrawer
    {
        
    }
}
