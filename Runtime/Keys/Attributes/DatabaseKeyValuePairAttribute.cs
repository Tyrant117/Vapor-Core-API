using System;

namespace Vapor.Keys
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DatabaseKeyValuePairAttribute : Attribute
    {
        public bool UseAddressables { get; }
        public string AddressableLabel { get; }
        public int Order { get; }

        public DatabaseKeyValuePairAttribute(bool useAddressables = false, string addressableLabel = null, int order = 0)
        {
            UseAddressables = useAddressables;
            AddressableLabel = addressableLabel;
            Order = order;
        }
    }
}
