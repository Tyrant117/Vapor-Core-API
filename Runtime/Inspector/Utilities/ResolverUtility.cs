using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;

namespace Vapor.Inspector
{
    public static class ResolverUtility
    {
        private const char k_ResolverCharacter = '@';
        private const char k_ColorHtmlHashtag = '#';

        public static bool HasResolver(string resolver, out string parsedResolver)
        {
            parsedResolver = resolver;
            if (string.IsNullOrEmpty(resolver))
            {                
                return false;
            }
            else
            {
                var first = resolver[0];
                if(first.Equals(k_ResolverCharacter))
                {
                    parsedResolver = resolver[1..];
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public static Color GetColor(string colorStringResolver, Color defaultColor, out bool hasResolver, out string parsedColorStringResolver)
        {
            var color = defaultColor;
            hasResolver = false;
            parsedColorStringResolver = colorStringResolver;
            if (colorStringResolver.EmptyOrNull())
            {                
                return color;
            }
            else
            {
                char first = colorStringResolver[0];
                if (first.Equals(k_ColorHtmlHashtag))
                {
                    color = ColorUtility.TryParseHtmlString(colorStringResolver, out var htmlColor) ? htmlColor : Color.white;
                }
                else if (HasResolver(colorStringResolver, out parsedColorStringResolver))
                {
                    hasResolver = true;
                }
                else
                {
                    var split = colorStringResolver.Split(',');
                    if (split.Length == 4)
                    {
                        color = float.TryParse(split[0], out var r) && float.TryParse(split[1], out var g) && float.TryParse(split[2], out var b) && float.TryParse(split[3], out var a)
                            ? new Color(r, g, b, a)
                            : Color.white;
                    }
                    else if (split.Length == 3)
                    {
                        color = float.TryParse(split[0], out var r) && float.TryParse(split[1], out var g) && float.TryParse(split[2], out var b)
                            ? new Color(r, g, b)
                            : Color.white;
                    }
                    else
                    {
                        color = Color.white;
                    }
                }
                return color;
            }
        }

        public static StyleLength GetStyleLength(string str)
        {
            if (str.EmptyOrNull())
            {
                return new StyleLength(StyleKeyword.None);
            }
            else if (str.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return new StyleLength(StyleKeyword.Auto);
            }
            else
            {
                return str[^1] == '%'
                    ? new StyleLength(new Length(float.Parse(str[..^1]), LengthUnit.Percent))
                    : new StyleLength(float.Parse(str));
            }
        }

        public static StyleLength GetStyleLength(int val)
        {
            return val == int.MinValue 
                ? new StyleLength(StyleKeyword.None) 
                : new StyleLength(val);
        }

        public static StyleLength GetStyleLength(float val)
        {
            return val == float.MinValue
                ? new StyleLength(StyleKeyword.None)
                : val is > 0 and < 1f ? new StyleLength(new Length(val * 100, LengthUnit.Percent)) : new StyleLength(val);
        }
    }
}
