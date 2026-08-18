using System;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Networking
{
    /// <summary>
    /// The roles this process is currently playing on the network, published by every running
    /// <see cref="NetworkSession"/>: a dedicated server counts one server, a client one client, and a
    /// host — a server session and a client session in one process — both. Code that only needs to
    /// know "am I on a server / a client / offline?" without a session in hand (log markup, gameplay
    /// events gated by role) reads it here instead of holding a reference to a manager singleton.
    /// </summary>
    [AutoStaticsCleanup]
    public static partial class NetworkRoles
    {
        private static int s_Servers;
        private static int s_Clients;

        /// <summary>Running server sessions in this process.</summary>
        public static int Servers => s_Servers;

        /// <summary>Running client sessions in this process.</summary>
        public static int Clients => s_Clients;

        public static bool IsServer => s_Servers > 0;
        public static bool IsClient => s_Clients > 0;
        public static bool IsHost => IsServer && IsClient;
        public static bool IsOffline => s_Servers == 0 && s_Clients == 0;

        /// <summary>Raised after the counts change (a session started or stopped).</summary>
        public static event Action Changed;

        internal static void Publish(SessionRole role)
        {
            switch (role)
            {
                case SessionRole.Server: s_Servers++; break;
                case SessionRole.Client: s_Clients++; break;
                default: return;
            }

            Changed?.Invoke();
        }

        internal static void Retract(SessionRole role)
        {
            switch (role)
            {
                case SessionRole.Server: s_Servers = Math.Max(0, s_Servers - 1); break;
                case SessionRole.Client: s_Clients = Math.Max(0, s_Clients - 1); break;
                default: return;
            }

            Changed?.Invoke();
        }

        /// <summary>Forgets every published role. Domain reloads and test teardown.</summary>
        public static void Reset()
        {
            bool changed = s_Servers != 0 || s_Clients != 0;
            s_Servers = 0;
            s_Clients = 0;
            if (changed) Changed?.Invoke();
        }
    }
}
