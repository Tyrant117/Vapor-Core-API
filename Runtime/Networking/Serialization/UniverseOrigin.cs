using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// Where this process's float world currently sits in the universe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Render space is a <see cref="Vector3"/> measured from here. Everything a peer draws, simulates and
    /// collides is near zero because the origin follows the peer; everything on the wire is a
    /// <see cref="UniversePosition"/> and therefore means the same thing to peers whose origins differ.
    /// That is what makes per-client rebasing possible at all: nobody has to agree on where zero is.
    /// </para>
    /// <para>
    /// <b>A static, deliberately.</b> One process renders one float world, so this is a genuine
    /// singleton rather than an ambient context being smuggled past an API. Making it an instance would
    /// oblige every caller to answer "which origin?" when there has only ever been one, and the callers
    /// are the transform's read path — the hottest code in the frame.
    /// </para>
    /// <para>
    /// Shifting it does <i>not</i> move anything. It only changes what render space means; moving what is
    /// already in render space is the sweep's job, and <see cref="Shifted"/> is how the sweep and anything
    /// else holding a world-space vector are told. Separating the two is what keeps "shift it twice" from
    /// being a whole class of bug.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class UniverseOrigin
    {
        /// <summary>Where render-space zero is.</summary>
        public static UniversePosition Current { get; private set; } = UniversePosition.Zero;

        /// <summary>
        /// Render space just moved. The argument is the delta to add to a held world-space position to
        /// keep it where it was.
        /// </summary>
        /// <remarks>
        /// Fired once per rebase, after <see cref="Current"/> is already the new value, so a handler that
        /// recomputes from the origin and one that adds the delta agree.
        /// </remarks>
        public static event Action<Vector3> Shifted;

        /// <summary>
        /// How many times render space has moved since the last <see cref="Reset"/>.
        /// </summary>
        /// <remarks>
        /// Kept here rather than on whatever decided to rebase, because the origin is the thing that
        /// moved and anything wanting the number — a HUD, a soak test, a bug report — can already see the
        /// origin. It saves every one of them a reference to the game's sweep.
        /// </remarks>
        public static int Shifts { get; private set; }

        /// <summary>Puts render-space zero somewhere else, and says by how much. Returns the delta.</summary>
        public static Vector3 ShiftTo(in UniversePosition origin)
        {
            if (origin == Current)
            {
                return Vector3.zero;
            }

            // What a point currently at render p becomes: p + delta. Computed before Current moves,
            // because afterwards the old origin is gone.
            var delta = Current.ToRender(origin);
            Current = origin;
            Shifts++;
            Shifted?.Invoke(delta);
            return delta;
        }

        /// <summary>Forgets everything. Leaving a session, or a test tearing down.</summary>
        public static void Reset()
        {
            Current = UniversePosition.Zero;
            Shifts = 0;
            Shifted = null;
        }

        /// <summary>
        /// Whether a focus has drifted far enough from the origin to be worth rebasing around.
        /// </summary>
        /// <remarks>
        /// The threshold is larger than a sector on purpose. Snapping the origin to whichever sector the
        /// focus is in would rebase every time a ship crossed a boundary — which, at three hundred metres
        /// a second, is every three seconds and twice a second while hovering on one. Hysteresis costs a
        /// little precision at the edges and buys a rebase only when one is actually needed.
        /// </remarks>
        public static bool ShouldRebase(Vector3 focusRender, float threshold) =>
            focusRender.sqrMagnitude > threshold * threshold;

        /// <summary>The origin a focus should rebase to: the corner of the sector it is standing in.</summary>
        /// <remarks>
        /// Snapped to a sector rather than set to the focus itself, so that origins are drawn from a
        /// fixed lattice. Two peers near each other then usually share an origin exactly, which makes
        /// their render spaces comparable when debugging — and makes a rebase reproducible rather than
        /// depending on where a ship happened to be at the moment it crossed the threshold.
        /// </remarks>
        public static UniversePosition RebaseTargetFor(Vector3 focusRender)
        {
            var focus = Current + focusRender;
            return UniversePosition.Create(focus.Sector, Vector3.zero);
        }
    }
}
