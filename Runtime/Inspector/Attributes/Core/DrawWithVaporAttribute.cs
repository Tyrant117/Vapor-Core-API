using System;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
    public class DrawWithVaporAttribute : Attribute
    {
        public UIGroupType InlinedGroupType { get; }

        public DrawWithVaporAttribute(UIGroupType inlinedGroupType = UIGroupType.Foldout)
        {
            InlinedGroupType = inlinedGroupType;
        }
    }
}
