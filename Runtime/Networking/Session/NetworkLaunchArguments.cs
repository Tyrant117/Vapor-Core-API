using System;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Networking
{
    /// <summary>
    /// The session role this process was launched to play, read from its command line: what a
    /// dedicated-server build is started with, and what an additional editor instance is given so it
    /// plays a role the scene it opens does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the contract between whoever writes a command line and whoever reads it, and both sides
    /// spell it from here on purpose. The last time the two ends of a reflection lookup were spelled
    /// separately, one of them went stale and the miss was silent — a wrong role would fail the same
    /// quiet way, with a client that simply never connects and nothing in the log to say why.
    /// </para>
    /// <para>
    /// Roles are the words, not the enum. <see cref="Vapor.Networking"/> has no notion of "offline" or
    /// "manual" — those belong to whatever framework maps these onto its own startup enum — so this
    /// stops at the raw, lower-cased token and lets the layer above decide what it means.
    /// </para>
    /// </remarks>
    [AutoStaticsCleanup]
    public static partial class NetworkLaunchArguments
    {
        public const string RoleArg = "-vapor-role";
        public const string AddressArg = "-vapor-address";
        public const string PortArg = "-vapor-port";

        public const string HostRole = "host";
        public const string ServerRole = "server";
        public const string ClientRole = "client";
        public const string OfflineRole = "offline";

        private static bool s_Parsed;
        private static string s_Role;
        private static string s_Address;
        private static ushort s_Port;

        /// <summary>The lower-cased role token, or null when the command line carries none.</summary>
        public static string Role
        {
            get
            {
                Parse();
                return s_Role;
            }
        }

        /// <summary>The address to connect to, or null. Null means "whatever the session already defaults to".</summary>
        public static string Address
        {
            get
            {
                Parse();
                return s_Address;
            }
        }

        /// <summary>The port to bind or connect to, or 0. Zero means "whatever the session already defaults to".</summary>
        public static ushort Port
        {
            get
            {
                Parse();
                return s_Port;
            }
        }

        public static bool HasRole => Role != null;

        /// <summary>Reads a <c>-key value</c> pair from this process's command line.</summary>
        public static string Read(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static void Parse()
        {
            if (s_Parsed)
            {
                return;
            }

            // Fixed for the life of the process and rebuilt from the command line on demand, which is
            // what makes clearing these safe.
            s_Parsed = true;
            s_Role = Read(RoleArg)?.Trim().ToLowerInvariant();
            if (s_Role != null && s_Role.Length == 0)
            {
                s_Role = null;
            }

            s_Address = Read(AddressArg);
            if (s_Address != null && s_Address.Trim().Length == 0)
            {
                s_Address = null;
            }

            ushort.TryParse(Read(PortArg), out s_Port);
        }
    }
}
