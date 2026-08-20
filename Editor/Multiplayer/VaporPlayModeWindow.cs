using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Multiplayer
{
    /// <summary>
    /// The one screen this feature has: the additional editor instances, what each one starts as, and
    /// whether it is up.
    /// </summary>
    /// <remarks>
    /// Status is polled rather than pushed. A running instance is a separate operating-system process
    /// and its connection comes and goes with every domain reload on either side, so there is no event
    /// worth subscribing to — twice a second is both cheap and more truthful than a cached flag.
    /// </remarks>
    public class VaporPlayModeWindow : EditorWindow
    {
        private const int RefreshMilliseconds = 500;

        private VisualElement _playerList;
        private readonly List<(int index, Label status, Button action)> _rows = new();

        [MenuItem("Vapor/Multiplayer/Play Mode", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<VaporPlayModeWindow>();
            window.titleContent = new GUIContent("Vapor Play Mode");
            window.minSize = new Vector2(460, 240);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 6;
            root.style.paddingBottom = 6;

            if (VaporPlayModeContext.IsClone)
            {
                root.Add(new HelpBox(
                    $"This is additional instance {VaporPlayModeContext.PlayerIndex}. Players are configured in the main editor.",
                    HelpBoxMessageType.Info));
                return;
            }

            root.Add(BuildSettings());
            root.Add(Separator());

            _playerList = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            root.Add(_playerList);

            root.Add(Separator());
            root.Add(BuildFooter());

            RebuildPlayerList();
            root.schedule.Execute(RefreshStatus).Every(RefreshMilliseconds);
        }

        #region - Sections -

        private VisualElement BuildSettings()
        {
            var settings = VaporPlayModeSettings.instance;
            var box = new VisualElement();

            box.Add(new HelpBox(
                "Instances play as soon as they load and start the session as the role set below, " +
                "overriding the scene's World Settings. The main editor's play mode is not involved.",
                HelpBoxMessageType.Info));

            var stripped = new Toggle("Stripped Editor Mode") { value = settings.StrippedEditorMode };
            stripped.tooltip = "Launch instances in Unity's cut-down clone editor mode. Undocumented, and " +
                               "may change between Unity versions — turn it off if instances come up blank.";
            stripped.RegisterValueChangedCallback(e =>
            {
                settings.StrippedEditorMode = e.newValue;
                settings.SaveIfMain();
            });
            box.Add(stripped);
            return box;
        }

        private VisualElement BuildFooter()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };

            row.Add(new Button(() =>
            {
                VaporPlayModeSettings.instance.AddPlayer();
                RebuildPlayerList();
            })
            { text = "Add Player" });

            row.Add(new Button(LaunchAll) { text = "Launch All", style = { flexGrow = 1 } });
            row.Add(new Button(StopAll) { text = "Stop All", style = { flexGrow = 1 } });
            return row;
        }

        private void RebuildPlayerList()
        {
            _playerList.Clear();
            _rows.Clear();

            var players = VaporPlayModeSettings.instance.Players;
            if (players.Count == 0)
            {
                _playerList.Add(new HelpBox("No additional players yet. Add one below.", HelpBoxMessageType.Info));
                return;
            }

            foreach (var player in players)
            {
                _playerList.Add(BuildPlayerRow(player));
            }

            RefreshStatus();
        }

        private VisualElement BuildPlayerRow(VaporPlayerConfig player)
        {
            var settings = VaporPlayModeSettings.instance;
            var card = new VisualElement
            {
                style =
                {
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = Color.gray, borderBottomColor = Color.gray,
                    borderLeftColor = Color.gray, borderRightColor = Color.gray,
                    marginBottom = 6, paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 4,
                }
            };

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var enabled = new Toggle { value = player.Enabled, tooltip = "Include this player in Launch All." };
            enabled.RegisterValueChangedCallback(e =>
            {
                player.Enabled = e.newValue;
                settings.SaveIfMain();
            });
            header.Add(enabled);

            var name = new TextField { value = player.Name, style = { flexGrow = 1 } };
            name.RegisterValueChangedCallback(e =>
            {
                player.Name = e.newValue;
                settings.SaveIfMain();
            });
            header.Add(name);

            var status = new Label("—") { style = { width = 96, unityTextAlign = TextAnchor.MiddleRight } };
            header.Add(status);

            var action = new Button { text = "Launch", style = { width = 70 } };
            action.clicked += () => ToggleRun(player);
            header.Add(action);

            var remove = new Button(() => RemovePlayer(player)) { text = "×", style = { width = 22 } };
            header.Add(remove);

            card.Add(header);

            var role = new EnumField("Role", player.Role);
            role.RegisterValueChangedCallback(e =>
            {
                player.Role = (VaporPlayerRole)e.newValue;
                settings.SaveIfMain();
            });
            card.Add(role);

            var address = new TextField("Address") { value = player.Address };
            address.tooltip = "Passed as -vapor-address. Empty leaves the scene's own default alone.";
            address.RegisterValueChangedCallback(e =>
            {
                player.Address = e.newValue;
                settings.SaveIfMain();
            });
            card.Add(address);

            var port = new IntegerField("Port") { value = player.Port };
            port.tooltip = "Passed as -vapor-port. Zero leaves the scene's own default alone.";
            port.RegisterValueChangedCallback(e =>
            {
                player.Port = e.newValue;
                settings.SaveIfMain();
            });
            card.Add(port);

            var scene = new ObjectField("Scene")
            {
                objectType = typeof(SceneAsset),
                allowSceneObjects = false,
                value = string.IsNullOrEmpty(player.ScenePath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SceneAsset>(player.ScenePath),
            };
            scene.tooltip = "Opened in the instance before it enters play mode. Empty keeps whatever it had open.";
            scene.RegisterValueChangedCallback(e =>
            {
                player.ScenePath = e.newValue == null ? string.Empty : AssetDatabase.GetAssetPath(e.newValue);
                settings.SaveIfMain();
            });
            card.Add(scene);

            _rows.Add((player.Index, status, action));
            return card;
        }

        private static VisualElement Separator() => new()
        {
            style = { height = 1, backgroundColor = new Color(0f, 0f, 0f, 0.25f), marginTop = 4, marginBottom = 4 }
        };

        #endregion

        #region - Actions -

        private void ToggleRun(VaporPlayerConfig player)
        {
            if (VaporPlayModeLauncher.IsRunning(player.Index))
            {
                VaporPlayModeLauncher.Stop(player.Index);
                return;
            }

            if (!VaporPlayModeLauncher.Launch(player, out string error))
            {
                Debug.LogError($"[Vapor Play Mode] Could not launch {player.Name}: {error}");
            }
        }

        private void LaunchAll()
        {
            foreach (var player in VaporPlayModeSettings.instance.Players)
            {
                if (player.Enabled && !VaporPlayModeLauncher.Launch(player, out string error))
                {
                    Debug.LogError($"[Vapor Play Mode] Could not launch {player.Name}: {error}");
                }
            }
        }

        private static void StopAll()
        {
            foreach (var player in VaporPlayModeSettings.instance.Players)
            {
                VaporPlayModeLauncher.Stop(player.Index);
            }
        }

        private void RemovePlayer(VaporPlayerConfig player)
        {
            bool deleteFolder = VaporVirtualProject.Exists(player.Index) && EditorUtility.DisplayDialog(
                "Remove Player",
                $"Also delete {player.Name}'s instance folder at Library/VaporVP?",
                "Delete Folder", "Keep Folder");

            VaporPlayModeLauncher.Stop(player.Index);

            if (deleteFolder && !VaporVirtualProject.Delete(player.Index, out string error))
            {
                Debug.LogError($"[Vapor Play Mode] {error}");
            }

            VaporPlayModeSettings.instance.RemovePlayer(player.Index);
            RebuildPlayerList();
        }

        private void RefreshStatus()
        {
            foreach (var (index, status, action) in _rows)
            {
                bool running = VaporPlayModeLauncher.IsRunning(index);

                status.text = running ? "running" : "stopped";
                status.style.color = running ? new Color(0.45f, 0.8f, 0.45f) : new Color(0.6f, 0.6f, 0.6f);
                action.text = running ? "Stop" : "Launch";
            }
        }

        #endregion
    }
}
