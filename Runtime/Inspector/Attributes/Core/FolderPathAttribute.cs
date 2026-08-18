using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class FolderPathAttribute : PropertyAttribute
    {
        public bool AbsolutePath { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="absolutePath">Should the absolute path be returned</param>
        public FolderPathAttribute(bool absolutePath = false)
        {
            AbsolutePath = absolutePath;
        }
    }
}
