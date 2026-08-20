using System;
using System.Diagnostics;
using System.IO;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VaporEditor.Multiplayer
{
    /// <summary>
    /// The folder an additional editor instance opens: a project whose <c>Assets</c> and
    /// <c>ProjectSettings</c> are NTFS junctions back to the real ones, and whose own <c>Library</c>
    /// holds nothing but per-instance bookkeeping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It lives at <c>Library/VaporVP/player&lt;n&gt;</c>, and the depth is load-bearing: the instance
    /// is launched with <c>-library-redirect ../..</c>, which resolves against its own project path and
    /// therefore has to land exactly on the main project's <c>Library</c>. Two levels under
    /// <c>Library</c>, no more and no fewer.
    /// </para>
    /// <para>
    /// That redirect is the whole reason this is not a second copy of the project. The instance imports
    /// nothing and compiles nothing — it reads the assemblies and artifacts the main editor already
    /// built, read-only — so it opens in seconds and costs a few hundred kilobytes on disk instead of
    /// the several gigabytes a real clone would.
    /// </para>
    /// <para>
    /// <b>Deleting one is the dangerous operation, not creating it.</b> A recursive delete that follows
    /// a junction deletes the project's real <c>Assets</c> folder. Every removal here unlinks the
    /// junctions first with <c>rmdir</c> (which removes the link and never its target), then refuses to
    /// recurse at all if any reparse point is still standing.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class VaporVirtualProject
    {
        private const string FolderName = "VaporVP";
        private const string PlayerPrefix = "player";

        /// <summary>The real project folder — the parent of <c>Assets</c>.</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');

        /// <summary>Where every instance folder lives.</summary>
        public static string Root => $"{ProjectRoot}/Library/{FolderName}";

        public static string PathFor(int index) => $"{Root}/{PlayerPrefix}{index}";

        /// <summary>True when the folder exists and both junctions are in place.</summary>
        public static bool Exists(int index)
        {
            string path = PathFor(index);
            return Directory.Exists(path)
                   && IsLink($"{path}/Assets")
                   && IsLink($"{path}/ProjectSettings");
        }

        /// <summary>
        /// Creates the instance folder, or repairs one whose junctions went missing. Cheap and
        /// idempotent — safe to call before every launch.
        /// </summary>
        public static bool Prepare(int index, out string error)
        {
            error = null;
            string root = ProjectRoot;
            if (string.IsNullOrEmpty(root))
            {
                error = "Could not resolve the project root.";
                return false;
            }

            string path = PathFor(index);
            try
            {
                Directory.CreateDirectory(path);

                // Its own, never shared: the instance writes its bookkeeping here while the heavy
                // Library it actually reads from is redirected to the main project's.
                Directory.CreateDirectory($"{path}/Library");
                Directory.CreateDirectory($"{path}/Logs");
                Directory.CreateDirectory($"{path}/Temp");
                Directory.CreateDirectory($"{path}/UserSettings");

                // Empty on purpose, paired with -noUpm: the package manager never runs in the instance,
                // so a second resolver cannot race the main editor over packages-lock.json.
                Directory.CreateDirectory($"{path}/Packages");

                if (!EnsureLink($"{path}/Assets", $"{root}/Assets", out error))
                {
                    return false;
                }

                if (!EnsureLink($"{path}/ProjectSettings", $"{root}/ProjectSettings", out error))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Removes an instance folder. Unlinks the junctions first and refuses to recurse while any
        /// reparse point remains, so a mistake here cannot reach the real project.
        /// </summary>
        public static bool Delete(int index, out string error)
        {
            error = null;
            string path = PathFor(index);
            if (!Directory.Exists(path))
            {
                return true;
            }

            // The path has to be one of ours, spelled the way we spell it, before anything is removed.
            string expected = $"{Root}/{PlayerPrefix}{index}";
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            {
                error = $"Refusing to delete {path}: not the expected instance folder.";
                return false;
            }

            try
            {
                Unlink($"{path}/Assets");
                Unlink($"{path}/ProjectSettings");

                if (ContainsReparsePoint(path, out string offender))
                {
                    error = $"Refusing to delete {path}: a link is still standing at {offender}. Remove it by hand.";
                    return false;
                }

                Directory.Delete(path, true);
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }

            return true;
        }

        #region - Links -

        private static bool IsLink(string path)
        {
            try
            {
                return Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool EnsureLink(string link, string target, out string error)
        {
            error = null;
            if (IsLink(link))
            {
                return true;
            }

            if (Directory.Exists(link))
            {
                // A real directory where a junction belongs: something created it by hand. Say so
                // rather than deleting a folder whose contents we did not put there.
                error = $"{link} exists but is not a link. Remove it and try again.";
                return false;
            }

            if (!Directory.Exists(target))
            {
                error = $"Link target {target} does not exist.";
                return false;
            }

            // A junction, not a symlink: junctions need neither administrator rights nor Developer Mode,
            // which is the difference between this working on a plain Windows install and not.
            if (!RunCmd($"mklink /J \"{Native(link)}\" \"{Native(target)}\"", out string output))
            {
                error = $"Could not link {link} to {target}. {output}";
                return false;
            }

            if (IsLink(link))
            {
                return true;
            }

            error = $"{link} was not created.";
            return false;
        }

        private static void Unlink(string link)
        {
            if (!IsLink(link))
            {
                return;
            }

            try
            {
                // The NON-recursive overload on a reparse point removes the link itself and never
                // descends into the target — it is a plain RemoveDirectory. The recursive overload is
                // the one that must never be pointed at a junction.
                Directory.Delete(link, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Vapor Play Mode] Could not unlink {link}: {e.Message}");
            }
        }

        private static bool ContainsReparsePoint(string path, out string offender)
        {
            offender = null;
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
                {
                    if (IsLink(directory))
                    {
                        offender = directory;
                        return true;
                    }

                    if (ContainsReparsePoint(directory, out offender))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable is not provably safe, so treat it as an offender rather than recursing past it.
                offender = path;
                return true;
            }

            return false;
        }

        private static string Native(string path) => path.Replace('/', '\\');

        private static bool RunCmd(string command, out string output)
        {
            var info = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                using var process = Process.Start(info);
                if (process == null)
                {
                    output = "cmd.exe did not start.";
                    return false;
                }

                output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
                process.WaitForExit(10000);
                return process.HasExited && process.ExitCode == 0;
            }
            catch (Exception e)
            {
                output = e.Message;
                Debug.LogException(e);
                return false;
            }
        }

        #endregion
    }
}
