using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Keeps a pane's scroll position across a rebuild, but only when it is still showing the same
    /// thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window rebuilds panes wholesale rather than patching rows, which is what keeps a badge
    /// from showing a count that has moved on underneath it. The cost was that adding a note threw
    /// you back to the top of the change you were reading — the pane had been rebuilt, so its
    /// scroll went with it.
    /// </para>
    /// <para>
    /// The key is what makes this a fix rather than a new bug. Restoring unconditionally would leave
    /// you halfway down a change you just selected; restoring never is where we started. So the
    /// offset survives a rebuild of the same target and resets on a move to a different one, which
    /// is what both actions should do anyway.
    /// </para>
    /// <para>
    /// The restore is scheduled rather than assigned. A <see cref="ScrollView"/> clamps
    /// <see cref="ScrollView.scrollOffset"/> against the content it currently has, and immediately
    /// after a rebuild that content has not been laid out, so an assignment here clamps to zero.
    /// </para>
    /// </remarks>
    internal sealed class ScratchpadScrollMemory
    {
        private readonly ScrollView _scroll;
        private string _key;

        public ScratchpadScrollMemory(ScrollView scroll)
        {
            _scroll = scroll;
        }

        /// <summary>Runs <paramref name="rebuild"/>, holding the scroll if the key has not changed.</summary>
        public void Around(string key, Action rebuild)
        {
            key ??= string.Empty;

            var same = string.Equals(key, _key, StringComparison.Ordinal);
            var offset = _scroll.scrollOffset;
            _key = key;

            rebuild();

            if (!same)
            {
                _scroll.scrollOffset = Vector2.zero;
                return;
            }

            _scroll.schedule.Execute(() => _scroll.scrollOffset = offset).ExecuteLater(0);
        }
    }
}
