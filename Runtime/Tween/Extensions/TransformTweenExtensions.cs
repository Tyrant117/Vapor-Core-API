using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// High-level tween helpers for <see cref="Transform"/>. Each helper uses cached <c>static</c> reader/writer
    /// delegates (no per-call closure) and tags the tween with the transform as its target, so
    /// <see cref="KillTweens"/> / <see cref="Tween.Kill(object,bool)"/> can cancel them.
    /// </summary>
    public static class TransformTweenExtensions
    {
        // ---- Cached delegates (allocated once, shared by every tween) ----
        private static readonly TweenReader<Vector3> s_ReadPosition = o => ((Transform)o).position;
        private static readonly TweenWriter<Vector3> s_WritePosition = (o, v) => ((Transform)o).position = v;

        private static readonly TweenReader<float> s_ReadPosX = o => ((Transform)o).position.x;
        private static readonly TweenWriter<float> s_WritePosX = (o, v) => { var t = (Transform)o; var p = t.position; p.x = v; t.position = p; };
        private static readonly TweenReader<float> s_ReadPosY = o => ((Transform)o).position.y;
        private static readonly TweenWriter<float> s_WritePosY = (o, v) => { var t = (Transform)o; var p = t.position; p.y = v; t.position = p; };
        private static readonly TweenReader<float> s_ReadPosZ = o => ((Transform)o).position.z;
        private static readonly TweenWriter<float> s_WritePosZ = (o, v) => { var t = (Transform)o; var p = t.position; p.z = v; t.position = p; };

        private static readonly TweenReader<Vector3> s_ReadLocalPosition = o => ((Transform)o).localPosition;
        private static readonly TweenWriter<Vector3> s_WriteLocalPosition = (o, v) => ((Transform)o).localPosition = v;

        private static readonly TweenReader<Vector3> s_ReadLocalScale = o => ((Transform)o).localScale;
        private static readonly TweenWriter<Vector3> s_WriteLocalScale = (o, v) => ((Transform)o).localScale = v;

        private static readonly TweenReader<Quaternion> s_ReadRotation = o => ((Transform)o).rotation;
        private static readonly TweenWriter<Quaternion> s_WriteRotation = (o, v) => ((Transform)o).rotation = v;
        private static readonly TweenReader<Quaternion> s_ReadLocalRotation = o => ((Transform)o).localRotation;
        private static readonly TweenWriter<Quaternion> s_WriteLocalRotation = (o, v) => ((Transform)o).localRotation = v;

        private static readonly TweenReader<Vector3> s_ReadEuler = o => ((Transform)o).eulerAngles;
        private static readonly TweenWriter<Vector3> s_WriteEuler = (o, v) => ((Transform)o).eulerAngles = v;

        // ---- World position ----
        public static Tween TweenPosition(this Transform transform, Vector3 endValue, float duration)
            => Tween.To(transform, s_ReadPosition, s_WritePosition, endValue, duration);

        public static Tween TweenMoveX(this Transform transform, float endValue, float duration)
            => Tween.To(transform, s_ReadPosX, s_WritePosX, endValue, duration);

        public static Tween TweenMoveY(this Transform transform, float endValue, float duration)
            => Tween.To(transform, s_ReadPosY, s_WritePosY, endValue, duration);

        public static Tween TweenMoveZ(this Transform transform, float endValue, float duration)
            => Tween.To(transform, s_ReadPosZ, s_WritePosZ, endValue, duration);

        // ---- Local position ----
        public static Tween TweenLocalPosition(this Transform transform, Vector3 endValue, float duration)
            => Tween.To(transform, s_ReadLocalPosition, s_WriteLocalPosition, endValue, duration);

        // ---- Scale ----
        public static Tween TweenLocalScale(this Transform transform, Vector3 endValue, float duration)
            => Tween.To(transform, s_ReadLocalScale, s_WriteLocalScale, endValue, duration);

        public static Tween TweenScale(this Transform transform, float uniformEndValue, float duration)
            => Tween.To(transform, s_ReadLocalScale, s_WriteLocalScale, Vector3.one * uniformEndValue, duration);

        // ---- Rotation ----
        public static Tween TweenRotation(this Transform transform, Quaternion endValue, float duration)
            => Tween.To(transform, s_ReadRotation, s_WriteRotation, endValue, duration);

        public static Tween TweenLocalRotation(this Transform transform, Quaternion endValue, float duration)
            => Tween.To(transform, s_ReadLocalRotation, s_WriteLocalRotation, endValue, duration);

        /// <summary>Rotates towards the given euler angles. Interpolates the euler values directly — best for small/simple spins.</summary>
        public static Tween TweenRotate(this Transform transform, Vector3 endEulerAngles, float duration)
            => Tween.To(transform, s_ReadEuler, s_WriteEuler, endEulerAngles, duration);

        /// <summary>Kills every tween whose target is this transform.</summary>
        public static int KillTweens(this Transform transform, bool complete = false)
            => Tween.Kill(transform, complete);
    }
}
