using System;
using UnityEngine.UIElements;

namespace Vapor.Tweening
{
    /// <summary>
    /// Helpers for starting UI Toolkit tweens and sequences only once an element's layout is resolved.
    ///
    /// Tweens capture their start value from <c>resolvedStyle</c> the first time they run. If you kick one off
    /// the same frame an element is created, attached, or flipped from <c>display:none</c>, its
    /// <c>resolvedStyle</c> may still be stale (a layout pass hasn't happened yet), so the animation starts from
    /// the wrong value. These helpers defer the start until the first real layout.
    /// </summary>
    public static class TweenLayoutExtensions
    {
        /// <summary>
        /// Invokes <paramref name="onReady"/> immediately if <paramref name="element"/> is already attached and
        /// laid out with real geometry; otherwise once after its first <see cref="GeometryChangedEvent"/>.
        /// </summary>
        public static void WhenReady(this VisualElement element, Action onReady)
        {
            if (onReady == null)
            {
                return;
            }
            if (element == null)
            {
                onReady();
                return;
            }

            // Already attached and laid out with real geometry → resolvedStyle is valid now.
            if (element.panel != null && element.layout.width > 0f && element.layout.height > 0f)
            {
                onReady();
                return;
            }

            // Otherwise wait for the first layout pass that produces geometry.
            void OnGeometryChanged(GeometryChangedEvent _)
            {
                element.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                onReady();
            }

            element.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        /// <summary>
        /// Pauses the tween (or sequence) and starts it only once <paramref name="element"/> is laid out, so its
        /// children capture their <c>resolvedStyle</c> start values from a valid layout. Chain it last:
        /// <code>
        /// Sequence.Create()
        ///     .Append(panel.TweenOpacity(1f, 0.3f))
        ///     .Join(panel.TweenScale(1f, 0.3f))
        ///     .PlayAfterLayout(panel);
        /// </code>
        /// </summary>
        public static T PlayAfterLayout<T>(this T tween, VisualElement element) where T : Tween
        {
            if (tween == null)
            {
                return null;
            }

            tween.Pause();
            element.WhenReady(() => tween.Play());
            return tween;
        }
    }
}
