using System;
using System.Collections.Generic;
using System.Text;
using Unity.Scripting.LifecycleManagement;
using Stopwatch = System.Diagnostics.Stopwatch;
using Debug = UnityEngine.Debug;

namespace Vapor
{
    /// <summary>
    /// A stopwatch breakdown of one authoring operation, printed as a single line when it ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists because the save path spans three layers — the document writes files, the registry
    /// re-ingests them, and the window rebuilds its list — and no one of them can see where the time
    /// actually went. Guessing at that produced the wrong answer twice: the obvious suspect (the size
    /// of the file being written) turned out to be a rounding error next to the asset database
    /// round-trip sitting behind it.
    /// </para>
    /// <para>
    /// Spans are flat and are expected not to overlap, so the numbers add up to something close to the
    /// total and the remainder is real unattributed time rather than double counting. A span recorded
    /// twice under one operation is summed and its count reported, which is how per-shard work reads
    /// as <c>write x3</c> rather than as three separate lines.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class VslSaveDiagnostics
    {
        /// <summary>Whether operations are timed and reported at all.</summary>
        /// <remarks>
        /// A plain field rather than an editor preference so the runtime assembly does not have to
        /// know about one. The editor menu item that toggles it owns the persistence.
        /// </remarks>
        public static bool Enabled = true;

        /// <summary>Whether the per-type registry rebuild lines are logged.</summary>
        /// <remarks>
        /// Off by default. There is one line per closed <see cref="DataRegistry{TData}"/>, so a rebuild
        /// wrote a few dozen console entries — each one a formatted rich-text string, a stack trace
        /// capture and, where a console mirror is running, a line written to disk. That is a real cost
        /// paid on every rebuild for output nobody reads unless they are debugging the registry.
        /// </remarks>
        public static bool Verbose;

        private readonly struct Span
        {
            public readonly double Milliseconds;
            public readonly int Count;

            public Span(double milliseconds, int count)
            {
                Milliseconds = milliseconds;
                Count = count;
            }
        }

        private static readonly Dictionary<string, Span> s_Spans = new();
        private static readonly List<string> s_Order = new();
        private static readonly StringBuilder s_Message = new();

        private static string s_Operation;
        private static long s_Start;
        private static int s_Depth;

        private static double Elapsed(long from) => (Stopwatch.GetTimestamp() - from) * 1000d / Stopwatch.Frequency;

        /// <summary>
        /// Starts an operation. Nested calls are folded into the outermost one rather than starting a
        /// second report, so a save that triggers another save still reads as one line.
        /// </summary>
        public static void Begin(string operation)
        {
            if (!Enabled)
            {
                return;
            }

            if (s_Depth++ > 0)
            {
                return;
            }

            s_Operation = operation;
            s_Spans.Clear();
            s_Order.Clear();
            s_Start = Stopwatch.GetTimestamp();
        }

        /// <summary>Ends the current operation and logs its breakdown.</summary>
        public static void End()
        {
            if (!Enabled || s_Depth == 0)
            {
                return;
            }

            if (--s_Depth > 0 || s_Operation == null)
            {
                return;
            }

            var total = Elapsed(s_Start);
            var attributed = 0d;

            s_Message.Clear();
            s_Message.Append(s_Operation).Append(' ').Append(total.ToString("0.0")).Append(" ms");

            foreach (var label in s_Order)
            {
                var span = s_Spans[label];
                attributed += span.Milliseconds;

                s_Message.Append(" | ").Append(label);
                if (span.Count > 1)
                {
                    s_Message.Append(" x").Append(span.Count);
                }

                s_Message.Append(' ').Append(span.Milliseconds.ToString("0.0"));
            }

            var rest = total - attributed;
            if (s_Order.Count > 0 && rest > 0.05d)
            {
                s_Message.Append(" | rest ").Append(rest.ToString("0.0"));
            }

            s_Operation = null;
            Debug.Log(s_Message.ToString());
        }

        /// <summary>Times a block of work under <paramref name="label"/>. Free when disabled.</summary>
        public static Scope Measure(string label) =>
            Enabled && s_Depth > 0 ? new Scope(label) : default;

        /// <summary>Records time measured elsewhere, for work that cannot be wrapped in a scope.</summary>
        public static void Record(string label, double milliseconds)
        {
            if (!Enabled || s_Depth == 0 || label == null)
            {
                return;
            }

            if (s_Spans.TryGetValue(label, out var existing))
            {
                s_Spans[label] = new Span(existing.Milliseconds + milliseconds, existing.Count + 1);
                return;
            }

            s_Spans[label] = new Span(milliseconds, 1);
            s_Order.Add(label);
        }

        /// <summary>The timer for one span. A default instance records nothing.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly string _label;
            private readonly long _start;

            internal Scope(string label)
            {
                _label = label;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_label != null)
                {
                    Record(_label, Elapsed(_start));
                }
            }
        }
    }
}
