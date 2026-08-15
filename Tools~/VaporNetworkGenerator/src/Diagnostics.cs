using Microsoft.CodeAnalysis;

namespace Vapor.Network.SourceGenerator
{
    /// <summary>
    /// Rpc diagnostics are errors: an rpc that fails to generate is a method that silently stops
    /// crossing the network. Formatter diagnostics that only mean "fell back to something slower or
    /// narrower" are warnings.
    /// </summary>
    internal static class Diagnostics
    {
        private const string RpcCategory = "VaporRpc";
        private const string FormatterCategory = "VaporNetworkFormatter";

        #region Rpc

        public static readonly DiagnosticDescriptor NotAnRpcHost = new DiagnosticDescriptor(
            "VNET001",
            "[VaporRpc] method is not on a VaporNetworkObject or NetworkComponent",
            "'{0}' is marked [VaporRpc] but '{1}' derives from neither VaporNetworkObject nor NetworkComponent. Rpcs are addressed through an object (and optionally one of its components), so they can only be declared on one of those.",
            RpcCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TypeNotPartial = new DiagnosticDescriptor(
            "VNET002",
            "Type declaring a [VaporRpc] method is not partial",
            "'{0}' declares [VaporRpc] methods, so it and every type containing it must be declared 'partial' for the send path to be generated into it.",
            RpcCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MethodNotPartial = new DiagnosticDescriptor(
            "VNET003",
            "[VaporRpc] method must be a partial declaration",
            "'{0}' must be declared as a partial method with no body — the generator writes the send path into it. Move the body to 'private partial void {1}(...)' and leave '[VaporRpc] private partial void {2}(...);' behind.",
            RpcCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidSignature = new DiagnosticDescriptor(
            "VNET004",
            "[VaporRpc] method has an unsupported signature",
            "'{0}': {1}",
            RpcCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NameMustEndInRpc = new DiagnosticDescriptor(
            "VNET005",
            "[VaporRpc] method name must end with 'Rpc'",
            "'{0}' is marked [VaporRpc], so its name must end with 'Rpc'.",
            RpcCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedParameter = new DiagnosticDescriptor(
            "VNET006",
            "[VaporRpc] parameter cannot be serialized",
            "'{0}': parameter '{1}' {2}",
            RpcCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DegenerateHash = new DiagnosticDescriptor(
            "VNET008",
            "[VaporRpc] method hashed to zero",
            "The rpc id for '{0}' hashed to 0, which the runtime reserves. Rename the method.",
            RpcCategory,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        #endregion

        #region Formatters

        public static readonly DiagnosticDescriptor FormatterTypeNotPartial = new DiagnosticDescriptor(
            "VNET101",
            "[NetworkSerializable] type is not partial",
            "'{0}' asks for a generated network formatter, so it and every type containing it must be declared 'partial'. No formatter was generated; the runtime will throw when the type reaches the wire unless one is registered by hand.",
            FormatterCategory,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FormatterNeedsConstructor = new DiagnosticDescriptor(
            "VNET102",
            "[NetworkSerializable] class has no parameterless constructor",
            "'{0}' has no parameterless constructor (any accessibility), so the receiving side cannot construct it. No formatter was generated.",
            FormatterCategory,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FormatterMemberSkipped = new DiagnosticDescriptor(
            "VNET103",
            "Network formatter skipped a member",
            "'{0}': member '{1}' {2}",
            FormatterCategory,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FormatterUnsupportedType = new DiagnosticDescriptor(
            "VNET104",
            "[NetworkSerializable] cannot be generic, abstract, static or an interface",
            "'{0}' cannot have a generated network formatter: {1}",
            FormatterCategory,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        #endregion
    }
}
