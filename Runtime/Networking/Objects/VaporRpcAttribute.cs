using System;

namespace Vapor.Networking
{
    /// <summary>
    /// Marks a partial method on a <see cref="VaporNetworkObject"/> or <see cref="NetworkComponent"/>
    /// as an rpc. The Vapor network generator writes the send path into the declaration and declares
    /// <c>&lt;Name&gt;_Implementation</c>, whose body you write; the body runs on every peer the rpc
    /// reaches, including the caller when it is a target.
    /// </summary>
    /// <example>
    /// <code>
    /// public partial class Ability : VaporNetworkObject
    /// {
    ///     [VaporRpc(RpcTarget.Server)]
    ///     private partial void ActivateRpc(ulong predictedKey);
    ///
    ///     private partial void ActivateRpc_Implementation(ulong predictedKey) { /* runs on the server */ }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class VaporRpcAttribute : Attribute
    {
        public VaporRpcAttribute(RpcTarget target, Delivery delivery = Delivery.ReliableSequenced)
        {
            Target = target;
            Delivery = delivery;
        }

        public RpcTarget Target { get; }
        public Delivery Delivery { get; }
    }

    /// <summary>
    /// Argument helpers the generated rpc code calls for types that need the receiving object's
    /// world to resolve — network object references travel as ids and come back as instances.
    /// </summary>
    public static class RpcArguments
    {
        public static void WriteObject(NetworkWriter writer, VaporNetworkObject value) =>
            writer.WriteVarUInt64(value != null && value.IsSpawned ? value.NetworkObjectId : 0);

        /// <summary>Resolves against the receiving host's world; null when the object is gone or the id was 0.</summary>
        public static VaporNetworkObject ReadObject(IRpcHost host, NetworkReader reader)
        {
            ulong id = reader.ReadVarUInt64();
            var world = host?.RpcObject?.World ?? NetworkWorld.Current;
            return id != 0 && world != null && world.TryGet(id, out var found) ? found : null;
        }

        public static void WriteComponent(NetworkWriter writer, NetworkComponent value)
        {
            var owner = value?.Owner;
            writer.WriteVarUInt64(owner != null && owner.IsSpawned ? owner.NetworkObjectId : 0);
            writer.WriteVarUInt32(value?.ComponentId ?? 0);
        }

        public static NetworkComponent ReadComponent(IRpcHost host, NetworkReader reader)
        {
            ulong id = reader.ReadVarUInt64();
            ushort componentId = checked((ushort)reader.ReadVarUInt32());
            var world = host?.RpcObject?.World ?? NetworkWorld.Current;
            if (id == 0 || world == null || !world.TryGet(id, out var found)) return null;
            return found.GetComponentById(componentId);
        }
    }
}
