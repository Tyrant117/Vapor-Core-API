using System;
using UnityEngine;

namespace Vapor.Inspector
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class TitleAttribute : PropertyAttribute
    {
        public string Title { get; }
        public string Subtitle { get; }
        public bool Underline { get; }

        public TitleAttribute(string title, string subtitle = "", bool underline = true)
        {
            Title = title;
            Subtitle = subtitle;
            Underline = underline;
        }        
    }
}
