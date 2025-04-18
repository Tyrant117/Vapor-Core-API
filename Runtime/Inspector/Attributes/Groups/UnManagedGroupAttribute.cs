using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class UnManagedGroupAttribute : Attribute
    {
        public UIGroupType Type { get; }
        public string GroupName { get; }
        public string ParentName { get;  }
        public int Order { get; }
        public string Header { get; }

        public UnManagedGroupAttribute(UIGroupType unmanagedGroupType = UIGroupType.Vertical, int unmanagedGroupOrder = int.MaxValue)
        {
            Type = unmanagedGroupType;
            GroupName = "Unmanaged";
            ParentName = "";
            Order = unmanagedGroupOrder;
            Header = "Un-Grouped";
        }

        public VaporGroupAttribute ToGroupAttribute()
        {
            return Type switch
            {
                UIGroupType.Horizontal => new HorizontalGroupAttribute(GroupName, null, null, Order),
                UIGroupType.Vertical => new VerticalGroupAttribute(GroupName, Order),
                UIGroupType.Foldout => new FoldoutGroupAttribute(GroupName, Header, Order),
                UIGroupType.Box => new BoxGroupAttribute(GroupName, Header, Order),
                UIGroupType.Tab => new VerticalGroupAttribute(GroupName, Order),
                UIGroupType.Title => new TitleGroupAttribute(GroupName, Header, order: Order),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}
