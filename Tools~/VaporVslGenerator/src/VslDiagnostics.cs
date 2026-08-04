using Microsoft.CodeAnalysis;

namespace Vapor.Vsl.SourceGenerator
{
    internal static class VslDiagnostics
    {
        private const string Category = "Vsl";

        /// <summary>
        /// The generated formatter is nested inside the type so it can reach private
        /// <c>[SerializeField]</c> members, which requires the type to be partial.
        /// </summary>
        public static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor(
            "VSL001",
            "VSL type is not partial",
            "'{0}' takes part in VSL serialization but is not partial, so no formatter can be generated for it. It still serializes correctly through reflection, just more slowly. Add 'partial' to the type (and to any type containing it) to get the generated formatter.",
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PropertyNeedsAccessors = new DiagnosticDescriptor(
            "VSL002",
            "VSL property needs a getter and a setter",
            "'{0}' is marked [VslSerialize] but does not have both a getter and a setter, so it cannot be serialized.",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoParameterlessConstructor = new DiagnosticDescriptor(
            "VSL003",
            "VSL type has no parameterless constructor",
            "'{0}' takes part in VSL serialization but has no accessible parameterless constructor, so it cannot be deserialized.",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>
        /// A private field declared on a base type is out of reach for a formatter nested in the
        /// derived type, so that whole type falls back to reflection.
        /// </summary>
        public static readonly DiagnosticDescriptor InaccessibleBaseMember = new DiagnosticDescriptor(
            "VSL004",
            "VSL member is not reachable from generated code",
            "'{0}' is a private member of a base type, so no formatter can be generated for '{1}'. It still serializes correctly through reflection, just more slowly. Make the field protected, or move it into the derived type, to get the generated formatter.",
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true);
    }
}
