using System;
using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// Base class for everything the tween runner drives. Owns the shared lifecycle: delay, easing,
    /// looping, callbacks and playback control. Concrete behaviour is provided by <see cref="Tween{T}"/>
    /// (interpolates a single value) and <see cref="Sequence"/> (drives a timeline of child tweens).
    ///
    /// The class is also the static entry point for creating tweens: <see cref="To{T}"/>,
    /// <see cref="FromTo{T}"/> and the typed overloads, plus <see cref="Kill"/>/<see cref="KillAll"/>.
    /// </summary>
    public abstract class Tween
    {
        // ----------------------------------------------------------------
        //  Global defaults (change once at startup to affect new tweens)
        // ----------------------------------------------------------------

        /// <summary>Easing applied to new tweens that don't call <see cref="SetEase(Ease)"/>. Sequences always default to <see cref="Ease.Linear"/>.</summary>
        public static Ease DefaultEase = Ease.OutQuad;

        /// <summary>Whether new tweens are automatically killed when they complete.</summary>
        public static bool DefaultAutoKill = true;

        // ----------------------------------------------------------------
        //  Configuration
        // ----------------------------------------------------------------
        protected float _duration;
        protected float _delay;
        protected Ease _ease;
        protected AnimationCurve _customCurve;
        protected Func<float, float> _customEase;
        protected int _loops = 1;
        protected LoopType _loopType = LoopType.Restart;
        protected bool _ignoreTimeScale;
        protected float _timeScale = 1f;
        protected bool _autoKill = DefaultAutoKill;
        protected bool _isRelative;

        // ----------------------------------------------------------------
        //  Runtime state
        // ----------------------------------------------------------------
        /// <summary>Seconds elapsed within the active timeline (i.e. after the start delay).</summary>
        protected double _position;
        protected float _delayElapsed;
        protected bool _started;
        protected int _completedLoops;

        // ----------------------------------------------------------------
        //  Callbacks (additive — multiple handlers may be attached)
        // ----------------------------------------------------------------
        private Action _onStart;
        private Action _onPlay;
        private Action _onPause;
        private Action _onUpdate;
        private Action _onStepComplete;
        private Action _onComplete;
        private Action _onKill;
        private Action _onRewind;

        // ----------------------------------------------------------------
        //  Identity / bookkeeping
        // ----------------------------------------------------------------
        internal object Target;
        internal object Id;
        internal bool IsSequenced;
        internal bool InRunner; // O(1) membership flag owned by TweenRunner (dedup + lazy removal)

        /// <summary>The current lifecycle state.</summary>
        public TweenState State { get; protected set; } = TweenState.Idle;

        /// <summary>
        /// Changes every time this instance is (re)initialized or recycled to the pool. A <see cref="TweenHandle"/>
        /// snapshots it and compares it back, which is how a handle detects that the tween it referenced has since
        /// died and been reused for a different animation.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>Wraps this tween in a <see cref="TweenHandle"/> that stays safe to use after the tween completes or is recycled.</summary>
        public TweenHandle AsHandle() => new(this);

        // ----------------------------------------------------------------
        //  Read-only inspection
        // ----------------------------------------------------------------
        public bool IsPlaying => State == TweenState.Playing;
        public bool IsAlive => State != TweenState.Killed;
        public bool IsComplete => State == TweenState.Completed;
        public float Duration => _duration;
        public float Delay => _delay;
        public int CompletedLoops => _completedLoops;

        /// <summary>Total duration across every loop, or <see cref="Mathf.Infinity"/> for infinite loops (delay excluded).</summary>
        public float FullDuration => _loops < 0 ? Mathf.Infinity : _duration * Mathf.Max(1, _loops);

        // Internal accessors so Sequence can measure its children (protected members aren't reachable cross-instance).
        internal int LoopsValue => _loops;
        internal float FullTimelineDuration => _delay + (_loops < 0 ? _duration : _duration * Mathf.Max(1, _loops));

        protected Tween()
        {
            _ease = DefaultEase;
        }

        /// <summary>Resets all shared state to defaults so a pooled instance can be safely reused.</summary>
        protected void ResetBaseState()
        {
            // Bumping the version invalidates any TweenHandle that referenced the previous life of this instance.
            Version++;

            _duration = 0f;
            _delay = 0f;
            _ease = DefaultEase;
            _customCurve = null;
            _customEase = null;
            _loops = 1;
            _loopType = LoopType.Restart;
            _ignoreTimeScale = false;
            _timeScale = 1f;
            _autoKill = DefaultAutoKill;
            _isRelative = false;

            _position = 0;
            _delayElapsed = 0f;
            _started = false;
            _completedLoops = 0;

            _onStart = _onPlay = _onPause = _onUpdate = _onStepComplete = _onComplete = _onKill = _onRewind = null;

            Target = null;
            Id = null;
            IsSequenced = false;
            InRunner = false;
            State = TweenState.Idle;
        }

        /// <summary>Returns a pooled tween to its pool. No-op for types that aren't pooled (e.g. <see cref="Sequence"/>).</summary>
        internal virtual void ReturnToPool() { }

        // ================================================================
        //  Fluent configuration
        // ================================================================

        /// <summary>Sets a built-in easing function.</summary>
        public Tween SetEase(Ease ease)
        {
            _ease = ease;
            _customCurve = null;
            _customEase = null;
            return this;
        }

        /// <summary>Sets a custom easing curve (evaluated over the 0..1 range).</summary>
        public Tween SetEase(AnimationCurve curve)
        {
            _customCurve = curve;
            _customEase = null;
            return this;
        }

        /// <summary>Sets a custom easing function mapping normalized time to an eased interpolant.</summary>
        public Tween SetEase(Func<float, float> easeFunction)
        {
            _customEase = easeFunction;
            _customCurve = null;
            return this;
        }

        /// <summary>Delays the start of the tween by <paramref name="delay"/> seconds.</summary>
        public Tween SetDelay(float delay)
        {
            _delay = Mathf.Max(0f, delay);
            return this;
        }

        /// <summary>Sets the number of loops (-1 for infinite) and how each loop behaves.</summary>
        public Tween SetLoops(int loops, LoopType loopType = LoopType.Restart)
        {
            _loops = loops == 0 ? 1 : loops;
            _loopType = loopType;
            return this;
        }

        /// <summary>When true the tween advances with unscaled time and keeps running while <see cref="Time.timeScale"/> is 0.</summary>
        public Tween SetUpdate(bool ignoreTimeScale)
        {
            _ignoreTimeScale = ignoreTimeScale;
            return this;
        }

        /// <summary>Shorthand for <c>SetUpdate(true)</c> — run on unscaled time.</summary>
        public Tween SetUnscaled()
        {
            _ignoreTimeScale = true;
            return this;
        }

        /// <summary>Multiplies this tween's playback speed (1 = normal, 2 = twice as fast, 0 = frozen).</summary>
        public Tween SetTimeScale(float timeScale)
        {
            _timeScale = Mathf.Max(0f, timeScale);
            return this;
        }

        /// <summary>Controls whether the tween is killed automatically when it completes (default true).</summary>
        public Tween SetAutoKill(bool autoKill = true)
        {
            _autoKill = autoKill;
            return this;
        }

        /// <summary>When true the supplied end value is treated as an offset from the captured start value.</summary>
        public Tween SetRelative(bool relative = true)
        {
            _isRelative = relative;
            return this;
        }

        /// <summary>Associates the tween with a target object so it can be found by <see cref="Kill(object,bool)"/>.</summary>
        public Tween SetTarget(object target)
        {
            Target = target;
            return this;
        }

        /// <summary>Associates an arbitrary id with the tween.</summary>
        public Tween SetId(object id)
        {
            Id = id;
            return this;
        }

        // ================================================================
        //  Callbacks
        // ================================================================
        public Tween OnStart(Action action) { _onStart += action; return this; }
        public Tween OnPlay(Action action) { _onPlay += action; return this; }
        public Tween OnPause(Action action) { _onPause += action; return this; }
        public Tween OnUpdate(Action action) { _onUpdate += action; return this; }
        public Tween OnStepComplete(Action action) { _onStepComplete += action; return this; }
        public Tween OnComplete(Action action) { _onComplete += action; return this; }
        public Tween OnKill(Action action) { _onKill += action; return this; }
        public Tween OnRewind(Action action) { _onRewind += action; return this; }

        // ================================================================
        //  Playback control
        // ================================================================

        /// <summary>Resumes a paused (or not-yet-started) tween.</summary>
        public Tween Play()
        {
            if (State is TweenState.Paused or TweenState.Idle)
            {
                State = TweenState.Playing;
                TweenRunner.Register(this);
                _onPlay?.Invoke();
            }
            return this;
        }

        /// <summary>Pauses the tween, keeping its current progress.</summary>
        public Tween Pause()
        {
            if (State == TweenState.Playing)
            {
                State = TweenState.Paused;
                _onPause?.Invoke();
            }
            return this;
        }

        /// <summary>Toggles between playing and paused.</summary>
        public Tween TogglePause() => State == TweenState.Paused ? Play() : Pause();

        /// <summary>Restarts from the beginning and resumes playing.</summary>
        public void Restart(bool includeDelay = true)
        {
            _position = 0;
            _completedLoops = 0;
            _delayElapsed = includeDelay ? 0f : _delay;
            _started = false;
            State = TweenState.Playing;
            TweenRunner.Register(this);
        }

        /// <summary>Snaps back to the start value and pauses.</summary>
        public void Rewind()
        {
            if (!_started)
            {
                BeginPlay(false);
            }
            _position = 0;
            _completedLoops = 0;
            _delayElapsed = 0f;
            Evaluate(0f, false);
            State = TweenState.Paused;
            _onRewind?.Invoke();
        }

        /// <summary>Immediately jumps to the end of the tween. Has no effect on infinite loops.</summary>
        public void Complete(bool withCallbacks = true)
        {
            if (State is TweenState.Killed or TweenState.Completed) return;
            if (_loops < 0) return;

            if (!_started)
            {
                BeginPlay(withCallbacks);
            }
            State = TweenState.Playing;
            _position = (double)_duration * Mathf.Max(1, _loops);
            Evaluate((float)_position, withCallbacks);
        }

        /// <summary>Jumps to an absolute time position (in seconds, delay excluded) and optionally resumes playing.</summary>
        public void Goto(float time, bool andPlay = false)
        {
            if (!_started)
            {
                BeginPlay(false);
            }
            _position = Mathf.Max(0f, time);
            Evaluate((float)_position, false);
            if (State != TweenState.Killed)
            {
                State = andPlay ? TweenState.Playing : TweenState.Paused;
            }
        }

        /// <summary>Stops the tween and removes it from the runner. Pass <paramref name="complete"/> to snap to the end first.</summary>
        public void Kill(bool complete = false)
        {
            if (State == TweenState.Killed) return;
            if (complete)
            {
                Complete();
            }
            DoKill();
        }

        // ================================================================
        //  Awaitable support (integrates with Unity's Awaitable / async)
        // ================================================================

        /// <summary>
        /// Returns an <see cref="Awaitable"/> that completes when this tween finishes or is killed.
        /// Usage: <c>await transform.TweenPosition(target, 1f).AsAwaitable();</c>
        /// </summary>
        public Awaitable AsAwaitable()
        {
            var source = new AwaitableCompletionSource();
            if (State is TweenState.Completed or TweenState.Killed)
            {
                source.SetResult();
                return source.Awaitable;
            }

            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                source.SetResult();
            }

            _onComplete += Finish;
            _onKill += Finish;
            return source.Awaitable;
        }

        // ================================================================
        //  Internal update pipeline (driven by TweenRunner)
        // ================================================================
        internal void InternalUpdate(float scaledDelta, float unscaledDelta)
        {
            if (State != TweenState.Playing) return;

            float dt = (_ignoreTimeScale ? unscaledDelta : scaledDelta) * _timeScale;
            if (dt <= 0f) return;

            AdvanceBy(dt);
        }

        private void AdvanceBy(float dt)
        {
            if (!_started)
            {
                if (_delay > 0f)
                {
                    _delayElapsed += dt;
                    if (_delayElapsed < _delay) return;
                    dt = _delayElapsed - _delay; // carry the remainder into the first playing frame
                }
                BeginPlay(true);
            }

            _position += dt;
            Evaluate((float)_position, true);
        }

        private void BeginPlay(bool fireCallbacks)
        {
            _started = true;
            CaptureStart();
            if (fireCallbacks)
            {
                _onStart?.Invoke();
            }
        }

        /// <summary>Renders the tween at an absolute timeline position and fires the appropriate callbacks.</summary>
        private void Evaluate(float position, bool fireCallbacks)
        {
            float d = _duration;
            int loopIndex;
            float localT;
            bool complete = false;

            if (d <= 0f)
            {
                // Zero-duration tween: resolve instantly to the end of its final loop.
                loopIndex = _loops > 0 ? _loops - 1 : 0;
                localT = 1f;
                complete = _loops >= 0;
            }
            else if (_loops >= 0 && position >= d * _loops)
            {
                complete = true;
                loopIndex = _loops - 1;
                localT = 1f;
            }
            else
            {
                loopIndex = (int)(position / d);
                localT = (position - loopIndex * d) / d;
            }

            // Fire OnStepComplete for every loop boundary crossed since the last render.
            int targetCompleted = complete ? (_loops < 0 ? loopIndex + 1 : _loops) : loopIndex;
            if (targetCompleted > _completedLoops)
            {
                if (fireCallbacks && _onStepComplete != null)
                {
                    int steps = targetCompleted - _completedLoops;
                    for (int i = 0; i < steps; i++)
                    {
                        _onStepComplete.Invoke();
                    }
                }
                _completedLoops = targetCompleted;
            }

            bool forward = _loopType != LoopType.Yoyo || (loopIndex & 1) == 0;
            float directionalT = forward ? localT : 1f - localT;
            float easedT = EvaluateEase(directionalT);

            ApplyPosition(easedT, loopIndex);

            if (fireCallbacks)
            {
                _onUpdate?.Invoke();
            }

            if (complete && State == TweenState.Playing)
            {
                State = TweenState.Completed;
                if (fireCallbacks)
                {
                    _onComplete?.Invoke();
                }
                // Only auto-kill if the completion callback didn't already revive the tween (e.g. via Restart).
                if (_autoKill && State == TweenState.Completed)
                {
                    DoKill();
                }
            }
        }

        protected float EvaluateEase(float t)
        {
            if (_customEase != null) return _customEase(t);
            if (_customCurve != null) return _customCurve.Evaluate(t);
            return EaseFunctions.Evaluate(_ease, t);
        }

        private void DoKill()
        {
            if (State == TweenState.Killed) return;
            State = TweenState.Killed;
            _onKill?.Invoke();
        }

        // ---- Sequence support ------------------------------------------------

        /// <summary>Prepares this tween to be driven by a <see cref="Sequence"/> instead of the runner.</summary>
        internal void MarkSequenced()
        {
            IsSequenced = true;
            _autoKill = false;
            _position = 0;
            _delayElapsed = 0f;
            _completedLoops = 0;
            _started = false;
            State = TweenState.Playing;
        }

        /// <summary>Samples this tween at an absolute time (delay included) without integrating delta time. Used by sequences.</summary>
        internal void GotoInternal(float time, bool fireCallbacks)
        {
            float t = time - _delay;
            if (t < 0f) return; // still inside its start delay

            if (!_started)
            {
                BeginPlay(fireCallbacks);
            }
            _position = t;
            Evaluate((float)_position, fireCallbacks);
        }

        // ================================================================
        //  Abstract hooks
        // ================================================================

        /// <summary>Captures the start value(s) at the moment the tween begins playing.</summary>
        protected abstract void CaptureStart();

        /// <summary>Applies the eased interpolant to the tweened target for the given loop index.</summary>
        protected abstract void ApplyPosition(float easedInterpolant, int loopIndex);

        // ================================================================
        //  Static factory — capturing form (arbitrary get/set closures)
        // ================================================================

        /// <summary>Creates and starts a tween for any type, using the supplied <see cref="TweenOps{T}"/>.</summary>
        public static Tween<T> To<T>(TweenGetter<T> getter, TweenSetter<T> setter, T endValue, float duration, TweenOps<T> ops)
        {
            Tween<T> tween = TweenPool<T>.Get();
            tween.InitializeCapturing(getter, setter, endValue, duration, ops);
            return Launch(tween);
        }

        public static Tween<float> To(TweenGetter<float> getter, TweenSetter<float> setter, float endValue, float duration)
            => To(getter, setter, endValue, duration, TweenLerp.Float);

        public static Tween<double> To(TweenGetter<double> getter, TweenSetter<double> setter, double endValue, float duration)
            => To(getter, setter, endValue, duration, TweenLerp.Double);

        public static Tween<Vector2> To(TweenGetter<Vector2> getter, TweenSetter<Vector2> setter, Vector2 endValue, float duration)
            => To(getter, setter, endValue, duration, TweenLerp.Vector2);

        public static Tween<Vector3> To(TweenGetter<Vector3> getter, TweenSetter<Vector3> setter, Vector3 endValue, float duration)
            => To(getter, setter, endValue, duration, TweenLerp.Vector3);

        public static Tween<Vector4> To(TweenGetter<Vector4> getter, TweenSetter<Vector4> setter, Vector4 endValue, float duration)
            => To(getter, setter, endValue, duration, TweenLerp.Vector4);

        public static Tween<Color> To(TweenGetter<Color> getter, TweenSetter<Color> setter, Color endValue, float duration)
            => To(getter, setter, endValue, duration, TweenLerp.Color);

        public static Tween<Quaternion> To(TweenGetter<Quaternion> getter, TweenSetter<Quaternion> setter, Quaternion endValue, float duration)
            => To(getter, setter, endValue, duration, TweenLerp.Quaternion);

        // ================================================================
        //  Static factory — capture-free form (target + cached delegates)
        // ================================================================

        /// <summary>Creates and starts a capture-free tween that reads/writes <paramref name="target"/> through
        /// cached delegates (no per-tween closure). <paramref name="target"/> is also set for <see cref="Kill(object,bool)"/>.</summary>
        public static Tween<T> To<T>(object target, TweenReader<T> reader, TweenWriter<T> writer, T endValue, float duration, TweenOps<T> ops)
        {
            var tween = TweenPool<T>.Get();
            tween.InitializeTargeted(target, reader, writer, endValue, duration, ops);
            return Launch(tween);
        }

        public static Tween<float> To(object target, TweenReader<float> reader, TweenWriter<float> writer, float endValue, float duration)
            => To(target, reader, writer, endValue, duration, TweenLerp.Float);

        public static Tween<Vector2> To(object target, TweenReader<Vector2> reader, TweenWriter<Vector2> writer, Vector2 endValue, float duration)
            => To(target, reader, writer, endValue, duration, TweenLerp.Vector2);

        public static Tween<Vector3> To(object target, TweenReader<Vector3> reader, TweenWriter<Vector3> writer, Vector3 endValue, float duration)
            => To(target, reader, writer, endValue, duration, TweenLerp.Vector3);

        public static Tween<Vector4> To(object target, TweenReader<Vector4> reader, TweenWriter<Vector4> writer, Vector4 endValue, float duration)
            => To(target, reader, writer, endValue, duration, TweenLerp.Vector4);

        public static Tween<Color> To(object target, TweenReader<Color> reader, TweenWriter<Color> writer, Color endValue, float duration)
            => To(target, reader, writer, endValue, duration, TweenLerp.Color);

        public static Tween<Quaternion> To(object target, TweenReader<Quaternion> reader, TweenWriter<Quaternion> writer, Quaternion endValue, float duration)
            => To(target, reader, writer, endValue, duration, TweenLerp.Quaternion);

        // ---- FromTo (explicit start value) -----------------------------------

        public static Tween<T> FromTo<T>(TweenSetter<T> setter, T from, T to, float duration, TweenOps<T> ops)
        {
            var tween = TweenPool<T>.Get();
            tween.InitializeCapturing(null, setter, to, duration, ops);
            tween.SetStartValue(from);
            return Launch(tween);
        }

        public static Tween<float> FromTo(TweenSetter<float> setter, float from, float to, float duration)
            => FromTo(setter, from, to, duration, TweenLerp.Float);

        public static Tween<Vector2> FromTo(TweenSetter<Vector2> setter, Vector2 from, Vector2 to, float duration)
            => FromTo(setter, from, to, duration, TweenLerp.Vector2);

        public static Tween<Vector3> FromTo(TweenSetter<Vector3> setter, Vector3 from, Vector3 to, float duration)
            => FromTo(setter, from, to, duration, TweenLerp.Vector3);

        public static Tween<Color> FromTo(TweenSetter<Color> setter, Color from, Color to, float duration)
            => FromTo(setter, from, to, duration, TweenLerp.Color);

        internal static Tween<T> Launch<T>(Tween<T> tween)
        {
            tween.State = TweenState.Playing;
            TweenRunner.Register(tween);
            return tween;
        }

        // ================================================================
        //  Static control
        // ================================================================

        /// <summary>Kills every active tween whose target equals <paramref name="target"/>. Returns how many were killed.</summary>
        public static int Kill(object target, bool complete = false) => TweenRunner.KillByTarget(target, complete);

        /// <summary>Kills every active tween and sequence.</summary>
        public static void KillAll(bool complete = false) => TweenRunner.KillAll(complete);
    }
}
