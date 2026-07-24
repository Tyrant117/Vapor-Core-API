// =====================================================================================================
// SDK glue for code completion. Signatures align to the ReSharper Platform SDK docs for 2026.1:
// CSharpItemsProviderBase<CSharpCodeCompletionContext>, [Language(typeof(CSharpLanguage))],
// IsAvailable / AddLookupItems(context, GroupedItemsCollector),
// context.LookupItemsFactory.CreateTextLookupItem(...) added via collector.AddAtDefaultPlace(...).
//
// SCOPE (this pass): string-mode autocomplete (insert the display name). uint-mode insertion — insert the
// hash literal while matching/displaying by name — needs a custom lookup item and lands with inlay hints
// in the next pass.
// =====================================================================================================

using System;
using JetBrains.DocumentModel;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.Match;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure.LookupItems.Impl;
using JetBrains.ReSharper.Feature.Services.CSharp.CodeCompletion.Infrastructure;
using JetBrains.ReSharper.Features.Intellisense.CodeCompletion.CSharp.Rules;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Feature.Services.Lookup;
using JetBrains.TextControl;
using JetBrains.UI.RichText;
using JetBrains.Util;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure;
using JetBrains.ReSharper.Features.Inspections.Resources;

// GetComponent
// GroupedItemsCollector
// TextLookupItem
// CSharpItemsProviderBase, CSharpCodeCompletionContext
// [Language], GetSolution
// CSharpLanguage
// ICSharpLiteralExpression

// GetContainingNode

namespace VaporKeysPlugin
{
    /// <summary>Offers data-key display names for string literals sitting in a recognized key context.</summary>
    [Language(typeof(CSharpLanguage))]
    public class KeyCompletionProvider : CSharpItemsProviderBase<CSharpCodeCompletionContext>
    {
        protected override bool IsAvailable(CSharpCodeCompletionContext context)
        {
            return ResolveContext(context).IsKey;
        }

        protected override bool AddLookupItems(CSharpCodeCompletionContext context, IItemsCollector collector)
        {
            var ctx = ResolveContext(context);
            if (!ctx.IsKey)
            {
                return false;
            }

            if (ctx.Mode == KeyInsertMode.String)
            {
                // Insert a fully-quoted "name" (replacing the whole string token via the engine's DEFAULT
                // ranges) and match/display the bare name. A hand-built range caused Tab to over-delete.
                foreach (var entry in ctx.Set.Entries)
                {
                    var item = new StringKeyLookupItem(entry.Name);
                    item.InitializeRanges(context.CompletionRanges, context.BasicContext);
                    collector.Add(item);
                }
            }
            else
            {
                // uint context (e.g. [DataKey] uint): display/match the name, but insert the hash literal.
                foreach (var entry in ctx.Set.Entries)
                {
                    var item = new UintKeyLookupItem(entry.Name, entry.Key, ctx.Set);
                    item.InitializeRanges(context.CompletionRanges, context.BasicContext);
                    collector.Add(item);
                }
            }

            return true;
        }

        private static KeyContext ResolveContext(CSharpCodeCompletionContext context)
        {
            // Boundary: an escaped exception here disables code completion for the session's popup.
            // Cancellation must still flow; everything else degrades to "not a key context".
            try
            {
                var node = context.UnterminatedContext.TreeNode ?? context.TerminatedContext.TreeNode;
                // Match a string literal OR an identifier being typed (so uint keys can be completed by NAME in a
                // numeric slot, e.g. TryGetAttribute(Str‸) -> the hash literal).
                var expr = node?.GetContainingNode<ICSharpExpression>(true);
                if (expr == null)
                {
                    return KeyContext.None;
                }

                var store = expr.GetSolution().GetComponent<KeyManifestComponent>().Store;
                return KeyContextResolver.Resolve(new CSharpLiteralFacts(expr), store);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception)
            {
                return KeyContext.None;
            }
        }

        private static ICSharpLiteralExpression FindLiteral(CSharpCodeCompletionContext context)
        {
            var node = context.UnterminatedContext?.TreeNode ?? context.TerminatedContext?.TreeNode;
            return node?.GetContainingNode<ICSharpLiteralExpression>(true);
        }


