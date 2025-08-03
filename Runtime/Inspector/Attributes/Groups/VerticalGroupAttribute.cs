using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class VerticalGroupAttribute : VaporGroupAttribute
    {
        public override UIGroupType Type => UIGroupType.Vertical;

        public VerticalGroupAttribute(string groupName, int order = 0, string showIf = "", string hideIf = "")
        {
            GroupName = groupName.Replace(" ", "");
            Order = order;
            // Assert.IsFalse(Order == int.MaxValue, "Int.MaxValue is reserved");

            int last = GroupName.LastIndexOf('/');
            ParentName = last != -1 ? GroupName[..last] : "";

            if (ResolverUtility.HasResolver(showIf, out var showResolver))
            {
                ShowIfResolver = showResolver;
            }
            
            if (ResolverUtility.HasResolver(hideIf, out var hideResolver))
            {
                HideIfResolver = hideResolver;
            }
        }
    }
}
