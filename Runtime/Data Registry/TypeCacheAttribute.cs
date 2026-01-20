using System;

namespace Vapor
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Assembly)]
    public class TypeCacheAttribute : Attribute { }
}