using System;
using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// A tween that interpolates a single value of type <typeparamref name="T"/> from a start value to an end
    /// value, writing each interpolated value back every frame. Created via <see cref="Tween.To{T}"/> /
    /// <see cref="Tween.FromTo{T}"/> or one of the typed helpers.
    ///
    /// It supports two read/write modes so the built-in extensions can avoid per-tween allocations:
    /// <list type="bullet">
    /// <item><b>Capturing</b> — a <see cref="TweenGetter{T}"/>/<see cref="TweenSetter{T}"/> pair (usually
    /// closures). Flexible; allocates the closures at the call site.</item>
    /// <item><b>Targeted</b> — a target object plus cached <see cref="TweenReader{T}"/>/<see cref="TweenWriter{T}"/>
    /// delegates. Allocation-free because the delegates can be <c>static readonly</c>.</item>
    /// </list>
    /// Instances are pooled and recycled: construction goes through <c>Initialize*</c> rather than a
    /// constructor so a recycled instance can be fully re-set.
    /// </summary>
    public sealed class Tween<T> : Tween
    {
        // Capturing mode.
        private TweenGetter<T> _getter;
        private TweenSetter<T> _setter;

        // Targeted mode.
        private object _target;
        private TweenReader<T> _reader;
        private TweenWriter<T> _writer;

        private TweenOps<T> _ops;
        private T _endInput;             // the configured "to" value (or delta when relative); never mutated
        private T _startValue;           // derived in CaptureStart
        private T _endValue;             // derived in CaptureStart
        private T _diff;                 // endValue - startValue, cached for incremental loops
        private bool _hasExplicitStart;
        private T _explicitStart;
        private bool _fromMode;

        /// <summary>True while sitting idle in the pool; guards against double-release.</summary>
        internal bool Pooled;

        // Parameterless: instances are created by the pool / factory and configured via Initialize*.
        internal Tween() { }

        internal void InitializeCapturing(TweenGetter<T> getter, TweenSetter<T> setter, T endValue, float duration, TweenOps<T> ops)
        {
            ResetBaseState();
            Pooled = false;
            _getter = getter;
            _setter = setter ?? throw new ArgumentNullException(nameof(setter));
            _target = null;
            _reader = null;
            _writer = null;
            _ops = ops ?? throw new ArgumentNullException(nameof(ops));
            _endInput = endValue;
            _duration = Mathf.Max(0f, duration);
            _hasExplicitStart = false;
            _fromMode = false;
        }

        internal void InitializeTargeted(object target, TweenReader<T> reader, TweenWriter<T> writer, T endValue, float duration, TweenOps<T> ops)
        {
            ResetBaseState();
            Pooled = false;
            _target = target;
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _getter = null;
            _setter = null;
            _ops = ops ?? throw new ArgumentNullException(nameof(ops));
            _endInput = endValue;
            _duration = Mathf.Max(0f, duration);
            _hasExplicitStart = false;
            _fromMode = false;
            Target = target; // enables Tween.Kill(target)
        }

        internal void SetStartValue(T start)
        {
            _hasExplicitStart = true;
            _explicitStart = start;
        }

        /// <summary>Plays from an explicit start value to the configured end value.</summary>
        public Tween From(T startValue)
        {
            SetStartValue(startValue);
            return this;
        }

        /// <summary>
        /// Inverts the tween: plays from the configured end value to the property's current value,
        /// captured when the tween starts. Useful for intro animations.
        /// </summary>
        public Tween From()
        {
            _fromMode = true;
            return this;
        }

        private T ReadCurrent()
        {
            if (_reader != null) return _reader(_target);
            if (_getter != null) return _getter();
            return default;
        }

        private void WriteValue(T value)
        {
            if (_writer != null)
            {
                _writer(_target, value);
            }
            else
            {
                _setter?.Invoke(value);
            }
        }

        protected override void CaptureStart()
        {
            // Derived entirely from _endInput (never mutated) and the current value, so re-capturing on a
            // sequence loop replay is idempotent — relative/From tweens won't drift.
            T current = ReadCurrent();
            if (_fromMode)
            {
                _startValue = _endInput;
                _endValue = current;
            }
            else
            {
                _startValue = _hasExplicitStart ? _explicitStart : current;
                _endValue = (_isRelative && _ops.Add != null) ? _ops.Add(_startValue, _endInput) : _endInput;
            }

            _diff = _ops.SupportsArithmetic
                ? _ops.Add(_endValue, _ops.Scale(_startValue, -1f)) // end - start
                : default;
        }

        protected override void ApplyPosition(float easedInterpolant, int loopIndex)
        {
            T value = _ops.Lerp(_startValue, _endValue, easedInterpolant);

            if (_loopType == LoopType.Incremental && loopIndex > 0 && _ops.SupportsArithmetic)
            {
                value = _ops.Add(value, _ops.Scale(_diff, loopIndex));
            }

            WriteValue(value);
        }

        internal override void ReturnToPool()
        {
            if (Pooled) return;
            Pooled = true;

            // Drop every reference so the pooled instance keeps nothing alive (callbacks live on the base).
            ResetBaseState();
            _getter = null;
            _setter = null;
            _reader = null;
            _writer = null;
            _target = null;
            _ops = null;
            _endInput = default;
            _startValue = default;
            _endValue = default;
            _diff = default;
            _explicitStart = default;
            _hasExplicitStart = false;
            _fromMode = false;

            TweenPool<T>.Release(this);
        }
    }
}
