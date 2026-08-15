using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Vapor.Network.SourceGenerator
{
    /// <summary>
    /// Emits a binary <c>NetworkFormatter&lt;T&gt;</c> for every type marked <c>[NetworkSerializable]</c>
    /// (or carrying a <c>[NetworkSerialize]</c> member), nested inside the type so it can reach private
    /// members, plus a registration that runs at load.
    /// </summary>
    /// <remarks>
    /// Member selection follows VSL's rules so a type describes its members once: public fields and
    /// <c>[SerializeField]</c> privates when the type is <c>[NetworkSerializable]</c> or
    /// <c>[VslSerializable]</c>; anything <c>[NetworkSerialize]</c> or <c>[VslSerialize]</c>; nothing
    /// <c>[NetworkIgnore]</c>, <c>[VslIgnore]</c> or <c>[NonSerialized]</c>. Members are written in
    /// declaration order, base type first, and every member goes through the formatter registry — so
    /// nested types, collections and enums all work as long as their own formatters exist.
    /// </remarks>
    [Generator]
    public sealed class FormatterGenerator : IIncrementalGenerator
    {
        private const string SerializableAttribute = "Vapor.Networking.NetworkSerializableAttribute";
        private const string SerializeAttribute = "Vapor.Networking.NetworkSerializeAttribute";
        private const string Formatters = "global::Vapor.Networking.NetworkFormatters";
        private const string FormatterBase = "global::Vapor.Networking.NetworkFormatter";
        private const string Writer = "global::Vapor.Networking.NetworkWriter";
        private const string Reader = "global::Vapor.Networking.NetworkReader";
        private const string FormatterName = "VaporNetworkFormatter";
        private const string RegistrarName = "RegisterVaporNetworkFormatter";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var typed = context.SyntaxProvider.ForAttributeWithMetadataName(
                SerializableAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol);

            var members = context.SyntaxProvider.ForAttributeWithMetadataName(
                SerializeAttribute,
                static (node, _) => node is FieldDeclarationSyntax || node is VariableDeclaratorSyntax || node is PropertyDeclarationSyntax,
                static (ctx, _) => ctx.TargetSymbol.ContainingType);

            var input = typed.Collect().Combine(members.Collect()).Combine(context.CompilationProvider);
            context.RegisterSourceOutput(input, static (spc, values) => Execute(spc, values.Left.Left, values.Left.Right, values.Right));
        }

        private static void Execute(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> typed, ImmutableArray<INamedTypeSymbol> fromMembers, Compilation compilation)
        {
            if (typed.IsDefaultOrEmpty && fromMembers.IsDefaultOrEmpty)
            {
                return;
            }

            var known = KnownSymbols.Resolve(compilation);
            if (known.NetworkSerializable == null)
            {
                return;
            }

            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var order = new List<INamedTypeSymbol>();
            foreach (var type in typed.Concat(fromMembers))
            {
                if (type != null && seen.Add(type)) order.Add(type);
            }

            foreach (var type in order)
            {
                var source = Build(type, known, compilation, context);
                if (source != null)
                {
                    context.AddSource($"{RpcModelBuilder.HintNameOf(type)}.VaporNetworkFormatter.g.cs", SourceText.From(source, Encoding.UTF8));
                }
            }
        }

        private static string Build(INamedTypeSymbol type, KnownSymbols known, Compilation compilation, SourceProductionContext context)
        {
            string display = type.ToDisplayString();

            if (type.TypeKind == TypeKind.Interface || type.IsAbstract || type.IsStatic || type.IsGenericType || HasGenericContainer(type))
            {
                RpcModelBuilder.Report(context, Diagnostics.FormatterUnsupportedType, type, display,
                    type.TypeKind == TypeKind.Interface ? "it is an interface." :
                    type.IsAbstract ? "it is abstract." :
                    type.IsStatic ? "it is static." : "it is generic (or nested in a generic type).");
                return null;
            }

            if (!RpcModelBuilder.IsPartialEverywhere(type))
            {
                RpcModelBuilder.Report(context, Diagnostics.FormatterTypeNotPartial, type, display);
                return null;
            }

            bool isClass = !type.IsValueType;
            if (isClass && !type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && !c.IsStatic))
            {
                RpcModelBuilder.Report(context, Diagnostics.FormatterNeedsConstructor, type, display);
                return null;
            }

            var members = CollectMembers(type, known, compilation, context);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine();

            int indent = 0;
            string ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
            if (ns != null)
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                indent++;
            }

            var containers = new List<INamedTypeSymbol>();
            for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType) containers.Insert(0, outer);
            foreach (var outer in containers)
            {
                sb.AppendLine($"{Pad(indent)}partial {RpcModelBuilder.TypeKeywordOf(outer)} {outer.Name}");
                sb.AppendLine($"{Pad(indent)}{{");
                indent++;
            }

            string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sb.AppendLine($"{Pad(indent)}partial {RpcModelBuilder.TypeKeywordOf(type)} {type.Name}");
            sb.AppendLine($"{Pad(indent)}{{");
            indent++;

            var pad = Pad(indent);
            var body = Pad(indent + 1);
            var deep = Pad(indent + 2);
            var deeper = Pad(indent + 3);

            sb.AppendLine($"{pad}/// <summary>Generated network formatter for <see cref=\"{type.Name}\"/>. Registered at load; not for direct use.</summary>");
            sb.AppendLine($"{pad}public sealed class {FormatterName} : {FormatterBase}<{typeName}>");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{body}public static readonly {FormatterName} Instance = new {FormatterName}();");
            sb.AppendLine();
            sb.AppendLine($"{body}public override void Write({Writer} writer, in {typeName} value)");
            sb.AppendLine($"{body}{{");
            if (isClass)
            {
                sb.AppendLine($"{deep}if (value == null)");
                sb.AppendLine($"{deep}{{");
                sb.AppendLine($"{deeper}writer.WriteBool(false);");
                sb.AppendLine($"{deeper}return;");
                sb.AppendLine($"{deep}}}");
                sb.AppendLine();
                sb.AppendLine($"{deep}writer.WriteBool(true);");
            }

            foreach (var member in members)
            {
                sb.AppendLine($"{deep}{Formatters}.Write<{member.TypeName}>(writer, value.{member.Name});");
            }

            sb.AppendLine($"{body}}}");
            sb.AppendLine();
            sb.AppendLine($"{body}public override {typeName} Read({Reader} reader)");
            sb.AppendLine($"{body}{{");
            if (isClass)
            {
                sb.AppendLine($"{deep}if (!reader.ReadBool())");
                sb.AppendLine($"{deep}{{");
                sb.AppendLine($"{deeper}return null;");
                sb.AppendLine($"{deep}}}");
                sb.AppendLine();
                sb.AppendLine($"{deep}var value = new {typeName}();");
            }
            else
            {
                sb.AppendLine($"{deep}var value = default({typeName});");
            }

            foreach (var member in members)
            {
                sb.AppendLine($"{deep}value.{member.Name} = {Formatters}.Read<{member.TypeName}>(reader);");
            }

            sb.AppendLine($"{deep}return value;");
            sb.AppendLine($"{body}}}");
            sb.AppendLine($"{pad}}}");
            sb.AppendLine();
            sb.AppendLine($"{pad}/// <summary>Registers <see cref=\"{FormatterName}\"/> before anything can reach the wire.</summary>");
            sb.AppendLine($"{pad}[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine($"{pad}[global::UnityEditor.InitializeOnLoadMethod]");
            sb.AppendLine("#endif");
            sb.AppendLine($"{pad}private static void {RegistrarName}()");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{body}{Formatters}.Register<{typeName}>({FormatterName}.Instance);");
            sb.AppendLine($"{pad}}}");

            indent--;
            sb.AppendLine($"{Pad(indent)}}}");
            foreach (var _ in containers)
            {
                indent--;
                sb.AppendLine($"{Pad(indent)}}}");
            }

            if (ns != null)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private struct Member
        {
            public string Name;
            public string TypeName;
        }

        /// <summary>Base type first, declaration order within each type — the same order VSL uses.</summary>
        private static List<Member> CollectMembers(INamedTypeSymbol type, KnownSymbols known, Compilation compilation, SourceProductionContext context)
        {
            var chain = new List<INamedTypeSymbol>();
            for (var current = type; current != null && current.SpecialType == SpecialType.None; current = current.BaseType)
            {
                chain.Insert(0, current);
            }

            var members = new List<Member>();
            foreach (var declaring in chain)
            {
                bool typeOptsIn = Has(declaring, known.NetworkSerializable) || Has(declaring, known.VslSerializable);
                foreach (var symbol in declaring.GetMembers())
                {
                    if (symbol.IsStatic || symbol.IsImplicitlyDeclared) continue;

                    switch (symbol)
                    {
                        case IFieldSymbol field:
                        {
                            if (field.IsConst || field.IsReadOnly || field.AssociatedSymbol != null) continue;
                            if (Has(field, known.NetworkIgnore) || Has(field, known.VslIgnore) || Has(field, known.NonSerialized)) continue;
                            bool optIn = Has(field, known.NetworkSerialize) || Has(field, known.VslSerialize);
                            bool byType = typeOptsIn && (field.DeclaredAccessibility == Accessibility.Public || Has(field, known.SerializeField));
                            if (!optIn && !byType) continue;
                            if (!compilation.IsSymbolAccessibleWithin(field, type))
                            {
                                RpcModelBuilder.Report(context, Diagnostics.FormatterMemberSkipped, field, type.ToDisplayString(), field.Name,
                                    $"is declared on '{declaring.Name}' and is not accessible from '{type.Name}'; it will not be replicated.");
                                continue;
                            }

                            members.Add(new Member { Name = field.Name, TypeName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) });
                            break;
                        }

                        case IPropertySymbol property:
                        {
                            if (property.IsIndexer) continue;
                            if (Has(property, known.NetworkIgnore) || Has(property, known.VslIgnore)) continue;
                            bool optIn = Has(property, known.NetworkSerialize) || Has(property, known.VslSerialize);
                            if (!optIn) continue;
                            if (property.GetMethod == null || property.SetMethod == null || property.SetMethod.IsInitOnly)
                            {
                                RpcModelBuilder.Report(context, Diagnostics.FormatterMemberSkipped, property, type.ToDisplayString(), property.Name,
                                    "needs both a getter and a (non-init) setter to be replicated.");
                                continue;
                            }

                            if (!compilation.IsSymbolAccessibleWithin(property.GetMethod, type) || !compilation.IsSymbolAccessibleWithin(property.SetMethod, type))
                            {
                                RpcModelBuilder.Report(context, Diagnostics.FormatterMemberSkipped, property, type.ToDisplayString(), property.Name,
                                    $"is declared on '{declaring.Name}' and its accessors are not accessible from '{type.Name}'; it will not be replicated.");
                                continue;
                            }

                            members.Add(new Member { Name = property.Name, TypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) });
                            break;
                        }
                    }
                }
            }

            return members;
        }

        private static bool Has(ISymbol symbol, INamedTypeSymbol attribute) =>
            attribute != null && symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));

        private static bool HasGenericContainer(INamedTypeSymbol type)
        {
            for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
            {
                if (outer.IsGenericType) return true;
            }

            return false;
        }

        private static string Pad(int indent) => new string(' ', indent * 4);
    }
}
