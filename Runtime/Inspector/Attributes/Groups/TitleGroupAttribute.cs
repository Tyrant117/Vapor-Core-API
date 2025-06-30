using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class TitleGroupAttribute : VaporGroupAttribute
    {
        public string Title { get; }
        public string Subtitle { get; }
        public bool Underline { get; }
        public override UIGroupType Type => UIGroupType.Title;

        public TitleGroupAttribute(string groupName, string title = "", string subtitle = "", bool underline = true, int order = 0, string showIf = "", string hideIf = "")
        {
            GroupName = groupName.Replace(" ", "");
            Title = string.Empty == title ? groupName : title;
            Subtitle = subtitle;
            Underline = underline;
            Order = order;

            int last = GroupName.LastIndexOf('/');
            ParentName = last != -1 ? GroupName[..last] : "";

            if (ResolverUtility.HasResolver(showIf, out var showParsedResolver))
            {
                ShowIfResolver = showParsedResolver;
            }
            
            if (ResolverUtility.HasResolver(hideIf, out var hideParsedResolver))
            {
                HideIfResolver = hideParsedResolver;
            }
        }
    }
}
