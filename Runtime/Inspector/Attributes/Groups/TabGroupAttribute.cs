using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class TabGroupAttribute : VaporGroupAttribute
    {
        public string TabName { get; }
        public string[] Tabs { get; }
        public override UIGroupType Type => UIGroupType.Tab;

        public TabGroupAttribute(string groupName, string[] tabs, string tabName, int order = 0, string showIf = "")
        {
            GroupName = groupName.Replace(" ", "");
            Tabs = tabs;
            TabName = tabName;
            Order = order;

            int last = GroupName.LastIndexOf('/');
            ParentName = last != -1 ? GroupName[..last] : "";

            if (ResolverUtility.HasResolver(showIf, out var parsedResolver))
            {
                ShowIfResolver = parsedResolver;
            }
        }

        public TabGroupAttribute(string groupName, string tabName, int order = 0, string showIf = "")
        {
            GroupName = groupName.Replace(" ", "");
            Tabs = null;
            TabName = tabName;
            Order = order;

            int last = GroupName.LastIndexOf('/');
            ParentName = last != -1 ? GroupName[..last] : "";

            if (ResolverUtility.HasResolver(showIf, out var parsedResolver))
            {
                ShowIfResolver = parsedResolver;
            }
        }
    }
}
