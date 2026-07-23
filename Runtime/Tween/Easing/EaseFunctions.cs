using System.Runtime.CompilerServices;
using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// Evaluates the built-in <see cref="Ease"/> functions. All functions map a normalized time
    /// <c>t</c> in the [0, 1] range to an eased interpolant. Some functions (Back, Elastic, Bounce)
    /// intentionally return values slightly outside [0, 1] to produce overshoot, which is why the
    /// value interpolators use the <c>Unclamped</c> lerp variants.
    /// </summary>
    public static class EaseFunctions
    {
        private const float PI = Mathf.PI;
        private const float HalfPI = Mathf.PI * 0.5f;

        // Back overshoot constants.
        private const float Back1 = 1.70158f;
        private const float Back2 = Back1 * 1.525f;
        private const float Back3 = Back1 + 1f;

        // Elastic period constants.
        private const float Elastic3 = (2f * PI) / 3f;
        private const float Elastic45 = (2f * PI) / 4.5f;

        /// <summary>Evaluates <paramref name="ease"/> at normalized time <paramref name="t"/>.</summary>
        public static float Evaluate(Ease ease, float t)
        {
            switch (ease)
            {
                case Ease.Linear: return t;

                case Ease.InSine: return 1f - Mathf.Cos(t * HalfPI);
                case Ease.OutSine: return Mathf.Sin(t * HalfPI);
                case Ease.InOutSine: return -(Mathf.Cos(PI * t) - 1f) * 0.5f;

                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Pow(-2f * t + 2f, 2) * 0.5f;

                case Ease.InCubic: return t * t * t;
                case Ease.OutCubic: return 1f - Pow(1f - t, 3);
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Pow(-2f * t + 2f, 3) * 0.5f;

                case Ease.InQuart: return t * t * t * t;
                case Ease.OutQuart: return 1f - Pow(1f - t, 4);
                case Ease.InOutQuart: return t < 0.5f ? 8f * t * t * t * t : 1f - Pow(-2f * t + 2f, 4) * 0.5f;

                case Ease.InQuint: return t * t * t * t * t;
                case Ease.OutQuint: return 1f - Pow(1f - t, 5);
                case Ease.InOutQuint: return t < 0.5f ? 16f * t * t * t * t * t : 1f - Pow(-2f * t + 2f, 5) * 0.5f;

                case Ease.InExpo: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case Ease.OutExpo: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case Ease.InOutExpo:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? Mathf.Pow(2f, 20f * t - 10f) * 0.5f
                        : (2f - Mathf.Pow(2f, -20f * t + 10f)) * 0.5f;

                case Ease.InCirc: return 1f - Mathf.Sqrt(1f - Pow(t, 2));
                case Ease.OutCirc: return Mathf.Sqrt(1f - Pow(t - 1f, 2));
                case Ease.InOutCirc:
                    return t < 0.5f
                        ? (1f - Mathf.Sqrt(1f - Pow(2f * t, 2))) * 0.5f
                        : (Mathf.Sqrt(1f - Pow(-2f * t + 2f, 2)) + 1f) * 0.5f;

                case Ease.InBack: return Back3 * t * t * t - Back1 * t * t;
                case Ease.OutBack: return 1f + Back3 * Pow(t - 1f, 3) + Back1 * Pow(t - 1f, 2);
                case Ease.InOutBack:
                    return t < 0.5f
                        ? Pow(2f * t, 2) * ((Back2 + 1f) * 2f * t - Back2) * 0.5f
                        : (Pow(2f * t - 2f, 2) * ((Back2 + 1f) * (t * 2f - 2f) + Back2) + 2f) * 0.5f;

                case Ease.InElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * Elastic3);
                case Ease.OutElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * Elastic3) + 1f;
                case Ease.InOutElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * Elastic45)) * 0.5f
                        : Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * Elastic45) * 0.5f + 1f;

                case Ease.InBounce: return 1f - OutBounce(1f - t);
                case Ease.OutBounce: return OutBounce(t);
                case Ease.InOutBounce:
                    return t < 0.5f
                        ? (1f - OutBounce(1f - 2f * t)) * 0.5f
                        : (1f + OutBounce(2f * t - 1f)) * 0.5f;

                default: return t;
            }
        }

        /// <summary>Integer power via repeated multiplication (cheaper and more precise than <see cref="Mathf.Pow"/> for small exponents).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Pow(float value, int power)
        {
            float result = 1f;
            for (int i = 0; i < power; i++)
            {
                result *= value;
            }
            return result;
        }

        private static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1)
            {
                return n1 * t * t;
            }
            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }
            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }

            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}
