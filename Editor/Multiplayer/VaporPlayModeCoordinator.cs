using System;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VaporEditor.Multiplayer
{
    /// <summary>
    /// What a launched instance does with itself: open the scene it was given, and play. The main
    /// editor's own play mode is not consulted and does not matter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no link back to the main editor, and that is the design rather than an omission. An
    /// instance is told what to be on its command line, which <c>GameModeBase</c> reads through
    /// <c>SessionLaunchArguments</c> when it starts the session — so an instance is self-sufficient the
    /// moment it loads, and needs nobody to tell it when to begin. A dedicated server instance and its
    /// clients form a working session with the main editor sitting in edit mode, or closed.
    /// </para>
    /// <para>
    /// It plays on <b>every</b> domain load, not just the first. A recompile reloads the domain, and an
    /// instance that came back to edit mode and stayed there would be a dead window that still looks
    /// alive. The cost of that choice is that an instance cannot be paused from inside — stopping play
    /// mode in one only makes it start again. Close it instead.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    [InitializeOnLoad]
    public static class VaporPlayModeCoordinator
    {
        private const string SceneOpenedKey = "Vapor.PlayMode.SceneOpened";

        static VaporPlayModeCoordinator()
        {
            if (VaporPlayModeContext.IsClone)
            {
                EditorApplication.update += TickInstance;
            }
            else
            {
                EditorApplication.quitting += StopEveryInstance;
            }
        }

        /// <summary>
        /// Waits for the editor to finish loading before touching anything. Entering play mode while
        /// the asset database is still catching up is how an instance comes up on the wrong scene, or
        /// simply refuses.
        /// </summary>
        private static void TickInstance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            EditorApplication.update -= TickInstance;

            // Already playing: this load is a domain reload from mid-game, and the scene must not be
            // reopened underneath the running game. Focus still has to be reclaimed — the reload puts
            // it back wherever the layout wants it.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                FocusGameView();
                return;
            }

            OpenAssignedScene();
            FocusGameView();
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Brings this instance's Game view to the front and focuses it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what makes a launched instance answer the mouse and keyboard, and it is not
        /// optional. The Input System's default <c>PointersAndKeyboardsRespectGameViewFocus</c> routes
        /// pointer and keyboard input to the editor rather than to player code whenever the Game view
        /// is not the focused <c>EditorWindow</c> — focused within that process, which has nothing to
        /// do with which window the operating system put in front.
        /// </para>
        /// <para>
        /// A fresh instance focuses whatever its layout lists first, usually the Scene view, so the
        /// game reads no input at all. Clicking to the main editor and back is exactly the click that
        /// gives the Game view focus, which is why doing that appears to fix it. The session keeps
        /// running throughout — only input was ever blocked — which is the confusing part: a client
        /// connects and replicates perfectly while ignoring every key you press.
        /// </para>
        /// </remarks>
        private static void FocusGameView()
        {
            try
            {
                var type = FindGameViewType();
                if (type == null)
                {
                    return;
                }

                // Focus the one the layout already has rather than creating a second; only open one
                // when the layout has none at all.
                var existing = Resources.FindObjectsOfTypeAll(type);
                if (existing is { Length: > 0 })
                {
                    EditorWindow.FocusWindowIfItsOpen(type);
                }
                else
                {
                    EditorWindow.GetWindow(type);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Vapor Play Mode] Player {VaporPlayModeContext.PlayerIndex} could not focus the Game view: {e.Message}");
            }
        }

        /// <summary>
        /// <c>UnityEditor.GameView</c> is internal, so it has to be found rather than referenced.
        /// </summary>
        /// <remarks>
        /// Asked of <see cref="TypeCache"/> instead of a named assembly on purpose. Which editor
        /// assembly holds a given window has moved between Unity versions, and an assembly-qualified
        /// lookup that guesses wrong returns <c>null</c> rather than throwing — so the focus would
        /// simply never happen, and the only symptom would be the input problem this exists to fix,
        /// looking exactly like it was never fixed at all.
        /// </remarks>
        private static Type FindGameViewType()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<EditorWindow>())
            {
                if (type.Name == "GameView" && type.Namespace == "UnityEditor")
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Opens the scene this instance was launched with, once per process. Guarded by
        /// <see cref="SessionState"/> rather than a static field so the domain reloads that follow do
        /// not reopen it.
        /// </summary>
        private static void OpenAssignedScene()
        {
            string scene = VaporPlayModeContext.Scene;
            if (string.IsNullOrEmpty(scene) || SessionState.GetBool(SceneOpenedKey, false))
            {
                return;
            }

            SessionState.SetBool(SceneOpenedKey, true);

            try
            {
                if (EditorSceneManager.GetActiveScene().path != scene)
                {
                    EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Vapor Play Mode] Player {VaporPlayModeContext.PlayerIndex} could not open '{scene}': {e.Message}");
            }
        }

        /// <summary>
        /// An orphaned instance keeps reading a shared <c>Library</c> whose owner is gone, and comes
        /// back to a project it no longer matches.
        /// </summary>
        private static void StopEveryInstance()
        {
            foreach (var player in VaporPlayModeSettings.instance.Players)
            {
                VaporPlayModeLauncher.Stop(player.Index);
            }
        }
    }
}
