// =====================================================================================================
// SDK glue for reference (validation) support. Signatures align to the ReSharper Platform SDK docs for
// 2026.1 (TreeReferenceBase<T>, IReferenceProviderFactory.CreateFactory(IPsiSourceFile, IFile) + OnChanged,
// IReferenceFactory returning IReference[], ResolveErrorType.OK / NOT_RESOLVED).
//
// CONFIDENCE: the factory/solution-component wiring and the text-based context detection are high
// confidence. The resolve internals in KeyManifestReference (EmptyResolveResult.Instance,
// EmptySymbolTable.INSTANCE, ResolveResultWithInfo ctor, GetAccessContext) are the most likely to need a
// one-line tweak against your exact SDK build — if the build stops there, paste the error.
// =====================================================================================================

using System;
using System.IO;
using JetBrains.Application.Parts;
using JetBrains.DataFlow;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Resolve;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Tree;
// Instantiation
// ISignal, Signal
// Lifetime
// IWordIndex
// ISolution, SolutionComponent, GetComponent
// IPsiSourceFile, PsiLanguageType.Is, IDeclaredElement, ISubstitution, ISymbolTable, EmptySymbolTable, GetSolution
// CSharpLanguage
// ICSharpLiteralExpression, IInvocationExpression, IObjectCreationExpression
// IReference, IReferenceFactory, IReferenceProviderFactory, ReferenceProviderFactory, TreeReferenceBase, ResolveResultWithInfo, ResolveErrorType, EmptyResolveResult, IAccessContext

// ITreeNode, IFile, TreeTextRange, GetContainingNode

namespace VaporKeysPlugin
{
    /// <summary>Owns the <see cref="ManifestStore"/> for a solution, pointed at the solution's Generated folder.</summary>
    [SolutionComponent(Instantiation.DemandAnyThreadSafe)]
    public sealed class KeyManifestComponent
    {
        public ManifestStore Store { get; }

        public KeyManifestComponent(ISolution solution)
        {
            var solutionDir = solution.SolutionDirectory.FullPath;
            var generated = Path.Combine(solutionDir, "Assets", "Vapor", "Keys", "Definitions", "Generated");
            Store = new ManifestStore(generated);
        }
    }

    /// <summary>
    /// Adapts a C# literal into the SDK-agnostic facts the resolver needs, using tree TEXT rather than symbol
    /// resolution so it is robust across SDK versions. Covers the common syntactic forms:
    /// <c>DataRegistry&lt;T&gt;.Get/TryGet("...")</c> and <c>new GameplayTag("...")</c>. Implicit conversions
    /// (assignment / method-argument target types) and <c>[DataKey]</c> attributes are a follow-up (return null).
    /// </summary>
    internal sealed class CSharpLiteralFacts : KeyContextResolver.ISyntaxFacts
    {
        private readonly ICSharpExpression _expr;

        public CSharpLiteralFacts(ICSharpExpression expr) => _expr = expr;

        public bool IsStringLiteral
        {
            get
            {
                // For a [DataKey] parameter the parameter's OWN type decides string-vs-uint insertion,
                // not what the user happened to type in that slot.
                var dk = ResolveDataKey();
                if (dk.category != null || dk.typeName != null)
                {
                    return dk.paramIsString;
                }

                // ConstantValue's typed accessors (StringValue etc.) THROW InvalidCastException on a kind
                // mismatch in the 2026.1 SDK — always check IsString() first. An unguarded StringValue here
                // ran on every literal in every file and killed daemon stages mid-file (partial highlighting).
                return _expr is ICSharpLiteralExpression literal && literal.ConstantValue.IsString();
            }
        }

        public string TargetTypeSimpleName
        {
            get
            {
                var creation = _expr.GetContainingNode<IObjectCreationExpression>();
                return creation != null ? ExtractCreatedType(creation.GetText()) : null;
            }
        }

        public string DataRegistryTypeArgumentSimpleName
        {
            get
            {
                var invocation = _expr.GetContainingNode<IInvocationExpression>();
                return invocation != null ? ExtractDataRegistryTypeArg(invocation.GetText()) : null;
            }
        }

        // Attribute-driven detection: read [DataKey(...)] off the parameter this literal binds to
        // (e.g. Actor.TryGetAttribute([DataKey(typeof(AttributeData))] uint attributeId)).
        public string DataKeyCategory => ResolveDataKey().category;
        public string DataKeyTypeSimpleName => ResolveDataKey().typeName;

        private bool _dataKeyResolved;
        private (string category, string typeName, bool paramIsString) _dataKey;

        private (string category, string typeName, bool paramIsString) ResolveDataKey()
        {
            if (_dataKeyResolved)
            {
                return _dataKey;
            }

            _dataKeyResolved = true;

            var argument = CSharpArgumentNavigator.GetByValue(_expr);
            var parameter = argument?.MatchingParameter?.Element;
            if (parameter == null)
            {
                return _dataKey;
            }

            foreach (var attr in parameter.GetAttributeInstances(AttributesSource.Self))
            {
                if (attr.GetClrName().ShortName != "DataKeyAttribute")
                {
                    continue;
                }

                var paramIsString = parameter.Type is IDeclaredType paramType
                                    && paramType.GetClrName()?.FullName == "System.String";
                var arg = attr.PositionParameter(0);
                if (arg.IsType && arg.TypeValue is IDeclaredType declaredType)
                {
                    _dataKey = (null, declaredType.GetClrName().ShortName, paramIsString);
                    break;
                }
                if (arg.IsConstant && arg.ConstantValue.IsString())
                {
                    _dataKey = (arg.ConstantValue.StringValue, null, paramIsString);
                    break;
                }
            }

            return _dataKey;
        }

