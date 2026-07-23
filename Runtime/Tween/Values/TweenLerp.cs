using System;
using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// Bundles the math operations a <see cref="Tween{T}"/> needs in order to interpolate a value of
    /// type <typeparamref name="T"/>:
    /// <list type="bullet">
    /// <item><see cref="Lerp"/> — required; interpolates between two values.</item>
    /// <item><see cref="Add"/> — optional; needed for relative (<see cref="Tween.SetRelative"/>) and incremental loops.</item>
    /// <item><see cref="Scale"/> — optional; multiplies a value by a scalar, needed for incremental loops.</item>
    /// </list>
    /// Provide your own instance to <see cref="Tween.To{T}"/> to tween any custom type.
    /// </summary>
    public sealed class TweenOps<T>
    {
        /// <summary>Interpolates from <c>a</c> to <c>b</c> by an unclamped factor <c>t</c>.</summary>
        public readonly Func<T, T, float, T> Lerp;

        /// <summary>Adds two values (<c>a + b</c>). Null for types without meaningful addition (e.g. Quaternion).</summary>
        public readonly Func<T, T, T> Add;

        /// <summary>Scales a value by a scalar (<c>a * s</c>). Null for types without meaningful scaling.</summary>
        public readonly Func<T, float, T> Scale;

        /// <summary>True when both <see cref="Add"/> and <see cref="Scale"/> are available (enables relative and incremental tweens).</summary>
        public bool SupportsArithmetic => Add != null && Scale != null;

        public TweenOps(Func<T, T, float, T> lerp, Func<T, T, T> add = null, Func<T, float, T> scale = null)
        {
            Lerp = lerp ?? throw new ArgumentNullException(nameof(lerp));
            Add = add;
            Scale = scale;
        }
    }

    /// <summary>
    /// Pre-built <see cref="TweenOps{T}"/> instances for the common Unity value types. All use the
    /// <c>Unclamped</c> interpolators so that overshooting eases (Back, Elastic, Bounce) render correctly.
    /// </summary>
    public static class TweenLerp
    {
        public static readonly TweenOps<float> Float = new(
            (a, b, t) => a + (b - a) * t,
            (a, b) => a + b,
            (a, s) => a * s);

        public static readonly TweenOps<double> Double = new(
            (a, b, t) => a + (b - a) * t,
            (a, b) => a + b,
            (a, s) => a * s);

        public static readonly TweenOps<Vector2> Vector2 = new(
            UnityEngine.Vector2.LerpUnclamped,
            (a, b) => a + b,
            (a, s) => a * s);

        public static readonly TweenOps<Vector3> Vector3 = new(
            UnityEngine.Vector3.LerpUnclamped,
            (a, b) => a + b,
            (a, s) => a * s);

        public static readonly TweenOps<Vector4> Vector4 = new(
            UnityEngine.Vector4.LerpUnclamped,
            (a, b) => a + b,
            (a, s) => a * s);

        public static readonly TweenOps<Color> Color = new(
            UnityEngine.Color.LerpUnclamped,
            (a, b) => a + b,
            (a, s) => a * s);

        public static readonly TweenOps<Quaternion> Quaternion = new(
            UnityEngine.Quaternion.SlerpUnclamped);
    }
}
