using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Tweening
{
    /// <summary>
    /// High-level tween helpers for UI Toolkit <see cref="VisualElement"/>s. Every helper uses cached
    /// <c>static</c> reader/writer delegates so no closure is allocated per call.
    ///
    /// Two families are provided:
    /// <list type="bullet">
    /// <item><b>Style properties</b> (opacity, colors, width/height, left/top) — animate inline style,
    /// reading the current computed value from <c>resolvedStyle</c> as the start.</item>
    /// <item><b>Transform styles</b> (translate, scale, rotation) — animate the <c>translate</c>, <c>scale</c>
    /// and <c>rotate</c> styles, which are applied after layout and do not reflow siblings.</item>
    /// </list>
    /// For any style property not covered here, use the generic <see cref="TweenTo(VisualElement,TweenGetter{float},TweenSetter{float},float,float)"/>
    /// overloads or <see cref="Tween.To{T}(object,TweenReader{T},TweenWriter{T},T,float,TweenOps{T})"/> directly.
    /// </summary>
    public static class VisualElementTweenExtensions
    {
        // ---- Cached delegates (allocated once, shared by every tween) ----
        private static readonly TweenReader<float> s_ReadOpacity = o => ((VisualElement)o).resolvedStyle.opacity;
        private static readonly TweenWriter<float> s_WriteOpacity = (o, v) => ((VisualElement)o).style.opacity = v;

        private static readonly TweenReader<Color> s_ReadBackgroundColor = o => ((VisualElement)o).resolvedStyle.backgroundColor;
        private static readonly TweenWriter<Color> s_WriteBackgroundColor = (o, v) => ((VisualElement)o).style.backgroundColor = v;

        private static readonly TweenReader<Color> s_ReadColor = o => ((VisualElement)o).resolvedStyle.color;
        private static readonly TweenWriter<Color> s_WriteColor = (o, v) => ((VisualElement)o).style.color = v;

        private static readonly TweenReader<Color> s_ReadTint = o => ((VisualElement)o).resolvedStyle.unityBackgroundImageTintColor;
        private static readonly TweenWriter<Color> s_WriteTint = (o, v) => ((VisualElement)o).style.unityBackgroundImageTintColor = v;

        private static readonly TweenReader<Color> s_ReadBorderColor = o => ((VisualElement)o).resolvedStyle.borderTopColor;
        private static readonly TweenWriter<Color> s_WriteBorderColor = (o, v) =>
        {
            var e = (VisualElement)o;
            e.style.borderTopColor = v;
            e.style.borderRightColor = v;
            e.style.borderBottomColor = v;
            e.style.borderLeftColor = v;
        };

        private static readonly TweenReader<float> s_ReadWidth = o => ((VisualElement)o).resolvedStyle.width;
        private static readonly TweenWriter<float> s_WriteWidth = (o, v) => ((VisualElement)o).style.width = v;

        private static readonly TweenReader<float> s_ReadHeight = o => ((VisualElement)o).resolvedStyle.height;
        private static readonly TweenWriter<float> s_WriteHeight = (o, v) => ((VisualElement)o).style.height = v;

        private static readonly TweenReader<Vector2> s_ReadSize = o =>
        {
            var e = (VisualElement)o;
            return new Vector2(e.resolvedStyle.width, e.resolvedStyle.height);
        };
        private static readonly TweenWriter<Vector2> s_WriteSize = (o, v) =>
        {
            var e = (VisualElement)o;
            e.style.width = v.x;
            e.style.height = v.y;
        };

        private static readonly TweenReader<float> s_ReadLeft = o => ((VisualElement)o).resolvedStyle.left;
        private static readonly TweenWriter<float> s_WriteLeft = (o, v) => ((VisualElement)o).style.left = v;

        private static readonly TweenReader<float> s_ReadTop = o => ((VisualElement)o).resolvedStyle.top;
        private static readonly TweenWriter<float> s_WriteTop = (o, v) => ((VisualElement)o).style.top = v;

        private static readonly TweenReader<Vector2> s_ReadTranslate = o =>
        {
            var e = (VisualElement)o;
            return new Vector2(e.resolvedStyle.translate.x, e.resolvedStyle.translate.y);
        };
        private static readonly TweenWriter<Vector2> s_WriteTranslate = (o, v) => ((VisualElement)o).style.translate = new Translate(v.x, v.y);

        private static readonly TweenReader<Vector3> s_ReadScale = o => ((VisualElement)o).resolvedStyle.scale.value;
        private static readonly TweenWriter<Vector3> s_WriteScale = (o, v) => ((VisualElement)o).style.scale = new Scale(v);

        private static readonly TweenReader<float> s_ReadRotate = o => ((VisualElement)o).resolvedStyle.rotate.angle.value;
        private static readonly TweenWriter<float> s_WriteRotate = (o, v) => ((VisualElement)o).style.rotate = new Rotate(v);

        // ================================================================
        //  Style value properties
        // ================================================================

        /// <summary>Fades the element's <c>opacity</c> (0..1).</summary>
        public static Tween TweenOpacity(this VisualElement element, float endValue, float duration)
            => Tween.To(element, s_ReadOpacity, s_WriteOpacity, endValue, duration);

        /// <summary>Tweens the <c>background-color</c>.</summary>
        public static Tween TweenBackgroundColor(this VisualElement element, Color endValue, float duration)
            => Tween.To(element, s_ReadBackgroundColor, s_WriteBackgroundColor, endValue, duration);

        /// <summary>Tweens the text <c>color</c>.</summary>
        public static Tween TweenColor(this VisualElement element, Color endValue, float duration)
            => Tween.To(element, s_ReadColor, s_WriteColor, endValue, duration);

        /// <summary>Tweens the background image tint color (<c>-unity-background-image-tint-color</c>).</summary>
        public static Tween TweenTintColor(this VisualElement element, Color endValue, float duration)
            => Tween.To(element, s_ReadTint, s_WriteTint, endValue, duration);

        /// <summary>Tweens all four border colors together.</summary>
        public static Tween TweenBorderColor(this VisualElement element, Color endValue, float duration)
            => Tween.To(element, s_ReadBorderColor, s_WriteBorderColor, endValue, duration);

        // ---- Layout size / position (in pixels) ----

        /// <summary>Tweens <c>width</c> in pixels.</summary>
        public static Tween TweenWidth(this VisualElement element, float endValue, float duration)
            => Tween.To(element, s_ReadWidth, s_WriteWidth, endValue, duration);

        /// <summary>Tweens <c>height</c> in pixels.</summary>
        public static Tween TweenHeight(this VisualElement element, float endValue, float duration)
            => Tween.To(element, s_ReadHeight, s_WriteHeight, endValue, duration);

        /// <summary>Tweens <c>width</c> and <c>height</c> together (pixels).</summary>
        public static Tween TweenSize(this VisualElement element, Vector2 endValue, float duration)
            => Tween.To(element, s_ReadSize, s_WriteSize, endValue, duration);

        /// <summary>Tweens the <c>left</c> style offset (pixels).</summary>
        public static Tween TweenLeft(this VisualElement element, float endValue, float duration)
            => Tween.To(element, s_ReadLeft, s_WriteLeft, endValue, duration);

        /// <summary>Tweens the <c>top</c> style offset (pixels).</summary>
        public static Tween TweenTop(this VisualElement element, float endValue, float duration)
            => Tween.To(element, s_ReadTop, s_WriteTop, endValue, duration);

        // ================================================================
        //  Transform styles (translate / scale / rotate — do not reflow siblings)
        // ================================================================

        /// <summary>Tweens the <c>translate</c> style (a pixel offset that does not affect layout).</summary>
        public static Tween TweenTranslate(this VisualElement element, Vector2 endValue, float duration)
            => Tween.To(element, s_ReadTranslate, s_WriteTranslate, endValue, duration);

        /// <summary>Tweens the <c>scale</c> style.</summary>
        public static Tween TweenScale(this VisualElement element, Vector3 endValue, float duration)
            => Tween.To(element, s_ReadScale, s_WriteScale, endValue, duration);

        /// <summary>Tweens the <c>scale</c> style uniformly on all axes.</summary>
        public static Tween TweenScale(this VisualElement element, float uniformEndValue, float duration)
            => Tween.To(element, s_ReadScale, s_WriteScale, Vector3.one * uniformEndValue, duration);

        /// <summary>Tweens the <c>rotate</c> style around the Z axis, in degrees.</summary>
        public static Tween TweenRotation(this VisualElement element, float endDegrees, float duration)
            => Tween.To(element, s_ReadRotate, s_WriteRotate, endDegrees, duration);

        // ================================================================
        //  Generic passthroughs for any other style property / value
        // ================================================================

        public static Tween TweenTo(this VisualElement element, TweenGetter<float> getter, TweenSetter<float> setter, float endValue, float duration)
            => Tween.To(getter, setter, endValue, duration).SetTarget(element);

        public static Tween TweenTo(this VisualElement element, TweenGetter<Color> getter, TweenSetter<Color> setter, Color endValue, float duration)
            => Tween.To(getter, setter, endValue, duration).SetTarget(element);

        public static Tween TweenTo(this VisualElement element, TweenGetter<Vector2> getter, TweenSetter<Vector2> setter, Vector2 endValue, float duration)
            => Tween.To(getter, setter, endValue, duration).SetTarget(element);

        public static Tween TweenTo(this VisualElement element, TweenGetter<Vector3> getter, TweenSetter<Vector3> setter, Vector3 endValue, float duration)
            => Tween.To(getter, setter, endValue, duration).SetTarget(element);

        // ================================================================
        //  Control
        // ================================================================

        /// <summary>Kills every tween whose target is this element.</summary>
        public static int KillTweens(this VisualElement element, bool complete = false)
            => Tween.Kill(element, complete);
    }
}
