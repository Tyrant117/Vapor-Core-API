using System;
using System.Text;
using UnityEngine;

namespace Vapor.Inspector
{
    /// <summary>
    /// A static class that can be used to add rich text colors to inspector tooltips.
    /// </summary>
    public static class TooltipMarkup
    {
        public const string LangWordStart = "<b><color=#3D8FD6FF>";
        public const string LangWordEnd = "</color></b>";

        public const string InterfaceStart = "<b><color=#B8D7A1FF>";
        public const string InterfaceEnd = "</color></b>";

        public const string ClassStart = "<b><color=#4AC9B0FF>";
        public const string ClassEnd = "</color></b>";

        public const string MethodStart = "<b><color=#DCDCAAFF>";
        public const string MethodEnd = "</color></b>";

        public const string StructStart = "<b><color=#85C490FF>";
        public const string StructEnd = "</color></b>";

        private static readonly StringBuilder s_Sb = new();

        public static string LangWord(string langWord) => $"{LangWordStart}{langWord}{LangWordEnd}";
        public static string Interface(string interfaceName) => $"{InterfaceStart}{interfaceName}{InterfaceEnd}";
        public static string Class(string className) => $"{ClassStart}{className}{ClassEnd}";
        public static string Method(string methodName) => $"{MethodStart}{methodName}{MethodEnd}";
        public static string Struct(string structName) => $"{StructStart}{structName}{StructEnd}";
        public static string ClassMethod(string className, string methodName) => $"{ClassStart}{className}{ClassEnd}.{MethodStart}{methodName}{MethodEnd}";
        public static string StructMethod(string structName, string methodName) => $"{StructStart}{structName}{StructEnd}.{MethodStart}{methodName}{MethodEnd}";

        public static string Colorize(string str, Color color) => $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>{str}</color>";
        public static string BoldColorize(string str, Color color) => $"<b><color=#{ColorUtility.ToHtmlStringRGBA(color)}>{str}</color></b>";

        public static string FormatString(string tooltip)
        {
            s_Sb.Clear();
            s_Sb.Append(tooltip);
            s_Sb.Replace("<lw>", LangWordStart);
            s_Sb.Replace("</lw>", LangWordEnd);
            s_Sb.Replace("<itf>", InterfaceStart);
            s_Sb.Replace("</itf>", InterfaceEnd);
            s_Sb.Replace("<cls>", ClassStart);
            s_Sb.Replace("</cls>", ClassEnd);
            s_Sb.Replace("<str>", StructStart);
            s_Sb.Replace("</str>", StructEnd);
            s_Sb.Replace("<mth>", MethodStart);
            s_Sb.Replace("</mth>", MethodEnd);
            return s_Sb.ToString();
        }
    }

    public static class TooltipMarkupExtensions
    {
        public static string ToLangWordMarkup(this bool langWord) => $"{TooltipMarkup.LangWordStart}{langWord}{TooltipMarkup.LangWordEnd}";
        public static string ToLangWordMarkup(this string langWord) => $"{TooltipMarkup.LangWordStart}{langWord}{TooltipMarkup.LangWordEnd}";
        public static string ToInterfaceMarkup(this string interfaceName) => $"{TooltipMarkup.InterfaceStart}{interfaceName}{TooltipMarkup.InterfaceEnd}";
        public static string ToClassMarkup(this string className) => $"{TooltipMarkup.ClassStart}{className}{TooltipMarkup.ClassEnd}";
        public static string ToMethodMarkup(this string methodName) => $"{TooltipMarkup.MethodStart}{methodName}{TooltipMarkup.MethodEnd}";
        public static string ToStructMarkup(this string structName) => $"{TooltipMarkup.StructStart}{structName}{TooltipMarkup.StructEnd}";
    }
}
