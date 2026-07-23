using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// A timeline that plays a group of child tweens, intervals and callbacks in a coordinated way.
    /// Build it fluently:
    /// <code>
    /// Sequence.Create()
    ///     .Append(panel.TweenOpacity(1f, 0.3f))
    ///     .Join(panel.TweenScale(1.1f, 0.3f))
    ///     .AppendInterval(0.5f)
    ///     .AppendCallback(() =&gt; Debug.Log("done"));
    /// </code>
    /// A sequence is itself a <see cref="Tween"/>, so it supports the same easing (defaults to linear),
    /// looping, callbacks and control methods. Child tweens added to a sequence are taken over by it —
    /// they no longer update independently.
    ///
    /// Build a sequence synchronously in one chain: it begins playing the moment it's created, so a gap
    /// between <see cref="Create"/> and the first element could let a zero-length sequence complete early.
    /// </summary>
    public sealed class Sequence : Tween
    {
        private struct Element
        {
            public float Start;      // start time on the sequence timeline
            public Tween Tween;      // the child tween, or null for callback/interval elements
            public Action Callback;  // a callback, or null for tween/interval elements
            public bool Fired;       // whether a callback element has fired this loop
            public float LastLocal;  // last local time this child was sampled at (NaN = never / re-arm)
        }

        private readonly List<Element> _elements = new();
        private float _lastElementStart;
        private int _lastLoopIndex = -1; // which sequence loop was rendered last, for loop-boundary handling

        private Sequence()
        {
            _ease = Ease.Linear;
            _loopType = LoopType.Restart;
        }

        /// <summary>Creates and starts an empty sequence.</summary>
        public static Sequence Create()
        {
            var sequence = new Sequence { State = TweenState.Playing };
            TweenRunner.Register(sequence);
            return sequence;
        }

        // ================================================================
        //  Building
        // ================================================================

        /// <summary>Adds a tween that starts after everything appended so far.</summary>
        public Sequence Append(Tween tween)
        {
            if (!TryOwn(tween))
            {
                return this;
            }
            float start = _duration;
            AddTweenElement(start, tween);
            _lastElementStart = start;
            _duration = Mathf.Max(_duration, start + tween.FullTimelineDuration);
            return this;
        }

        /// <summary>Adds a tween that starts at the same time as the most recently appended element (runs in parallel).</summary>
        public Sequence Join(Tween tween)
        {
            if (!TryOwn(tween))
            {
                return this;
            }
            float start = _lastElementStart;
            AddTweenElement(start, tween);
            _duration = Mathf.Max(_duration, start + tween.FullTimelineDuration);
            return this;
        }

        /// <summary>Adds a tween at an absolute time on the sequence timeline.</summary>
        public Sequence Insert(float atTime, Tween tween)
        {
            if (!TryOwn(tween))
            {
                return this;
            }
            atTime = Mathf.Max(0f, atTime);
            AddTweenElement(atTime, tween);
            _duration = Mathf.Max(_duration, atTime + tween.FullTimelineDuration);
            return this;
        }

        /// <summary>Adds a tween at the very start, pushing everything already in the sequence later.</summary>
        public Sequence Prepend(Tween tween)
        {
            if (!TryOwn(tween))
            {
                return this;
            }
            float shift = tween.FullTimelineDuration;
            ShiftAll(shift);
            AddTweenElement(0f, tween);
            _duration += shift;
            _lastElementStart += shift;
            return this;
        }

        /// <summary>Extends the timeline by an empty gap after everything appended so far.</summary>
        public Sequence AppendInterval(float interval)
        {
            _lastElementStart = _duration;
            _duration += Mathf.Max(0f, interval);
            return this;
        }

        /// <summary>Inserts an empty gap at the very start, pushing everything later.</summary>
        public Sequence PrependInterval(float interval)
        {
            interval = Mathf.Max(0f, interval);
            ShiftAll(interval);
            _duration += interval;
            _lastElementStart += interval;
            return this;
        }

        /// <summary>Schedules a callback after everything appended so far.</summary>
        public Sequence AppendCallback(Action callback)
        {
            _elements.Add(new Element { Start = _duration, Callback = callback });
            _lastElementStart = _duration;
            return this;
        }

        /// <summary>Schedules a callback at an absolute time on the sequence timeline.</summary>
        public Sequence InsertCallback(float atTime, Action callback)
        {
            _elements.Add(new Element { Start = Mathf.Max(0f, atTime), Callback = callback });
            return this;
        }

        private void AddTweenElement(float start, Tween tween)
        {
            _elements.Add(new Element { Start = start, Tween = tween, LastLocal = float.NaN });
        }

        /// <summary>Takes ownership of a tween so the sequence can drive it. Returns false (and skips adding) if it can't be owned.</summary>
        private bool TryOwn(Tween tween)
        {
            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }
            if (tween.IsSequenced)
            {
                Debug.LogWarning("[Vapor.Tweening] A tween that already belongs to a sequence was added again; ignoring the duplicate.");
                return false;
            }
            if (tween.State == TweenState.Killed)
            {
                Debug.LogWarning("[Vapor.Tweening] A killed tween was added to a Sequence; ignoring it.");
                return false;
            }
            if (tween.LoopsValue < 0)
            {
                Debug.LogWarning("[Vapor.Tweening] A tween set to loop infinitely was added to a Sequence; its timeline length is undefined. Give it a finite loop count before sequencing.");
            }
            TweenRunner.Unregister(tween);
            tween.MarkSequenced();
            return true;
        }

        private void ShiftAll(float shift)
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                Element e = _elements[i];
                e.Start += shift;
                _elements[i] = e;
            }
        }

        // ================================================================
        //  Playback
        // ================================================================
        protected override void CaptureStart()
        {
            // Runs on (re)start — before the first sample. Re-arm every child so a restarted sequence replays
            // from scratch. Each child still captures its own start value the first time the timeline reaches it.
            _lastLoopIndex = -1;
            RearmChildren();
        }

        protected override void ApplyPosition(float easedInterpolant, int loopIndex)
        {
            HandleLoopBoundary(loopIndex);
            SampleTimeline(easedInterpolant * _duration);
        }

        /// <summary>
        /// Detects crossing into a new sequence loop. For Restart/Incremental loops it finishes the loop being
        /// left (so children fire their completion callbacks) and re-arms every child so the next loop replays
        /// cleanly with its per-tween callbacks. Yoyo is handled by position sampling and is never re-armed.
        /// </summary>
        private void HandleLoopBoundary(int loopIndex)
        {
            if (loopIndex == _lastLoopIndex)
            {
                return;
            }

            if (_loopType != LoopType.Yoyo && _lastLoopIndex >= 0 && loopIndex > _lastLoopIndex)
            {
                SampleTimeline(_duration); // finish the loop we're leaving so children complete + fire callbacks
                RearmChildren();
            }

            _lastLoopIndex = loopIndex;
        }

        /// <summary>Resets every child's playback state and callback flags so the timeline can be replayed.</summary>
        private void RearmChildren()
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                Element e = _elements[i];
                if (e.Tween != null)
                {
                    e.Tween.MarkSequenced(); // resets started/state/position/completedLoops for a clean replay
                    e.LastLocal = float.NaN;
                }
                else
                {
                    e.Fired = false;
                }
                _elements[i] = e;
            }
        }

        /// <summary>Drives every child and callback to the given time on the sequence timeline.</summary>
        private void SampleTimeline(float seqTime)
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                Element e = _elements[i];

                if (e.Tween != null)
                {
                    if (seqTime < e.Start)
                    {
                        // The timeline hasn't reached this child yet: leave it untouched so it doesn't apply
                        // its start value prematurely, and re-arm it so it re-applies once reached (e.g. on a loop).
                        if (!float.IsNaN(e.LastLocal))
                        {
                            e.LastLocal = float.NaN;
                            _elements[i] = e;
                        }
                        continue;
                    }

                    float local = Mathf.Min(seqTime - e.Start, e.Tween.FullTimelineDuration);
                    if (local == e.LastLocal)
                    {
                        // Local time is unchanged (typically a child that already finished): skip the write so
                        // we don't re-dirty a UI Toolkit element and force a needless repaint every frame.
                        continue;
                    }

                    e.LastLocal = local;
                    _elements[i] = e;
                    e.Tween.GotoInternal(local, true);
                }
                else if (e.Callback != null)
                {
                    if (!e.Fired && seqTime >= e.Start)
                    {
                        e.Callback.Invoke();
                        e.Fired = true;
                        _elements[i] = e;
                    }
                    else if (e.Fired && seqTime < e.Start)
                    {
                        // Reset so the callback can fire again on the next loop.
                        e.Fired = false;
                        _elements[i] = e;
                    }
                }
            }
        }
    }
}
