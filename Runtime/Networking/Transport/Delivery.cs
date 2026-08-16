namespace Vapor.Networking
{
    /// <summary>
    /// How a payload is allowed to arrive. Four classes are enough: everything a replicator sends is
    /// either "must arrive, in order" (spawns, sync, most rpcs), "latest wins" (transform snapshots),
    /// or "fire and forget" (cosmetic hints).
    /// </summary>
    /// <remarks>
    /// There is deliberately no unordered-reliable class. UTP has no such pipeline, Steam's would map
    /// to reliable-sequenced anyway, and nothing in the replicator wants it.
    /// </remarks>
    public enum Delivery : byte
    {
        /// <summary>May be lost, duplicated or reordered. Cheapest.</summary>
        Unreliable = 0,

        /// <summary>May be lost; anything older than the newest delivered packet is dropped. Snapshots.</summary>
        UnreliableSequenced = 1,

        /// <summary>Arrives, in order, or the connection is dead. Payload bounded by the transport MTU.</summary>
        ReliableSequenced = 2,

        /// <summary>Reliable and ordered, and larger than one datagram. Full spawn state.</summary>
        ReliableFragmentedSequenced = 3,
    }

    public static class DeliveryExtensions
    {
        public const int Count = 4;

        public static bool IsReliable(this Delivery delivery) =>
            delivery == Delivery.ReliableSequenced || delivery == Delivery.ReliableFragmentedSequenced;

        public static bool IsSequenced(this Delivery delivery) => delivery != Delivery.Unreliable;

        public static bool IsFragmented(this Delivery delivery) => delivery == Delivery.ReliableFragmentedSequenced;
    }
}
