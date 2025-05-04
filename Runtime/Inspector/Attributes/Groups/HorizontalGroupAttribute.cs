using System;
using System.Diagnostics;
using UnityEngine.Assertions;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class HorizontalGroupAttribute : VaporGroupAttribute
    {
        public override UIGroupType Type => UIGroupType.Horizontal;

        public bool UseSingleLabel { get; }
        public string SingleLabel { get; }
        public string SingleLabelResolver { get; }
        public StyleLength SingleLabelWidth { get; }
        public StyleLength FlexBasis { get; }

        public HorizontalGroupAttribute(string groupName, string singleLabel = "", string singleLabelWidth = null, int order = 0, string showIf = "", string flexBasis = "")
        {
            GroupName = groupName.Replace(" ", "");
            Order = order;

            if (!singleLabel.EmptyOrNull())
            {
                UseSingleLabel = true;
                SingleLabel = singleLabel;
                if (ResolverUtility.HasResolver(singleLabel, out var parsedSingleLabel))
                {
                    SingleLabelResolver = parsedSingleLabel;
                }
                SingleLabelWidth = ResolverUtility.GetStyleLength(singleLabelWidth);
            }

            if (!flexBasis.EmptyOrNull())
            {
                FlexBasis = ResolverUtility.GetStyleLength(flexBasis);
            }

            // Assert.IsFalse(Order == int.MaxValue, "Int.MaxValue is reserved");

            int last = GroupName.LastIndexOf('/');
            ParentName = last != -1 ? GroupName[..last] : "";

            if (ResolverUtility.HasResolver(showIf, out var parsedResolver))
            {
                ShowIfResolver = parsedResolver;
            }
        }
    }
}
