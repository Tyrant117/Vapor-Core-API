namespace Vapor.Tweening
{
    /// <summary>
    /// Reads the current value of the property being tweened. Called once when a tween starts to
    /// capture its start value (unless an explicit start was supplied via <c>From</c>/<c>FromTo</c>).
    /// </summary>
    public delegate T TweenGetter<out T>();

    /// <summary>
    /// Writes an interpolated value back to the property being tweened. Called every frame the tween updates.
    /// </summary>
    public delegate void TweenSetter<in T>(T value);

    /// <summary>
    /// Capture-free counterpart of <see cref="TweenGetter{T}"/>: reads the value from a target passed in as
    /// an argument rather than captured in a closure. Because nothing is captured, one cached <c>static</c>
    /// delegate can serve every tween of that property, so no per-tween closure is allocated.
    /// </summary>
    public delegate T TweenReader<out T>(object target);

    /// <summary>
    /// Capture-free counterpart of <see cref="TweenSetter{T}"/>: writes the value to a target passed in as
    /// an argument rather than captured in a closure.
    /// </summary>
    public delegate void TweenWriter<in T>(object target, T value);

    /// <summary>
    /// The lifecycle state of a <see cref="Tween"/> or <see cref="Sequence"/>.
    /// </summary>
    public enum TweenState
    {
        /// <summary>Created but not yet playing (paused before its first tick, or rewound).</summary>
        Idle,

        /// <summary>Actively advancing every frame.</summary>
        Playing,

        /// <summary>Temporarily halted; retains its progress and can be resumed with <see cref="Tween.Play"/>.</summary>
        Paused,

        /// <summary>Reached the end of all its loops. Inert unless restarted (only possible when auto-kill is disabled).</summary>
        Completed,

        /// <summary>Stopped and removed from the runner. A killed tween must not be reused.</summary>
        Killed,
    }

    /// <summary>
    /// Determines what happens each time a looping tween reaches the end of a single loop.
    /// </summary>
    public enum LoopType
    {
        /// <summary>Jump straight back to the start value and play forwards again.</summary>
        Restart,

        /// <summary>Alternate direction each loop: forwards, then backwards, then forwards, and so on.</summary>
        Yoyo,

        /// <summary>Treat the end value as the new start and advance by the same delta every loop (numeric types only).</summary>
        Incremental,
    }
}
