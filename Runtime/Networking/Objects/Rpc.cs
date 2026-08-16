using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>Who an rpc reaches. Clients may only address <see cref="Server"/>; the server proxies every other target on their behalf.</summary>
    public enum RpcTarget : byte
    {
        /// <summary>The server. From the server: runs locally.</summary>
        Server = 0,
        /// <summary>The object's owner. From the server when the server owns it: runs locally.</summary>
        Owner = 1,
        /// <summary>Everyone except the owner (the server included, when it is not the owner).</summary>
        NotOwner = 2,
        /// <summary>Every client; not the server.</summary>
        NotServer = 3,
        /// <summary>Every client and the server.</summary>
        Everyone = 4,
        /// <summary>The caller only. Never leaves the process.</summary>
        Me = 5,
        /// <summary>Everyone except the caller.</summary>
        NotMe = 6,
    }

    /// <summary>
    /// Anything that can declare <c>[VaporRpc]</c> methods: a <see cref="VaporNetworkObject"/> or a
    /// <see cref="NetworkComponent"/> riding on one. The replicator addresses an rpc by object id and
    /// component id, so both resolve to an object plus a slot.
    /// </summary>
    public interface IRpcHost
    {
        /// <summary>The spawned object that carries this host's traffic — itself, or the component's owner.</summary>
        VaporNetworkObject RpcObject { get; }

        /// <summary>0 for the object itself; the component's id otherwise.</summary>
        ushort RpcComponentId { get; }
    }

    /// <summary>The generated receive stub for one rpc.</summary>
    public delegate void RpcReceiveHandler(IRpcHost target, NetworkReader reader);

    /// <summary>
    /// The global rpc table, keyed by the compile-time hash of declaring type + signature. Filled by
    /// the generated per-type registrars at load; consulted by the replicator on receive.
    /// </summary>
    [NoAutoStaticsCleanup]
    public static class RpcRegistry
    {
        private readonly struct Entry
        {
            public readonly RpcReceiveHandler Handler;
            public readonly string Name;

            public Entry(RpcReceiveHandler handler, string name)
            {
                Handler = handler;
                Name = name;
            }
        }

        private static readonly Dictionary<uint, Entry> s_Handlers = new();

        public static void Register(uint hash, RpcReceiveHandler handler, string name)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (s_Handlers.TryGetValue(hash, out var existing) && existing.Name != name)
            {
                Debug.LogError($"VaporRpc hash collision: {name} and {existing.Name} both hash to [{hash:X8}]. {name} will not be callable.");
                return;
            }

            s_Handlers[hash] = new Entry(handler, name);
        }

        public static bool TryGet(uint hash, out RpcReceiveHandler handler, out string name)
        {
            if (s_Handlers.TryGetValue(hash, out var entry))
            {
                handler = entry.Handler;
                name = entry.Name;
                return true;
            }

            handler = null;
            name = null;
            return false;
        }

        public static int Count => s_Handlers.Count;

        /// <summary>Tests only: forgets every registration.</summary>
        internal static void Clear() => s_Handlers.Clear();
    }
}
