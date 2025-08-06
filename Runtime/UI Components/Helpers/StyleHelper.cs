using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public static class StyleHelper
    {
        public static string GetInspectorLabelStyle() => "ml=3 fg=1 fs=1 ov=hidden tt=ellipsis ta=middleleft miw=33% maw=33%";

        public static void ApplyStyleProps(VisualElement ve, ComponentStyleProps props)
        {
            if (ve == null || props == null)
            {
                return;
            }

            // Margin
            if (props.m.HasValue) ve.style.marginTop = ve.style.marginBottom = ve.style.marginLeft = ve.style.marginRight = props.m.Value;
            if (props.mx.HasValue) ve.style.marginLeft = ve.style.marginRight = props.mx.Value;
            if (props.my.HasValue) ve.style.marginTop = ve.style.marginBottom = props.my.Value;
            if (props.mt.HasValue) ve.style.marginTop = props.mt.Value;
            if (props.mb.HasValue) ve.style.marginBottom = props.mb.Value;
            if (props.ml.HasValue) ve.style.marginLeft = props.ml.Value;
            if (props.mr.HasValue) ve.style.marginRight = props.mr.Value;

            // Padding
            if (props.p.HasValue) ve.style.paddingTop = ve.style.paddingBottom = ve.style.paddingLeft = ve.style.paddingRight = props.p.Value;
            if (props.px.HasValue) ve.style.paddingLeft = ve.style.paddingRight = props.px.Value;
            if (props.py.HasValue) ve.style.paddingTop = ve.style.paddingBottom = props.py.Value;
            if (props.pt.HasValue) ve.style.paddingTop = props.pt.Value;
            if (props.pb.HasValue) ve.style.paddingBottom = props.pb.Value;
            if (props.pl.HasValue) ve.style.paddingLeft = props.pl.Value;
            if (props.pr.HasValue) ve.style.paddingRight = props.pr.Value;

            // Border, Background, Color
            if (props.bd.HasValue) ve.style.borderTopWidth = ve.style.borderBottomWidth = ve.style.borderLeftWidth = ve.style.borderRightWidth = props.bd.Value;
            if (props.bdrs.HasValue) ve.style.borderTopLeftRadius = ve.style.borderTopRightRadius = ve.style.borderBottomLeftRadius = ve.style.borderBottomRightRadius = props.bdrs.Value;
            if (props.bdc.HasValue) ve.style.borderTopColor = ve.style.borderBottomColor = ve.style.borderLeftColor = ve.style.borderRightColor = props.bdc.Value;
            if (props.bg.HasValue) ve.style.backgroundColor = props.bg.Value;
            if (props.bt.HasValue) ve.style.unityBackgroundImageTintColor = props.bt.Value;
            if (props.c.HasValue) ve.style.color = props.c.Value;

            // Font
            if (props.ff.HasValue) ve.style.unityFont = props.ff.Value;
            if (props.fz.HasValue) ve.style.fontSize = props.fz.Value;
            if (props.lts.HasValue) ve.style.letterSpacing = props.lts.Value;
            if (props.fw.HasValue) ve.style.unityFontStyleAndWeight = props.fw.Value;
            if (props.ta.HasValue) ve.style.unityTextAlign = props.ta.Value;
            if (props.tt.HasValue) ve.style.textOverflow = props.tt.Value;

            // Size
            if (props.w.HasValue) ve.style.width = props.w.Value;
            if (props.miw.HasValue) ve.style.minWidth = props.miw.Value;
            if (props.maw.HasValue) ve.style.maxWidth = props.maw.Value;
            if (props.h.HasValue) ve.style.height = props.h.Value;
            if (props.mih.HasValue) ve.style.minHeight = props.mih.Value;
            if (props.mah.HasValue) ve.style.maxHeight = props.mah.Value;

            // Position
            if (props.top.HasValue) ve.style.top = props.top.Value;
            if (props.left.HasValue) ve.style.left = props.left.Value;
            if (props.bottom.HasValue) ve.style.bottom = props.bottom.Value;
            if (props.right.HasValue) ve.style.right = props.right.Value;

            // Flexbox
            if (props.fb.HasValue) ve.style.flexBasis = props.fb.Value;
            if (props.fs.HasValue) ve.style.flexShrink = props.fs.Value;
            if (props.fg.HasValue) ve.style.flexGrow = props.fg.Value;

            // Other
            if (props.pos.HasValue) ve.style.position = props.pos.Value;
            if (props.display.HasValue) ve.style.display = props.display.Value;
            if (props.pm.HasValue) ve.pickingMode = props.pm.Value;
            if (props.ov.HasValue) ve.style.overflow = props.ov.Value;
            if (props.justify.HasValue) ve.style.justifyContent = props.justify.Value;
            if (props.align.HasValue) ve.style.alignItems = props.align.Value;
        }

        public static StyleFloat? ParseFloat(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
        
            value = value.Trim();
        
            if (value.EndsWith("px"))
            {
                if (float.TryParse(value[..^2], out var px))
                {
                    return new StyleFloat(px);
                }
            }
            else if (float.TryParse(value, out var p))
            {
                return new StyleFloat(p);
            }
        
            Debug.LogWarning($"Unable to parse float value: '{value}'");
            return null;
        }
        
        public static Length? ParseLength(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
        
            value = value.Trim();
        
            if (value.EndsWith("px"))
            {
                if (float.TryParse(value[..^2], out var px))
                {
                    return new Length(px, LengthUnit.Pixel);
                }
            }
            else if (value.EndsWith("%"))
            {
                if (float.TryParse(value[..^1], out var percent))
                {
                    return new Length(percent, LengthUnit.Percent);
                }
            }
            else if (float.TryParse(value, out var number))
            {
                // Default to pixels if no unit
                return new Length(number, LengthUnit.Pixel);
            }
        
            Debug.LogWarning($"Unable to parse length value: '{value}'");
            return null;
        }
        
        public static StyleColor? ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
        
            value = value.Trim();
        
            // Hex: #RGB, #RRGGBB, #RRGGBBAA
            if (value.StartsWith("#"))
            {
                if (ColorUtility.TryParseHtmlString(value, out var hexColor))
                    return new StyleColor(hexColor);
                return null;
            }
        
            // rgb(255, 255, 255)
            var rgbMatch = Regex.Match(value, @"^rgb\s*\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)$");
            if (rgbMatch.Success)
            {
                if (TryParseByte(rgbMatch.Groups[1].Value, out var r) &&
                    TryParseByte(rgbMatch.Groups[2].Value, out var g) &&
                    TryParseByte(rgbMatch.Groups[3].Value, out var b))
                {
                    return new StyleColor(new Color32(r, g, b, 255));
                }
            }
        
            // rgba(255, 255, 255, 0.5)
            var rgbaMatch = Regex.Match(value, @"^rgba\s*\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d*\.?\d+)\s*\)$");
            if (rgbaMatch.Success)
            {
                if (TryParseByte(rgbaMatch.Groups[1].Value, out var r) &&
                    TryParseByte(rgbaMatch.Groups[2].Value, out var g) &&
                    TryParseByte(rgbaMatch.Groups[3].Value, out var b) &&
                    float.TryParse(rgbaMatch.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                {
                    byte alpha = (byte)(Mathf.Clamp01(a) * 255);
                    return new StyleColor(new Color32(r, g, b, alpha));
                }
            }
        
            return null;
        }
        
        private static bool TryParseByte(string s, out byte result)
        {
            if (byte.TryParse(s, out result))
                return true;
        
            result = 0;
            return false;
        }
        
        public static bool TryParseEnum<TEnum>(string input, out TEnum result) where TEnum : struct
        {
            foreach (var name in Enum.GetNames(typeof(TEnum)))
            {
                if (string.Equals(name, input, StringComparison.OrdinalIgnoreCase))
                {
                    result = (TEnum)Enum.Parse(typeof(TEnum), name);
                    return true;
                }
            }
        
            result = default;
            return false;
        }
    }
}