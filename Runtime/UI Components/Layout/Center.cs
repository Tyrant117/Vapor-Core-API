using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class StyleProps
    {
        private static readonly Dictionary<string, string> s_CachedProps = new();

        private static readonly Dictionary<string, Action<StyleProps, string>> s_PropSetters = new()
        {
            // Margin
            ["m"] = (s, v) => s.m = v,
            ["mt"] = (s, v) => s.mt = v,
            ["mb"] = (s, v) => s.mb = v,
            ["ml"] = (s, v) => s.ml = v,
            ["mr"] = (s, v) => s.mr = v,
            ["mx"] = (s, v) => s.mx = v,
            ["my"] = (s, v) => s.my = v,

            // Padding
            ["p"] = (s, v) => s.p = v,
            ["pt"] = (s, v) => s.pt = v,
            ["pb"] = (s, v) => s.pb = v,
            ["pl"] = (s, v) => s.pl = v,
            ["pr"] = (s, v) => s.pr = v,
            ["px"] = (s, v) => s.px = v,
            ["py"] = (s, v) => s.py = v,

            // Border / Background / Color
            ["bd"] = (s, v) => s.bd = v,
            ["bdrs"] = (s, v) => s.bdrs = v,
            ["bdc"] = (s, v) => s.bdc = v,
            ["bg"] = (s, v) => s.bg = v,
            ["bt"] = (s, v) => s.bt = v,
            ["c"] = (s, v) => s.c = v,

            // Font
            ["ff"] = (s, v) => s.ff = v,
            ["fz"] = (s, v) => s.fz = v,
            ["fw"] = (s, v) => s.fw = v.Trim().ToLowerInvariant(),
            ["lts"] = (s, v) => s.lts = v,
            ["ta"] = (s, v) => s.ta = v.Trim().ToLowerInvariant(),
            ["lh"] = (s, v) => s.lh = v,
            ["ts"] = (s, v) => s.ts = v,
            ["tt"] = (s, v) => s.tt = v.Trim().ToLowerInvariant(),
            ["td"] = (s, v) => s.td = v.Trim().ToLowerInvariant(),

            // Size
            ["w"] = (s, v) => s.w = v,
            ["miw"] = (s, v) => s.miw = v,
            ["maw"] = (s, v) => s.maw = v,
            ["h"] = (s, v) => s.h = v,
            ["mih"] = (s, v) => s.mih = v,
            ["mah"] = (s, v) => s.mah = v,

            // Position
            ["top"] = (s, v) => s.top = v,
            ["left"] = (s, v) => s.left = v,
            ["bottom"] = (s, v) => s.bottom = v,
            ["right"] = (s, v) => s.right = v,
            ["pos"] = (s, v) => s.pos = v.Trim().ToLowerInvariant(),

            // Flexbox
            ["fb"] = (s, v) => s.fb = v,
            ["fs"] = (s, v) => s.fs = v,
            ["fg"] = (s, v) => s.fg = v,
            ["display"] = (s, v) => s.display = v.Trim().ToLowerInvariant(),

            // Combo
            ["pm"] = (s, v) => s.pm = v,
        };

        public string m, mt, mb, ml, mr, mx, my;
        public string p, pt, pb, pl, pr, px, py;
        public string bd, bdrs, bdc;
        public string bg, bt, c;
        public string ff, fz, fw, lts, ta, lh, ts, tt, td;
        public string w, miw, maw, h, mih, mah;
        public string top, left, bottom, right;
        public string fb, fs, fg;
        public string pos;
        public string display;
        public string pm;

        public StyleProps(string props)
        {
            ParseStyleString(props);
            foreach (var (key, value) in s_CachedProps)
            {
                if (s_PropSetters.TryGetValue(key, out var apply))
                {
                    apply(this, value);
                }
            }
        }

        private static void ParseStyleString(string props)
        {
            s_CachedProps.Clear();
            var parts = props.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var split = part.Split('=', 2);
                if (split.Length != 2) continue;

                var key = split[0].Trim();
                var value = split[1].Trim();
                s_CachedProps[key] = value;
            }
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class BoxProps
    {
        private static readonly Dictionary<string, string> s_CachedProps = new();

        private static readonly Dictionary<string, Action<BoxProps, string>> s_PropSetters = new()
        {
            // Margin
            ["m"] = (s, v) => s.m = StyleHelper.ParseLength(v),
            ["mt"] = (s, v) => s.mt = StyleHelper.ParseLength(v),
            ["mb"] = (s, v) => s.mb = StyleHelper.ParseLength(v),
            ["ml"] = (s, v) => s.ml = StyleHelper.ParseLength(v),
            ["mr"] = (s, v) => s.mr = StyleHelper.ParseLength(v),
            ["mx"] = (s, v) => s.mx = StyleHelper.ParseLength(v),
            ["my"] = (s, v) => s.my = StyleHelper.ParseLength(v),

            // Padding
            ["p"] = (s, v) => s.p = StyleHelper.ParseLength(v),
            ["pt"] = (s, v) => s.pt = StyleHelper.ParseLength(v),
            ["pb"] = (s, v) => s.pb = StyleHelper.ParseLength(v),
            ["pl"] = (s, v) => s.pl = StyleHelper.ParseLength(v),
            ["pr"] = (s, v) => s.pr = StyleHelper.ParseLength(v),
            ["px"] = (s, v) => s.px = StyleHelper.ParseLength(v),
            ["py"] = (s, v) => s.py = StyleHelper.ParseLength(v),

            // Border / Background / Color
            ["bd"] = (s, v) => s.bd = StyleHelper.ParseFloat(v),
            ["bdrs"] = (s, v) => s.bdrs = StyleHelper.ParseLength(v),
            ["bdc"] = (s, v) => s.bdc = StyleHelper.ParseColor(v),
            ["bg"] = (s, v) => s.bg = StyleHelper.ParseColor(v),
            ["bt"] = (s, v) => s.bt = StyleHelper.ParseColor(v),
            ["c"] = (s, v) => s.c = StyleHelper.ParseColor(v),

            // Font
            ["ff"] = (s, v) => s.ff = Resources.Load<Font>(v),
            ["fz"] = (s, v) => s.fz = StyleHelper.ParseLength(v),
            ["fw"] = (s, v) => s.fw = StyleHelper.TryParseEnum<FontStyle>(v.Trim().ToLowerInvariant(), out var parsed) ? parsed : null,
            ["lts"] = (s, v) => s.lts = StyleHelper.ParseLength(v),
            ["ta"] = (s, v) => s.ta = StyleHelper.TryParseEnum<TextAnchor>(v.Trim().ToLowerInvariant(), out var parsed) ? parsed : null,
            ["lh"] = (s, v) => s.lh = StyleHelper.ParseLength(v),
            ["ts"] = (s, v) => s.ts = StyleHelper.ParseLength(v),
            ["tt"] = (s, v) => s.tt = StyleHelper.TryParseEnum<TextOverflow>(v.Trim().ToLowerInvariant(), out var parsed) ? parsed : null,
            ["td"] = (s, v) => s.td = StyleHelper.ParseLength(v),

            // Size
            ["w"] = (s, v) => s.w = StyleHelper.ParseLength(v),
            ["miw"] = (s, v) => s.miw = StyleHelper.ParseLength(v),
            ["maw"] = (s, v) => s.maw = StyleHelper.ParseLength(v),
            ["h"] = (s, v) => s.h = StyleHelper.ParseLength(v),
            ["mih"] = (s, v) => s.mih = StyleHelper.ParseLength(v),
            ["mah"] = (s, v) => s.mah = StyleHelper.ParseLength(v),

            // Position
            ["top"] = (s, v) => s.top = StyleHelper.ParseLength(v),
            ["left"] = (s, v) => s.left = StyleHelper.ParseLength(v),
            ["bottom"] = (s, v) => s.bottom = StyleHelper.ParseLength(v),
            ["right"] = (s, v) => s.right = StyleHelper.ParseLength(v),
            ["pos"] = (s, v) => s.pos = StyleHelper.TryParseEnum<Position>(v.Trim().ToLowerInvariant(), out var parsed) ? parsed : null,

            // Flexbox
            ["fb"] = (s, v) => s.fb = StyleHelper.ParseLength(v),
            ["fs"] = (s, v) => s.fs = StyleHelper.ParseFloat(v),
            ["fg"] = (s, v) => s.fg = StyleHelper.ParseFloat(v),
            ["display"] = (s, v) => s.display = StyleHelper.TryParseEnum<DisplayStyle>(v.Trim().ToLowerInvariant(), out var parsed) ? parsed : null,

            // Combo
            ["pm"] = (s, v) => s.pm = StyleHelper.TryParseEnum<PickingMode>(v.Trim().ToLowerInvariant(), out var parsed) ? parsed : null,
        };
        
        public StyleLength? m, mt, mb, ml, mr, mx, my;
        public StyleLength? p, pt, pb, pl, pr, px, py;
        public StyleFloat? bd;
        public StyleLength? bdrs;
        public StyleColor? bdc, bg, bt, c;
        public StyleFont? ff;
        public StyleLength? fz, lts, lh, ts, td;
        public FontStyle? fw;
        public TextAnchor? ta;
        public TextOverflow? tt;
        public StyleLength? w, miw, maw, h, mih, mah;
        public StyleLength? top, left, bottom, right;
        public StyleLength? fb;
        public StyleFloat? fs, fg;
        public Position? pos;
        public DisplayStyle? display;
        public PickingMode? pm;

        public static BoxProps ResetProps()
        {
            var nullLength = new StyleLength(StyleKeyword.Null);
            var nullFloat = new StyleFloat(StyleKeyword.Null);
            var nullColor = new StyleColor(StyleKeyword.Null);
            var nullFont = new StyleFont(StyleKeyword.Null);

            return new BoxProps
            {
                m = nullLength, mt = nullLength, mb = nullLength, ml = nullLength, mr = nullLength, mx = nullLength, my = nullLength,
                p = nullLength, pt = nullLength, pb = nullLength, pl = nullLength, pr = nullLength, px = nullLength, py = nullLength,

                bd = nullFloat,
                bdrs = nullLength,
                bdc = nullColor, bg = nullColor, bt = nullColor, c = nullColor,

                ff = nullFont,
                fz = nullLength, lts = nullLength, lh = nullLength, ts = nullLength, td = nullLength,
                fw = null,               // nullable FontStyle
                ta = null,               // nullable TextAnchor
                tt = null,               // nullable TextOverflow

                w = nullLength, miw = nullLength, maw = nullLength,
                h = nullLength, mih = nullLength, mah = nullLength,

                top = nullLength, left = nullLength, bottom = nullLength, right = nullLength,

                fb = nullLength,
                fs = nullFloat, fg = nullFloat,

                pos = null,              // nullable Position
                display = null,          // nullable DisplayStyle
                pm = null                // nullable PickingMode
            };
        }
        
        public static BoxProps CreateProps(string props)
        {
            var boxProps = new BoxProps();
            ParseStyleString(props);
            foreach (var (key, value) in s_CachedProps)
            {
                if (s_PropSetters.TryGetValue(key, out var apply))
                {
                    apply(boxProps, value);
                }
            }

            return boxProps;
        }

        

        private static void ParseStyleString(string props)
        {
            s_CachedProps.Clear();
            var parts = props.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var split = part.Split('=', 2);
                if (split.Length != 2) continue;

                var key = split[0].Trim();
                var value = split[1].Trim();
                s_CachedProps[key] = value;
            }
        }
    }

    public static class StyleHelper
    {
        public static void ApplyStyleProps(Box box, BoxProps props)
        {
            // Margin
            if (props.m.HasValue) box.style.marginTop = box.style.marginBottom = box.style.marginLeft = box.style.marginRight = props.m.Value;
            if (props.mx.HasValue) box.style.marginLeft = box.style.marginRight = props.mx.Value;
            if (props.my.HasValue) box.style.marginTop = box.style.marginBottom = props.my.Value;
            if (props.mt.HasValue) box.style.marginTop = props.mt.Value;
            if (props.mb.HasValue) box.style.marginBottom = props.mb.Value;
            if (props.ml.HasValue) box.style.marginLeft = props.ml.Value;
            if (props.mr.HasValue) box.style.marginRight = props.mr.Value;

            // Padding
            if (props.p.HasValue) box.style.paddingTop = box.style.paddingBottom = box.style.paddingLeft = box.style.paddingRight = props.p.Value;
            if (props.px.HasValue) box.style.paddingLeft = box.style.paddingRight = props.px.Value;
            if (props.py.HasValue) box.style.paddingTop = box.style.paddingBottom = props.py.Value;
            if (props.pt.HasValue) box.style.paddingTop = props.pt.Value;
            if (props.pb.HasValue) box.style.paddingBottom = props.pb.Value;
            if (props.pl.HasValue) box.style.paddingLeft = props.pl.Value;
            if (props.pr.HasValue) box.style.paddingRight = props.pr.Value;

            // Border, Background, Color
            if (props.bd.HasValue) box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = props.bd.Value;
            if (props.bdrs.HasValue) box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = props.bdrs.Value;
            if (props.bdc.HasValue) box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = props.bdc.Value;
            if (props.bg.HasValue) box.style.backgroundColor = props.bg.Value;
            if (props.bt.HasValue) box.style.unityBackgroundImageTintColor = props.bt.Value;
            if (props.c.HasValue) box.style.color = props.c.Value;

            // Font
            if (props.ff.HasValue) box.style.unityFont = props.ff.Value;
            if (props.fz.HasValue) box.style.fontSize = props.fz.Value;
            if (props.lts.HasValue) box.style.letterSpacing = props.lts.Value;
            // if (props.lh.HasValue) box.style.lineHeight = props.lh.Value;
            // if (props.ts.HasValue) box.style.textShadow = props.ts.Value;
            // if (props.td.HasValue) box.style.textDecoration = props.td.Value;
            if (props.fw.HasValue) box.style.unityFontStyleAndWeight = props.fw.Value;
            if (props.ta.HasValue) box.style.unityTextAlign = props.ta.Value;
            if (props.tt.HasValue) box.style.textOverflow = props.tt.Value;

            // Size
            if (props.w.HasValue) box.style.width = props.w.Value;
            if (props.miw.HasValue) box.style.minWidth = props.miw.Value;
            if (props.maw.HasValue) box.style.maxWidth = props.maw.Value;
            if (props.h.HasValue) box.style.height = props.h.Value;
            if (props.mih.HasValue) box.style.minHeight = props.mih.Value;
            if (props.mah.HasValue) box.style.maxHeight = props.mah.Value;

            // Position
            if (props.top.HasValue) box.style.top = props.top.Value;
            if (props.left.HasValue) box.style.left = props.left.Value;
            if (props.bottom.HasValue) box.style.bottom = props.bottom.Value;
            if (props.right.HasValue) box.style.right = props.right.Value;

            // Flexbox
            if (props.fb.HasValue) box.style.flexBasis = props.fb.Value;
            if (props.fs.HasValue) box.style.flexShrink = props.fs.Value;
            if (props.fg.HasValue) box.style.flexGrow = props.fg.Value;

            // Other
            if (props.pos.HasValue) box.style.position = props.pos.Value;
            if (props.display.HasValue) box.style.display = props.display.Value;
            if (props.pm.HasValue) box.pickingMode = props.pm.Value;
        }

        public static void ApplyStyleProps(Box box, string styleProps)
        {
            var props = new StyleProps(styleProps);
            SetStyle(box, props);
        }

        private static void SetStyle(Box box, StyleProps p)
        {
            // Margin
            SetMargin(box, p);
            // Padding
            SetPadding(box, p);
            // Border
            SetBorder(box, p);
            // Colors
            SetColors(box, p);
            // Font
            SetFont(box, p);

            // Size
            SetSize(box, p);
            // Inset
            SetInset(box, p);
            // Flex
            SetFlex(box, p);

            // Enums
            SetEnums(box, p);
        }

        private static void SetMargin(Box box, StyleProps p)
        {
            // Base padding
            var all = ParseLength(p.m);
            if (all.HasValue)
                box.style.marginTop = box.style.marginRight =
                    box.style.marginBottom = box.style.marginLeft = all.Value;

            // Axis overrides
            var mx = ParseLength(p.mx);
            if (mx.HasValue)
            {
                box.style.marginLeft = box.style.marginRight = mx.Value;
            }

            var my = ParseLength(p.my);
            if (my.HasValue)
            {
                box.style.marginTop = box.style.marginBottom = my.Value;
            }

            // Individual overrides
            var mt = ParseLength(p.mt);
            if (mt.HasValue) box.style.marginTop = mt.Value;

            var mb = ParseLength(p.mb);
            if (mb.HasValue) box.style.marginBottom = mb.Value;

            var ml = ParseLength(p.ml);
            if (ml.HasValue) box.style.marginLeft = ml.Value;

            var mr = ParseLength(p.mr);
            if (mr.HasValue) box.style.marginRight = mr.Value;
        }

        private static void SetPadding(Box box, StyleProps p)
        {
            // Base padding
            var all = ParseLength(p.p);
            if (all.HasValue)
                box.style.paddingTop = box.style.paddingRight =
                    box.style.paddingBottom = box.style.paddingLeft = all.Value;

            // Axis overrides
            var px = ParseLength(p.px);
            if (px.HasValue)
            {
                box.style.paddingLeft = box.style.paddingRight = px.Value;
            }

            var py = ParseLength(p.py);
            if (py.HasValue)
            {
                box.style.paddingTop = box.style.paddingBottom = py.Value;
            }

            // Individual overrides
            var pt = ParseLength(p.pt);
            if (pt.HasValue) box.style.paddingTop = pt.Value;

            var pb = ParseLength(p.pb);
            if (pb.HasValue) box.style.paddingBottom = pb.Value;

            var pl = ParseLength(p.pl);
            if (pl.HasValue) box.style.paddingLeft = pl.Value;

            var pr = ParseLength(p.pr);
            if (pr.HasValue) box.style.paddingRight = pr.Value;
        }

        private static void SetBorder(Box box, StyleProps p)
        {
            var all = ParseFloat(p.bd);
            if (all.HasValue)
            {
                box.style.borderTopWidth = all.Value;
                box.style.borderRightWidth = all.Value;
                box.style.borderBottomWidth = all.Value;
                box.style.borderLeftWidth = all.Value;
            }

            var radius = ParseLength(p.bdrs);
            if (radius.HasValue)
            {
                box.style.borderTopLeftRadius = radius.Value;
                box.style.borderTopRightRadius = radius.Value;
                box.style.borderBottomLeftRadius = radius.Value;
                box.style.borderBottomRightRadius = radius.Value;
            }

            var borderColor = ParseColor(p.bdc);
            if (borderColor.HasValue)
            {
                box.style.borderTopColor = borderColor.Value;
                box.style.borderBottomColor = borderColor.Value;
                box.style.borderLeftColor = borderColor.Value;
                box.style.borderRightColor = borderColor.Value;
            }
        }

        private static void SetColors(Box box, StyleProps p)
        {
            var bg = ParseColor(p.bg);
            if (bg.HasValue)
            {
                box.style.backgroundColor = bg.Value;
            }

            var bt = ParseColor(p.bt);
            if (bt.HasValue)
            {
                box.style.unityBackgroundImageTintColor = bt.Value;
            }

            var c = ParseColor(p.c);
            if (c.HasValue)
            {
                box.style.color = c.Value;
            }
        }

        private static void SetSize(Box box, StyleProps p)
        {
            var w = ParseLength(p.w);
            if (w.HasValue)
            {
                box.style.width = w.Value;
            }

            var miw = ParseLength(p.miw);
            if (miw.HasValue)
            {
                box.style.minWidth = miw.Value;
            }

            var maw = ParseLength(p.maw);
            if (maw.HasValue)
            {
                box.style.maxWidth = maw.Value;
            }

            var h = ParseLength(p.h);
            if (h.HasValue)
            {
                box.style.height = h.Value;
            }

            var mih = ParseLength(p.mih);
            if (mih.HasValue)
            {
                box.style.minHeight = mih.Value;
            }

            var mah = ParseLength(p.mah);
            if (mah.HasValue)
            {
                box.style.maxHeight = mah.Value;
            }
        }

        private static void SetInset(Box box, StyleProps p)
        {
            var top = ParseLength(p.top);
            if (top.HasValue)
            {
                box.style.top = top.Value;
            }

            var left = ParseLength(p.left);
            if (left.HasValue)
            {
                box.style.left = left.Value;
            }

            var right = ParseLength(p.right);
            if (right.HasValue)
            {
                box.style.right = right.Value;
            }

            var bottom = ParseLength(p.bottom);
            if (bottom.HasValue)
            {
                box.style.bottom = bottom.Value;
            }
        }

        private static void SetFlex(Box box, StyleProps p)
        {
            var fb = ParseLength(p.fb);
            if (fb.HasValue)
            {
                box.style.flexBasis = fb.Value;
            }

            var fs = ParseFloat(p.fs);
            if (fs.HasValue)
            {
                box.style.flexShrink = fs.Value;
            }

            var fg = ParseFloat(p.fg);
            if (fg.HasValue)
            {
                box.style.flexGrow = fg.Value;
            }
        }

        private static void SetEnums(Box box, StyleProps p)
        {
            if (TryParseEnum<Position>(p.pos, out var pos))
            {
                box.style.position = pos;
            }

            if (TryParseEnum<DisplayStyle>(p.display, out var display))
            {
                box.style.display = display;
            }

            if (TryParseEnum<PickingMode>(p.pm, out var pm))
            {
                box.pickingMode = pm;
            }
        }

        private static void SetFont(Box box, StyleProps p)
        {
            // Font
            if(!string.IsNullOrEmpty(p.ff))
            {
                box.style.unityFont = Resources.Load<Font>(p.ff);
            }
            
            var fz = ParseLength(p.fz);
            if (fz.HasValue)
            {
                box.style.fontSize = fz.Value;
            }

            if (TryParseEnum<FontStyle>(p.fw, out var fw))
            {
                box.style.unityFontStyleAndWeight = fw;
            }
            
            var lts = ParseLength(p.lts);
            if (lts.HasValue)
            {
                box.style.letterSpacing = lts.Value;
            }

            if (TryParseEnum<TextAnchor>(p.ta, out var ta))
            {
                box.style.unityTextAlign = ta;
            }
            
            var lh = ParseLength(p.lh);
            if (lh.HasValue)
            {
                // box.style.text = lh.Value;
            }
            
            var ts = ParseLength(p.ts);
            if (ts.HasValue)
            {
                // box.style.textShadow = ts.Value;
            }
            
            if (TryParseEnum<TextOverflow>(p.tt, out var tt))
            {
                box.style.textOverflow = tt;
            }
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

    [UxmlElement]
    public partial class Box : VisualElement
    {
        private string _styleProps;

        [UxmlAttribute, TextArea, CreateProperty]
        public string StyleProps
        {
            get => _styleProps;
            set
            {
                if (_styleProps == value)
                {
                    return;
                }
                _styleProps = value;
                _styleIsDirty = true;
                ApplyStyles();
            }
        }

        private bool _styleIsDirty = true;

        private BoxProps _cachedProps;
        
        public Box() { }

        protected virtual void ApplyStyles()
        {
            if (_styleIsDirty)
            {
                _cachedProps = !string.IsNullOrEmpty(_styleProps) ? BoxProps.CreateProps(_styleProps) : BoxProps.ResetProps();
                StyleHelper.ApplyStyleProps(this, _cachedProps);
                _styleIsDirty = false;
            }
            else
            {
                // Apply current cached props.
                StyleHelper.ApplyStyleProps(this, _cachedProps);
            }
        }

        public Box WithChildren(Action<Box> childFactory)
        {
            childFactory(this);
            return this;
        }
        
        public virtual void AddChild(VisualElement child)
        {
            Add(child);
        }
    }

    [UxmlElement]
    public partial class Container : Box
    {
        public const string ROOT_SELECTOR = "vapor-container-root";

        private static readonly CustomStyleProperty<string> s_ContainerSize = new("--container-size");

        private bool _fluid;

        [UxmlAttribute]
        public bool Fluid
        {
            get => _fluid;
            set
            {
                _fluid = value;
                ApplyStyles();
            }
        }

        private string _size;

        [UxmlAttribute]
        public string Size
        {
            get => _size;
            set
            {
                _size = value;
                _cachedSize = StyleHelper.ParseLength(Size) ?? new StyleLength(StyleKeyword.Null);
                ApplyStyles();
            }
        }

        public Container()
        {
            AddToClassList(ROOT_SELECTOR);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        private StyleLength? _cachedSize;

        protected override void ApplyStyles()
        {
            base.ApplyStyles();
            if (Fluid)
            {
                style.maxWidth = new Length(100, LengthUnit.Percent);
            }
            else
            {
                if (!string.IsNullOrEmpty(Size))
                {
                    style.maxWidth = StyleHelper.ParseLength(Size) ?? Length.Auto();
                }
            }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(s_ContainerSize, out var value))
            {
                style.maxWidth = StyleHelper.ParseLength(value) ?? Length.Auto();
            }

            ApplyStyles();
        }
    }

    [UxmlElement]
    public partial class Center : Box
    {
        public const string ROOT_SELECTOR = "vapor-center-root";
        
        private bool _inline;
        [UxmlAttribute]
        public bool Inline
        {
            get => _inline;
            set
            {
                _inline = value;
                ApplyStyles();
            }
        }
        
        public Center()
        {
            AddToClassList(ROOT_SELECTOR);
            
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
        }

        protected override void ApplyStyles()
        {
            base.ApplyStyles();
            style.flexDirection = Inline ? FlexDirection.Row : FlexDirection.Column;
            
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
        }
    }

    [UxmlElement]
    public partial class Flex : Box
    {
        public const string ROOT_SELECTOR = "vapor-flex-root";

        private Align _align;

        [UxmlAttribute]
        public Align Align
        {
            get => _align;
            set
            {
                _align = value;
                ApplyStyles();
            }
        }

        private Justify _justify;

        [UxmlAttribute]
        public Justify Justify
        {
            get => _justify;
            set
            {
                _justify = value;
                ApplyStyles();
            }
        }

        private FlexDirection _direction;

        [UxmlAttribute]
        public FlexDirection FlexDirection
        {
            get => _direction;
            set
            {
                _direction = value;
                ApplyStyles();
            }
        }

        private Wrap _wrap;

        [UxmlAttribute]
        public Wrap Wrap
        {
            get => _wrap;
            set
            {
                _wrap = value;
                ApplyStyles();
            }
        }

        private string _gap;

        [UxmlAttribute]
        public string Gap
        {
            get => _gap;
            set
            {
                _gap = value;
                _cachedGap = StyleHelper.ParseLength(value) ?? Length.Auto();
                ApplyStyles();
            }
        }

        private Length? _cachedGap;

        public Flex()
        {
            AddToClassList(ROOT_SELECTOR);
            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);
        }

        protected override void ApplyStyles()
        {
            base.ApplyStyles();
            style.alignItems = Align;
            style.justifyContent = Justify;
            style.flexDirection = FlexDirection;
            style.flexWrap = Wrap;
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            this.Query<Gap>().ForEach(g => g.SetGap(_cachedGap.Value));
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            var children = Children().ToList();
            Clear();

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is Gap)
                {
                    continue;
                }

                Add(children[i]);

                // Add Gap after each element except the last
                if (i < children.Count - 1)
                {
                    Add(new Gap(_cachedGap.Value)); // or whatever spacing size you prefer
                }
            }
        }

        public override void AddChild(VisualElement child)
        {
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            if (childCount > 0)
            {
                Add(new Gap(_cachedGap.Value));
            }

            Add(child);
        }
    }

    [UxmlElement]
    public partial class Group : Box
    {
        public const string ROOT_SELECTOR = "vapor-group-root";
        private Align _align;

        [UxmlAttribute]
        public Align Align
        {
            get => _align;
            set
            {
                _align = value;
                ApplyStyles();
            }
        }

        private Justify _justify;

        [UxmlAttribute]
        public Justify Justify
        {
            get => _justify;
            set
            {
                _justify = value;
                ApplyStyles();
            }
        }

        private Wrap _wrap;

        [UxmlAttribute]
        public Wrap Wrap
        {
            get => _wrap;
            set
            {
                _wrap = value;
                ApplyStyles();
            }
        }

        private string _gap;

        [UxmlAttribute]
        public string Gap
        {
            get => _gap;
            set
            {
                _gap = value;
                _cachedGap = StyleHelper.ParseLength(value) ?? Length.Auto();
                ApplyStyles();
            }
        }

        private bool _grow;

        [UxmlAttribute]
        public bool Grow
        {
            get => _grow;
            set
            {
                _grow = value;
                ApplyStyles();
            }
        }

        private bool _reverse;

        [UxmlAttribute]
        public bool Reverse
        {
            get => _reverse;
            set
            {
                _reverse = value;
                ApplyStyles();
            }
        }

        private Length? _cachedGap;

        public Group()
        {
            AddToClassList(ROOT_SELECTOR);
            
            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        protected override void ApplyStyles()
        {
            base.ApplyStyles();
            style.alignItems = Align;
            style.justifyContent = Justify;
            style.flexDirection = Reverse ? FlexDirection.RowReverse : FlexDirection.Row;
            style.flexWrap = Wrap;

            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            this.Query<Gap>().ForEach(g => g.SetGap(_cachedGap.Value));

            foreach (var c in Children().Where(q => q is not UIComponents.Gap))
            {
                c.style.flexGrow = Grow ? 1f : new StyleFloat(StyleKeyword.Initial);
            }
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            var children = Children().ToList();
            Clear();

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is Gap)
                {
                    continue;
                }
                
                if (Grow)
                {
                    children[i].style.flexGrow = 1f;
                }
                Add(children[i]);

                // Add Gap after each element except the last
                if (i < children.Count - 1)
                {
                    Add(new Gap(_cachedGap.Value)); // or whatever spacing size you prefer
                }
            }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ApplyStyles();
        }

        public override void AddChild(VisualElement child)
        {
            _cachedGap ??= StyleHelper.ParseLength(Gap) ?? Length.Auto();
            if (childCount > 0)
            {
                Add(new Gap(_cachedGap.Value));
            }
            if (Grow)
            {
                child.style.flexGrow = 1f;
            }
            Add(child);
        }
    }

    [UxmlElement]
    public partial class Stack : Box
    {
        [UxmlAttribute]
        public Align Align { get => style.alignItems.value; set => style.alignItems = value; }

        [UxmlAttribute]
        public Justify Justify { get => style.justifyContent.value; set => style.justifyContent = value; }

        [UxmlAttribute]
        public Wrap Wrap { get => style.flexWrap.value; set => style.flexWrap = value; }

        private string _gap;
        [UxmlAttribute]
        public string Gap
        {
            get => _gap;
            set
            {
                _gap = value;
                _cachedGap = StyleHelper.ParseLength(value) ?? Length.Auto();
            }
        }

        [UxmlAttribute]
        public bool Grow { get; set; }

        private bool _reverse;

        [UxmlAttribute]
        public bool Reverse
        {
            get => style.flexDirection == FlexDirection.ColumnReverse;
            set
            {
                _reverse = value;
                if (_reverse)
                {
                    style.flexDirection = FlexDirection.ColumnReverse;
                }
                else
                {
                    style.flexDirection = FlexDirection.Column;
                }
            }
        }

        private Length _cachedGap = Length.None();
        
        public Stack()
        {
            style.flexDirection = Reverse ? FlexDirection.ColumnReverse : FlexDirection.Column;
            RegisterCallbackOnce<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }
        
        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_cachedGap == Length.None())
            {
                _cachedGap = StyleHelper.ParseLength(Gap) ?? Length.Auto();
            }
            
            var children = Children().ToList();
            Clear();

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is Gap)
                {
                    continue;
                }
                
                if (Grow)
                {
                    children[i].style.flexGrow = 1f;
                }
                Add(children[i]);

                // Add Gap after each element except the last
                if (i < children.Count - 1)
                {
                    Add(new Gap(_cachedGap)); // or whatever spacing size you prefer
                }
            }
        }
        
        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            
        }

        public override void AddChild(VisualElement child)
        {
            if (_cachedGap == Length.None())
            {
                _cachedGap = StyleHelper.ParseLength(Gap) ?? Length.Auto();
            }

            if (childCount > 0)
            {
                Add(new Gap(_cachedGap));
            }

            if (Grow)
            {
                child.style.flexGrow = 1f;
            }
            Add(child);
        }
    }

    public static class TreeBuilder
    {
        public static Box BuildTree()
        {
            return new Group().WithChildren(p =>
            {
                p.AddChild(new Label());
                p.AddChild(new Stack
                {
                    Align = Align.Center,
                }.WithChildren(p2 =>
                {
                    p2.AddChild(new Label());
                    p2.AddChild(new Label());
                }));
            });
        }
    }
}
