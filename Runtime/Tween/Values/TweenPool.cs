using System.Collections.Generic;

namespace Vapor.Tweening
{
    /// <summary>
    /// Per-type object pool for <see cref="Tween{T}"/> instances. Because it is a generic static class, each
    /// closed type <c>T</c> gets its own independent pool with no dictionary lookup.
    ///
    /// Every <see cref="Tween{T}"/> is created through <see cref="Get"/> and returned via <see cref="Release"/>
    /// when it dies, so steady-state tween creation is allocation-free once the pool has warmed up.
    /// </summary>
    internal static class TweenPool<T>
    {
        private const int MaxRetained = 512;

        private static readonly Stack<Tween<T>> s_Pool = new();

        internal static Tween<T> Get()
            => s_Pool.Count > 0 ? s_Pool.Pop() : new Tween<T>();

        internal static void Release(Tween<T> tween)
        {
            // Bound the pool so a one-off burst of thousands of tweens doesn't retain them forever.
            if (s_Pool.Count < MaxRetained)
            {
                s_Pool.Push(tween);
            }
        }
    }
}
