using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    // [SuppressMessage("ReSharper", "InconsistentNaming")]
    // public class StyleProps
    // {
    //     private static readonly Dictionary<string, string> s_CachedProps = new();
    //
    //     private static readonly Dictionary<string, Action<StyleProps, string>> s_PropSetters = new()
    //     {
    //         // Margin
    //         ["m"] = (s, v) => s.m = v,
    //         ["mt"] = (s, v) => s.mt = v,
    //         ["mb"] = (s, v) => s.mb = v,
    //         ["ml"] = (s, v) => s.ml = v,
    //         ["mr"] = (s, v) => s.mr = v,
    //         ["mx"] = (s, v) => s.mx = v,
    //         ["my"] = (s, v) => s.my = v,
    //
    //         // Padding
    //         ["p"] = (s, v) => s.p = v,
    //         ["pt"] = (s, v) => s.pt = v,
    //         ["pb"] = (s, v) => s.pb = v,
    //         ["pl"] = (s, v) => s.pl = v,
    //         ["pr"] = (s, v) => s.pr = v,
    //         ["px"] = (s, v) => s.px = v,
    //         ["py"] = (s, v) => s.py = v,
    //
    //         // Border / Background / Color
    //         ["bd"] = (s, v) => s.bd = v,
    //         ["bdrs"] = (s, v) => s.bdrs = v,
    //         ["bdc"] = (s, v) => s.bdc = v,
    //         ["bg"] = (s, v) => s.bg = v,
    //         ["bt"] = (s, v) => s.bt = v,
    //         ["c"] = (s, v) => s.c = v,
    //
    //         // Font
    //         ["ff"] = (s, v) => s.ff = v,
    //         ["fz"] = (s, v) => s.fz = v,
    //         ["fw"] = (s, v) => s.fw = v.Trim().ToLowerInvariant(),
    //         ["lts"] = (s, v) => s.lts = v,
    //         ["ta"] = (s, v) => s.ta = v.Trim().ToLowerInvariant(),
    //         ["lh"] = (s, v) => s.lh = v,
    //         ["ts"] = (s, v) => s.ts = v,
    //         ["tt"] = (s, v) => s.tt = v.Trim().ToLowerInvariant(),
    //         ["td"] = (s, v) => s.td = v.Trim().ToLowerInvariant(),
    //
    //         // Size
    //         ["w"] = (s, v) => s.w = v,
    //         ["miw"] = (s, v) => s.miw = v,
    //         ["maw"] = (s, v) => s.maw = v,
    //         ["h"] = (s, v) => s.h = v,
    //         ["mih"] = (s, v) => s.mih = v,
    //         ["mah"] = (s, v) => s.mah = v,
    //
    //         // Position
    //         ["top"] = (s, v) => s.top = v,
    //         ["left"] = (s, v) => s.left = v,
    //         ["bottom"] = (s, v) => s.bottom = v,
    //         ["right"] = (s, v) => s.right = v,
    //         ["pos"] = (s, v) => s.pos = v.Trim().ToLowerInvariant(),
    //
    //         // Flexbox
    //         ["fb"] = (s, v) => s.fb = v,
    //         ["fs"] = (s, v) => s.fs = v,
    //         ["fg"] = (s, v) => s.fg = v,
    //         ["display"] = (s, v) => s.display = v.Trim().ToLowerInvariant(),
    //
    //         // Combo
    //         ["pm"] = (s, v) => s.pm = v,
    //     };
    //
    //     public string m, mt, mb, ml, mr, mx, my;
    //     public string p, pt, pb, pl, pr, px, py;
    //     public string bd, bdrs, bdc;
    //     public string bg, bt, c;
    //     public string ff, fz, fw, lts, ta, lh, ts, tt, td;
    //     public string w, miw, maw, h, mih, mah;
    //     public string top, left, bottom, right;
    //     public string fb, fs, fg;
    //     public string pos;
    //     public string display;
    //     public string pm;
    //
    //     public StyleProps(string props)
    //     {
    //         ParseStyleString(props);
    //         foreach (var (key, value) in s_CachedProps)
    //         {
    //             if (s_PropSetters.TryGetValue(key, out var apply))
    //             {
    //                 apply(this, value);
    //             }
    //         }
    //     }
    //
    //     private static void ParseStyleString(string props)
    //     {
    //         s_CachedProps.Clear();
    //         var parts = props.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    //         foreach (var part in parts)
    //         {
    //             var split = part.Split('=', 2);
    //             if (split.Length != 2) continue;
    //
    //             var key = split[0].Trim();
    //             var value = split[1].Trim();
    //             s_CachedProps[key] = value;
    //         }
    //     }
    // }

    // [UxmlElement]
    // public partial class Box : VisualElement, IStyleOverride
    // {
    //     private string _styleProps;
    //
    //     [UxmlAttribute, TextArea, CreateProperty]
    //     public string StyleProps
    //     {
    //         get => _styleProps;
    //         set
    //         {
    //             if (_styleProps == value)
    //             {
    //                 return;
    //             }
    //             _styleProps = value;
    //             _styleIsDirty = true;
    //             ApplyStyles();
    //         }
    //     }
    //
    //     public StyleOverride StyleOverride { get; } = new();
    //
    //
    //     private bool _styleIsDirty = true;
    //     private BoxProps _cachedProps;
    //
    //     public Box()
    //     {
    //         ApplyStyles();
    //     }
    //     
    //     public void ApplyStyles()
    //     {
    //         // Apply Base Styles
    //         if (_styleIsDirty)
    //         {
    //             _cachedProps = !string.IsNullOrEmpty(_styleProps) ? BoxProps.CreateProps(_styleProps) : BoxProps.ResetProps();
    //             StyleHelper.ApplyStyleProps(this, _cachedProps);
    //             _styleIsDirty = false;
    //         }
    //         else
    //         {
    //             // Apply current cached props.
    //             StyleHelper.ApplyStyleProps(this, _cachedProps);
    //         }
    //
    //         // Apply Custom Styles
    //         ApplyCustomStyles();
    //         
    //         // Apply Style Overrides
    //         StyleOverride.ApplyTo(this);
    //     }
    //
    //     protected virtual void ApplyCustomStyles() { }
    //
    //     public Box WithChildren(Action<Box> childFactory)
    //     {
    //         childFactory(this);
    //         return this;
    //     }
    //
    //     public T WithChildren<T>(Action<T> childFactory) where T : Box
    //     {
    //         Debug.Assert(this is T, $"{name} must be of type {typeof(T)} to be valid, but it is {GetType()}");
    //         var thisT = (T)this;
    //         childFactory(thisT);
    //         return thisT;
    //     }
    //
    //     public virtual void AddChild(VisualElement child)
    //     {
    //         Add(child);
    //     }
    // }

    public class Center : VisualElement, IVaporUIComponent
    {
        public const string ROOT_SELECTOR = "vapor-center-root";
        
        public ComponentStyleProps StyleProperties { get; } = new();
        public StyleOverride StyleOverride { get; } = new();
        public IVaporUIComponent VaporComponent => this;
        
        private bool _inline;

        public bool Inline
        {
            get => _inline;
            set
            {
                _inline = value;
                VaporComponent.ApplyStyles();
            }
        }
        
        public Center(string style = null)
        {
            AddToClassList(ROOT_SELECTOR);
            VaporComponent.ApplyStyles();
        }

        public void ApplyCustomStyles()
        {
            style.flexDirection = Inline ? FlexDirection.Row : FlexDirection.Column;
            
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
        }
    }

    // public static class TreeBuilder
    // {
    //     public static IVaporUIComponent BuildTree()
    //     {
    //         return new Group().VaporComponent.WithChildren<Group>(p =>
    //         {
    //             p.AddChild(new Label());
    //             p.AddChild(new Stack
    //             {
    //                 Align = Align.Center,
    //             }.VaporComponent.WithChildren<Stack>(p2 =>
    //             {
    //                 p2.AddChild(new Label());
    //                 p2.AddChild(new Label());
    //             }));
    //         });
    //     }
    // }
}
