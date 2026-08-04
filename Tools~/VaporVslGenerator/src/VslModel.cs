using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vapor.Vsl.SourceGenerator
{
    internal sealed class VslMemberModel
    {
        public string VslName;
        public string Access;      // expression relative to 'value', e.g. "_health"
        public string TypeName;    // fully qualified with global::
        public string Comment;     // [VslComment] text, or null
    }

    internal sealed class VslTypeModel
    {
        public string HintName;
        public string Namespace;
        public string TypeName;              // fully qualified with global::
        public string SimpleName;
        public bool IsValueType;
        public bool IsSealedOrValueType;
        public List<string> ContainingTypes = new List<string>();   // outermost first, e.g. "partial class Outer"
        public string TypeKeyword;                                   // "class", "struct", "record"
        public List<VslMemberModel> Members = new List<VslMemberModel>();
    }

    internal static class VslModelBuilder
    {
        public const string SerializableAttribute = "Vapor.Serialization.VslSerializableAttribute";
        public const string SerializeAttribute = "Vapor.Serialization.VslSerializeAttribute";
        public const string IgnoreAttribute = "Vapor.Serialization.VslIgnoreAttribute";
        public const string NameAttribute = "Vapor.Serialization.VslNameAttribute";
        public const string CommentAttribute = "Vapor.Serialization.VslCommentAttribute";
        public const string SerializeFieldAttribute = "UnityEngine.SerializeField";

        /// <summary>
        /// Builds the model for a type, or returns null when the type must fall back to reflection.
        /// Reports whatever the author needs to know either way.
        /// </summary>
        public static VslTypeModel Build(INamedTypeSymbol type, SourceProductionContext context)
        {
            // Abstract types are never instantiated, and open generics would need a formatter per
            // construction. Reflection covers both.
            if (type.IsAbstract || type.IsStatic || type.IsGenericType || type.TypeKind == TypeKind.Interface)
            {
                return null;
            }

            if (!IsPartialEverywhere(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VslDiagnostics.NotPartial, Location(type), type.ToDisplayString()));
                return null;
            }

            if (type.TypeKind == TypeKind.Class && !HasParameterlessConstructor(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VslDiagnostics.NoParameterlessConstructor, Location(type), type.ToDisplayString()));
                return null;
            }

            var members = CollectMembers(type, context, out var blockedBy);
            if (blockedBy != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VslDiagnostics.InaccessibleBaseMember, Location(type), blockedBy, type.ToDisplayString()));
                return null;
            }

            if (members == null)
            {
                return null;
            }

            var model = new VslTypeModel
            {
                Namespace = type.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : type.ContainingNamespace.ToDisplayString(),
                TypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                SimpleName = type.Name,
                IsValueType = type.IsValueType,
                IsSealedOrValueType = type.IsSealed || type.IsValueType,
                TypeKeyword = TypeKeywordOf(type),
                Members = members,
                HintName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty)
                    .Replace('.', '_')
                    .Replace('<', '_')
                    .Replace('>', '_'),
            };

            for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
            {
                model.ContainingTypes.Insert(0, $"partial {TypeKeywordOf(outer)} {outer.Name}");
            }

            return model;
        }

        /// <summary>
        /// Mirrors <c>VslTypeSchema.Build</c> exactly. Any divergence here shows up as generated and
        /// reflected output disagreeing, which is what the differential test exists to catch.
        /// </summary>
        private static List<VslMemberModel> CollectMembers(
            INamedTypeSymbol type, SourceProductionContext context, out string blockedBy)
        {
            blockedBy = null;

            var hierarchy = new List<INamedTypeSymbol>();
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                hierarchy.Insert(0, current);
            }

            var members = new List<VslMemberModel>();
            var claimed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var level in hierarchy)
            {
                var unityRules = HasAttribute(level, SerializableAttribute);
                var isBase = !SymbolEqualityComparer.Default.Equals(level, type);

                foreach (var symbol in level.GetMembers())
                {
                    switch (symbol)
                    {
                        case IFieldSymbol field when !field.IsImplicitlyDeclared:
                        {
                            if (!ShouldSerialize(field, unityRules))
                            {
                                continue;
                            }

                            // A formatter nested in the derived type cannot see a base type's
                            // privates; the whole type has to fall back rather than serialize a
                            // partial view of itself.
                            if (isBase && field.DeclaredAccessibility == Accessibility.Private)
                            {
                                blockedBy = $"{level.Name}.{field.Name}";
                                return null;
                            }

                            members.Add(CreateMember(field.Name, field.Type, field, claimed));
                            break;
                        }

                        case IPropertySymbol property:
                        {
                            if (!ShouldSerialize(property, context))
                            {
                                continue;
                            }

                            if (isBase && property.DeclaredAccessibility == Accessibility.Private)
                            {
                                blockedBy = $"{level.Name}.{property.Name}";
                                return null;
                            }

                            members.Add(CreateMember(property.Name, property.Type, property, claimed));
                            break;
                        }
                    }
                }
            }

            return members;
        }

        private static bool ShouldSerialize(IFieldSymbol field, bool unityRules)
        {
            if (field.IsConst || field.IsReadOnly || field.IsStatic)
            {
                return false;
            }

            if (HasAttribute(field, IgnoreAttribute))
            {
                return false;
            }

            if (HasAttribute(field, SerializeAttribute))
            {
                return true;
            }

            if (!unityRules || HasAttribute(field, "System.NonSerializedAttribute"))
            {
                return false;
            }

            return field.DeclaredAccessibility == Accessibility.Public ||
                   HasAttribute(field, SerializeFieldAttribute);
        }

        private static bool ShouldSerialize(IPropertySymbol property, SourceProductionContext context)
        {
            if (property.IsStatic || property.IsIndexer || HasAttribute(property, IgnoreAttribute))
            {
                return false;
            }

            if (!HasAttribute(property, SerializeAttribute))
            {
                return false;
            }

            if (property.GetMethod != null && property.SetMethod != null)
            {
                return true;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                VslDiagnostics.PropertyNeedsAccessors, Location(property), property.ToDisplayString()));
            return false;
        }

        private static VslMemberModel CreateMember(
            string memberName, ITypeSymbol memberType, ISymbol symbol, HashSet<string> claimed)
        {
            var rename = GetAttributeString(symbol, NameAttribute);
            var comment = GetAttributeString(symbol, CommentAttribute);

            string name;
            if (!string.IsNullOrEmpty(rename))
            {
                name = rename;
            }
            else
            {
                var normalized = ToCamelCase(StripPrefix(memberName));
                name = normalized.Length > 0 && !claimed.Contains(normalized) ? normalized : memberName;
            }

            claimed.Add(name);

            return new VslMemberModel
            {
                VslName = name,
                Access = memberName,
                TypeName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Comment = comment,
            };
        }

        public static string StripPrefix(string name)
        {
            if (name.Length >= 2 && (name[0] == 'm' || name[0] == 'M') && name[1] == '_')
            {
                return name.Substring(2);
            }

            return name.Length >= 1 && name[0] == '_' ? name.Substring(1) : name;
        }

        public static string ToCamelCase(string name)
        {
            if (name.Length == 0 || !char.IsUpper(name[0]))
            {
                return name;
            }

            if (name.Length > 1 && char.IsUpper(name[1]))
            {
                return name;
            }

            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static bool IsPartialEverywhere(INamedTypeSymbol type)
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

        private static bool HasParameterlessConstructor(INamedTypeSymbol type) =>
            type.InstanceConstructors.Any(ctor =>
                ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility != Accessibility.Private);

        private static string TypeKeywordOf(INamedTypeSymbol type)
        {
            if (type.IsRecord)
            {
                return type.IsValueType ? "record struct" : "record";
            }

            return type.IsValueType ? "struct" : "class";
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName) =>
            symbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == metadataName);

        private static string GetAttributeString(ISymbol symbol, string metadataName)
        {
            var attribute = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == metadataName);

            if (attribute == null || attribute.ConstructorArguments.Length == 0)
            {
                return null;
            }

            return attribute.ConstructorArguments[0].Value as string;
        }

        private static Location Location(ISymbol symbol) =>
            symbol.Locations.FirstOrDefault() ?? Microsoft.CodeAnalysis.Location.None;
    }
}
