using System;

namespace Vapor.Inspector
{
    public enum UIGroupType
    {
        Horizontal,
        Vertical,
        Foldout,
        Box,
        Tab,
        Title
    }

    public abstract class VaporGroupAttribute : Attribute
    {
        public virtual UIGroupType Type { get; protected set; }
        public string GroupName { get; protected set; }
        public string ParentName { get; protected set; }
        public int Order { get; protected set; }
        public string ShowIfResolver { get; protected set; }
    }
}
