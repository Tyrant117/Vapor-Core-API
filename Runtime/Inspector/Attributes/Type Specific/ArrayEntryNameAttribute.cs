using System;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public class ArrayEntryNameAttribute : Attribute
    {
        public string Resolver { get; } = "";

        public ArrayEntryNameAttribute(string resolver)
        {
            if (!ResolverUtility.HasResolver(resolver, out var parsed)) return;

            Resolver = parsed;
        }
    }
}
