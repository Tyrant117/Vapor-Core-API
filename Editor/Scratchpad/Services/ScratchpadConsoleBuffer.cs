using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Scratchpad
{
    /// <summary>One console entry, kept so it can be turned into a note later.</summary>
    internal readonly struct ScratchpadLogEntry
    {
        public readonly string Message;
        public readonly string Stack;
        public readonly LogType Type;
        public readonly DateTime Time;

        public ScratchpadLogEntry(string message, string stack, LogType type, DateTime time)
        {
            Message = message;
            Stack = stack;
            Type = type;
            Time = time;
        }

        public bool IsError => Type is LogType.Error or LogType.Exception or LogType.Assert;

        /// <summary>A single line, short enough for a menu.</summary>
        public string Summary
        {
            get
            {
                var text = (Message ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
                return text.Length > 90 ? text[..87] + "..." : text;
            }
        }

        /// <summary>Message and stack together, as the note body should carry them.</summary>
        public string Detail => string.IsNullOrWhiteSpace(Stack)
            ? Message ?? string.Empty
            : $"{Message}\n\n{Stack.TrimEnd()}";
    }

    /// <summary>
    /// A rolling window of the last few console entries, so one can be attached to a note.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious implementation would be an item on the Console window's right-click menu. Unity
    /// exposes no public hook for that menu, and the internal route is undocumented and moves between
    /// editor versions — for a convenience feature that is a bad trade. Keeping our own buffer needs
    /// only a public callback, works the same in every version, and has the side benefit that the
    /// entry is still attachable after the console has been cleared.
    /// </para>
    /// <para>
    /// Every log type is kept, not just errors: a warning is often exactly the thing worth flagging,
    /// and filtering here would mean the one you wanted was the one thrown away.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Exempt from statics cleanup: the errors worth capturing are very often the ones raised during
    /// play mode, and clearing the buffer on the way out of it would throw away exactly those.
    /// </remarks>
    [InitializeOnLoad]
    [NoAutoStaticsCleanup]
    internal static class ScratchpadConsoleBuffer
    {
        private const int Capacity = 50;

        /// <summary>
        /// Where the buffer lives across a script recompile.
        /// </summary>
        /// <remarks>
        /// <see cref="SessionState"/> rather than <c>EditorPrefs</c> because its lifetime is exactly
        /// right: it survives a domain reload and dies with the editor. A console entry from a
        /// previous session of the editor is of no use to anybody.
        /// </remarks>
        private const string SessionKey = "Vapor.Scratchpad.ConsoleBuffer";

        /// <summary>A stack trace past this is truncated before being stashed.</summary>
        private const int MaxStoredStack = 4000;

        private static readonly List<ScratchpadLogEntry> s_Entries = new(Capacity);
        private static readonly object s_Lock = new();

        static ScratchpadConsoleBuffer()
        {
            // Threaded, because a log raised off the main thread is exactly the kind that is hard to
            // catch by hand and worth having kept for you.
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;

            // A static list does not survive a script recompile, but the Console window's contents do.
            // Without this the buffer disagrees with the console every time you touch a script, and
            // the entries it is missing are the older ones — which is to say the ones you had to go
            // looking for.
            AssemblyReloadEvents.beforeAssemblyReload -= Stash;
            AssemblyReloadEvents.beforeAssemblyReload += Stash;

            Restore();
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (s_Lock)
            {
                // An error inside an editor update repeats every frame. Collapsing a run of identical
                // messages keeps the buffer holding fifty distinct things rather than one thing fifty
                // times.
                if (s_Entries.Count > 0)
                {
                    var last = s_Entries[^1];
                    if (last.Type == type && last.Message == condition)
                    {
                        return;
                    }
                }

                s_Entries.Add(new ScratchpadLogEntry(condition, stackTrace, type, DateTime.Now));

                if (s_Entries.Count > Capacity)
                {
                    s_Entries.RemoveRange(0, s_Entries.Count - Capacity);
                }
            }
        }

        /// <summary>The most recent entries, newest first.</summary>
        public static List<ScratchpadLogEntry> Recent(int count)
        {
            lock (s_Lock)
            {
                var result = new List<ScratchpadLogEntry>(Math.Min(count, s_Entries.Count));
                for (var i = s_Entries.Count - 1; i >= 0 && result.Count < count; i--)
                {
                    result.Add(s_Entries[i]);
                }

                return result;
            }
        }

        /// <summary>The most recent entries of one type, newest first.</summary>
        /// <remarks>
        /// Offered per type because the menu can only show a handful, and a busy console is nearly all
        /// plain logs — take the newest ten of everything and a warning from a minute ago is simply
        /// not among them. Which is the one you wanted.
        /// </remarks>
        public static List<ScratchpadLogEntry> RecentOfKind(Func<ScratchpadLogEntry, bool> predicate, int count)
        {
            lock (s_Lock)
            {
                var result = new List<ScratchpadLogEntry>();
                for (var i = s_Entries.Count - 1; i >= 0 && result.Count < count; i--)
                {
                    if (predicate(s_Entries[i]))
                    {
                        result.Add(s_Entries[i]);
                    }
                }

                return result;
            }
        }

        /// <summary>How many entries are held, for a menu that wants to say so.</summary>
        public static int Count
        {
            get
            {
                lock (s_Lock)
                {
                    return s_Entries.Count;
                }
            }
        }

        #region Surviving a reload

        [Serializable]
        private sealed class Stashed
        {
            public List<StashedEntry> Entries = new();
        }

        [Serializable]
        private sealed class StashedEntry
        {
            public string Message;
            public string Stack;
            public int Type;
            public long Ticks;
        }

        private static void Stash()
        {
            lock (s_Lock)
            {
                var stashed = new Stashed();

                foreach (var entry in s_Entries)
                {
                    var stack = entry.Stack ?? string.Empty;

                    stashed.Entries.Add(new StashedEntry
                    {
                        Message = entry.Message,
                        Stack = stack.Length > MaxStoredStack ? stack[..MaxStoredStack] : stack,
                        Type = (int)entry.Type,
                        Ticks = entry.Time.Ticks,
                    });
                }

                SessionState.SetString(SessionKey, JsonUtility.ToJson(stashed));
            }
        }

        private static void Restore()
        {
            var json = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                var stashed = JsonUtility.FromJson<Stashed>(json);
                if (stashed?.Entries == null)
                {
                    return;
                }

                lock (s_Lock)
                {
                    s_Entries.Clear();
                    foreach (var entry in stashed.Entries)
                    {
                        s_Entries.Add(new ScratchpadLogEntry(entry.Message, entry.Stack,
                            (LogType)entry.Type, new DateTime(entry.Ticks)));
                    }
                }
            }
            catch (Exception)
            {
                // A buffer that will not deserialize is worth exactly nothing and is not worth a
                // console error of its own. Start empty.
                SessionState.EraseString(SessionKey);
            }
        }

        /// <summary>Stands in for a domain reload, which a test cannot cause.</summary>
        internal static void StashForTests() => Stash();

        /// <summary>Stands in for the statics being wiped by that reload.</summary>
        internal static void ClearForTests()
        {
            lock (s_Lock)
            {
                s_Entries.Clear();
            }
        }

        /// <summary>Stands in for the static constructor running again afterwards.</summary>
        internal static void RestoreForTests() => Restore();

        #endregion

        /// <summary>The newest error, which is nearly always the one you meant.</summary>
        public static bool TryGetLastError(out ScratchpadLogEntry entry)
        {
            lock (s_Lock)
            {
                for (var i = s_Entries.Count - 1; i >= 0; i--)
                {
                    if (!s_Entries[i].IsError)
                    {
                        continue;
                    }

                    entry = s_Entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}
