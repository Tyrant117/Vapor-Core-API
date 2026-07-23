using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Tweening
{
    /// <summary>
    /// The single driver that advances every active tween once per frame. It is a hidden,
    /// don't-destroy-on-load <see cref="MonoBehaviour"/> created on demand the first time a tween is
    /// started, mirroring the project's existing runtime-singleton pattern. There is nothing to place
    /// in a scene and nothing to configure.
    ///
    /// Registration, de-registration and the per-frame kill sweep are all O(1) amortized: each tween
    /// carries an <c>InRunner</c> flag so we never scan the list to dedup, and dead entries are removed
    /// with a single compaction pass instead of per-element <c>RemoveAt</c>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    internal sealed class TweenRunner : MonoBehaviour
    {
        private static TweenRunner s_Instance;
        private static bool s_Quitting;

        private readonly List<Tween> _active = new(128);
        private readonly List<Tween> _pending = new(32);
        private bool _isTicking;

        internal static TweenRunner Instance
        {
            get
            {
                if (s_Instance) return s_Instance;
                if (s_Quitting || !Application.isPlaying) return null;

                var go = new GameObject("[TweenRunner]") { hideFlags = HideFlags.HideAndDontSave };
                s_Instance = go.AddComponent<TweenRunner>();
                DontDestroyOnLoad(go);
                return s_Instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "enter play mode without domain reload".
            s_Instance = null;
            s_Quitting = false;
        }

        // ----------------------------------------------------------------
        //  Registration (O(1): the InRunner flag is the dedup, not a scan)
        // ----------------------------------------------------------------
        internal static void Register(Tween tween)
        {
            if (tween == null || tween.IsSequenced || tween.InRunner) return;

            TweenRunner runner = Instance;
            if (!runner) return;

            tween.InRunner = true;
            // While ticking, buffer newcomers so we don't mutate the list mid-iteration.
            (runner._isTicking ? runner._pending : runner._active).Add(tween);
        }

        internal static void Unregister(Tween tween)
        {
            if (!s_Instance || tween == null || !tween.InRunner) return;

            tween.InRunner = false;
            if (!s_Instance._isTicking)
            {
                // Not mid-iteration: keep the lists tight immediately.
                s_Instance._active.Remove(tween);
                s_Instance._pending.Remove(tween);
            }
            // Otherwise the tick loop skips it (InRunner == false) and the sweep drops it.
        }

        // ----------------------------------------------------------------
        //  Update loop
        // ----------------------------------------------------------------
        private void Update()
        {
            Tick(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void Tick(float scaledDelta, float unscaledDelta)
        {
            IntegratePending();

            bool needsCompact = false;
            _isTicking = true;
            for (int i = 0; i < _active.Count; i++)
            {
                Tween tween = _active[i];
                if (tween.State == TweenState.Killed || !tween.InRunner)
                {
                    needsCompact = true;
                    continue;
                }

                tween.InternalUpdate(scaledDelta, unscaledDelta);

                if (tween.State == TweenState.Killed || !tween.InRunner)
                {
                    needsCompact = true;
                }
            }
            _isTicking = false;

            if (needsCompact) Compact();
            IntegratePending();
        }

        // Single O(n) pass that keeps live entries in order and drops killed/unregistered ones.
        private void Compact()
        {
            int write = 0;
            for (int read = 0; read < _active.Count; read++)
            {
                Tween tween = _active[read];
                if (tween.State == TweenState.Killed)
                {
                    tween.InRunner = false;
                    tween.ReturnToPool(); // recycles value tweens; no-op for sequences
                    continue;
                }
                if (!tween.InRunner)
                {
                    continue; // unregistered (e.g. taken over by a Sequence)
                }
                _active[write++] = tween;
            }

            if (write < _active.Count)
            {
                _active.RemoveRange(write, _active.Count - write);
            }
        }

        private void IntegratePending()
        {
            if (_pending.Count == 0) return;
            _active.AddRange(_pending); // InRunner dedup guarantees no duplicates
            _pending.Clear();
        }

        // ----------------------------------------------------------------
        //  Bulk control
        // ----------------------------------------------------------------
        internal static int KillByTarget(object target, bool complete)
        {
            if (!s_Instance || target == null) return 0;

            int killed = 0;
            killed += KillMatching(s_Instance._active, target, complete);
            killed += KillMatching(s_Instance._pending, target, complete);
            return killed;
        }

        private static int KillMatching(List<Tween> list, object target, bool complete)
        {
            int killed = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Tween tween = list[i];
                if (tween.State != TweenState.Killed && Equals(tween.Target, target))
                {
                    tween.Kill(complete);
                    killed++;
                }
            }
            return killed;
        }

        internal static void KillAll(bool complete)
        {
            if (!s_Instance) return;
            KillEvery(s_Instance._active, complete);
            KillEvery(s_Instance._pending, complete);
        }

        private static void KillEvery(List<Tween> list, bool complete)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                Tween tween = list[i];
                if (tween.State != TweenState.Killed)
                {
                    tween.Kill(complete);
                }
            }
        }

        private void OnApplicationQuit()
        {
            s_Quitting = true;
        }
    }
}