        // Builds a completion range covering just the text inside the quotes, so inserting a key name keeps the
        // string's surrounding "" (otherwise the whole "..." token is replaced and the quotes are lost).
        // Falls back to the default ranges for verbatim / interpolated / raw string literals.
        private static TextLookupRanges GetStringContentRanges(CSharpCodeCompletionContext context)
        {
            var literal = FindLiteral(context);
            if (literal == null)
            {
                return context.CompletionRanges;
            }

            var text = literal.GetText();
            bool regular = text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"';
            bool raw = text.Length >= 3 && text[0] == '"' && text[1] == '"' && text[2] == '"';
            if (!regular || raw)
            {
                return context.CompletionRanges;
            }

            var full = literal.GetDocumentRange();
            var tr = full.TextRange;
            var content = new DocumentRange(full.Document, new TextRange(tr.StartOffset + 1, tr.EndOffset - 1));
            return new TextLookupRanges(content, content);
        }

        // A completion item for uint keys, used for [DataKey] uint parameters like Actor.TryGetAttribute.
        // Text holds the human NAME, not the hash: in Rider the FRONTEND filters typed prefixes against Text
        // (CompletionHostUtil.GetItemTextToMatch → LookupItemUtil.GetText → TextLookupItemBase.Text) — the
        // backend Match() override is never consulted while typing, so a hash-literal Text made every item
        // vanish on the first keystroke. At accept time the inserted text becomes
        // "2847362910u /* Attribute.Armor.Magic */" so the call site stays readable in diffs/reviews too.
        private sealed class UintKeyLookupItem : TextLookupItem
        {
            private readonly string _name;
            private readonly string _hashLiteral;
            private readonly KeySet _set;

            public UintKeyLookupItem(string name, uint key, KeySet set) : base(name, key.ToString())
            {
                _name = name;
                _hashLiteral = key + "u";
                _set = set;
            }

            public override void Accept(ITextControl textControl, DocumentRange nameRange,
                LookupItemInsertType insertType, Suffix suffix, ISolution solution, bool keepCaretStill)
            {
                RemoveStaleNameComment(textControl, nameRange, insertType);
                Text = _hashLiteral + " /* " + _name + " */";
                base.Accept(textControl, nameRange, insertType, suffix, solution, keepCaretStill);
            }

            // Re-completing an annotated literal replaces only the number token, so the previous
            // "/* Name */" would survive and lie about the new value. Swallow a block comment sitting
            // directly after the accept range (same line) — but only one that looks like a key name
            // (known to the set, or a bare dotted path), never prose like "/* TODO revisit */".
            private void RemoveStaleNameComment(ITextControl textControl, DocumentRange nameRange, LookupItemInsertType insertType)
            {
                var document = textControl.Document;
                var replaceRange = Ranges == null ? nameRange : Ranges.GetAcceptRange(nameRange, insertType);
                int start = replaceRange.TextRange.EndOffset;
                int max = Math.Min(document.GetTextLength(), start + 160);
                if (start >= max)
                {
                    return;
                }

                var tail = document.GetText(new TextRange(start, max));
                int i = 0;
                while (i < tail.Length && (tail[i] == ' ' || tail[i] == '\t'))
                {
                    i++;
                }

                if (i + 1 >= tail.Length || tail[i] != '/' || tail[i + 1] != '*')
                {
                    return;
                }

                int close = tail.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    return;
                }

                var inner = tail.Substring(i + 2, close - (i + 2)).Trim();
                bool looksLikeKeyName = _set.ContainsName(inner)
                                        || (inner.Length > 0 && inner.IndexOf(' ') < 0 && inner.IndexOf('.') >= 0);
                if (!looksLikeKeyName)
                {
                    return;
                }

                document.ReplaceText(new TextRange(start, start + close + 2), string.Empty);
            }
        }

        // A completion item for string keys: inserts a fully-quoted "name" (so the surrounding "" are always
        // correct, replacing the whole string token) while matching and displaying the bare name.
        private sealed class StringKeyLookupItem : TextLookupItem
        {
            private readonly string _name;

            public StringKeyLookupItem(string name) : base("\"" + name + "\"")
            {
                _name = name;
            }

            public override MatchingResult Match(PrefixMatcher prefixMatcher) => prefixMatcher.Match(_name);

            protected override RichText GetDisplayName() => new RichText(_name);
        }
    }

    /// <summary>
    /// Helper for the future uint inlay-hint provider: the display name for a raw uint key literal, or null.
    /// Wiring this into an SDK inlay-hint daemon stage is a follow-up; the lookup itself is trivial.
    /// </summary>
    public static class KeyInlayHints
    {
        public static string HintFor(uint literalValue, ManifestStore store)
        {
            return store != null && store.TryGetNameForKey(literalValue, out var name) ? name : null;
        }
    }
}