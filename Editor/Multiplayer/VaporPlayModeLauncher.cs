using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using Vapor.Networking;
using Debug = UnityEngine.Debug;

namespace VaporEditor.Multiplayer
{
    /// <summary>
    /// Starts and stops the additional editor instances, and remembers which are running across the
    /// domain reloads that entering play mode and recompiling both cause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The argument list is the entire feature. <c>-library-redirect ../..</c> with <c>-readonly</c>
    /// points the instance at the main project's already-built <c>Library</c>, so it imports nothing
    /// and compiles nothing; <c>-noUpm</c> keeps a second package manager from touching the shared
    /// project; <c>-forgetProjectPath</c> keeps these throwaway folders out of the Hub's recent list.
    /// Together they are the difference between an instance that opens in seconds and a second full
    /// copy of the project that does not.
    /// </para>
    /// <para>
    /// Process ids go in <see cref="SessionState"/> rather than a static field, because a static field
    /// is gone the moment the user presses Play — which is exactly when the window most needs to know
    /// what is still running.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class VaporPlayModeLauncher
    {
        private const string PidKey = "Vapor.PlayMode.Pid.";

        /// <summary>Unity's own stripped-down instance editor mode. Opt-in: it is not a documented flag.</summary>
        private const string StrippedMode = "com.unity.mppm.clone";

        /// <summary>The process id of a running instance, or 0.</summary>
        public static int ProcessIdOf(int index) => SessionState.GetInt(PidKey + index, 0);

        public static bool IsRunning(int index)
        {
            int pid = ProcessIdOf(index);
            if (pid == 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // No such process: it exited while we were not looking.
            }
            catch (InvalidOperationException)
            {
            }

            SessionState.EraseInt(PidKey + index);
            return false;
        }

        /// <summary>Prepares the instance folder if needed and launches it. Does nothing if already running.</summary>
        public static bool Launch(VaporPlayerConfig player, out string error)
        {
            error = null;
            if (player == null)
            {
                error = "No player.";
                return false;
            }

            if (IsRunning(player.Index))
            {
                return true;
            }

            if (!VaporVirtualProject.Prepare(player.Index, out error))
            {
                return false;
            }

            string unity = EditorApplication.applicationPath;
            string projectPath = VaporVirtualProject.PathFor(player.Index);
            string arguments = BuildArguments(player, projectPath);

            var info = new ProcessStartInfo(unity, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = VaporVirtualProject.ProjectRoot,
            };

            try
            {
                var process = Process.Start(info);
                if (process == null)
                {
                    error = "The editor process did not start.";
                    return false;
                }

                SessionState.SetInt(PidKey + player.Index, process.Id);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Terminates the instance. Blunt on purpose, and safe: it holds no lock, it opened the shared
        /// <c>Library</c> read-only, and its own folder is disposable — there is nothing a polite
        /// shutdown would have saved, so there is no channel back to it to ask.
        /// </summary>
        public static void Stop(int index)
        {
            if (!IsRunning(index))
            {
                return;
            }

            try
            {
                using var process = Process.GetProcessById(ProcessIdOf(index));
                process.Kill();
                process.WaitForExit(3000);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Vapor Play Mode] Could not stop player {index}: {e.Message}");
            }

            SessionState.EraseInt(PidKey + index);
        }

        /// <summary>The command line an instance is launched with. Public so the window can show it.</summary>
        public static string BuildArguments(VaporPlayerConfig player, string projectPath)
        {
            var args = new List<string>
            {
                "-projectPath", projectPath,

                // Resolves against projectPath, so the instance folder MUST sit two levels under the
                // main Library for this to land on it. See VaporVirtualProject.
                "-library-redirect", "../..",
                "-readonly",

                "-noUpm",
                "-upmRestorePackages",
                "-noLaunchScreen",
                "-forgetProjectPath",
                "-no-cloud-project-bind-popup",
                "-suppressDefaultMenuEntries",

                // The main editor already watches the real Assets folder; a second watcher on the same
                // tree through a junction is pure cost.
                "-DisableDirectoryMonitor",

                "-name", player.Name,
                "-logFile", $"{projectPath}/Logs/Editor.log",
            };

            if (VaporPlayModeSettings.instance.StrippedEditorMode)
            {
                args.Add("-editor-mode");
                args.Add(StrippedMode);
            }

            args.Add(VaporPlayModeContext.InstanceArg);
            args.Add(player.Index.ToString());

            if (!string.IsNullOrEmpty(player.ScenePath))
            {
                args.Add(VaporPlayModeContext.SceneArg);
                args.Add(player.ScenePath);
            }

            // Read back at runtime by GameModeBase, through SessionLaunchArguments. Omitting an
            // argument is meaningful: it leaves the scene's own authored setting standing.
            string role = player.Role.ToArgument();
            if (role != null)
            {
                args.Add(NetworkLaunchArguments.RoleArg);
                args.Add(role);
            }

            if (!string.IsNullOrEmpty(player.Address))
            {
                args.Add(NetworkLaunchArguments.AddressArg);
                args.Add(player.Address);
            }

            if (player.Port > 0)
            {
                args.Add(NetworkLaunchArguments.PortArg);
                args.Add(player.Port.ToString());
            }

            return Join(args);
        }

        private static string Join(List<string> args)
        {
            var builder = new StringBuilder();
            foreach (string arg in args)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                if (arg.IndexOf(' ') >= 0)
                {
                    builder.Append('"').Append(arg).Append('"');
                }
                else
                {
                    builder.Append(arg);
                }
            }

            return builder.ToString();
        }
    }
}
