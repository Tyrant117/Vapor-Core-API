using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public delegate void SetLayoutDelegate(Rect value);
    public delegate ref Matrix4x4 WorldTransformRefDelegate();

    public enum ElementAnchor
    {
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,
        TopLeft,
        Center,
    }

    public static class VisualElementExtensions
    {
        private static MethodInfo s_SetPropertyMethod;

        private static Dictionary<VisualElement, SetLayoutDelegate> _setLayoutDelegateMap;
        private static Dictionary<VisualElement, WorldTransformRefDelegate> _getWorldTransformRefDelegateMap;

        #region - Pseudo States -
        /// <summary>
        /// Gets the psuedo state value via reflection
        /// </summary>
        /// <param name="element"></param>
        /// <returns>The int flag value of <see cref="PseudoStates"/></returns>
        public static int GetPseudoState(this VisualElement element)
        {                        
            return (int)element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(element);
        }

        public static void AddPseudoState(this VisualElement element, int state)
        {
            int result = element.GetPseudoState() | state;
            var enumType = element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(element).GetType();
            if (enumType != null && enumType.IsEnum)
            {
                object enumValue = Enum.ToObject(enumType, result);
                element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(element, enumValue);
            }
            else
            {
                Debug.Log("pseudoStates is not enum");
            }
        }

        public static void RemovePseudoState(this VisualElement element, int state)
        {
            int result = element.GetPseudoState();
            result &= ~state;
            var enumType = element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(element).GetType();
            if (enumType != null && enumType.IsEnum)
            {
                object enumValue = Enum.ToObject(enumType, result);
                element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(element, enumValue);
            }
            else
            {
                Debug.Log("pseudoStates is not enum");
            }
        }

        public static bool HasPseudoFlag(this VisualElement element, int flag)
        {
            int result = element.GetPseudoState();
            return (result & flag) == flag; 
        }

        public static void PseudoXOR(this VisualElement element, int flag)
        {
            int result = element.GetPseudoState();
            result ^= flag;
            var enumType = element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(element).GetType();
            if (enumType != null && enumType.IsEnum)
            {
                object enumValue = Enum.ToObject(enumType, result);
                element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(element, enumValue);
            }
            else
            {
                Debug.Log("pseudoStates is not enum");
            }
        }
        #endregion


        public static VisualElement GetFirstAncestorWhere(this VisualElement element, Predicate<VisualElement> predicate)
        {
            for (VisualElement visualElement = element.parent; visualElement != null; visualElement = visualElement.hierarchy.parent)
            {
                if (predicate(visualElement))
                {
                    return visualElement;
                }
            }

            return null;
        }

        public static void SetProperty(this VisualElement element, PropertyName key, object value)
        {
            s_SetPropertyMethod ??= typeof(VisualElement).GetMethod("SetProperty", BindingFlags.NonPublic | BindingFlags.Instance);
            s_SetPropertyMethod.Invoke(element, new object[] { key, value });
        }

        /// <summary>
        /// Create the delegate using reflection
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        public static SetLayoutDelegate CreateSetLayoutDelegate(this VisualElement element)
        {
            _setLayoutDelegateMap ??= new Dictionary<VisualElement, SetLayoutDelegate>();
            if (_setLayoutDelegateMap.TryGetValue(element, out var del))
            {
                return del;
            }

            // Get the internal setter method info
            MethodInfo setMethod = typeof(VisualElement).GetProperty("layout", BindingFlags.Instance | BindingFlags.NonPublic)
                                             .GetSetMethod(true);

            // Create the delegate
            del = (SetLayoutDelegate)Delegate.CreateDelegate(
                typeof(SetLayoutDelegate), element, setMethod);

            _setLayoutDelegateMap.Add(element, del);
            return del;
        }

        public static WorldTransformRefDelegate CreateWorldTransformRefDelegate(this VisualElement element)
        {
            _getWorldTransformRefDelegateMap ??= new Dictionary<VisualElement, WorldTransformRefDelegate>();
            if (_getWorldTransformRefDelegateMap.TryGetValue(element, out var del))
            {
                return del;
            }

            // Get the internal getter method info
            PropertyInfo propertyInfo = typeof(VisualElement).GetProperty("worldTransformRef", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo getMethod = propertyInfo.GetGetMethod(true);

            // Create the delegate
            del = (WorldTransformRefDelegate)Delegate.CreateDelegate(
                typeof(WorldTransformRefDelegate), element, getMethod);

            _getWorldTransformRefDelegateMap.Add(element, del);
            return del;
        }

        #region - Fluent Builder -
        public static T CreateChild<T>(this VisualElement parent, params string[] classes) where T : VisualElement, new()
        {
            var child = new T();
            child.AddClasses(classes).AddTo(parent);
            return child;
        }

        public static T QOrCreate<T>(this VisualElement parent, string name = null, params string[] classes) where T : VisualElement, new()
        {
            var found = parent.Q<T>(name, classes);
            return found ?? parent.CreateChild<T>(classes);
        }

        public static T AddTo<T>(this T child, VisualElement parent) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        public static T AddClasses<T>(this T element, params string[] classes) where T : VisualElement
        {
            foreach (var @class in classes)
            {
                if (!@class.EmptyOrNull())
                {
                    element.AddToClassList(@class);
                }
            }
            return element;
        }

        public static T WithManipulator<T>(this T element, IManipulator manipulator) where T : VisualElement
        {
            element.AddManipulator(manipulator);
            return element;
        }

        public static T WithName<T>(this T element, string name) where T : VisualElement
        {
            element.name = name;
            return element;
        }

        public static T WithPosition<T>(this T element, Position position) where T : VisualElement
        {
            element.style.position = position;
            return element;
        }

        public static void ToggleDisplay(this VisualElement element)
        {
            var current = element.style.display.value;
            switch (current)
            {
                case DisplayStyle.Flex:
                    element.style.display = DisplayStyle.None;
                    break;
                case DisplayStyle.None:
                    element.style.display = DisplayStyle.Flex;
                    break;
            }
        }

        public static bool IsOpen(this VisualElement element) => element.style.display == DisplayStyle.Flex;

        public static void Show(this VisualElement element) { element.style.display = DisplayStyle.Flex; }

        public static void Hide(this VisualElement element) { element.style.display = DisplayStyle.None; }
        #endregion

        #region - Layout -
        public static void ClampToPanel(this VisualElement element, VisualElement panel, float worldX, float worldY)
        {
            var bound = panel.worldBound;
            var elementBound = element.worldBound;

            // Clamp horizontally
            worldX = Mathf.Clamp(worldX, bound.xMin, bound.xMax - elementBound.width);

            // Clamp vertically
            worldY = Mathf.Clamp(worldY, bound.yMin, bound.yMax - elementBound.height);

            //if (worldX < bound.x)
            //{
            //    worldX = bound.x;
            //}
            //else if (element.worldBound.width + worldX > bound.width)
            //{
            //    worldX = bound.width - element.worldBound.width;
            //}

            //if (worldY < 0)
            //{
            //    worldY = bound.y;
            //}
            //else if (element.worldBound.height + worldY > bound.height)
            //{
            //    worldY = bound.height - element.worldBound.height;
            //}

            element.transform.position = new Vector3(worldX, worldY, 0);
        }

        public static void StretchToElementSize(this VisualElement element, VisualElement other)
        {
            element.style.width = other.style.width;
            element.style.height = other.style.height;
        }

        public static void DisconnectChildren(this VisualElement element)
        {
            for (var i = element.childCount - 1; i >= 0; i--)
            {
                element.RemoveAt(i);
            }
        }

        public static void Resize(this VisualElement element, Length width, Length height)
        {
            element.style.width = width;
            element.style.height = height;
        }

        public static void SetAnchors(this VisualElement element, ElementAnchor anchor)
        {
            switch (anchor)
            {
                case ElementAnchor.Top:
                    element.style.translate = new Translate(Length.Percent(-50), Length.Percent(0));
                    break;
                case ElementAnchor.TopRight:
                    element.style.translate = new Translate(Length.Percent(-100), Length.Percent(0));
                    break;
                case ElementAnchor.Right:
                    element.style.translate = new Translate(Length.Percent(-100), Length.Percent(-50));
                    break;
                case ElementAnchor.BottomRight:
                    element.style.translate = new Translate(Length.Percent(-100), Length.Percent(-100));
                    break;
                case ElementAnchor.Bottom:
                    element.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
                    break;
                case ElementAnchor.BottomLeft:
                    element.style.translate = new Translate(Length.Percent(0), Length.Percent(-100));
                    break;
                case ElementAnchor.Left:
                    element.style.translate = new Translate(Length.Percent(0), Length.Percent(-50));
                    break;
                case ElementAnchor.TopLeft:
                    element.style.translate = new Translate(Length.Percent(0), Length.Percent(0));
                    break;
                case ElementAnchor.Center:
                default:
                    element.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                    break;
            }
        }
        #endregion

        #region - Styling -

        public static void ConstructFromResourcePath(this VisualElement visualElement, string uxmlPath, string ussPath)
        {
            var uxml = Resources.Load<VisualTreeAsset>(uxmlPath);
            var ss = Resources.Load<StyleSheet>(ussPath);
            visualElement.styleSheets.Add(ss);
            uxml.CloneTree(visualElement);
        }
        
        public static void AddStylesheetFromResourcePath(this VisualElement visualElement, string ussPath)
        {
            var ss = Resources.Load<StyleSheet>(ussPath);
            visualElement.styleSheets.Add(ss);
        }
        
        public static void LoadUxmlFromResourcePath(this VisualElement visualElement, string uxmlPath)
        {
            var uxml = Resources.Load<VisualTreeAsset>(uxmlPath);
            uxml.CloneTree(visualElement);
        }

        public static VisualElement WithMargins(this VisualElement visualElement, Length margins)
        {
            visualElement.style.marginTop = margins;
            visualElement.style.marginBottom = margins;
            visualElement.style.marginLeft = margins;
            visualElement.style.marginRight = margins;
            return visualElement;
        }
        
        public static VisualElement WithMargins(this VisualElement visualElement, Length leftAndRight, Length topAndBottom)
        {
            visualElement.style.marginTop = topAndBottom;
            visualElement.style.marginBottom = topAndBottom;
            visualElement.style.marginLeft = leftAndRight;
            visualElement.style.marginRight = leftAndRight;
            return visualElement;
        }
        
        public static VisualElement WithMargins(this VisualElement visualElement, Length left, Length right, Length top, Length bottom)
        {
            visualElement.style.marginTop = top;
            visualElement.style.marginBottom = bottom;
            visualElement.style.marginLeft = left;
            visualElement.style.marginRight = right;
            return visualElement;
        }
        
        public static VisualElement WithPadding(this VisualElement visualElement, Length margins)
        {
            visualElement.style.paddingTop = margins;
            visualElement.style.paddingBottom = margins;
            visualElement.style.paddingLeft = margins;
            visualElement.style.paddingRight = margins;
            return visualElement;
        }
        
        public static VisualElement WithPadding(this VisualElement visualElement, Length leftAndRight, Length topAndBottom)
        {
            visualElement.style.paddingTop = topAndBottom;
            visualElement.style.paddingBottom = topAndBottom;
            visualElement.style.paddingLeft = leftAndRight;
            visualElement.style.paddingRight = leftAndRight;
            return visualElement;
        }
        
        public static VisualElement WithPadding(this VisualElement visualElement, Length left, Length right, Length top, Length bottom)
        {
            visualElement.style.paddingTop = top;
            visualElement.style.paddingBottom = bottom;
            visualElement.style.paddingLeft = left;
            visualElement.style.paddingRight = right;
            return visualElement;
        }
        
        public static VisualElement WithBorder(this VisualElement visualElement, float width, Length radius, Color borderColor)
        {
            visualElement.style.borderTopColor = borderColor;
            visualElement.style.borderBottomColor = borderColor;
            visualElement.style.borderLeftColor = borderColor;
            visualElement.style.borderRightColor = borderColor;
            
            visualElement.style.borderTopLeftRadius = radius;
            visualElement.style.borderTopRightRadius = radius;
            visualElement.style.borderBottomLeftRadius = radius;
            visualElement.style.borderBottomRightRadius = radius;
            
            visualElement.style.borderTopWidth = width;
            visualElement.style.borderBottomWidth = width;
            visualElement.style.borderLeftWidth = width;
            visualElement.style.borderRightWidth = width;
            
            return visualElement;
        }
        #endregion

        #region - Utility -

        public static bool IsFocused(this VisualElement element)
        {
            return element.focusController != null && element.focusController.focusedElement == element;
        }
        #endregion

        #region - Hierarchy -
        public static bool HasAncestor(this VisualElement element, VisualElement ancestor)
        {
            if (ancestor == null || element == null)
                return false;
            return element == ancestor || element.parent.HasAncestor(ancestor);
        }

        public static VisualElement GetFirstAncestorWithClass<T>(this VisualElement element, string className) where T : VisualElement
        {
            while (true)
            {
                if (element == null)
                {
                    return null;
                }

                if (element.ClassListContains(className) && element is T)
                {
                    return element;
                }

                element = element.hierarchy.parent;
            }
        }

        public static VisualElement GetFirstAncestorWithName<T>(this VisualElement element, string name) where T : VisualElement
        {
            while (true)
            {
                if (element == null)
                {
                    return null;
                }

                if (element.name.Equals(name) && element is T)
                {
                    return element;
                }

                element = element.hierarchy.parent;
            }
        }

        #endregion

        #region - Animation -
        public static void EnableAnimation(this VisualElement element, StyleList<StylePropertyName> name, StyleList<EasingFunction> easing, StyleList<TimeValue> duration, StyleList<TimeValue> delay)
        {
            if (element.style.transitionProperty == StyleKeyword.Null)
            {
                element.style.transitionProperty = name;
                element.style.transitionTimingFunction = easing;
                element.style.transitionDuration = duration;
                element.style.transitionDelay = delay;
            }
        }

        public static void DisableAnimation(this VisualElement element)
        {
            if(element.style.transitionProperty != StyleKeyword.Null)
            {
                element.style.transitionProperty = StyleKeyword.Null;
                element.style.transitionTimingFunction = StyleKeyword.Null;
                element.style.transitionDuration = StyleKeyword.Null;
                element.style.transitionDelay = StyleKeyword.Null;
            }
        }
        #endregion
    }
}
