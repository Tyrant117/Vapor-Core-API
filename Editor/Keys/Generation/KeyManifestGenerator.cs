using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using Vapor;
using Vapor.Inspector;
using Vapor.Keys;

namespace VaporEditor.Keys
{
    /// <summary>
    /// Emits lightweight tab-separated "manifests" describing every data key, alongside the generated .cs
    /// key classes. Text files are not scripts, so regenerating them costs a trivial asset import but never a
    /// script recompile. The Vapor Keys Rider/ReSharper plugin reads these to offer autocomplete and
    /// validation for string / uint key literals without waiting on a recompile.
    ///
    /// <para>Output (under <see cref="GeneratedFolderRelative"/>):</para>
    /// <list type="bullet">
    /// <item><c>&lt;ScriptName&gt;.keys.tsv</c> — one <c>DisplayName\tKey</c> row per key, per data type.</item>
    /// <item><c>GameplayTags.keys.tsv</c> — the union of every key name plus its dotted prefixes (tag nodes).</item>
    /// <item><c>keys.index.tsv</c> — maps <c>typeFullName\ttypeSimpleName\tcategory\tscriptName\trelativePath</c>.</item>
    /// </list>
    /// Keys are read fresh from the live data assets (via <see cref="DataRegistry{TData}"/>), so newly added
    /// content appears in the manifests immediately, before/without regenerating the compiled key classes.
    /// </summary>
    public static class KeyManifestGenerator
    {
        public const string GeneratedFolderRelative = "Assets/Vapor/Keys/Definitions/Generated";
        public const string IndexFileName = "keys.index.tsv";
        public const string ManifestSuffix = ".keys.tsv";
        public const string TagsManifestName = "GameplayTags";
        public const string TagsCategory = "GameplayTags";

        /// <summary>A row in <c>keys.index.tsv</c> pointing the plugin from a call-site context to a manifest file.</summary>
        public readonly struct IndexEntry
        {
            public readonly string TypeFullName;
            public readonly string TypeSimpleName;
            public readonly string Category;
            public readonly string ScriptName;
            public readonly string RelativePath;

            public IndexEntry(string typeFullName, string typeSimpleName, string category, string scriptName, string relativePath)
            {
                TypeFullName = typeFullName;
                TypeSimpleName = typeSimpleName;
                Category = category;
                ScriptName = scriptName;
                RelativePath = relativePath;
            }
        }

