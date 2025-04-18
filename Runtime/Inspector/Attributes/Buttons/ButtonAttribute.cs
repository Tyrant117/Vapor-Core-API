using System;
using System.Diagnostics;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ButtonAttribute : Attribute
    {
        public string Label { get; }
        public int Size { get; }
        public bool RebuildTree { get; }

        public ButtonAttribute(int size = 15, bool rebuildTree = false)
        {
            Label = null;
            Size = size;
            RebuildTree = rebuildTree;
        }

        public ButtonAttribute(string label, int size = 15, bool rebuildTree = false)
        {
            Label = label;
            Size = size;
            RebuildTree = rebuildTree;
        }
    }
}
