using System;

namespace Vapor.Networking
{
    /// <summary>
    /// Spatial relevance, supplied by whoever owns the positions (the actor layer's grid). Consulted
    /// only for objects with <see cref="VaporNetworkObject.UsesSpatialRelevance"/>.
    /// </summary>
    public interface ISpatialRelevance
    {
        /// <param name="currentlyObserving">Whether the client already holds the object — lets a provider apply hysteresis so objects at the edge do not flicker.</param>
        bool IsRelevant(VaporNetworkObject networkObject, ulong clientId, bool currentlyObserving);
    }

    /// <summary>
    /// Scales an object's snapshot rate per client — distance tiers, priority classes, anything. 1 is
    /// the authored rate; 0 silences the object for that client.
    /// </summary>
    public interface INetworkLod
    {
        float RateScale(VaporNetworkObject networkObject, ulong clientId);
    }

    /// <summary>
    /// The public face of interest management on a networked world: which channels each client is
    /// subscribed to, and the spatial provider. Everything else — who knows what, when to spawn and
    /// despawn — is the replicator's business and follows from these.
    /// </summary>
    public sealed class InterestManager
    {
        private readonly Replicator _replicator;

        internal InterestManager(Replicator replicator) => _replicator = replicator;

        /// <summary>Installed by the actor layer once positions exist. Null means spatial objects are treated as global.</summary>
        public ISpatialRelevance Spatial { get; set; }

        /// <summary>Per-client snapshot rate scaling. Null means every object sends at its authored rate.</summary>
        public INetworkLod Lod { get; set; }

        /// <summary>
        /// Server: bytes of unreliable snapshot traffic per client per tick before lower-priority
        /// objects wait for the next tick. Defaults to two packets' worth.
        /// </summary>
        public int SnapshotBudgetBytesPerTick { get; set; } = -1;

        /// <summary>Server: lets a client see the objects in a channel. Returns true when this was new.</summary>
        public bool Subscribe(ulong clientId, InterestGroup group) => _replicator.Subscribe(clientId, group);

        /// <summary>Server: the reverse. Objects visible only through the channel are despawned on the client.</summary>
        public bool Unsubscribe(ulong clientId, InterestGroup group) => _replicator.Unsubscribe(clientId, group);

        public bool IsSubscribed(ulong clientId, InterestGroup group) => _replicator.IsSubscribed(clientId, group);

        /// <summary>Server: whether the client currently holds an instance of the object.</summary>
        public bool IsObserving(ulong clientId, VaporNetworkObject networkObject) =>
            networkObject != null && _replicator.IsObserving(clientId, networkObject.NetworkObjectId);

        /// <summary>
        /// Server: re-evaluates spatial relevance for every spatial object against every client. The
        /// spatial provider calls this (or the per-object overload) when positions have moved enough
        /// to matter — typically once per tick from the grid.
        /// </summary>
        public void RefreshSpatial() => _replicator.RefreshSpatialRelevance();

        public void Refresh(VaporNetworkObject networkObject) => _replicator.RefreshRelevance(networkObject);
    }
}
