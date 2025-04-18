using System;
using System.Diagnostics;
using UnityEngine;

namespace Vapor.Inspector
{
    [Conditional("VAPOR_INSPECTOR")]
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class FilePathAttribute : PropertyAttribute
    {
        public bool AbsolutePath { get; }
        public string FileExtension { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="absolutePath">Should the absolute path be returned</param>
        /// <param name="fileExtension">The file extension to filter for, do not include the period in the extension</param>
        public FilePathAttribute(bool absolutePath = false, string fileExtension = "")
        {
            AbsolutePath = absolutePath;
            FileExtension = fileExtension;
        }
    }
}
