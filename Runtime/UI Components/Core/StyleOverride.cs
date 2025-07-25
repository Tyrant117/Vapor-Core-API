using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.UIComponents
{
    public class StyleOverride
    {
        // Generic helper
        private static bool TryGetValue<T>(T? field, out T value) where T : struct
        {
            if (field.HasValue)
            {
                value = field.Value;
                return true;
            }

            value = default;
            return false;
        }

        // ===== Properties =====

        private StyleLength? _width;
        private StyleLength? _height;
        private StyleLength? _minWidth;
        private StyleLength? _maxWidth;
        private StyleLength? _minHeight;
        private StyleLength? _maxHeight;

        private StyleLength? _marginTop;
        private StyleLength? _marginBottom;
        private StyleLength? _marginLeft;
        private StyleLength? _marginRight;

        private StyleLength? _paddingTop;
        private StyleLength? _paddingBottom;
        private StyleLength? _paddingLeft;
        private StyleLength? _paddingRight;

        private StyleFloat? _borderTopWidth;
        private StyleFloat? _borderBottomWidth;
        private StyleFloat? _borderLeftWidth;
        private StyleFloat? _borderRightWidth;

        private StyleColor? _borderTopColor;
        private StyleColor? _borderBottomColor;
        private StyleColor? _borderLeftColor;
        private StyleColor? _borderRightColor;

        private StyleLength? _borderTopLeftRadius;
        private StyleLength? _borderTopRightRadius;
        private StyleLength? _borderBottomLeftRadius;
        private StyleLength? _borderBottomRightRadius;

        private StyleColor? _backgroundColor;
        private StyleColor? _color;

        private StyleFont? _unityFont;
        private StyleLength? _fontSize;
        private StyleEnum<FontStyle>? _fontStyle;
        private StyleEnum<TextAnchor>? _textAlign;
        private StyleEnum<TextOverflow>? _textOverflow;
        private StyleLength? _letterSpacing;
        private StyleLength? _lineHeight;
        private StyleTextShadow? _textShadow;

        private StyleEnum<DisplayStyle>? _display;
        private StyleEnum<Position>? _position;
        private StyleLength? _top;
        private StyleLength? _bottom;
        private StyleLength? _left;
        private StyleLength? _right;

        private StyleFloat? _flexGrow;
        private StyleFloat? _flexShrink;
        private StyleLength? _flexBasis;
        private StyleEnum<FlexDirection>? _flexDirection;
        private StyleEnum<Wrap>? _flexWrap;
        private StyleEnum<Align>? _alignItems;
        private StyleEnum<Align>? _alignContent;
        private StyleEnum<Align>? _alignSelf;
        private StyleEnum<Justify>? _justifyContent;

        private StyleFloat? _opacity;
        private StyleEnum<Overflow>? _overflow;

        // ===== Setters & TryGets =====

        public void SetWidth(StyleLength? value) => _width = value;
        public bool TryGetWidth(out StyleLength value) => TryGetValue(_width, out value);

        public void SetHeight(StyleLength? value) => _height = value;
        public bool TryGetHeight(out StyleLength value) => TryGetValue(_height, out value);

        public void SetMinWidth(StyleLength? value) => _minWidth = value;
        public bool TryGetMinWidth(out StyleLength value) => TryGetValue(_minWidth, out value);

        public void SetMaxWidth(StyleLength? value) => _maxWidth = value;
        public bool TryGetMaxWidth(out StyleLength value) => TryGetValue(_maxWidth, out value);

        public void SetMinHeight(StyleLength? value) => _minHeight = value;
        public bool TryGetMinHeight(out StyleLength value) => TryGetValue(_minHeight, out value);

        public void SetMaxHeight(StyleLength? value) => _maxHeight = value;
        public bool TryGetMaxHeight(out StyleLength value) => TryGetValue(_maxHeight, out value);

        public void SetMarginTop(StyleLength? value) => _marginTop = value;
        public bool TryGetMarginTop(out StyleLength value) => TryGetValue(_marginTop, out value);

        public void SetMarginBottom(StyleLength? value) => _marginBottom = value;
        public bool TryGetMarginBottom(out StyleLength value) => TryGetValue(_marginBottom, out value);

        public void SetMarginLeft(StyleLength? value) => _marginLeft = value;
        public bool TryGetMarginLeft(out StyleLength value) => TryGetValue(_marginLeft, out value);

        public void SetMarginRight(StyleLength? value) => _marginRight = value;
        public bool TryGetMarginRight(out StyleLength value) => TryGetValue(_marginRight, out value);

        public void SetPaddingTop(StyleLength? value) => _paddingTop = value;
        public bool TryGetPaddingTop(out StyleLength value) => TryGetValue(_paddingTop, out value);

        public void SetPaddingBottom(StyleLength? value) => _paddingBottom = value;
        public bool TryGetPaddingBottom(out StyleLength value) => TryGetValue(_paddingBottom, out value);

        public void SetPaddingLeft(StyleLength? value) => _paddingLeft = value;
        public bool TryGetPaddingLeft(out StyleLength value) => TryGetValue(_paddingLeft, out value);

        public void SetPaddingRight(StyleLength? value) => _paddingRight = value;
        public bool TryGetPaddingRight(out StyleLength value) => TryGetValue(_paddingRight, out value);

        public void SetBackgroundColor(StyleColor? value) => _backgroundColor = value;
        public bool TryGetBackgroundColor(out StyleColor value) => TryGetValue(_backgroundColor, out value);

        public void SetColor(StyleColor? value) => _color = value;
        public bool TryGetColor(out StyleColor value) => TryGetValue(_color, out value);

        public void SetFont(StyleFont? value) => _unityFont = value;
        public bool TryGetFont(out StyleFont value) => TryGetValue(_unityFont, out value);

        public void SetFontSize(StyleLength? value) => _fontSize = value;
        public bool TryGetFontSize(out StyleLength value) => TryGetValue(_fontSize, out value);

        public void SetFontStyle(FontStyle? value) => _fontStyle = value.HasValue ? new StyleEnum<FontStyle>(value.Value) : null;
        public bool TryGetFontStyle(out StyleEnum<FontStyle> value) => TryGetValue(_fontStyle, out value);

        public void SetTextAlign(TextAnchor? value) => _textAlign = value.HasValue ? new StyleEnum<TextAnchor>(value.Value) : null;
        public bool TryGetTextAlign(out StyleEnum<TextAnchor> value) => TryGetValue(_textAlign, out value);

        public void SetTextOverflow(TextOverflow? value) => _textOverflow = value.HasValue ? new StyleEnum<TextOverflow>(value.Value) : null;
        public bool TryGetTextOverflow(out StyleEnum<TextOverflow> value) => TryGetValue(_textOverflow, out value);

        public void SetLetterSpacing(StyleLength? value) => _letterSpacing = value;
        public bool TryGetLetterSpacing(out StyleLength value) => TryGetValue(_letterSpacing, out value);

        public void SetLineHeight(StyleLength? value) => _lineHeight = value;
        public bool TryGetLineHeight(out StyleLength value) => TryGetValue(_lineHeight, out value);

        public void SetTextShadow(StyleTextShadow? value) => _textShadow = value;
        public bool TryGetTextShadow(out StyleTextShadow value) => TryGetValue(_textShadow, out value);

        public void SetDisplay(DisplayStyle? value) => _display = value.HasValue ? new StyleEnum<DisplayStyle>(value.Value) : null;
        public bool TryGetDisplay(out StyleEnum<DisplayStyle> value) => TryGetValue(_display, out value);

        public void SetPosition(Position? value) => _position = value.HasValue ? new StyleEnum<Position>(value.Value) : null;
        public bool TryGetPosition(out StyleEnum<Position> value) => TryGetValue(_position, out value);

        public void SetTop(StyleLength? value) => _top = value;
        public bool TryGetTop(out StyleLength value) => TryGetValue(_top, out value);

        public void SetBottom(StyleLength? value) => _bottom = value;
        public bool TryGetBottom(out StyleLength value) => TryGetValue(_bottom, out value);

        public void SetLeft(StyleLength? value) => _left = value;
        public bool TryGetLeft(out StyleLength value) => TryGetValue(_left, out value);

        public void SetRight(StyleLength? value) => _right = value;
        public bool TryGetRight(out StyleLength value) => TryGetValue(_right, out value);

        public void SetFlexGrow(StyleFloat? value) => _flexGrow = value;
        public bool TryGetFlexGrow(out StyleFloat value) => TryGetValue(_flexGrow, out value);

        public void SetFlexShrink(StyleFloat? value) => _flexShrink = value;
        public bool TryGetFlexShrink(out StyleFloat value) => TryGetValue(_flexShrink, out value);

        public void SetFlexBasis(StyleLength? value) => _flexBasis = value;
        public bool TryGetFlexBasis(out StyleLength value) => TryGetValue(_flexBasis, out value);

        public void SetFlexDirection(FlexDirection? value) => _flexDirection = value.HasValue ? new StyleEnum<FlexDirection>(value.Value) : null;
        public bool TryGetFlexDirection(out StyleEnum<FlexDirection> value) => TryGetValue(_flexDirection, out value);

        public void SetFlexWrap(Wrap? value) => _flexWrap = value.HasValue ? new StyleEnum<Wrap>(value.Value) : null;
        public bool TryGetFlexWrap(out StyleEnum<Wrap> value) => TryGetValue(_flexWrap, out value);

        public void SetAlignItems(Align? value) => _alignItems = value.HasValue ? new StyleEnum<Align>(value.Value) : null;
        public bool TryGetAlignItems(out StyleEnum<Align> value) => TryGetValue(_alignItems, out value);

        public void SetAlignContent(Align? value) => _alignContent = value.HasValue ? new StyleEnum<Align>(value.Value) : null;
        public bool TryGetAlignContent(out StyleEnum<Align> value) => TryGetValue(_alignContent, out value);

        public void SetAlignSelf(Align? value) => _alignSelf = value.HasValue ? new StyleEnum<Align>(value.Value) : null;
        public bool TryGetAlignSelf(out StyleEnum<Align> value) => TryGetValue(_alignSelf, out value);

        public void SetJustifyContent(Justify? value) => _justifyContent = value.HasValue ? new StyleEnum<Justify>(value.Value) : null;
        public bool TryGetJustifyContent(out StyleEnum<Justify> value) => TryGetValue(_justifyContent, out value);

        public void SetOpacity(StyleFloat? value) => _opacity = value;
        public bool TryGetOpacity(out StyleFloat value) => TryGetValue(_opacity, out value);

        public void SetOverflow(Overflow? value) => _overflow = value.HasValue ? new StyleEnum<Overflow>(value.Value) : null;
        public bool TryGetOverflow(out StyleEnum<Overflow> value) => TryGetValue(_overflow, out value);

        public void ApplyTo(VisualElement element)
        {
            if (element == null)
            {
                return;
            }
            
            var s = element.style;

            if (TryGetWidth(out var width)) s.width = width;
            if (TryGetHeight(out var height)) s.height = height;
            if (TryGetMinWidth(out var minWidth)) s.minWidth = minWidth;
            if (TryGetMaxWidth(out var maxWidth)) s.maxWidth = maxWidth;
            if (TryGetMinHeight(out var minHeight)) s.minHeight = minHeight;
            if (TryGetMaxHeight(out var maxHeight)) s.maxHeight = maxHeight;

            if (TryGetMarginTop(out var mt)) s.marginTop = mt;
            if (TryGetMarginBottom(out var mb)) s.marginBottom = mb;
            if (TryGetMarginLeft(out var ml)) s.marginLeft = ml;
            if (TryGetMarginRight(out var mr)) s.marginRight = mr;

            if (TryGetPaddingTop(out var pt)) s.paddingTop = pt;
            if (TryGetPaddingBottom(out var pb)) s.paddingBottom = pb;
            if (TryGetPaddingLeft(out var pl)) s.paddingLeft = pl;
            if (TryGetPaddingRight(out var pr)) s.paddingRight = pr;

            if (TryGetBackgroundColor(out var bg)) s.backgroundColor = bg;
            if (TryGetColor(out var color)) s.color = color;

            if (TryGetFont(out var font)) s.unityFont = font;
            if (TryGetFontSize(out var fontSize)) s.fontSize = fontSize;
            if (TryGetFontStyle(out var fontStyle)) s.unityFontStyleAndWeight = fontStyle;
            if (TryGetTextAlign(out var ta)) s.unityTextAlign = ta;
            if (TryGetTextOverflow(out var to)) s.textOverflow = to;
            if (TryGetLetterSpacing(out var ls)) s.letterSpacing = ls;
            // if (TryGetLineHeight(out var lh)) s.lineHeight = lh;
            if (TryGetTextShadow(out var ts)) s.textShadow = ts;

            if (TryGetDisplay(out var display)) s.display = display;
            if (TryGetPosition(out var pos)) s.position = pos;

            if (TryGetTop(out var top)) s.top = top;
            if (TryGetBottom(out var bottom)) s.bottom = bottom;
            if (TryGetLeft(out var left)) s.left = left;
            if (TryGetRight(out var right)) s.right = right;

            if (TryGetFlexGrow(out var grow)) s.flexGrow = grow;
            if (TryGetFlexShrink(out var shrink)) s.flexShrink = shrink;
            if (TryGetFlexBasis(out var basis)) s.flexBasis = basis;
            if (TryGetFlexDirection(out var fd)) s.flexDirection = fd;
            if (TryGetFlexWrap(out var wrap)) s.flexWrap = wrap;
            if (TryGetAlignItems(out var ai)) s.alignItems = ai;
            if (TryGetAlignContent(out var ac)) s.alignContent = ac;
            if (TryGetAlignSelf(out var aself)) s.alignSelf = aself;
            if (TryGetJustifyContent(out var jc)) s.justifyContent = jc;

            if (TryGetOpacity(out var opacity)) s.opacity = opacity;
            if (TryGetOverflow(out var overflow)) s.overflow = overflow;
        }
    }
}