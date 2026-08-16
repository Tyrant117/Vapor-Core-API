using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vapor.Network.SourceGenerator
{
    internal sealed class RpcParameterModel
    {
        public string Name;          // the name the author gave it
        public string Local;         // what the receive handler calls its local
        public string TypeName;      // fully qualified with global::

        /// <summary>Format for the send side: {0} = writer local, {1} = the argument expression.</summary>
        public string WriteFormat;

        /// <summary>Format for the receive side: {0} = host local (IRpcHost), {1} = reader local. Yields an expression of the parameter type.</summary>
        public string ReadFormat;

        public string WriteCall(string writer, string value) => string.Format(WriteFormat, writer, value);
        public string ReadCall(string host, string reader) => string.Format(ReadFormat, host, reader);
    }

    internal sealed class RpcMethodModel
    {
        public string Name;
        public string ImplementationName;
        public string ReceiveName;
        public string Modifiers;         // the author's modifiers, minus 'partial'
        public string WriterLocal;       // what the send path calls its writer
        public uint Hash;
        public string Target;            // rendered RpcTarget expression
        public string Delivery;          // rendered Delivery expression
        public string DisplayName;       // what the runtime's collision message prints
        public List<RpcParameterModel> Parameters = new List<RpcParameterModel>();

        public string ParameterList => string.Join(", ", Parameters.Select(p => $"{p.TypeName} {p.Name}"));
        public string ArgumentList => string.Join(", ", Parameters.Select(p => p.Name));
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

    /// <summary>The runtime types the generated code is written against. A null NetworkObject means the assembly does not reference Vapor.Networking.</summary>
    internal sealed class KnownSymbols
    {
        public INamedTypeSymbol NetworkObject;
        public INamedTypeSymbol NetworkComponent;
        public INamedTypeSymbol NetworkReader;
        public INamedTypeSymbol NetworkWriter;
        public INamedTypeSymbol NetworkSerializable;
        public INamedTypeSymbol NetworkSerialize;
        public INamedTypeSymbol NetworkIgnore;
        public INamedTypeSymbol VslSerializable;
        public INamedTypeSymbol VslSerialize;
        public INamedTypeSymbol VslIgnore;
        public INamedTypeSymbol SerializeField;
        public INamedTypeSymbol NonSerialized;

        public static KnownSymbols Resolve(Compilation compilation) => new KnownSymbols
        {
            NetworkObject = compilation.GetTypeByMetadataName(RpcModelBuilder.NetworkObjectType),
            NetworkComponent = compilation.GetTypeByMetadataName(RpcModelBuilder.NetworkComponentType),
            NetworkReader = compilation.GetTypeByMetadataName("Vapor.Networking.NetworkReader"),
            NetworkWriter = compilation.GetTypeByMetadataName("Vapor.Networking.NetworkWriter"),
            NetworkSerializable = compilation.GetTypeByMetadataName("Vapor.Networking.NetworkSerializableAttribute"),
            NetworkSerialize = compilation.GetTypeByMetadataName("Vapor.Networking.NetworkSerializeAttribute"),
            NetworkIgnore = compilation.GetTypeByMetadataName("Vapor.Networking.NetworkIgnoreAttribute"),
            VslSerializable = compilation.GetTypeByMetadataName("Vapor.Serialization.VslSerializableAttribute"),
            VslSerialize = compilation.GetTypeByMetadataName("Vapor.Serialization.VslSerializeAttribute"),
            VslIgnore = compilation.GetTypeByMetadataName("Vapor.Serialization.VslIgnoreAttribute"),
            SerializeField = compilation.GetTypeByMetadataName("UnityEngine.SerializeField"),
            NonSerialized = compilation.GetTypeByMetadataName("System.NonSerializedAttribute"),
        };
    }

    internal static class RpcModelBuilder
    {
        public const string RpcAttribute = "Vapor.Networking.VaporRpcAttribute";
        public const string NetworkObjectType = "Vapor.Networking.VaporNetworkObject";
        public const string NetworkComponentType = "Vapor.Networking.NetworkComponent";

        private const string Formatters = "global::Vapor.Networking.NetworkFormatters";
        private const string Arguments = "global::Vapor.Networking.RpcArguments";
        private const string DefaultDelivery = "global::Vapor.Networking.Delivery.ReliableSequenced";

        /// <summary>Builds the model for one rpc, or returns null after reporting why it cannot be generated.</summary>
        public static RpcMethodModel BuildMethod(
            IMethodSymbol method, AttributeData attribute, KnownSymbols known, string assemblyName, SourceProductionContext context)
        {
            var display = method.ToDisplayString();

            // A malformed attribute — [VaporRpc] with no target — is already a compile error, and
            // there is no sensible target to fall back to.
            if (attribute.ConstructorArguments.Length == 0)
            {
                return null;
            }

            if (!DerivesFromOrIs(method.ContainingType, known.NetworkObject) && !DerivesFromOrIs(method.ContainingType, known.NetworkComponent))
            {
                Report(context, Diagnostics.NotAnRpcHost, method, display, method.ContainingType.ToDisplayString());
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

            var hash = RpcHash.Compute(method, assemblyName);
            if (hash == 0)
            {
                Report(context, Diagnostics.DegenerateHash, method, display);
                return null;
            }

            var target = EnumValue(attribute, 0, null);
            if (target == null)
            {
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
                Target = target,
                Delivery = EnumValue(attribute, 1, DefaultDelivery),
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
                HintName = HintNameOf(type),
            };

            for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
            {
                model.ContainingTypes.Insert(0, $"partial {TypeKeywordOf(outer)} {outer.Name}");
            }

            return model;
        }

        public static string HintNameOf(INamedTypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty)
                .Replace('.', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace(',', '_')
                .Replace(" ", string.Empty);

        #region Validation

        private static bool ValidateSignature(IMethodSymbol method, string display, SourceProductionContext context)
        {
            bool valid = true;

            if (!method.IsPartialDefinition)
            {
                Report(context, Diagnostics.MethodNotPartial, method, display, method.Name + "_Implementation", method.Name);
                valid = false;
            }
            else if (method.PartialImplementationPart != null)
            {
                Report(context, Diagnostics.InvalidSignature, method, display,
                    $"this partial method already has an implementation. The generator writes it — put your body in '{method.Name}_Implementation' instead.");
                valid = false;
            }

            if (method.IsStatic)
            {
                Report(context, Diagnostics.InvalidSignature, method, display, "rpcs are sent through an instance, so they cannot be static.");
                valid = false;
            }

            if (method.IsAbstract || method.IsVirtual || method.IsOverride)
            {
                Report(context, Diagnostics.InvalidSignature, method, display,
                    "rpcs are dispatched by id to the declaring type, not by vtable, so they cannot be abstract, virtual, or overrides.");
                valid = false;
            }

            if (method.IsGenericMethod || method.ContainingType.IsGenericType)
            {
                Report(context, Diagnostics.InvalidSignature, method, display,
                    "rpcs cannot be generic or declared on a generic type — each construction would need its own id.");
                valid = false;
            }

            if (!method.ReturnsVoid)
            {
                Report(context, Diagnostics.InvalidSignature, method, display, "rpcs must return void; there is nothing to return a value to.");
                valid = false;
            }

            if (method.IsAsync)
            {
                Report(context, Diagnostics.InvalidSignature, method, display, "rpcs cannot be async.");
                valid = false;
            }

            if (!method.Name.EndsWith("Rpc"))
            {
                Report(context, Diagnostics.NameMustEndInRpc, method, display);
                valid = false;
            }

            return valid;
        }

        private static List<RpcParameterModel> BuildParameters(IMethodSymbol method, string display, KnownSymbols known, SourceProductionContext context)
        {
            bool collides = method.Parameters.Any(p => p.Name == "rpcTarget" || p.Name == "rpcReader");
            var parameters = new List<RpcParameterModel>();
            bool valid = true;

            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var parameter = method.Parameters[i];

                if ((known.NetworkReader != null && SymbolEqualityComparer.Default.Equals(parameter.Type, known.NetworkReader)) ||
                    (known.NetworkWriter != null && SymbolEqualityComparer.Default.Equals(parameter.Type, known.NetworkWriter)))
                {
                    Report(context, Diagnostics.UnsupportedParameter, parameter, display, parameter.Name,
                        "is a NetworkReader/NetworkWriter. Declare typed parameters instead; the serialization is generated.");
                    valid = false;
                    continue;
                }

                if (parameter.RefKind != RefKind.None)
                {
                    Report(context, Diagnostics.UnsupportedParameter, parameter, display, parameter.Name,
                        "is ref, out, or in. An rpc argument only travels one way, so it has to be passed by value.");
                    valid = false;
                    continue;
                }

                if (parameter.Type.TypeKind == TypeKind.Pointer || parameter.Type is ITypeParameterSymbol)
                {
                    Report(context, Diagnostics.UnsupportedParameter, parameter, display, parameter.Name,
                        $"has an unsupported type '{parameter.Type.ToDisplayString()}'.");
                    valid = false;
                    continue;
                }

                var model = SelectSerializer(parameter, known);
                model.Local = collides ? $"arg{i}" : parameter.Name;
                parameters.Add(model);
            }

            return valid ? parameters : null;
        }

        /// <summary>
        /// Object and component references travel as ids and resolve against the receiving world;
        /// everything else goes through the formatter registry, which the generated formatters and
        /// the built-ins fill.
        /// </summary>
        private static RpcParameterModel SelectSerializer(IParameterSymbol parameter, KnownSymbols known)
        {
            var type = parameter.Type;
            var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var model = new RpcParameterModel { Name = parameter.Name, TypeName = typeName };

            if (DerivesFromOrIs(type, known.NetworkObject))
            {
                string cast = SymbolEqualityComparer.Default.Equals(type, known.NetworkObject) ? string.Empty : $"({typeName})";
                model.WriteFormat = Arguments + ".WriteObject({0}, {1})";
                model.ReadFormat = cast + Arguments + ".ReadObject({0}, {1})";
                return model;
            }

            if (DerivesFromOrIs(type, known.NetworkComponent))
            {
                string cast = SymbolEqualityComparer.Default.Equals(type, known.NetworkComponent) ? string.Empty : $"({typeName})";
                model.WriteFormat = Arguments + ".WriteComponent({0}, {1})";
                model.ReadFormat = cast + Arguments + ".ReadComponent({0}, {1})";
                return model;
            }

            model.WriteFormat = Formatters + ".Write<" + typeName + ">({0}, {1})";
            model.ReadFormat = Formatters + ".Read<" + typeName + ">({1})";
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

        public static bool DerivesFromOrIs(ITypeSymbol type, INamedTypeSymbol candidate)
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

        private static string FreeName(string preferred, IMethodSymbol method)
        {
            while (method.Parameters.Any(parameter => parameter.Name == preferred))
            {
                preferred += "_";
            }

            return preferred;
        }

        public static string TypeKeywordOf(INamedTypeSymbol type)
        {
            if (type.IsRecord)
            {
                return type.IsValueType ? "record struct" : "record";
            }

            return type.IsValueType ? "struct" : "class";
        }

        public static void Report(SourceProductionContext context, DiagnosticDescriptor descriptor, ISymbol symbol, params object[] arguments) =>
            context.ReportDiagnostic(Diagnostic.Create(descriptor, symbol.Locations.FirstOrDefault() ?? Location.None, arguments));

        #endregion
    }
}
