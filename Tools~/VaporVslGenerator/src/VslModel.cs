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
        public uint ProfileMask = uint.MaxValue;   // [VslProfile], or every profile
        public bool DeclaredHere;                  // on the type itself rather than a base
        public ITypeSymbol Type;                   // for the clone expression

        public bool IsInEveryProfile => ProfileMask == uint.MaxValue;
    }

    internal sealed class VslTypeModel
    {
        public string HintName;
        public string Namespace;
        public string TypeName;              // fully qualified with global::
        public string SimpleName;
        public bool IsValueType;
        public bool IsSealedOrValueType;
        public bool IsAbstract;
        public List<string> ContainingTypes = new List<string>();   // outermost first, e.g. "partial class Outer"
        public string TypeKeyword;                                   // "class", "struct", "record"
        public List<VslMemberModel> Members = new List<VslMemberModel>();

        /// <summary>False when the type falls back to reflection but still gets its clone members.</summary>
        public bool EmitFormatter = true;

        // [VslCloneable]
        public bool IsCloneable;
        public bool BaseIsCloneable;
        public bool BaseDeclaresClone;      // any base has a parameterless Clone() -> 'new'
    }

    internal static class VslModelBuilder
    {
        public const string SerializableAttribute = "Vapor.Serialization.VslSerializableAttribute";
        public const string SerializeAttribute = "Vapor.Serialization.VslSerializeAttribute";
        public const string IgnoreAttribute = "Vapor.Serialization.VslIgnoreAttribute";
        public const string NameAttribute = "Vapor.Serialization.VslNameAttribute";
        public const string CommentAttribute = "Vapor.Serialization.VslCommentAttribute";
        public const string ProfileAttribute = "Vapor.Serialization.VslProfileAttribute";
        public const string CloneableAttribute = "Vapor.Serialization.VslCloneableAttribute";
        public const string SerializeFieldAttribute = "UnityEngine.SerializeField";

        /// <summary>
        /// Builds the model for a type, or returns null when there is nothing to generate. A type that
        /// must fall back to reflection for its formatter still gets a model (with
        /// <see cref="VslTypeModel.EmitFormatter"/> off) when it is <c>[VslCloneable]</c>, because the
        /// clone members are per-level and never blocked by a base's privates.
        /// </summary>
        public static VslTypeModel Build(INamedTypeSymbol type, SourceProductionContext context)
        {
            if (type.IsStatic || type.IsGenericType || type.TypeKind == TypeKind.Interface)
            {
                return null;
            }

            bool cloneable = HasAttribute(type, CloneableAttribute) && type.TypeKind == TypeKind.Class;

            // Abstract types are never instantiated, and reflection covers them; but an abstract
            // cloneable base still has to contribute its CopyFrom to the chain.
            bool emitFormatter = !type.IsAbstract;
            if (!emitFormatter && !cloneable)
            {
                return null;
            }

            if (!IsPartialEverywhere(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VslDiagnostics.NotPartial, Location(type), type.ToDisplayString()));
                return null;
            }

            if (type.TypeKind == TypeKind.Class && !type.IsAbstract && !HasParameterlessConstructor(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VslDiagnostics.NoParameterlessConstructor, Location(type), type.ToDisplayString()));
                return null;
            }

            var members = CollectMembers(type, context, out var blockedBy);
            if (members == null)
            {
                return null;
            }

            if (blockedBy != null)
            {
                if (emitFormatter)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        VslDiagnostics.InaccessibleBaseMember, Location(type), blockedBy, type.ToDisplayString()));
                }

                emitFormatter = false;
                if (!cloneable)
                {
                    return null;
                }
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
                IsAbstract = type.IsAbstract,
                TypeKeyword = TypeKeywordOf(type),
                Members = members,
                EmitFormatter = emitFormatter,
                IsCloneable = cloneable,
                BaseIsCloneable = cloneable && BaseHasAttribute(type, CloneableAttribute),
                BaseDeclaresClone = cloneable && BaseDeclaresParameterlessClone(type),
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

        public static bool BaseHasAttribute(INamedTypeSymbol type, string attribute)
        {
            for (var current = type.BaseType; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (HasAttribute(current, attribute)) return true;
            }

            return false;
        }

        private static bool BaseDeclaresParameterlessClone(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                if (HasAttribute(current, CloneableAttribute)) return true;
                if (current.GetMembers("Clone").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 0 && !m.IsStatic)) return true;
            }

            return false;
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
                            // privates; the formatter has to fall back rather than serialize a
                            // partial view of the type. (Clone members are unaffected: each level
                            // copies its own.)
                            if (isBase && field.DeclaredAccessibility == Accessibility.Private)
                            {
                                blockedBy ??= $"{level.Name}.{field.Name}";
                            }

                            var member = CreateMember(field.Name, field.Type, field, claimed);
                            member.DeclaredHere = !isBase;
                            members.Add(member);
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
                                blockedBy ??= $"{level.Name}.{property.Name}";
                            }

                            var member = CreateMember(property.Name, property.Type, property, claimed);
                            member.DeclaredHere = !isBase;
                            members.Add(member);
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
                ProfileMask = GetProfileMask(symbol),
                Type = memberType,
            };
        }

        /// <summary>The [VslProfile] mask as the runtime sees it: the enum's underlying uint, or every bit.</summary>
        private static uint GetProfileMask(ISymbol symbol)
        {
            var attribute = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ProfileAttribute);
            if (attribute == null || attribute.ConstructorArguments.Length == 0)
            {
                return uint.MaxValue;
            }

            var value = attribute.ConstructorArguments[0].Value;
            try
            {
                return System.Convert.ToUInt32(value);
            }
            catch
            {
                return uint.MaxValue;
            }
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
