using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// High-level tween helpers for uGUI <see cref="RectTransform"/>. Note that <see cref="RectTransform"/>
    /// also inherits every <see cref="Transform"/> helper (position, scale, rotation). Uses cached
    /// <c>static</c> reader/writer delegates so no closure is allocated per call.
    /// </summary>
    public static class RectTransformTweenExtensions
    {
        private static readonly TweenReader<Vector2> s_ReadAnchored = o => ((RectTransform)o).anchoredPosition;
        private static readonly TweenWriter<Vector2> s_WriteAnchored = (o, v) => ((RectTransform)o).anchoredPosition = v;

        private static readonly TweenReader<Vector3> s_ReadAnchored3D = o => ((RectTransform)o).anchoredPosition3D;
        private static readonly TweenWriter<Vector3> s_WriteAnchored3D = (o, v) => ((RectTransform)o).anchoredPosition3D = v;

        private static readonly TweenReader<float> s_ReadAnchoredX = o => ((RectTransform)o).anchoredPosition.x;
        private static readonly TweenWriter<float> s_WriteAnchoredX = (o, v) => { var r = (RectTransform)o; var p = r.anchoredPosition; p.x = v; r.anchoredPosition = p; };
        private static readonly TweenReader<float> s_ReadAnchoredY = o => ((RectTransform)o).anchoredPosition.y;
        private static readonly TweenWriter<float> s_WriteAnchoredY = (o, v) => { var r = (RectTransform)o; var p = r.anchoredPosition; p.y = v; r.anchoredPosition = p; };

        private static readonly TweenReader<Vector2> s_ReadSizeDelta = o => ((RectTransform)o).sizeDelta;
        private static readonly TweenWriter<Vector2> s_WriteSizeDelta = (o, v) => ((RectTransform)o).sizeDelta = v;

        private static readonly TweenReader<Vector2> s_ReadPivot = o => ((RectTransform)o).pivot;
        private static readonly TweenWriter<Vector2> s_WritePivot = (o, v) => ((RectTransform)o).pivot = v;

        public static Tween TweenAnchoredPosition(this RectTransform rect, Vector2 endValue, float duration)
            => Tween.To(rect, s_ReadAnchored, s_WriteAnchored, endValue, duration);

        public static Tween TweenAnchoredPosition3D(this RectTransform rect, Vector3 endValue, float duration)
            => Tween.To(rect, s_ReadAnchored3D, s_WriteAnchored3D, endValue, duration);

        public static Tween TweenAnchoredPositionX(this RectTransform rect, float endValue, float duration)
            => Tween.To(rect, s_ReadAnchoredX, s_WriteAnchoredX, endValue, duration);

        public static Tween TweenAnchoredPositionY(this RectTransform rect, float endValue, float duration)
            => Tween.To(rect, s_ReadAnchoredY, s_WriteAnchoredY, endValue, duration);

        public static Tween TweenSizeDelta(this RectTransform rect, Vector2 endValue, float duration)
            => Tween.To(rect, s_ReadSizeDelta, s_WriteSizeDelta, endValue, duration);

        public static Tween TweenPivot(this RectTransform rect, Vector2 endValue, float duration)
            => Tween.To(rect, s_ReadPivot, s_WritePivot, endValue, duration);
    }
}
