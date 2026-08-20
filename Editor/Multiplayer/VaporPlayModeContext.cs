using Unity.Scripting.LifecycleManagement;
using Vapor.Networking;

namespace VaporEditor.Multiplayer
{
    /// <summary>
    /// What this editor process is: the main editor, or one of the additional instances it launched.
    /// </summary>
    /// <remarks>
    /// An instance reaches the very same <c>Assets</c> folder through a junction, so every
    /// <c>[InitializeOnLoad]</c> in this package runs in both processes and has to know which it is in.
    /// </remarks>
    [AutoStaticsCleanup]
    public static partial class VaporPlayModeContext
    {
        /// <summary>Marks a launched instance and carries its player index.</summary>
        public const string InstanceArg = "-vapor-vp-index";

        /// <summary>Scene the instance opens before it starts playing.</summary>
        public const string SceneArg = "-vapor-scene";

        private static bool s_Parsed;
        private static int s_PlayerIndex;
        private static string s_Scene;

        /// <summary>True in an instance this package launched. False in the main editor.</summary>
        public static bool IsClone
        {
            get
            {
                Parse();
                return s_PlayerIndex > 0;
            }
        }

        /// <summary>This instance's player index, or 0 in the main editor.</summary>
        public static int PlayerIndex
        {
            get
            {
                Parse();
                return s_PlayerIndex;
            }
        }

        /// <summary>Scene asset path this instance should open, or empty.</summary>
        public static string Scene
        {
            get
            {
                Parse();
                return s_Scene;
            }
        }

        /// <summary>
        /// The role token written to an instance's command line. The words come from
        /// <see cref="NetworkLaunchArguments"/>, which is also what reads them back at runtime — the
        /// two ends of this contract are deliberately never spelled twice.
        /// </summary>
        public static string ToArgument(this VaporPlayerRole role) => role switch
        {
            VaporPlayerRole.Host => NetworkLaunchArguments.HostRole,
            VaporPlayerRole.Server => NetworkLaunchArguments.ServerRole,
            VaporPlayerRole.Client => NetworkLaunchArguments.ClientRole,
            VaporPlayerRole.Offline => NetworkLaunchArguments.OfflineRole,
            _ => null,
        };

        private static void Parse()
        {
            if (s_Parsed)
            {
                return;
            }

            // Fixed for the life of the process and rebuilt from the command line on demand, which is
            // what makes clearing these statics safe.
            s_Parsed = true;
            s_Scene = NetworkLaunchArguments.Read(SceneArg) ?? string.Empty;
            int.TryParse(NetworkLaunchArguments.Read(InstanceArg), out s_PlayerIndex);
        }
    }
}
