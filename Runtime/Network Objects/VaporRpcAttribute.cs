using System;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// Marks a method on a <see cref="VaporNetworkObject"/> as a remote procedure call. Calling it
    /// serializes its arguments and sends them to the peers named by <see cref="SendTo"/>.
    /// </summary>
    /// <remarks>
    /// The method is declared as a <c>partial</c> with no body, and its body goes in a second partial
    /// named <c>&lt;Name&gt;_Implementation</c>, which is always <c>private</c>. The VaporRpc source
    /// generator (Tools~/VaporRpcGenerator) supplies the other half of each — the send path, and the
    /// declaration that obliges you to write the body.
    /// <code>
    /// [VaporRpc(SendTo.Server)]
    /// private partial void ActivateAbilityRpc(ulong predictedKey);
    ///
    /// private partial void ActivateAbilityRpc_Implementation(ulong predictedKey)
    /// {
    ///     // runs on every peer the rpc reached, including this one when it is a target
    /// }
    /// </code>
    /// The declaring type — and anything containing it — has to be <c>partial</c>. The name has to end
    /// in <c>Rpc</c>, the method has to return void, and it cannot be static, generic, virtual, or
    /// async. Parameters are passed by value; see the generator's README for how each type is
    /// serialized.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public class VaporRpcAttribute : Attribute
    {
        public SendTo SendTo { get; }
        public NetworkDelivery NetworkDelivery { get; }
        
        public VaporRpcAttribute(SendTo sendTo, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced)
        {
            SendTo = sendTo;
            NetworkDelivery = networkDelivery;
        }
    }
}