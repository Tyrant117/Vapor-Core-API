namespace Vapor.Tweening
{
    /// <summary>
    /// The set of built-in easing functions. Pass one to <see cref="Tween.SetEase(Ease)"/>.
    /// For a fully custom curve use <see cref="Tween.SetEase(UnityEngine.AnimationCurve)"/> or
    /// <see cref="Tween.SetEase(System.Func{float,float})"/>.
    /// </summary>
    public enum Ease
    {
        Linear,

        InSine, OutSine, InOutSine,
        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc,
        InBack, OutBack, InOutBack,
        InElastic, OutElastic, InOutElastic,
        InBounce, OutBounce, InOutBounce,
    }
}
