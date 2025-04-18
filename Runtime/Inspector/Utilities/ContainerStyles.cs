using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public static class ContainerStyles
    {
        public static StyleColor HeaderColor = new(new Color(0.3f, 0.3f, 0.3f));
        public static StyleColor BackgroundColor =  new(new Color(0.2509804f, 0.2509804f, 0.2509804f));
        public static StyleColor InspectorBackgroundColor = new(ColorFromHtml("#383838"));
        public static StyleColor DarkInspectorBackgroundColor = new(ColorFromHtml("#313131"));
        public static StyleColor BorderColor = new(new Color(0.132f, 0.132f, 0.132f));

        public static StyleColor TextDefault = new(new Color(0.7686275f, 0.7686275f, 0.7686275f));

        public static StyleColor LabelDefault = new(ColorFromHtml("#C4C4C4"));

        public static StyleColor ErrorText = new(ColorFromHtml("#D32222"));

        public static Color ColorFromHtml(string html)
        {
            ColorUtility.TryParseHtmlString(html, out var color);
            return color;
        }
    }
}
