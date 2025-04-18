using System;

namespace Vapor.Keys
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DatabaseKeyValuePairAttribute : Attribute
    {
        public bool UseAddressables { get; }
        public string AddressableLabel { get; }

        public DatabaseKeyValuePairAttribute(string addressableLabel = null)
        {
            UseAddressables = addressableLabel != null;
            AddressableLabel = addressableLabel;
        }
    }
}