        // "new  Foo.GameplayTag(\"x\")" -> "GameplayTag"
        private static string ExtractCreatedType(string creationText)
        {
            if (string.IsNullOrEmpty(creationText)) return null;
            var t = creationText.TrimStart();
            if (!t.StartsWith("new", StringComparison.Ordinal) || t.Length < 4 || !char.IsWhiteSpace(t[3])) return null;
            t = t.Substring(3).TrimStart();

            int i = 0;
            while (i < t.Length && (char.IsLetterOrDigit(t[i]) || t[i] == '_' || t[i] == '.')) i++;
            var name = t.Substring(0, i);
            var dot = name.LastIndexOf('.');
            if (dot >= 0) name = name.Substring(dot + 1);
            return name.Length > 0 ? name : null;
        }

        // "Vapor.DataRegistry<ItemData>.Get(\"x\")" -> "ItemData" (only for .Get / .TryGet calls)
        private static string ExtractDataRegistryTypeArg(string invocationText)
        {
            if (string.IsNullOrEmpty(invocationText)) return null;
            const string marker = "DataRegistry<";
            var start = invocationText.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            var end = invocationText.IndexOf('>', start);
            if (end < 0) return null;

            var after = invocationText.Substring(end + 1).TrimStart();
            if (!after.StartsWith(".Get", StringComparison.Ordinal) && !after.StartsWith(".TryGet", StringComparison.Ordinal))
                return null;

            var typeArg = invocationText.Substring(start, end - start).Trim();
            var dot = typeArg.LastIndexOf('.');
            if (dot >= 0) typeArg = typeArg.Substring(dot + 1);
            return typeArg.Length > 0 ? typeArg : null;
        }
    }

    /// <summary>Registers the reference factory. Instantiated once per solution; gets the store via DI.</summary>
    [ReferenceProviderFactory]
    public class KeyReferenceProviderFactory : IReferenceProviderFactory
    {
        private readonly KeyManifestComponent _component;

        public KeyReferenceProviderFactory(KeyManifestComponent component)
        {
            _component = component;
            Changed = new Signal<IReferenceProviderFactory>("VaporKeys.ReferenceProviderFactory.Changed");
        }

        public IReferenceFactory CreateFactory(IPsiSourceFile sourceFile, IFile file, IWordIndex wordIndexForChecks)
        {
            return sourceFile.PrimaryPsiLanguage.Is<CSharpLanguage>() ? new KeyReferenceFactory(_component.Store) : null;
        }

        // Fired to force re-evaluation of references. We never change at runtime (the store polls the manifest
        // files itself), so this signal is created but never raised.
        public ISignal<IReferenceProviderFactory> Changed { get; }
    }

    /// <summary>Attaches a <see cref="KeyManifestReference"/> to literals that sit in a key context.</summary>
    public class KeyReferenceFactory : IReferenceFactory
    {
        private readonly ManifestStore _store;

        public KeyReferenceFactory(ManifestStore store) => _store = store;

        public ReferenceCollection GetReferences(ITreeNode element, ReferenceCollection oldReferences)
        {
            // This runs on every literal of every file; a single escaped exception aborts the calling
            // daemon/search pass for the whole file. Cancellation must still flow, everything else degrades
            // to "no reference".
            try
            {
                if (element is ICSharpLiteralExpression literal)
                {
                    var ctx = KeyContextResolver.Resolve(new CSharpLiteralFacts(literal), _store);
                    if (ctx.IsKey)
                        return new ReferenceCollection(new KeyManifestReference(literal, _store));
                }
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception)
            {
                // Swallow: completion/validation quietly degrade rather than breaking the IDE.
            }

            return ReferenceCollection.Empty;
        }

        public bool HasReference(ITreeNode element, IReferenceNameContainer names)
        {
            return element is ICSharpLiteralExpression;
        }
    }

    /// <summary>Resolves iff the literal's value is a known key; unknown → NOT_RESOLVED (configure the severity as a warning).</summary>
    public class KeyManifestReference(ICSharpLiteralExpression owner, ManifestStore store) : TreeReferenceBase<ICSharpLiteralExpression>(owner)
    {
        public override ResolveResultWithInfo ResolveWithoutCache()
        {
            try
            {
                var ctx = KeyContextResolver.Resolve(new CSharpLiteralFacts(myOwner), store);
                var name = LiteralName();
                if (!ctx.IsKey || ctx.Mode != KeyInsertMode.String || name == null)
                    return new ResolveResultWithInfo(EmptyResolveResult.Instance, ResolveErrorType.OK);

                // v1 scope: validate string keys. uint-literal validation lands with the uint completion/inlay pass.
                return new ResolveResultWithInfo(EmptyResolveResult.Instance,
                    ctx.Set.ContainsName(name) ? ResolveErrorType.OK : ResolveErrorType.NOT_RESOLVED);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception)
            {
                return new ResolveResultWithInfo(EmptyResolveResult.Instance, ResolveErrorType.OK);
            }
        }

        public override string GetName() => LiteralName() ?? "???";

        // ConstantValue typed accessors throw InvalidCastException on kind mismatch (2026.1) — guard first.
        private string LiteralName()
        {
            var value = myOwner.ConstantValue;
            return value.IsString() ? value.StringValue : null;
        }

        public override TreeTextRange GetTreeTextRange() => myOwner.GetTreeTextRange();

        public override ISymbolTable GetReferenceSymbolTable(bool useReferenceName) => EmptySymbolTable.INSTANCE;

        public override IReference BindTo(IDeclaredElement element) => this;

        public override IReference BindTo(IDeclaredElement element, ISubstitution substitution) => this;

        public override IAccessContext GetAccessContext() => null;
    }
}
