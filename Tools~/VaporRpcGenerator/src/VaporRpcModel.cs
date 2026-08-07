using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vapor.Rpc.SourceGenerator
{
    internal sealed class RpcParameterModel
    {
        public string Name;          // the name the author gave it
        public string Local;         // what the receive handler calls its local
        public string TypeName;      // fully qualified with global::
        public string WriteCall;     // fully qualified, ready for "(writer, value)"
        public string ReadCall;      // fully qualified, ready for "(reader)"
        public string ReadCast;      // "" or "(global::Some.Type)"

        /// <summary>
        /// True for the <c>WriteValue&lt;T&gt;</c> branch, the only one that goes through
        /// <c>NetworkVariableSerialization&lt;T&gt;</c> and so needs Netcode to have generated a
        /// serializer for the type. See <see cref="RpcMethodModel.SerializationTypes"/>.
        /// </summary>
        public bool NeedsGeneratedSerialization;
    }

    internal sealed class RpcMethodModel
    {
        public string Name;
        public string ImplementationName;
        public string ReceiveName;
        public string Modifiers;         // the author's modifiers, minus 'partial'
        public string WriterLocal;       // what the send path calls its writer
        public uint Hash;
        public string SendTo;            // rendered expression
        public string NetworkDelivery;   // rendered expression
        public string DisplayName;       // what the runtime's collision message prints
        public List<RpcParameterModel> Parameters = new List<RpcParameterModel>();

        public string ParameterList => string.Join(", ", Parameters.Select(p => $"{p.TypeName} {p.Name}"));
        public string ArgumentList => string.Join(", ", Parameters.Select(p => p.Name));

        /// <summary>
        /// The distinct parameter types that travel through <c>NetworkVariableSerialization&lt;T&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Netcode's own IL post-processor discovers the types it must generate serialization for by
        /// scanning <c>NetworkVariable&lt;T&gt;</c> declarations and <c>[GenerateSerializationFor…]</c>
        /// attributes — it has no idea what an rpc signature looks like. Without a declaration
        /// somewhere, a type used only as an rpc argument reaches <c>FallbackSerializer</c> and throws
        /// on the first send. Naming them here closes that at build time. It is safe for types Netcode
        /// cannot generate for: it simply emits no initializer and the runtime behaves as before.
        /// </remarks>
        public IEnumerable<string> SerializationTypes => Parameters
            .Where(p => p.NeedsGeneratedSerialization)
            .Select(p => p.TypeName)
            .Distinct();
    }

    internal sealed class RpcTypeModel
    {
        public string HintName;
        public string Namespace;
        public string TypeName;          // fully qualified with global::
        public string SimpleName;
        public string TypeKeyword;       // "class" or "record"
        public List<string> ContainingTypes = new List<string>();   // outermost first
        public List<RpcMethodModel> Methods = new List<RpcMethodModel>();
    }

    /// <summary>
    /// The types the generated send and receive paths are written against. Resolved once per
    /// compilation; a null <see cref="NetworkObject"/> means this assembly does not reference the
    /// Vapor runtime at all and there is nothing to generate.
    /// </summary>
    internal sealed class RpcKnownSymbols
    {
        public INamedTypeSymbol NetworkObject;
        public INamedTypeSymbol NetworkBehaviour;
        public INamedTypeSymbol NetworkPacket;
        public INamedTypeSymbol NetworkSerializable;
        public INamedTypeSymbol FastBufferReader;

        public static RpcKnownSymbols Resolve(Compilation compilation) => new RpcKnownSymbols
        {
            NetworkObject = compilation.GetTypeByMetadataName(VaporRpcModelBuilder.NetworkObjectType),
            NetworkBehaviour = compilation.GetTypeByMetadataName("Unity.Netcode.NetworkBehaviour"),
            NetworkPacket = compilation.GetTypeByMetadataName("Vapor.NetworkObjects.INetworkPacket"),
            NetworkSerializable = compilation.GetTypeByMetadataName("Unity.Netcode.INetworkSerializable"),
            FastBufferReader = compilation.GetTypeByMetadataName("Unity.Netcode.FastBufferReader"),
        };
    }

    internal static class VaporRpcModelBuilder
    {
        public const string RpcAttribute = "Vapor.NetworkObjects.VaporRpcAttribute";
        public const string NetworkObjectType = "Vapor.NetworkObjects.VaporNetworkObject";

        private const string Serialization = "global::Vapor.NetworkObjects.RpcSerialization";
        private const string DefaultDelivery = "global::Unity.Netcode.NetworkDelivery.ReliableSequenced";

        /// <summary>
        /// Builds the model for one rpc, or returns null after reporting why it cannot be generated.
        /// </summary>
        public static RpcMethodModel BuildMethod(
            IMethodSymbol method, AttributeData attribute, RpcKnownSymbols known, string assemblyName, SourceProductionContext context)
        {
            var display = method.ToDisplayString();

            // A malformed attribute — [VaporRpc] with no SendTo — is already a compile error, and
            // there is no sensible target to fall back to. Emitting a send with a hole in it would
            // only bury that error under generated ones.
            if (attribute.ConstructorArguments.Length == 0)
            {
                return null;
            }

            if (!DerivesFromNetworkObject(method.ContainingType, known.NetworkObject))
            {
                Report(context, VaporRpcDiagnostics.NotANetworkObject, method, display, method.ContainingType.ToDisplayString());
                return null;
            }

            if (!ValidateSignature(method, display, context))
            {
                return null;
            }

            var parameters = BuildParameters(method, display, known, context);
            if (parameters == null)
            {
                return null;
            }

            var hash = VaporRpcHash.Compute(method, assemblyName);
            if (hash == 0)
            {
                Report(context, VaporRpcDiagnostics.DegenerateHash, method, display);
                return null;
            }

            // Same reasoning as the empty-argument guard: an unrenderable SendTo means the attribute
            // is already broken, and half a send call helps nobody.
            var sendTo = EnumValue(attribute, 0, null);
            if (sendTo == null)
            {
                return null;
            }

            if (sendTo.EndsWith(".SpecifiedInParams"))
            {
                Report(context, VaporRpcDiagnostics.UnsupportedSendTo, method, display);
                return null;
            }

            return new RpcMethodModel
            {
                Name = method.Name,
                ImplementationName = method.Name + "_Implementation",
                ReceiveName = method.Name + "_Receive",
                Modifiers = Modifiers(method),
                WriterLocal = FreeName("rpcWriter", method),
                Hash = hash,
                SendTo = sendTo,
                NetworkDelivery = EnumValue(attribute, 1, DefaultDelivery),
                DisplayName = $"{method.ContainingType.ToDisplayString()}.{method.Name}",
                Parameters = parameters,
            };
        }

        public static RpcTypeModel BuildType(INamedTypeSymbol type)
        {
            var model = new RpcTypeModel
            {
                Namespace = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
                TypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SimpleName = type.Name,
                TypeKeyword = TypeKeywordOf(type),
                // Generic types are rejected per-method, so a hint name never actually reaches
                // AddSource with brackets in it — but a file name is not the place to rely on that.
                HintName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty)
                    .Replace('.', '_')
                    .Replace('<', '_')
                    .Replace('>', '_')
                    .Replace(',', '_')
                    .Replace(" ", string.Empty),
            };

            for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
            {
                model.ContainingTypes.Insert(0, $"partial {TypeKeywordOf(outer)} {outer.Name}");
            }

            return model;
        }

        #region Validation

        private static bool ValidateSignature(IMethodSymbol method, string display, SourceProductionContext context)
        {
            bool valid = true;

            // The one every rpc written against the IL weaver trips on, so it is checked first and its
            // message spells out the move.
            if (!method.IsPartialDefinition)
            {
                Report(context, VaporRpcDiagnostics.MethodNotPartial, method,
                    display, method.Name + "_Implementation", method.Name);
                valid = false;
            }
            else if (method.PartialImplementationPart != null)
            {
                Report(context, VaporRpcDiagnostics.InvalidSignature, method, display,
                    $"this partial method already has an implementation. The generator writes it — put your body in '{method.Name}_Implementation' instead.");
                valid = false;
            }

            if (method.IsStatic)
            {
                Report(context, VaporRpcDiagnostics.InvalidSignature, method, display, "rpcs are sent through an instance, so they cannot be static.");
                valid = false;
            }

            if (method.IsAbstract || method.IsVirtual || method.IsOverride)
            {
                Report(context, VaporRpcDiagnostics.InvalidSignature, method, display,
                    "rpcs are dispatched by id to the declaring type, not by vtable, so they cannot be abstract, virtual, or overrides.");
                valid = false;
            }

            if (method.IsGenericMethod || method.ContainingType.IsGenericType)
            {
                Report(context, VaporRpcDiagnostics.InvalidSignature, method, display,
                    "rpcs cannot be generic or declared on a generic type — each construction would need its own id.");
                valid = false;
            }

            if (!method.ReturnsVoid)
            {
                Report(context, VaporRpcDiagnostics.InvalidSignature, method, display, "rpcs must return void; there is nothing to return a value to.");
                valid = false;
            }

            if (method.IsAsync)
            {
                Report(context, VaporRpcDiagnostics.InvalidSignature, method, display, "rpcs cannot be async.");
                valid = false;
            }

            if (!method.Name.EndsWith("Rpc"))
            {
                Report(context, VaporRpcDiagnostics.NameMustEndInRpc, method, display);
                valid = false;
            }

            return valid;
        }

        private static List<RpcParameterModel> BuildParameters(
            IMethodSymbol method, string display, RpcKnownSymbols known, SourceProductionContext context)
        {
            // The receive handler's own parameters would shadow an argument that happened to share
            // their name, so in that case every local falls back to a positional name.
            bool collides = method.Parameters.Any(p => p.Name == "rpcTarget" || p.Name == "rpcReader");

            var parameters = new List<RpcParameterModel>();
            bool valid = true;

            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var parameter = method.Parameters[i];

                if (known.FastBufferReader != null && SymbolEqualityComparer.Default.Equals(parameter.Type, known.FastBufferReader))
                {
                    Report(context, VaporRpcDiagnostics.UnsupportedParameter, parameter, display, parameter.Name,
                        "is a FastBufferReader — the hand-registered rpc pattern. Declare typed parameters instead; the serialization is generated.");
                    valid = false;
                    continue;
                }

                if (parameter.RefKind != RefKind.None)
                {
                    Report(context, VaporRpcDiagnostics.UnsupportedParameter, parameter, display, parameter.Name,
                        "is ref, out, or in. An rpc argument only travels one way, so it has to be passed by value.");
                    valid = false;
                    continue;
                }

                if (parameter.Type.TypeKind == TypeKind.Pointer || parameter.Type is ITypeParameterSymbol)
                {
                    Report(context, VaporRpcDiagnostics.UnsupportedParameter, parameter, display, parameter.Name,
                        $"has an unsupported type '{parameter.Type.ToDisplayString()}'.");
                    valid = false;
                    continue;
                }

                var serializer = SelectSerializer(parameter, known, display, context);
                if (serializer == null)
                {
                    valid = false;
                    continue;
                }

                serializer.Local = collides ? $"arg{i}" : parameter.Name;
                parameters.Add(serializer);
            }

            return valid ? parameters : null;
        }

        /// <summary>
        /// Picks how one argument crosses the wire. The order matters: VaporNetworkObject itself
        /// implements INetworkPacket, so it has to be tested before the packet case.
        /// </summary>
        private static RpcParameterModel SelectSerializer(
            IParameterSymbol parameter, RpcKnownSymbols known, string display, SourceProductionContext context)
        {
            var type = parameter.Type;
            var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var model = new RpcParameterModel
            {
                Name = parameter.Name,
                TypeName = typeName,
                ReadCast = string.Empty,
            };

            if (DerivesFromOrIs(type, known.NetworkObject))
            {
                model.WriteCall = $"{Serialization}.WriteNetworkObject";
                model.ReadCall = $"{Serialization}.ReadNetworkObject";
                model.ReadCast = SymbolEqualityComparer.Default.Equals(type, known.NetworkObject) ? string.Empty : $"({typeName})";
                return model;
            }

            if (DerivesFromOrIs(type, known.NetworkBehaviour))
            {
                model.WriteCall = $"{Serialization}.WriteNetworkBehaviour";
                model.ReadCall = $"{Serialization}.ReadNetworkBehaviour";
                model.ReadCast = SymbolEqualityComparer.Default.Equals(type, known.NetworkBehaviour) ? string.Empty : $"({typeName})";
                return model;
            }

            if (ImplementsOrIs(type, known.NetworkPacket))
            {
                model.WriteCall = $"{Serialization}.WritePacket";
                model.ReadCall = $"{Serialization}.ReadPacket";
                model.ReadCast = SymbolEqualityComparer.Default.Equals(type, known.NetworkPacket) ? string.Empty : $"({typeName})";
                return model;
            }

            if (ImplementsOrIs(type, known.NetworkSerializable))
            {
                if (!CanBeConstructed(type))
                {
                    Report(context, VaporRpcDiagnostics.SerializableNeedsConstructor, parameter,
                        display, parameter.Name, type.ToDisplayString());
                    return null;
                }

                model.WriteCall = $"{Serialization}.WriteSerializable<{typeName}>";
                model.ReadCall = $"{Serialization}.ReadSerializable<{typeName}>";
                return model;
            }

            // Everything else — primitives, vectors, enums, plain structs — goes through
            // NetworkVariableSerialization<T>, the one path that needs Netcode to have generated a
            // serializer for the type.
            model.WriteCall = $"{Serialization}.WriteValue<{typeName}>";
            model.ReadCall = $"{Serialization}.ReadValue<{typeName}>";

            // Only value types are named to Netcode's post-processor. Its managed-type branch errors
            // outright on anything without IEquatable<T>, and even with it only generates a serializer
            // for INetworkSerializable — which is handled above. So for a managed type here, there is
            // nothing to ask for and asking would break the build.
            if (type.IsValueType)
            {
                model.NeedsGeneratedSerialization = true;
            }
            else
            {
                Report(context, VaporRpcDiagnostics.UnserializableManagedParameter, parameter,
                    display, parameter.Name, type.ToDisplayString());
            }

            return model;
        }

        #endregion

        #region Symbol helpers

        public static bool IsPartialEverywhere(INamedTypeSymbol type)
        {
            for (var current = type; current != null; current = current.ContainingType)
            {
                var partial = current.DeclaringSyntaxReferences
                    .Select(reference => reference.GetSyntax())
                    .OfType<TypeDeclarationSyntax>()
                    .Any(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

                if (!partial)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool DerivesFromNetworkObject(INamedTypeSymbol type, INamedTypeSymbol networkObject) =>
            networkObject != null && DerivesFromOrIs(type, networkObject);

        private static bool DerivesFromOrIs(ITypeSymbol type, INamedTypeSymbol candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ImplementsOrIs(ITypeSymbol type, INamedTypeSymbol @interface)
        {
            if (@interface == null)
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(type, @interface) ||
                   type.AllInterfaces.Any(implemented => SymbolEqualityComparer.Default.Equals(implemented, @interface));
        }

        /// <summary>
        /// Matches what the <c>new()</c> constraint on the runtime's serializable helpers demands.
        /// </summary>
        private static bool CanBeConstructed(ITypeSymbol type)
        {
            if (type.IsValueType)
            {
                return true;
            }

            return type is INamedTypeSymbol named && !named.IsAbstract && named.TypeKind == TypeKind.Class &&
                   named.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public);
        }

        /// <summary>
        /// The implementing half of a partial method has to repeat the declaring half's modifiers
        /// exactly, so they are taken from the author's own declaration rather than rebuilt.
        /// </summary>
        private static string Modifiers(IMethodSymbol method)
        {
            var syntax = method.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();

            if (syntax == null)
            {
                return "private";
            }

            var modifiers = syntax.Modifiers
                .Where(token => !token.IsKind(SyntaxKind.PartialKeyword))
                .Select(token => token.ValueText);

            return string.Join(" ", modifiers);
        }

        /// <summary>
        /// Renders an enum argument by member name where one matches, so the generated call reads the
        /// way the attribute did.
        /// </summary>
        private static string EnumValue(AttributeData attribute, int index, string fallback)
        {
            if (attribute.ConstructorArguments.Length <= index)
            {
                return fallback;
            }

            var argument = attribute.ConstructorArguments[index];
            if (argument.Type == null)
            {
                return fallback;
            }

            var typeName = argument.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var member = argument.Type.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, argument.Value));

            return member != null ? $"{typeName}.{member.Name}" : $"(({typeName}){argument.Value})";
        }

        /// <summary>
        /// Picks a name for a generated local that no parameter has already taken.
        /// </summary>
        private static string FreeName(string preferred, IMethodSymbol method)
        {
            while (method.Parameters.Any(parameter => parameter.Name == preferred))
            {
                preferred += "_";
            }

            return preferred;
        }

        private static string TypeKeywordOf(INamedTypeSymbol type) => type.IsRecord ? "record" : "class";

        private static void Report(SourceProductionContext context, DiagnosticDescriptor descriptor, ISymbol symbol, params object[] arguments) =>
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor, symbol.Locations.FirstOrDefault() ?? Location.None, arguments));

        #endregion
    }
}
