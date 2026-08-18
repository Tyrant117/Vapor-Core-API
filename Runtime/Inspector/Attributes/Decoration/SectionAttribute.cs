using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class SectionAttribute : PropertyAttribute
    {
    
    }
}
