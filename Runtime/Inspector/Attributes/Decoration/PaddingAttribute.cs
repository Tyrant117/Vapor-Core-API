using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class PaddingAttribute : PropertyAttribute
    {
        public StyleLength Left { get; }
        public StyleLength Right { get; }
        public StyleLength Top { get; }
        public StyleLength Bottom { get; }

        /// <summary>
        /// Use this attribute to change the padding of a property.
        /// <para>
        /// The <paramref name="padding"/> parameter can be a formatted string representing different padding values:
        /// </para>
        /// <list type="table">
        /// <listheader>
        /// <term>String Format</term>
        /// <description>Resulting Margins</description>
        /// </listheader>
        /// <item>
        /// <term>0</term>
        /// <description>All margins set to 0.</description>
        /// </item>
        /// <item>
        /// <term>0,0</term>
        /// <description>Horizontal margins set to 0, vertical margins set to 0.</description>
        /// </item>
        /// <item>
        /// <term>0,0,0,0</term>
        /// <description>Left margin set to 0, right margin set to 0, top margin set to 0, bottom margin set to 0.</description>
        /// </item>
        /// </list>
        /// </summary>
        /// <param name="padding">A formatted string representing the margins.</param>
        public PaddingAttribute(string padding)
        {
            if (padding.EmptyOrNull())
            {
                Left = ResolverUtility.GetStyleLength(null);
                Right = ResolverUtility.GetStyleLength(null);
                Top = ResolverUtility.GetStyleLength(null);
                Bottom = ResolverUtility.GetStyleLength(null);
                return;
            }

            var split = padding.Split(',');
            switch (split.Length)
            {
                case 0:
                default:
                    Left = ResolverUtility.GetStyleLength(null);
                    Right = ResolverUtility.GetStyleLength(null);
                    Top = ResolverUtility.GetStyleLength(null);
                    Bottom = ResolverUtility.GetStyleLength(null);
                    break;
                case 1:
                    Left = ResolverUtility.GetStyleLength(split[0]);
                    Right = ResolverUtility.GetStyleLength(split[0]);
                    Top = ResolverUtility.GetStyleLength(split[0]);
                    Bottom = ResolverUtility.GetStyleLength(split[0]);
                    break;
                case 2:
                    Left = ResolverUtility.GetStyleLength(split[0]);
                    Right = ResolverUtility.GetStyleLength(split[0]);
                    Top = ResolverUtility.GetStyleLength(split[1]);
                    Bottom = ResolverUtility.GetStyleLength(split[1]);
                    break;
                case 4:
                    Left = ResolverUtility.GetStyleLength(split[0]);
                    Right = ResolverUtility.GetStyleLength(split[1]);
                    Top = ResolverUtility.GetStyleLength(split[2]);
                    Bottom = ResolverUtility.GetStyleLength(split[3]);
                    break;
            }
        }
    }
}
