using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// High-level tween helpers for uGUI <see cref="CanvasGroup"/> — most commonly fading whole panels in and
    /// out. Uses cached <c>static</c> reader/writer delegates so no closure is allocated per call.
    /// </summary>
    public static class CanvasGroupTweenExtensions
    {
        private static readonly TweenReader<float> s_ReadAlpha = o => ((CanvasGroup)o).alpha;
        private static readonly TweenWriter<float> s_WriteAlpha = (o, v) => ((CanvasGroup)o).alpha = v;

        /// <summary>Fades the group's alpha to <paramref name="endValue"/> (0..1).</summary>
        public static Tween TweenAlpha(this CanvasGroup canvasGroup, float endValue, float duration)
            => Tween.To(canvasGroup, s_ReadAlpha, s_WriteAlpha, endValue, duration);

        /// <summary>Fades in from fully transparent to fully opaque.</summary>
        public static Tween TweenFadeIn(this CanvasGroup canvasGroup, float duration)
        {
            canvasGroup.alpha = 0f;
            return Tween.To(canvasGroup, s_ReadAlpha, s_WriteAlpha, 1f, duration);
        }

        /// <summary>Fades out from fully opaque to fully transparent.</summary>
        public static Tween TweenFadeOut(this CanvasGroup canvasGroup, float duration)
        {
            canvasGroup.alpha = 1f;
            return Tween.To(canvasGroup, s_ReadAlpha, s_WriteAlpha, 0f, duration);
        }

        /// <summary>Kills every tween whose target is this canvas group.</summary>
        public static int KillTweens(this CanvasGroup canvasGroup, bool complete = false)
            => Tween.Kill(canvasGroup, complete);
    }
}
