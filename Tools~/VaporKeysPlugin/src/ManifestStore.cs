using System;
using System.Collections.Generic;
using System.IO;

namespace VaporKeysPlugin
{
    /// <summary>A single key: its human display name and the uint hash the runtime uses.</summary>
    public readonly struct KeyEntry(string name, uint key)
    {
        public readonly string Name = name;
        public readonly uint Key = key;
    }

    /// <summary>An immutable, indexed set of keys (one manifest, or a merge of manifests sharing a category).</summary>
    public sealed class KeySet
    {
        public static readonly KeySet Empty = new KeySet(Array.Empty<KeyEntry>());

        public IReadOnlyList<KeyEntry> Entries { get; }
        private readonly Dictionary<string, uint> _byName;
        private readonly Dictionary<uint, string> _byKey;

        public KeySet(IReadOnlyList<KeyEntry> entries)
        {
            Entries = entries;
            _byName = new Dictionary<string, uint>(entries.Count, StringComparer.Ordinal);
            _byKey = new Dictionary<uint, string>(entries.Count);
            foreach (var e in entries)
            {
                _byName[e.Name] = e.Key;
                _byKey[e.Key] = e.Name;
            }
        }

        public bool ContainsName(string name) => name != null && _byName.ContainsKey(name);
        public bool ContainsKey(uint key) => _byKey.ContainsKey(key);
        public bool TryGetKey(string name, out uint key) => _byName.TryGetValue(name ?? string.Empty, out key);
        public bool TryGetName(uint key, out string name) => _byKey.TryGetValue(key, out name);
    }

    /// <summary>
    /// Loads and caches the Vapor key manifests emitted by Unity under
    /// <c>Assets/Vapor/Keys/Definitions/Generated/</c>. Parses once and reloads only when a manifest's
    /// last-write time changes, so IDE hot paths (Resolve/completion/inlay) are pure dictionary lookups
    /// and never touch the disk. All members are safe to call from any thread and never throw into the IDE.
    /// </summary>
    public sealed class ManifestStore(string generatedFolderFullPath)
    {
        public const string INDEX_FILE_NAME = "keys.index.tsv";
        public const string TAGS_SIMPLE_NAME = "GameplayTag";
        public const string TAGS_CATEGORY = "GameplayTags";

        private readonly object _gate = new object();

        // Current snapshot (swapped atomically under _gate).
        private Dictionary<string, KeySet> _byDataType = new Dictionary<string, KeySet>(StringComparer.Ordinal);
        private Dictionary<string, KeySet> _byCategory = new Dictionary<string, KeySet>(StringComparer.OrdinalIgnoreCase);
        private KeySet _tags = KeySet.Empty;
        private KeyEntry[] _allDataEntries = Array.Empty<KeyEntry>();
        private Dictionary<string, KeySet> _prefixCache = new Dictionary<string, KeySet>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<uint, string> _globalByKey = new Dictionary<uint, string>();
        private Dictionary<string, DateTime> _fileTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private bool _loadedOnce;

        /// <summary>Keys for a <c>DataRegistry&lt;T&gt;</c> generic argument, matched by the type's simple name (e.g. "AttributeData").</summary>
        public KeySet GetByDataTypeSimpleName(string simpleName)
        {
            EnsureFresh();
            lock (_gate)
            {
                return simpleName != null && _byDataType.TryGetValue(simpleName, out var set) ? set : KeySet.Empty;
            }
        }

        /// <summary>Keys for a <c>[DataKey("...")]</c> category (e.g. "Attributes").</summary>
        public KeySet GetByCategory(string category)
        {
            EnsureFresh();
            lock (_gate)
            {
                return category != null && _byCategory.TryGetValue(category, out var set) ? set : KeySet.Empty;
            }
        }

        /// <summary>
        /// Keys whose display name sits under a dotted name prefix, e.g. "Attribute" →
        /// "Attribute.Armor.Melee". Drawn from the real per-type data keys only — never the synthetic
        /// tag-hierarchy nodes — so a <c>[DataKey("Attribute")]</c> uint parameter completes exactly the
        /// keys that share that prefix. A name equal to the prefix itself also matches; "Attributes" does not.
        /// </summary>
        public KeySet GetByNamePrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return KeySet.Empty;
            }