        /// <summary>
        /// Regenerates all key manifests from the live data assets. Never touches the .cs key classes, so it
        /// causes no script recompile. Safe to call frequently — identical files are not rewritten.
        /// </summary>
        /// <param name="refresh">When true, calls <see cref="AssetDatabase.Refresh()"/> if any file changed.</param>
        public static void GenerateAll(bool refresh = true)
        {
            var fullFolder = FileUtility.ConvertRelativeToFullPath(GeneratedFolderRelative);
            if (!Directory.Exists(fullFolder))
            {
                Directory.CreateDirectory(fullFolder);
            }

            GlobalDataRegistry.Initialize();

            var types = GlobalDataRegistry.GetAllTypes().ToList();
            var baseTypes = types
                .Select(t => t.BaseType)
                .Where(t => t != null && t != typeof(object) && typeof(IData).IsAssignableFrom(t))
                .ToList();
            types.AddRange(baseTypes);
            types = types.Distinct().ToList();

            var index = new List<IndexEntry>();
            var tagSeen = new HashSet<string>();
            var tagRows = new List<(string name, uint key)>();
            bool wroteAny = false;

            foreach (var type in types)
            {
                var keyOptions = type.GetCustomAttribute<KeyOptionsAttribute>();
                var genericType = typeof(DataRegistry<>).MakeGenericType(type);
                var getAllMethod = genericType.GetMethod("GetAll");
                if (getAllMethod == null)
                {
                    continue;
                }

                var allData = ((IEnumerable<IData>)getAllMethod.Invoke(null, null)).ToList();
                if (allData.Count == 0)
                {
                    continue;
                }

                var rows = new List<(string name, uint key)>(allData.Count);
                foreach (var data in allData)
                {
                    if (data?.Name == null)
                    {
                        continue;
                    }

                    var kvp = KeyGenerator.StringToKeyValuePair(data.Name);
                    rows.Add((kvp.DisplayName, kvp.Key));
                    AddTagAndPrefixes(data.Name, tagSeen, tagRows);
                }

                if (rows.Count == 0)
                {
                    continue;
                }

                var (scriptName, category) = KeyGenerator.DeriveScriptAndCategory(type, keyOptions);
                var relativePath = $"{GeneratedFolderRelative}/{scriptName}{ManifestSuffix}";
                wroteAny |= WriteManifest(relativePath, rows);
                index.Add(new IndexEntry(type.FullName, type.Name, category, scriptName, relativePath));
            }

            // Fold in compiled keys that are not IData-backed (e.g. hand-authored tag registries) so gameplay-tag
            // completion stays broad. These lag a recompile, but the fresh IData rows above cover new content.
            // Guarded: a single malformed generated key class must not break manifest generation.
            try
            {
                foreach (var model in KeyUtility.GetAllDropdownModels())
                {
                    AddTagAndPrefixes(model.Name, tagSeen, tagRows);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[Vapor Keys] Skipped compiled-key fold-in for tag manifest: {e.Message}");
            }

            var tagsRelative = $"{GeneratedFolderRelative}/{TagsManifestName}{ManifestSuffix}";
            wroteAny |= WriteManifest(tagsRelative, tagRows);
            index.Add(new IndexEntry(string.Empty, "GameplayTag", TagsCategory, TagsManifestName, tagsRelative));

            wroteAny |= WriteIndex(index);

            if (refresh && wroteAny)
            {
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Adds <paramref name="name"/> and every dotted prefix ("A.B.C" -&gt; "A", "A.B", "A.B.C") to the tag
        /// row set, hashing each path segment the same way the runtime tag tree does so intermediate tag nodes
        /// resolve. Deduplicates via <paramref name="seen"/>.
        /// </summary>
        private static void AddTagAndPrefixes(string name, HashSet<string> seen, List<(string name, uint key)> rows)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var path = string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                path = i == 0 ? parts[0] : $"{path}.{parts[i]}";
                if (seen.Add(path))
                {
                    rows.Add((path, KeyGenerator.StringToKeyValuePair(path).Key));
                }
            }
        }

        private static bool WriteManifest(string relativePath, List<(string name, uint key)> rows)
        {
            var sb = new StringBuilder();
            sb.Append("# DisplayName\tKey\n");
            foreach (var (name, key) in rows.OrderBy(r => r.name, StringComparer.Ordinal))
            {
                sb.Append(Sanitize(name));
                sb.Append('\t');
                sb.Append(key);
                sb.Append('\n');
            }

            var fullPath = FileUtility.ConvertRelativeToFullPath(relativePath);
            return FileUtility.WriteAllTextIfChanged(fullPath, sb.ToString());
        }

        private static bool WriteIndex(List<IndexEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("# dataTypeFullName\tdataTypeSimpleName\tcategory\tscriptName\trelativePath\n");
            foreach (var e in entries.OrderBy(e => e.ScriptName, StringComparer.Ordinal))
            {
                sb.Append(Sanitize(e.TypeFullName));
                sb.Append('\t');
                sb.Append(Sanitize(e.TypeSimpleName));
                sb.Append('\t');
                sb.Append(Sanitize(e.Category));
                sb.Append('\t');
                sb.Append(Sanitize(e.ScriptName));
                sb.Append('\t');
                sb.Append(Sanitize(e.RelativePath));
                sb.Append('\n');
            }

            var fullPath = FileUtility.ConvertRelativeToFullPath($"{GeneratedFolderRelative}/{IndexFileName}");
            return FileUtility.WriteAllTextIfChanged(fullPath, sb.ToString());
        }

        private static string Sanitize(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            // Tabs / newlines are the field and row separators; collapse any that appear inside a value.
            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
