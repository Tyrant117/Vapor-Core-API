using System;
using UnityEngine.Assertions;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method , AllowMultiple = true, Inherited = true)]
    public class BoxGroupAttribute : VaporGroupAttribute
    {
        public string Header { get; }
        public override UIGroupType Type => UIGroupType.Box;

        public BoxGroupAttribute(string groupName, string header = "", int order = 0, string showIf = "")
        {
            GroupName = groupName.Replace(" ", "");
            Header = string.Empty == header ? groupName : header;
            Order = order;
            // Assert.IsFalse(Order == int.MaxValue, "Int.MaxValue is reserved");

            var last = GroupName.LastIndexOf('/');
            ParentName = last != -1 ? GroupName[..last] : "";

            if(ResolverUtility.HasResolver(showIf, out var parsedResolver))
            {
                ShowIfResolver = parsedResolver;
            }
        }
    }
}
