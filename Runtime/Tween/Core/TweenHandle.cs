using System;

namespace Vapor.Tweening
{
    /// <summary>
    /// A safe, value-type reference to a <see cref="Tween"/>. Because completed tweens are recycled through a
    /// pool, holding the <see cref="Tween"/> object directly is dangerous — once it dies, that instance may be
    /// reused for an unrelated animation. A handle snapshots the tween's <see cref="Tween.Version"/> at creation
    /// and re-checks it on every access, so once the tween completes and is recycled the handle simply resolves
    /// to "dead": queries return <c>false</c> and control calls become no-ops instead of touching the wrong tween.
    ///
    /// Obtain one by assignment (there's an implicit conversion) or via <see cref="Tween.AsHandle"/>:
    /// <code>
    /// TweenHandle fade = panel.TweenOpacity(0f, 0.3f); // snapshot it if you intend to keep it around
    /// // ...later, any time...
    /// if (fade.IsActive) fade.Kill();                  // safe even if it already finished and recycled
    /// </code>
    /// This is a generation-checked handle, not a GC <see cref="WeakReference"/>: it is allocation-free and does
    /// not keep anything alive in a way that matters (the referenced tween is pooled regardless).
    /// </summary>
    public readonly struct TweenHandle
    {
        private readonly Tween _tween;
        private readonly int _version;

        public TweenHandle(Tween tween)
        {
            _tween = tween;
            _version = tween?.Version ?? 0;
        }

        /// <summary>Returns the tween if this handle still refers to it, or null if it has been recycled.</summary>
        private Tween Resolve() => _tween != null && _tween.Version == _version ? _tween : null;

        // ---- Queries (all false once the tween has been recycled) ----

        /// <summary>True while the handle still points at its original tween and that tween has not been recycled.</summary>
        public bool IsValid => Resolve() != null;

        /// <summary>True while the tween is still alive (valid and not killed/recycled). Goes false when it completes with auto-kill.</summary>
        public bool IsActive
        {
            get { Tween t = Resolve(); return t != null && t.State != TweenState.Killed; }
        }

        /// <summary>True while the tween is actively advancing.</summary>
        public bool IsPlaying
        {
            get { Tween t = Resolve(); return t is { IsPlaying: true }; }
        }

        /// <summary>True if the tween reached its end (only observable when auto-kill is disabled; otherwise it recycles).</summary>
        public bool IsComplete
        {
            get { Tween t = Resolve(); return t is { IsComplete: true }; }
        }

        /// <summary>Gets the live tween if the handle is still valid. Prefer the handle's own methods; use this only for advanced access.</summary>
        public bool TryGet(out Tween tween)
        {
            tween = Resolve();
            return tween != null;
        }

        // ---- Control (no-op if the tween has been recycled) ----

        public TweenHandle Kill(bool complete = false) { Resolve()?.Kill(complete); return this; }
        public TweenHandle Pause() { Resolve()?.Pause(); return this; }
        public TweenHandle Play() { Resolve()?.Play(); return this; }
        public TweenHandle Complete(bool withCallbacks = true) { Resolve()?.Complete(withCallbacks); return this; }
        public TweenHandle Restart(bool includeDelay = true) { Resolve()?.Restart(includeDelay); return this; }
        public TweenHandle Goto(float time, bool andPlay = false) { Resolve()?.Goto(time, andPlay); return this; }

        // ---- Attach callbacks later, safely ----

        public TweenHandle OnComplete(Action action) { Resolve()?.OnComplete(action); return this; }
        public TweenHandle OnKill(Action action) { Resolve()?.OnKill(action); return this; }
        public TweenHandle OnUpdate(Action action) { Resolve()?.OnUpdate(action); return this; }

        public static implicit operator TweenHandle(Tween tween) => new(tween);
    }
}
