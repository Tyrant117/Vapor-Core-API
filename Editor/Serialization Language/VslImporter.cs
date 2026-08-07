using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using Vapor.Serialization;

namespace VaporEditor.Serialization
{
    /// <summary>
    /// Imports a <c>.vsl</c> document as a <see cref="TextAsset"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity only treats a fixed set of extensions — <c>.txt</c>, <c>.json</c>, <c>.bytes</c> and a
    /// handful more — as text. A <c>.vsl</c> file without an importer is a <c>DefaultAsset</c>: it
    /// cannot be marked addressable usefully and cannot be loaded at all in a player. This is what
    /// makes the documents shippable while keeping the extension the language actually uses.
    /// </para>
    /// <para>
    /// The document is also parsed at import time. A syntax error surfaces against the asset in the
    /// console, where it belongs, rather than as a registry failure at startup.
    /// </para>
    /// </remarks>
    [ScriptedImporter(Version, Extension)]
    public sealed class VslImporter : ScriptedImporter
    {
        private const int Version = 1;

        // ScriptedImporter takes the extension without its dot.
        private const string Extension = "vsl";

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text;
            try
            {
                text = File.ReadAllText(ctx.assetPath);
            }
            catch (Exception e)
            {
                ctx.LogImportError($"Could not read '{ctx.assetPath}': {e.Message}");
                text = string.Empty;
            }

            var asset = new TextAsset(text)
            {
                // The runtime matches a loaded document back to its data type by this name, so it has
                // to be the file name rather than the empty default.
                name = Path.GetFileNameWithoutExtension(ctx.assetPath),
            };

            ctx.AddObjectToAsset("text", asset);
            ctx.SetMainObject(asset);

            Validate(ctx, text);
        }

        /// <summary>
        /// Walks the document to check its syntax, binding it to no type at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deliberately structural rather than semantic. VSL is a general format — a <c>.vsl</c> file
        /// may hold a save game or any other object graph, not only a data registry document — so
        /// checking it against a particular shape would report false errors on perfectly good files.
        /// Skipping the root value proves the brackets and tokens are well formed and nothing more.
        /// </para>
        /// <para>
        /// A failure is logged, not thrown: an unparseable document still imports, so the file stays
        /// editable instead of vanishing from the project window until it is fixed.
        /// </para>
        /// </remarks>
        private static void Validate(AssetImportContext ctx, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                var reader = new VslReader(text.AsSpan(), VslContext.Default);
                reader.ReadHeader();
                reader.SkipValue();
            }
            catch (VslException e)
            {
                ctx.LogImportError($"{Path.GetFileName(ctx.assetPath)}: {e.Message}");
            }
            catch (Exception e)
            {
                ctx.LogImportError($"{Path.GetFileName(ctx.assetPath)}: {e}");
            }
        }
    }
}
