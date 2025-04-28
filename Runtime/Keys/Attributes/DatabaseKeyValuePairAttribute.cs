using System;

namespace Vapor.Keys
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DatabaseKeyValuePairAttribute : Attribute
    {
        public bool UseAddressables { get; }
        public string AddressableLabel { get; }

        public DatabaseKeyValuePairAttribute(bool useAddressables = false, string addressableLabel = null)
        {
            UseAddressables = useAddressables;
            AddressableLabel = addressableLabel;
        }
    }
}
