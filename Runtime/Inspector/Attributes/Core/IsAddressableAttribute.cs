using System;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class IsAddressableAttribute : Attribute
    {
        public string AddressableLabel { get; }

        public IsAddressableAttribute(string addressableLabel)
        {
            AddressableLabel = addressableLabel;
        }
    }
}