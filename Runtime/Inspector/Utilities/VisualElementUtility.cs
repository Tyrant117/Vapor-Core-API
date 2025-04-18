using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    

    public static class VisualElementUtility
    {
        private static readonly HashSet<string> s_usedNames = new HashSet<string>();

        private static readonly Type s_FoldoutType = typeof(Foldout);

        private static readonly string s_InspectorElementUssClassName = "unity-inspector-element";

        public static string GetUniqueName(string nameBase)
        {
            string text = nameBase;
            int num = 2;
            while (s_usedNames.Contains(text))
            {
                text = nameBase + num;
                num++;
            }

            s_usedNames.Add(text);
            return text;
        }

        public static int GetFoldoutDepth(this VisualElement element)
        {
            int num = 0;
            if (element.parent != null)
            {
                for (VisualElement parent = element.parent; parent != null; parent = parent.parent)
                {
                    if (s_FoldoutType.IsAssignableFrom(parent.GetType()))
                    {
                        num++;
                    }
                }
            }

            return num;
        }

        public static void AssignInspectorStyleIfNecessary(this VisualElement element, string classNameToEnable)
        {
            VisualElement firstAncestorWhere = element.GetFirstAncestorWhere((VisualElement i) => i.ClassListContains(s_InspectorElementUssClassName));
            element.EnableInClassList(classNameToEnable, firstAncestorWhere != null);
        }
        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 MultiplyMatrix44Point2(ref Matrix4x4 lhs, Vector2 point)
        {
            Vector2 result = default;
            result.x = lhs.m00 * point.x + lhs.m01 * point.y + lhs.m03;
            result.y = lhs.m10 * point.x + lhs.m11 * point.y + lhs.m13;
            return result;
        }

    }
}
