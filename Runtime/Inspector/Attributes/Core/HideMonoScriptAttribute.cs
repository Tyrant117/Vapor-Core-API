using System;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class HideMonoScriptAttribute : Attribute
    {

    }
}
