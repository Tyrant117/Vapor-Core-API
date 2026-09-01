using System;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// The attach-console menu, shared by the detail composer and the quick-capture popup.
    /// </summary>
    /// <remarks>
    /// Grouped by log type rather than offered as one recency-ordered list. A busy console is nearly
    /// all plain logs, so the ten newest entries can easily contain no warning and no error at all —
    /// which is exactly the complaint that prompted this: the menu "only finds logs". Splitting the
    /// list means each type is reachable no matter how noisy the others are.
    /// </remarks>
    internal static class ScratchpadConsoleMenu
    {
        /// <summary>How many of each type to offer. Enough to find one, few enough to read.</summary>
        private const int PerKind = 10;

        public static GenericMenu Build(Action<string> onPicked, bool canDetach)
        {
            var menu = new GenericMenu();

            if (ScratchpadConsoleBuffer.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Nothing in the console yet"));
                return menu;
            }

            AddGroup(menu, onPicked, "Errors", e => e.IsError);
            AddGroup(menu, onPicked, "Warnings", e => e.Type == LogType.Warning);
            AddGroup(menu, onPicked, "Logs", e => e.Type == LogType.Log);

            if (canDetach)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Detach"), false, () => onPicked(null));
            }

            return menu;
        }

        private static void AddGroup(GenericMenu menu, Action<string> onPicked, string heading,
            Func<ScratchpadLogEntry, bool> predicate)
        {
            var entries = ScratchpadConsoleBuffer.RecentOfKind(predicate, PerKind);

            if (entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent($"{heading}/none"));
                return;
            }

            foreach (var entry in entries)
            {
                var captured = entry;

                // Slashes in a log message would otherwise read as submenu separators and cut the
                // line into a tree of fragments. The heading's own slash is the one we mean.
                var label = $"{heading}/{captured.Summary.Replace('/', '∕')}";

                menu.AddItem(new GUIContent(label), false, () => onPicked(captured.Detail));
            }
        }
    }
}
