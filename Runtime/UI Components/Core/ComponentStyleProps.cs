using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class ComponentStyleProps
    {
        private static readonly Dictionary<string, string> s_CachedProps = new();

        private static readonly Dictionary<string, Action<ComponentStyleProps, string>> s_PropSetters = new()
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

        public bool IsDirty { get; private set; }
        private string _style;

        public void SetStyle(string style)
        {
            if (_style == style)
            {
                return;
            }

            _style = style;
            IsDirty = true;
            if (_style.EmptyOrNull())
            {
                var nullLength = new StyleLength(StyleKeyword.Null);
                var nullFloat = new StyleFloat(StyleKeyword.Null);
                var nullColor = new StyleColor(StyleKeyword.Null);
                var nullFont = new StyleFont(StyleKeyword.Null);

                m = nullLength;
                mt = nullLength;
                mb = nullLength;
                ml = nullLength;
                mr = nullLength;
                mx = nullLength;
                my = nullLength;
                p = nullLength;
                pt = nullLength;
                pb = nullLength;
                pl = nullLength;
                pr = nullLength;
                px = nullLength;
                py = nullLength;
                bd = nullFloat;
                bdrs = nullLength;
                bdc = nullColor;
                bg = nullColor;
                bt = nullColor;
                c = nullColor;
                ff = nullFont;
                fz = nullLength;
                lts = nullLength;
                lh = nullLength;
                ts = nullLength;
                td = nullLength;
                fw = null; // nullable FontStyle
                ta = null; // nullable TextAnchor
                tt = null; // nullable TextOverflow
                w = nullLength;
                miw = nullLength;
                maw = nullLength;
                h = nullLength;
                mih = nullLength;
                mah = nullLength;
                top = nullLength;
                left = nullLength;
                bottom = nullLength;
                right = nullLength;
                fb = nullLength;
                fs = nullFloat;
                fg = nullFloat;
                pos = null; // nullable Position
                display = null; // nullable DisplayStyle
                pm = null; // nullable PickingMode
            }
            else
            {
                ParseStyleString(_style);
                foreach (var (key, value) in s_CachedProps)
                {
                    if (s_PropSetters.TryGetValue(key, out var apply))
                    {
                        apply(this, value);
                    }
                }
            }
        }

        public static ComponentStyleProps ResetProps()
        {
            var props = new ComponentStyleProps();
            props.SetStyle(string.Empty);
            return props;
        }

        public static ComponentStyleProps CreateProps(string props)
        {
            var boxProps = new ComponentStyleProps();
            boxProps.SetStyle(props);
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
}