using Microsoft.CodeAnalysis;

namespace Vapor.Rpc.SourceGenerator
{
    /// <summary>
    /// Every diagnostic here is an error. An rpc that fails to generate is not a slow rpc, it is a
    /// method that silently stops crossing the network — so nothing degrades quietly.
    /// </summary>
    internal static class VaporRpcDiagnostics
    {
        private const string Category = "VaporRpc";

        public static readonly DiagnosticDescriptor NotANetworkObject = new DiagnosticDescriptor(
            "VRPC001",
            "[VaporRpc] method is not on a VaporNetworkObject",
            "'{0}' is marked [VaporRpc] but '{1}' does not derive from VaporNetworkObject. Rpcs are sent and dispatched through VaporNetworkObject, so they can only be declared on one.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// The send path is emitted as the implementing half of a partial method declared inside the
        /// type, which the compiler only accepts in a partial type.
        /// </summary>
        public static readonly DiagnosticDescriptor TypeNotPartial = new DiagnosticDescriptor(
            "VRPC002",
            "Type declaring a [VaporRpc] method is not partial",
            "'{0}' declares [VaporRpc] methods, so it and every type containing it must be declared 'partial' for the send path to be generated into it.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// The one diagnostic every rpc written against the old IL weaver will hit, so its message is
        /// the migration instruction.
        /// </summary>
        public static readonly DiagnosticDescriptor MethodNotPartial = new DiagnosticDescriptor(
            "VRPC003",
            "[VaporRpc] method must be a partial declaration",
            "'{0}' must be declared as a partial method with no body — the generator writes the send path into it. Move the body to 'private partial void {1}(...)' and leave '[VaporRpc] private partial void {2}(...);' behind.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidSignature = new DiagnosticDescriptor(
            "VRPC004",
            "[VaporRpc] method has an unsupported signature",
            "'{0}': {1}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// A call that goes over the network should read like one at the call site, and the suffix is
        /// what makes the generated <c>_Implementation</c> and <c>_Receive</c> names read sensibly.
        /// </summary>
        public static readonly DiagnosticDescriptor NameMustEndInRpc = new DiagnosticDescriptor(
            "VRPC005",
            "[VaporRpc] method name must end with 'Rpc'",
            "'{0}' is marked [VaporRpc], so its name must end with 'Rpc'.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedParameter = new DiagnosticDescriptor(
            "VRPC006",
            "[VaporRpc] parameter cannot be serialized",
            "'{0}': parameter '{1}' {2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SerializableNeedsConstructor = new DiagnosticDescriptor(
            "VRPC007",
            "INetworkSerializable parameter needs a parameterless constructor",
            "'{0}': parameter '{1}' of type '{2}' implements INetworkSerializable but has no parameterless constructor, so the receiving side cannot construct it.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Vapor resolves an rpc's targets from the attribute alone — there is no <c>RpcParams</c>
        /// equivalent to carry a caller-supplied client list. The runtime throws
        /// <c>ArgumentOutOfRangeException</c> from inside the send if this ever reaches it, so it is
        /// worth catching at build time instead.
        /// </summary>
        public static readonly DiagnosticDescriptor UnsupportedSendTo = new DiagnosticDescriptor(
            "VRPC010",
            "[VaporRpc] does not support SendTo.SpecifiedInParams",
            "'{0}': SendTo.SpecifiedInParams needs a per-call client list, which VaporRpc has no way to pass. Name the targets in the attribute, or send to each client through NetworkMessages.SendToGroup.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// The one warning here, and the only diagnostic that is not an error.
        /// </summary>
        /// <remarks>
        /// A managed argument type that is not a packet, an INetworkSerializable, or a network object
        /// reference reaches <c>NetworkVariableSerialization&lt;T&gt;</c>, and Netcode's post-processor
        /// only ever generates a serializer for a managed type through INetworkSerializable — so there
        /// is no arrangement of attributes that makes one work. It throws on the first send.
        /// <para>
        /// It stays a warning rather than an error because <c>UserNetworkVariableSerialization&lt;T&gt;</c>
        /// is a legitimate escape hatch that this generator cannot see being used.
        /// </para>
        /// </remarks>
        public static readonly DiagnosticDescriptor UnserializableManagedParameter = new DiagnosticDescriptor(
            "VRPC009",
            "[VaporRpc] parameter has no serialization and will throw when sent",
            "'{0}': parameter '{1}' is of managed type '{2}', which has no generated serialization and will throw on the first send. Make it implement INetworkPacket or INetworkSerializable, use a value type, or assign UserNetworkVariableSerialization<{2}> yourself.",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// Zero is how the runtime's rpc table spells "absent", so an rpc that hashes to it would be
        /// unreachable rather than merely unlucky.
        /// </summary>
        public static readonly DiagnosticDescriptor DegenerateHash = new DiagnosticDescriptor(
            "VRPC008",
            "[VaporRpc] method hashed to zero",
            "The rpc id for '{0}' hashed to 0, which the runtime reserves. Rename the method.",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
