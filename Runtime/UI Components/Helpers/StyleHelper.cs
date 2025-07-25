using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public static class StyleHelper
    {
        public static void ApplyStyleProps(VisualElement ve, ComponentStyleProps props)
        {
            if(ve == null || props == null)
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
            // if (props.lh.HasValue) box.style.lineHeight = props.lh.Value;
            // if (props.ts.HasValue) box.style.textShadow = props.ts.Value;
            // if (props.td.HasValue) box.style.textDecoration = props.td.Value;
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
        }

        // public static void ApplyStyleProps(Box box, string styleProps)
        // {
        //     var props = new StyleProps(styleProps);
        //     SetStyle(box, props);
        // }
        //
        // private static void SetStyle(Box box, StyleProps p)
        // {
        //     // Margin
        //     SetMargin(box, p);
        //     // Padding
        //     SetPadding(box, p);
        //     // Border
        //     SetBorder(box, p);
        //     // Colors
        //     SetColors(box, p);
        //     // Font
        //     SetFont(box, p);
        //
        //     // Size
        //     SetSize(box, p);
        //     // Inset
        //     SetInset(box, p);
        //     // Flex
        //     SetFlex(box, p);
        //
        //     // Enums
        //     SetEnums(box, p);
        // }
        //
        // private static void SetMargin(Box box, StyleProps p)
        // {
        //     // Base padding
        //     var all = ParseLength(p.m);
        //     if (all.HasValue)
        //         box.style.marginTop = box.style.marginRight =
        //             box.style.marginBottom = box.style.marginLeft = all.Value;
        //
        //     // Axis overrides
        //     var mx = ParseLength(p.mx);
        //     if (mx.HasValue)
        //     {
        //         box.style.marginLeft = box.style.marginRight = mx.Value;
        //     }
        //
        //     var my = ParseLength(p.my);
        //     if (my.HasValue)
        //     {
        //         box.style.marginTop = box.style.marginBottom = my.Value;
        //     }
        //
        //     // Individual overrides
        //     var mt = ParseLength(p.mt);
        //     if (mt.HasValue) box.style.marginTop = mt.Value;
        //
        //     var mb = ParseLength(p.mb);
        //     if (mb.HasValue) box.style.marginBottom = mb.Value;
        //
        //     var ml = ParseLength(p.ml);
        //     if (ml.HasValue) box.style.marginLeft = ml.Value;
        //
        //     var mr = ParseLength(p.mr);
        //     if (mr.HasValue) box.style.marginRight = mr.Value;
        // }
        //
        // private static void SetPadding(Box box, StyleProps p)
        // {
        //     // Base padding
        //     var all = ParseLength(p.p);
        //     if (all.HasValue)
        //         box.style.paddingTop = box.style.paddingRight =
        //             box.style.paddingBottom = box.style.paddingLeft = all.Value;
        //
        //     // Axis overrides
        //     var px = ParseLength(p.px);
        //     if (px.HasValue)
        //     {
        //         box.style.paddingLeft = box.style.paddingRight = px.Value;
        //     }
        //
        //     var py = ParseLength(p.py);
        //     if (py.HasValue)
        //     {
        //         box.style.paddingTop = box.style.paddingBottom = py.Value;
        //     }
        //
        //     // Individual overrides
        //     var pt = ParseLength(p.pt);
        //     if (pt.HasValue) box.style.paddingTop = pt.Value;
        //
        //     var pb = ParseLength(p.pb);
        //     if (pb.HasValue) box.style.paddingBottom = pb.Value;
        //
        //     var pl = ParseLength(p.pl);
        //     if (pl.HasValue) box.style.paddingLeft = pl.Value;
        //
        //     var pr = ParseLength(p.pr);
        //     if (pr.HasValue) box.style.paddingRight = pr.Value;
        // }
        //
        // private static void SetBorder(Box box, StyleProps p)
        // {
        //     var all = ParseFloat(p.bd);
        //     if (all.HasValue)
        //     {
        //         box.style.borderTopWidth = all.Value;
        //         box.style.borderRightWidth = all.Value;
        //         box.style.borderBottomWidth = all.Value;
        //         box.style.borderLeftWidth = all.Value;
        //     }
        //
        //     var radius = ParseLength(p.bdrs);
        //     if (radius.HasValue)
        //     {
        //         box.style.borderTopLeftRadius = radius.Value;
        //         box.style.borderTopRightRadius = radius.Value;
        //         box.style.borderBottomLeftRadius = radius.Value;
        //         box.style.borderBottomRightRadius = radius.Value;
        //     }
        //
        //     var borderColor = ParseColor(p.bdc);
        //     if (borderColor.HasValue)
        //     {
        //         box.style.borderTopColor = borderColor.Value;
        //         box.style.borderBottomColor = borderColor.Value;
        //         box.style.borderLeftColor = borderColor.Value;
        //         box.style.borderRightColor = borderColor.Value;
        //     }
        // }
        //
        // private static void SetColors(Box box, StyleProps p)
        // {
        //     var bg = ParseColor(p.bg);
        //     if (bg.HasValue)
        //     {
        //         box.style.backgroundColor = bg.Value;
        //     }
        //
        //     var bt = ParseColor(p.bt);
        //     if (bt.HasValue)
        //     {
        //         box.style.unityBackgroundImageTintColor = bt.Value;
        //     }
        //
        //     var c = ParseColor(p.c);
        //     if (c.HasValue)
        //     {
        //         box.style.color = c.Value;
        //     }
        // }
        //
        // private static void SetSize(Box box, StyleProps p)
        // {
        //     var w = ParseLength(p.w);
        //     if (w.HasValue)
        //     {
        //         box.style.width = w.Value;
        //     }
        //
        //     var miw = ParseLength(p.miw);
        //     if (miw.HasValue)
        //     {
        //         box.style.minWidth = miw.Value;
        //     }
        //
        //     var maw = ParseLength(p.maw);
        //     if (maw.HasValue)
        //     {
        //         box.style.maxWidth = maw.Value;
        //     }
        //
        //     var h = ParseLength(p.h);
        //     if (h.HasValue)
        //     {
        //         box.style.height = h.Value;
        //     }
        //
        //     var mih = ParseLength(p.mih);
        //     if (mih.HasValue)
        //     {
        //         box.style.minHeight = mih.Value;
        //     }
        //
        //     var mah = ParseLength(p.mah);
        //     if (mah.HasValue)
        //     {
        //         box.style.maxHeight = mah.Value;
        //     }
        // }
        //
        // private static void SetInset(Box box, StyleProps p)
        // {
        //     var top = ParseLength(p.top);
        //     if (top.HasValue)
        //     {
        //         box.style.top = top.Value;
        //     }
        //
        //     var left = ParseLength(p.left);
        //     if (left.HasValue)
        //     {
        //         box.style.left = left.Value;
        //     }
        //
        //     var right = ParseLength(p.right);
        //     if (right.HasValue)
        //     {
        //         box.style.right = right.Value;
        //     }
        //
        //     var bottom = ParseLength(p.bottom);
        //     if (bottom.HasValue)
        //     {
        //         box.style.bottom = bottom.Value;
        //     }
        // }
        //
        // private static void SetFlex(Box box, StyleProps p)
        // {
        //     var fb = ParseLength(p.fb);
        //     if (fb.HasValue)
        //     {
        //         box.style.flexBasis = fb.Value;
        //     }
        //
        //     var fs = ParseFloat(p.fs);
        //     if (fs.HasValue)
        //     {
        //         box.style.flexShrink = fs.Value;
        //     }
        //
        //     var fg = ParseFloat(p.fg);
        //     if (fg.HasValue)
        //     {
        //         box.style.flexGrow = fg.Value;
        //     }
        // }
        //
        // private static void SetEnums(Box box, StyleProps p)
        // {
        //     if (TryParseEnum<Position>(p.pos, out var pos))
        //     {
        //         box.style.position = pos;
        //     }
        //
        //     if (TryParseEnum<DisplayStyle>(p.display, out var display))
        //     {
        //         box.style.display = display;
        //     }
        //
        //     if (TryParseEnum<PickingMode>(p.pm, out var pm))
        //     {
        //         box.pickingMode = pm;
        //     }
        // }
        //
        // private static void SetFont(Box box, StyleProps p)
        // {
        //     // Font
        //     if(!string.IsNullOrEmpty(p.ff))
        //     {
        //         box.style.unityFont = Resources.Load<Font>(p.ff);
        //     }
        //     
        //     var fz = ParseLength(p.fz);
        //     if (fz.HasValue)
        //     {
        //         box.style.fontSize = fz.Value;
        //     }
        //
        //     if (TryParseEnum<FontStyle>(p.fw, out var fw))
        //     {
        //         box.style.unityFontStyleAndWeight = fw;
        //     }
        //     
        //     var lts = ParseLength(p.lts);
        //     if (lts.HasValue)
        //     {
        //         box.style.letterSpacing = lts.Value;
        //     }
        //
        //     if (TryParseEnum<TextAnchor>(p.ta, out var ta))
        //     {
        //         box.style.unityTextAlign = ta;
        //     }
        //     
        //     var lh = ParseLength(p.lh);
        //     if (lh.HasValue)
        //     {
        //         // box.style.text = lh.Value;
        //     }
        //     
        //     var ts = ParseLength(p.ts);
        //     if (ts.HasValue)
        //     {
        //         // box.style.textShadow = ts.Value;
        //     }
        //     
        //     if (TryParseEnum<TextOverflow>(p.tt, out var tt))
        //     {
        //         box.style.textOverflow = tt;
        //     }
        // }
        //
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