            EnsureFresh();
            lock (_gate)
            {
                if (_prefixCache.TryGetValue(prefix, out var cached))
                {
                    return cached;
                }

                var entries = new List<KeyEntry>();
                foreach (var e in _allDataEntries)
                {
                    if (IsUnderPrefix(prefix, e.Name))
                    {
                        entries.Add(e);
                    }
                }

                var set = entries.Count > 0 ? new KeySet(entries) : KeySet.Empty;
                _prefixCache[prefix] = set;
                return set;
            }
        }

        // "Attribute" covers "Attribute" and "Attribute.Armor", but not "Attributes".
        private static bool IsUnderPrefix(string prefix, string name)
        {
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   && (name.Length == prefix.Length || name[prefix.Length] == '.');
        }

        /// <summary>The full gameplay-tag set (every key name plus its dotted prefixes).</summary>
        public KeySet GetTags()
        {
            EnsureFresh();
            lock (_gate)
            {
                return _tags;
            }
        }

        /// <summary>Reverse lookup used by inlay hints: the display name for a uint key across all sets.</summary>
        public bool TryGetNameForKey(uint key, out string name)
        {
            EnsureFresh();
            lock (_gate)
            {
                return _globalByKey.TryGetValue(key, out name);
            }
        }

        /// <summary>Reloads the manifests if the folder's index or any tracked file changed on disk. Cheap when unchanged.</summary>
        public void EnsureFresh()
        {
            if (_loadedOnce && !HasChangedOnDisk())
            {
                return;
            }

            lock (_gate)
            {
                if (_loadedOnce && !HasChangedOnDisk())
                {
                    return;
                }

                try
                {
                    Reload();
                }
                catch
                {
                    // Never propagate IO/parse failures into the IDE; keep the previous snapshot.
                }

                _loadedOnce = true;
            }
        }

        private bool HasChangedOnDisk()
        {
            try
            {
                var indexPath = Path.Combine(generatedFolderFullPath, INDEX_FILE_NAME);
                if (!File.Exists(indexPath))
                {
                    return _fileTimes.Count != 0; // manifests disappeared
                }

                lock (_gate)
                {
                    // Index changed?
                    if (!_fileTimes.TryGetValue(indexPath, out var known) || known != File.GetLastWriteTimeUtc(indexPath))
                    {
                        return true;
                    }

                    // Any tracked manifest changed / removed?
                    foreach (var kvp in _fileTimes)
                    {
                        if (!File.Exists(kvp.Key) || File.GetLastWriteTimeUtc(kvp.Key) != kvp.Value)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void Reload()
        {
            var byDataType = new Dictionary<string, KeySet>(StringComparer.Ordinal);
            var categoryEntries = new Dictionary<string, List<KeyEntry>>(StringComparer.OrdinalIgnoreCase);
            var globalByKey = new Dictionary<uint, string>();
            var fileTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            var tags = KeySet.Empty;
            var loadedSets = new Dictionary<string, KeySet>(StringComparer.OrdinalIgnoreCase);
            var allDataEntries = new List<KeyEntry>();
            var allDataNames = new HashSet<string>(StringComparer.Ordinal);

            var indexPath = Path.Combine(generatedFolderFullPath, INDEX_FILE_NAME);
            if (File.Exists(indexPath))
            {
                fileTimes[indexPath] = File.GetLastWriteTimeUtc(indexPath);

                foreach (var line in ReadRows(indexPath))
                {
                    // dataTypeFullName, dataTypeSimpleName, category, scriptName, relativePath
                    var cols = line.Split('\t');
                    if (cols.Length < 5)
                    {
                        continue;
                    }

                    var simpleName = cols[1];
                    var category = cols[2];
                    var relativePath = cols[4];
                    var manifestPath = ResolveManifestPath(relativePath);

                    if (!loadedSets.TryGetValue(manifestPath, out var set))
                    {
                        set = LoadManifest(manifestPath, fileTimes);
                        loadedSets[manifestPath] = set;
                    }

                    if (!string.IsNullOrEmpty(simpleName))
                    {
                        byDataType[simpleName] = set;
                    }

                    if (!string.IsNullOrEmpty(category))
                    {
                        if (!categoryEntries.TryGetValue(category, out var list))
                        {
                            list = new List<KeyEntry>();
                            categoryEntries[category] = list;
                        }

                        list.AddRange(set.Entries);
                    }

                    bool isTagsUnion = string.Equals(simpleName, TAGS_SIMPLE_NAME, StringComparison.Ordinal);
                    if (isTagsUnion || string.Equals(category, TAGS_CATEGORY, StringComparison.OrdinalIgnoreCase))
                    {
                        tags = set;
                    }

                    // The prefix pool holds only real data keys; the tags union re-lists them plus
                    // synthetic hierarchy nodes ("Attribute", "Attribute.Armor") that are not valid ids.
                    if (!isTagsUnion)
                    {
                        foreach (var e in set.Entries)
                        {
                            if (allDataNames.Add(e.Name))
                            {
                                allDataEntries.Add(e);
                            }
                        }
                    }

                    foreach (var e in set.Entries)
                    {
                        globalByKey[e.Key] = e.Name;
                    }
                }
            }

            var byCategory = new Dictionary<string, KeySet>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in categoryEntries)
            {
                byCategory[kvp.Key] = new KeySet(kvp.Value);
            }

            // Swap in the new snapshot.
            _byDataType = byDataType;
            _byCategory = byCategory;
            _tags = tags;
            _allDataEntries = allDataEntries.ToArray();
            _prefixCache = new Dictionary<string, KeySet>(StringComparer.OrdinalIgnoreCase);
            _globalByKey = globalByKey;
            _fileTimes = fileTimes;
        }

        private string ResolveManifestPath(string relativePath)
        {
            // The index stores solution-relative paths like "Assets/Vapor/Keys/Definitions/Generated/X.keys.tsv".
            // We only need the file name since every manifest lives in _generatedFolder.
            var fileName = relativePath.Replace('\\', '/');
            var slash = fileName.LastIndexOf('/');
            if (slash >= 0)
            {
                fileName = fileName.Substring(slash + 1);
            }

            return Path.Combine(generatedFolderFullPath, fileName);
        }

        private static KeySet LoadManifest(string path, Dictionary<string, DateTime> fileTimes)
        {
            if (!File.Exists(path))
            {
                return KeySet.Empty;
            }

            fileTimes[path] = File.GetLastWriteTimeUtc(path);

            var entries = new List<KeyEntry>();
            foreach (var line in ReadRows(path))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0)
                {
                    continue;
                }

                var name = line.Substring(0, tab);
                var keyText = line.Substring(tab + 1).Trim();
                if (uint.TryParse(keyText, out var key))
                {
                    entries.Add(new KeyEntry(name, key));
                }
            }

            return new KeySet(entries);
        }

        private static IEnumerable<string> ReadRows(string path)
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrEmpty(raw) || raw[0] == '#')
                {
                    continue;
                }

                yield return raw;
            }
        }
    }
}