using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Multiplayer
{
    /// <summary>
    /// What a launched player starts as. Written to the clone's command line as <c>-vapor-role</c>,
    /// which is the same argument a built player reads, so a role behaves identically in an editor
    /// instance and in a standalone build.
    /// </summary>
    public enum VaporPlayerRole : byte
    {
        /// <summary>Start nothing; the scene's own picker decides.</summary>
        Manual,
        Host,
        Server,
        Client,
        Offline,
    }

    /// <summary>One additional editor instance: what it is, where it connects, and what it opens.</summary>
    [Serializable]
    public class VaporPlayerConfig
    {
        /// <summary>Stable 1-based identity. The main editor is 0, so the first extra player is 1.</summary>
        public int Index = 1;

        /// <summary>Shown in the window and passed as the instance's window title.</summary>
        public string Name = "Player 1";

        /// <summary>Included when the window launches "all". Off leaves the clone folder in place, unlaunched.</summary>
        public bool Enabled = true;

        public VaporPlayerRole Role = VaporPlayerRole.Client;

        /// <summary>Empty means "whatever the scene defaults to" — no <c>-vapor-address</c> is passed.</summary>
        public string Address = "127.0.0.1";

        /// <summary>0 means "whatever the scene defaults to" — no <c>-vapor-port</c> is passed.</summary>
        public int Port;

        /// <summary>Asset path of a scene to open before entering play mode. Empty keeps whatever the instance last had open.</summary>
        public string ScenePath = string.Empty;
    }

    /// <summary>
    /// The player list and the one port the control link runs on, stored in <c>ProjectSettings</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately in <c>ProjectSettings</c> rather than <c>UserSettings</c>: a clone reaches the main
    /// project's <c>ProjectSettings</c> through a junction, so it reads exactly the same file the main
    /// editor wrote, with no copy to keep in sync. Only the main editor ever saves it — see
    /// <see cref="VaporPlayModeContext.IsClone"/>.
    /// </remarks>
    [FilePath("ProjectSettings/VaporPlayMode.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class VaporPlayModeSettings : ScriptableSingleton<VaporPlayModeSettings>
    {
        /// <summary>Launch instances with Unity's stripped clone editor mode instead of a normal editor window.</summary>
        [SerializeField] private bool _strippedEditorMode;

        [SerializeField] private List<VaporPlayerConfig> _players = new();

        public bool StrippedEditorMode
        {
            get => _strippedEditorMode;
            set => _strippedEditorMode = value;
        }

        public List<VaporPlayerConfig> Players => _players;

        public VaporPlayerConfig Find(int index) => _players.Find(p => p.Index == index);

        /// <summary>Appends a player with the lowest free index and a role that continues the pattern.</summary>
        public VaporPlayerConfig AddPlayer()
        {
            int index = 1;
            while (_players.Exists(p => p.Index == index))
            {
                index++;
            }

            var player = new VaporPlayerConfig
            {
                Index = index,
                Name = $"Player {index}",
                Role = VaporPlayerRole.Client,
            };
            _players.Add(player);
            SaveIfMain();
            return player;
        }

        public void RemovePlayer(int index)
        {
            _players.RemoveAll(p => p.Index == index);
            SaveIfMain();
        }

        /// <summary>Writes the file, unless this process is a clone — a clone must never author the shared settings.</summary>
        public void SaveIfMain()
        {
            if (VaporPlayModeContext.IsClone)
            {
                return;
            }

            Save(true);
        }
    }
}
