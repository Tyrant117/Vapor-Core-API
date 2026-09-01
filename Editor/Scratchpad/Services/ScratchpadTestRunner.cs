using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Runs a feature's tests and turns what failed into notes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point is not to replace the Test Runner window — it is to close the loop that was
    /// otherwise manual: run the tests, read the failures, retype them as issues, send them back.
    /// Every step of that except deciding to run is mechanical, and a retyped stack trace is a
    /// worse stack trace.
    /// </para>
    /// <para>
    /// Failures land as <see cref="NoteKind.Issue"/> notes on the newest session, which is the one
    /// whose changes most likely caused them. They are ordinary notes from that point on: editable,
    /// resolvable, and closed by id from a later handoff like anything else.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Exempt from statics cleanup: <see cref="IsRunning"/> tracks a run that is happening right now,
    /// and a run does not stop being in flight because play mode ended.
    /// </remarks>
    [NoAutoStaticsCleanup]
    internal sealed class ScratchpadTestRunner : ICallbacks
    {
        /// <summary>How many runs to keep per feature. Enough to see a trend, not a log file.</summary>
        private const int HistoryLimit = 20;

        /// <summary>A failure message past this is truncated in the note body.</summary>
        private const int MaxMessage = 1200;

        private readonly ScratchpadStore _store;
        private readonly string _featureName;
        private readonly Action<ScratchpadTestRun> _onFinished;

        private readonly List<string> _failures = new();
        private TestRunnerApi _api;

        private ScratchpadTestRunner(ScratchpadStore store, string featureName,
            Action<ScratchpadTestRun> onFinished)
        {
            _store = store;
            _featureName = featureName;
            _onFinished = onFinished;
        }

        /// <summary>True while a run is in flight, so the button can say so and refuse a second.</summary>
        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Starts an EditMode run for one feature's filter.
        /// </summary>
        /// <returns>False when there is nothing to run or a run is already going.</returns>
        public static bool Run(ScratchpadStore store, ScratchpadFeature feature,
            Action<ScratchpadTestRun> onFinished)
        {
            if (IsRunning || feature == null)
            {
                return false;
            }

            var names = store.TestsFor(feature);
            if (names.Count == 0)
            {
                Debug.LogWarning($"[Scratchpad] \"{feature.Name}\" has no tests linked to it. They come " +
                                 "from the handoffs' `tests` list, or from Add test in the Tests section.");
                return false;
            }

            var runner = new ScratchpadTestRunner(store, feature.Name, onFinished)
            {
                _api = ScriptableObject.CreateInstance<TestRunnerApi>(),
            };

            runner._api.RegisterCallbacks(runner);
            IsRunning = true;

            try
            {
                runner._api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = TestMode.EditMode,

                    // Matched against full test names, so one entry serves a namespace, a fixture or a
                    // single test without the caller having to say which it meant.
                    groupNames = names.Select(Regex).ToArray(),
                }));
            }
            catch (Exception e)
            {
                // Execute throwing is the one path that would leave the flag set with no run to clear
                // it, which shows up as a Run button that is disabled for no visible reason.
                IsRunning = false;
                runner.Release();

                Debug.LogError($"[Scratchpad] Could not start the tests for \"{feature.Name}\": {e.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Turns a plain name into a filter that matches it and everything under it.
        /// </summary>
        /// <remarks>
        /// <c>groupNames</c> are regular expressions matched against the full test name, so a bare
        /// namespace matches nothing on its own — the dots are wildcards and there is no anchor. This
        /// escapes what was typed and appends the "or anything below it" tail.
        /// </remarks>
        private static string Regex(string filter) =>
            "^" + System.Text.RegularExpressions.Regex.Escape(filter.Trim()) + "($|\\.)";

        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            // Leaves only; a suite's result is the roll-up of the tests already recorded.
            if (result.Test.IsSuite || result.TestStatus != TestStatus.Failed)
            {
                return;
            }

            var message = (result.Message ?? string.Empty).Trim();
            var stack = (result.StackTrace ?? string.Empty).Trim();

            var detail = string.IsNullOrEmpty(stack) ? message : $"{message}\n\n{stack}";
            if (detail.Length > MaxMessage)
            {
                detail = detail[..MaxMessage] + "\n…";
            }

            _failures.Add($"{result.Test.FullName}\n{detail}");
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            IsRunning = false;

            var run = new ScratchpadTestRun
            {
                When = ScratchpadPaths.Timestamp(DateTime.Now),
                Passed = result.PassCount,
                Failed = result.FailCount,
                Skipped = result.SkipCount,
                Duration = (float)result.Duration,
                Failures = _failures.Select(FirstLine).ToList(),
            };

            try
            {
                Record(run);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Scratchpad] The tests ran but the result could not be recorded: {e.Message}");
            }
            finally
            {
                Release();
            }

            _onFinished?.Invoke(run);
        }

        /// <summary>
        /// Drops the API object once it has nothing left to tell us.
        /// </summary>
        /// <remarks>
        /// Deferred rather than destroyed on the spot: this runs from inside the API's own callback,
        /// and destroying the object whose stack we are standing on is the kind of thing that works
        /// until it does not.
        /// </remarks>
        private void Release()
        {
            if (_api == null)
            {
                return;
            }

            var api = _api;
            _api = null;

            api.UnregisterCallbacks(this);
            EditorApplication.delayCall += () =>
            {
                if (api != null)
                {
                    UnityEngine.Object.DestroyImmediate(api);
                }
            };
        }

        /// <summary>
        /// Every EditMode test in the project, as full names, for the picker.
        /// </summary>
        /// <remarks>
        /// Asynchronous because the framework builds the tree by loading assemblies. The callback may
        /// arrive a frame or several later, so the caller gets a list rather than a return value.
        /// </remarks>
        public static void ListTests(Action<List<string>> onListed)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            api.RetrieveTestList(TestMode.EditMode, root =>
            {
                var names = new List<string>();
                Collect(root, names);
                names.Sort(StringComparer.OrdinalIgnoreCase);

                EditorApplication.delayCall += () => UnityEngine.Object.DestroyImmediate(api);
                onListed?.Invoke(names);
            });
        }

        private static void Collect(ITestAdaptor node, List<string> into)
        {
            if (node == null)
            {
                return;
            }

            if (!node.IsSuite)
            {
                into.Add(node.FullName);
                return;
            }

            foreach (var child in node.Children)
            {
                Collect(child, into);
            }
        }

        private void Record(ScratchpadTestRun run)
        {
            // The store was rebuilt underneath us if anything refreshed during the run, so the feature
            // is looked up again by name rather than held across it.
            var feature = _store.FindFeature(_featureName);
            if (feature == null)
            {
                Debug.LogWarning($"[Scratchpad] \"{_featureName}\" is gone, so its test run was not recorded.");
                return;
            }

            var log = _store.LoadTestLog(feature);
            log.Runs.Insert(0, run);

            if (log.Runs.Count > HistoryLimit)
            {
                log.Runs.RemoveRange(HistoryLimit, log.Runs.Count - HistoryLimit);
            }

            _store.SaveTestLog(feature, log);

            if (_failures.Count == 0)
            {
                Debug.Log($"[Scratchpad] {feature.Name}: {run.Summary}.");
                return;
            }

            var session = feature.Sessions.FirstOrDefault(s => !s.Archived);
            if (session == null)
            {
                Debug.LogWarning($"[Scratchpad] {feature.Name}: {run.Failed} failed, but there is no " +
                                 "session to file them on. They are in the run history.");
                return;
            }

            // Already-open issues for the same test, by the test name on their first line. A test that
            // keeps failing across runs is one problem, not one per run — and skipping rather than
            // replacing means an issue you have edited or replied to is never rewritten underneath you.
            var known = new HashSet<string>(session.Notes.Notes
                .Where(n => n.Kind == NoteKind.Issue && n.IsOutstanding)
                .Select(n => FirstLine(n.Body)));

            var filed = 0;
            var repeats = 0;

            foreach (var failure in _failures)
            {
                if (!known.Add(FirstLine(failure)))
                {
                    repeats++;
                    continue;
                }

                _store.AddNote(session, string.Empty, NoteKind.Issue, failure, NoteSource.Console);
                filed++;
            }

            // A stale issue for a test that now fails differently is left alone rather than tidied
            // away. It is still true that the test failed, and deleting a note nobody asked to delete
            // is the worse mistake.
            var repeated = repeats > 0 ? $" {repeats} already filed." : string.Empty;

            Debug.Log($"[Scratchpad] {feature.Name}: {run.Summary}. Filed {filed} " +
                      $"{(filed == 1 ? "issue" : "issues")} on {session.DisplayStamp}.{repeated}");
        }

        private static string FirstLine(string failure)
        {
            var newline = failure.IndexOf('\n');
            return newline < 0 ? failure : failure[..newline];
        }
    }
}
