using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Vapor.Inspector;
using Vapor.Keys;
using Vapor.Serialization;

namespace Vapor
{
    /// <summary>
    /// The layout of the VSL data documents: where they live, what they are called, and how a
    /// document turns into <see cref="IData"/> instances and back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One document per authored type, at <c>Assets/Vapor/Data/{TypeName}.vsl</c>. A family of types
    /// shares the document of its root, so a montage document holds one-shots and mixers together
    /// rather than splitting them across files.
    /// </para>
    /// <para>
    /// The root of each document is a sequence of <see cref="IData"/>, so every entry carries its own
    /// <c>!tag</c> — a subclass authored alongside its base type round trips as itself rather than
    /// being sliced down to the base, and a document loaded at runtime says what it holds without
    /// being told.
    /// </para>
    /// <para>
    /// Lives in the runtime assembly because both halves need it: the editor window reads and writes
    /// through here, and <see cref="GlobalDataRegistry"/> parses through here at startup.
    /// </para>
    /// </remarks>
    public static class VslDataStore
    {
        /// <summary>Project-relative folder holding every data document.</summary>
        public const string RelativeFolder = "Assets/Vapor/Data";

        /// <summary>Addressables label the documents are published under.</summary>
        public const string AddressableLabel = "VaporData";

        /// <summary>
        /// The member profile a data document is written and read under: templates. A member that
        /// belongs to another profile only — the save state of a live instance — stays out of the
        /// file and is left alone when the file is read back. Every member without a profile is in
        /// this one, so types that never mention profiles are unaffected.
        /// </summary>
        public static VslContext DocumentContext => VslContext.For(VslProfiles.Template);

        /// <summary>Absolute path of <see cref="RelativeFolder"/>. Editor-only in practice.</summary>
        public static string AbsoluteFolder =>
            Path.Combine(Application.dataPath, RelativeFolder["Assets/".Length..]).Replace('\\', '/');

        #region Types

        /// <summary>
        /// The <see cref="IData"/> types that own a document: those marked
        /// <see cref="DataAuthoringAttribute"/> with no authored ancestor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Abstract types count. A family of montages or abilities shares one body of data, and the
        /// base is what names it — the concrete subclasses are what you actually add. A document is a
        /// sequence of <see cref="IData"/> with a tag per entry, so holding several concrete types in
        /// one file is the format working as intended rather than a compromise.
        /// </para>
        /// <para>
        /// A subclass that declares its own <see cref="DataAuthoringAttribute"/> breaks away and owns
        /// its own document, which is how a type opts out of its family's file.
        /// </para>
        /// </remarks>
        public static IEnumerable<Type> GetAuthoredTypes() =>
            VaporTypeCache.GetTypesDerivedFrom<IData>()
                .Where(t => !t.IsInterface && IsAuthored(t) && GetDocumentOwner(t) == t)
                .OrderBy(GetCategory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(GetDisplayName, StringComparer.OrdinalIgnoreCase);

        private static bool IsAuthored(Type dataType) =>
            dataType != null && dataType.IsDefined(typeof(DataAuthoringAttribute), false);

        /// <summary>
        /// The type whose folder this type's entries live in: the highest authored ancestor, or the
        /// type itself.
        /// </summary>
        public static Type GetDocumentOwner(Type dataType)
        {
            if (dataType == null)
            {
                return null;
            }

            var owner = IsAuthored(dataType) ? dataType : null;
            for (var ancestor = dataType.BaseType; ancestor != null && ancestor != typeof(object); ancestor = ancestor.BaseType)
            {
                if (IsAuthored(ancestor))
                {
                    owner = ancestor;
                }
            }

            return owner ?? dataType;
        }

        /// <summary>
        /// The concrete types a document of <paramref name="root"/> can hold, in hierarchy order.
        /// </summary>
        /// <remarks>
        /// Ordered so a subclass follows the type it derives from, which is what lets the rail draw the
        /// family by indenting rather than by nesting containers.
        /// </remarks>
        public static List<Type> GetConcreteTypes(Type root)
        {
            // [IgnoreDropdown] keeps a type out of every picker, and this list feeds the pickers: it is
            // how a test fixture or an internal subclass stays out of the authoring windows while
            // still being a perfectly good member of the family at runtime.
            var found = VaporTypeCache.GetTypesDerivedFrom<IData>()
                .Where(t => !t.IsInterface && !t.IsAbstract && root.IsAssignableFrom(t) && GetDocumentOwner(t) == root
                            && !t.IsDefined(typeof(IgnoreDropdownAttribute), false))
                .ToList();

            found.Sort((a, b) =>
            {
                var byDepth = GetDepth(a, root).CompareTo(GetDepth(b, root));
                return byDepth != 0 ? byDepth : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return found;
        }

        /// <summary>How many steps <paramref name="dataType"/> sits below <paramref name="root"/>.</summary>
        public static int GetDepth(Type dataType, Type root)
        {
            var depth = 0;
            for (var t = dataType; t != null && t != root; t = t.BaseType)
            {
                depth++;
            }

            return depth;
        }

        public static DataAuthoringAttribute GetAuthoring(Type dataType) =>
            dataType?.GetCustomAttribute<DataAuthoringAttribute>(false);

        public static string GetCategory(Type dataType)
        {
            var authoring = GetAuthoring(dataType);
            if (!string.IsNullOrEmpty(authoring?.Category))
            {
                return authoring.Category;
            }

            // Falls back to the same category the generated key classes are grouped under, so the
            // rail matches the vocabulary already used elsewhere in the editor.
            var (_, category) = KeyGenerator.DeriveScriptAndCategory(dataType, dataType.GetCustomAttribute<KeyOptionsAttribute>());
            return category;
        }

        public static string GetDisplayName(Type dataType)
        {
            var authoring = GetAuthoring(dataType);
            return string.IsNullOrEmpty(authoring?.DisplayName) ? dataType.Name : authoring.DisplayName;
        }

        public static int GetOrder(Type dataType) => GetAuthoring(dataType)?.Order ?? 0;

        /// <summary>
        /// The dotted prefix a new entry of this type is named under.
        /// </summary>
        public static string GetNamePrefix(Type dataType)
        {
            var authoring = GetAuthoring(dataType);
            var prefix = authoring?.NamePrefix;
            return string.IsNullOrWhiteSpace(prefix) ? dataType.Name : prefix.Trim().TrimEnd('.');
        }

        /// <summary>
        /// The colour this type is marked with in the editor.
        /// </summary>
        /// <remarks>
        /// Declared as a hex string, or derived from the type name when none is given. Derived rather
        /// than defaulted to grey so a project that never sets one still gets a stable, distinct
        /// colour per type instead of a rail of identical bars.
        /// </remarks>
        public static Color GetColor(Type dataType)
        {
            var declared = GetAuthoring(dataType)?.Color;
            if (!string.IsNullOrWhiteSpace(declared) &&
                ColorUtility.TryParseHtmlString(declared.StartsWith("#") ? declared : "#" + declared, out var parsed))
            {
                return parsed;
            }

            // Hue from the name, with fixed saturation and value so every derived colour reads as part
            // of one palette rather than an arbitrary spread.
            var hue = (dataType?.Name.GetHashCode() ?? 0) & 0x7FFFFFFF;
            return Color.HSVToRGB(hue % 360 / 360f, 0.55f, 0.85f);
        }

        #endregion

        #region Paths

        /// <summary>The document a type's entries live in, without the extension.</summary>
        public static string GetFileName(Type dataType)
        {
            var authoring = GetAuthoring(dataType);
            return string.IsNullOrWhiteSpace(authoring?.FileName) ? dataType.Name : authoring.FileName;
        }

        /// <summary>Project-relative asset path of a type's first shard, for the asset database.</summary>
        /// <remarks>
        /// Shard zero is named exactly as the whole document used to be, so this keeps answering what
        /// it always answered: the path a project with one file's worth of data actually has.
        /// </remarks>
        public static string GetAssetPath(Type dataType) => GetShardAssetPath(GetFileName(dataType));

        /// <summary>Absolute path of a type's first shard, for plain file IO.</summary>
        public static string GetAbsolutePath(Type dataType) => GetShardAbsolutePath(GetFileName(dataType));

        /// <summary>
        /// Works out which authored type a loaded document belongs to.
        /// </summary>
        /// <remarks>
        /// The entries are the reliable answer: the first one's type is walked upward looking for the
        /// attribute, which covers a document holding a whole family of subclasses. The file name is
        /// only the fallback, for a document that is empty and so says nothing about itself.
        /// </remarks>
        public static Type ResolveOwningType(string documentName, IReadOnlyList<IData> entries)
        {
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    for (var type = entry.GetType(); type != null && type != typeof(object); type = type.BaseType)
                    {
                        if (type.IsDefined(typeof(DataAuthoringAttribute), false))
                        {
                            return type;
                        }
                    }
                }
            }

            // Nothing to read: fall back to a file name that matches a type's document, including the
            // numbered shards of one.
            if (string.IsNullOrEmpty(documentName))
            {
                return null;
            }

            // Only the real extension is stripped, and only when it is there. GetFileNameWithoutExtension
            // would take the last dotted segment whatever it is, which reads 'GameplayTagData.Weapons' as
            // 'GameplayTagData' - claiming a freely named file by its prefix rather than by what is in
            // it, and getting it wrong the moment such a file holds a different type.
            var name = Path.GetFileName(documentName);
            if (name.EndsWith(Vsl.FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^Vsl.FileExtension.Length];
            }

            return ResolveConventionalOwner(name);
        }

        #endregion

        #region Shards

        /// <summary>
        /// How large a shard is allowed to get before new entries start a fresh one.
        /// </summary>
        /// <remarks>
        /// A ceiling on the cost of one save, not on the size of the data. Saving rewrites whole files,
        /// so what matters is how much has to be serialized and written to record a one-field edit -
        /// and the answer should not grow with the size of the project. The limit is deliberately soft:
        /// an entry is never split across two files, so a single entry larger than this simply gets a
        /// shard of its own.
        /// </remarks>
        public const int ShardSizeLimit = 256 * 1024;

        /// <summary>The name of a type's <paramref name="index"/>'th conventional shard.</summary>
        /// <remarks>
        /// Shard zero is the bare document name. That is what makes this change invisible to a project
        /// that never grows past one file - and it keeps the addressable address of the existing
        /// documents exactly as it was.
        /// </remarks>
        public static string GetShardName(Type dataType, int index) =>
            index <= 0 ? GetFileName(dataType) : $"{GetFileName(dataType)}.{index}";

        /// <summary>Project-relative asset path of a shard, for the asset database.</summary>
        public static string GetShardAssetPath(string shardName) =>
            $"{RelativeFolder}/{shardName}{Vsl.FileExtension}";

        /// <summary>Absolute path of a shard, for plain file IO.</summary>
        public static string GetShardAbsolutePath(string shardName) =>
            $"{AbsoluteFolder}/{shardName}{Vsl.FileExtension}";

        /// <summary>
        /// The type a file name claims by convention, or null when the name claims nothing.
        /// </summary>
        /// <remarks>
        /// An exact match wins before the numbered form is considered, so a type whose file name
        /// genuinely ends in <c>.1</c> owns its own file rather than being read as another type's
        /// second shard.
        /// </remarks>
        private static Type ResolveConventionalOwner(string stem)
        {
            if (string.IsNullOrEmpty(stem))
            {
                return null;
            }

            foreach (var type in GetAuthoredTypes())
            {
                if (string.Equals(GetFileName(type), stem, StringComparison.OrdinalIgnoreCase))
                {
                    return type;
                }
            }

            var split = stem.LastIndexOf('.');
            if (split <= 0 || !int.TryParse(stem[(split + 1)..], out var index) || index < 1)
            {
                return null;
            }

            var root = stem[..split];
            foreach (var type in GetAuthoredTypes())
            {
                if (string.Equals(GetFileName(type), root, StringComparison.OrdinalIgnoreCase))
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>The shard index a conventional name carries. Zero for the bare document name.</summary>
        private static int GetShardIndex(string stem)
        {
            var split = stem.LastIndexOf('.');
            return split > 0 && int.TryParse(stem[(split + 1)..], out var index) && index >= 1 ? index : 0;
        }

        /// <summary>
        /// Owners worked out by parsing, for files whose names claim nothing.
        /// </summary>
        /// <remarks>
        /// Keyed by file name and validated against the file's stamp and length, so a hand-authored or
        /// generated document is parsed once to find out what it holds rather than on every listing.
        /// </remarks>
        private static readonly Dictionary<string, (long Stamp, long Length, Type Owner)> s_AdoptedOwners = new();

        /// <summary>
        /// Every <c>.vsl</c> in the data folder, paired with the type that owns it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Conventional shards are recognised by name, which costs nothing. Anything else - a file
        /// dropped in by hand, or written out of a prompt - is parsed to find out which type its
        /// entries belong to, and then belongs to that type's document like any other shard. That is
        /// what lets authored content arrive from outside the windows without a separate import step.
        /// </para>
        /// <para>
        /// Ordered so a type's shards come out conventional-first by index, then adopted files by name.
        /// The order decides nothing about where existing entries live - those stay in the file they
        /// were read from - only which shard a new entry is offered first.
        /// </para>
        /// </remarks>
        private static List<(Type Owner, string Shard)> IndexFolder()
        {
            var index = new List<(Type Owner, string Shard)>();
            var folder = AbsoluteFolder;
            if (!Directory.Exists(folder))
            {
                return index;
            }

            var conventional = new List<(Type Owner, string Shard, int Index)>();
            var adopted = new List<(Type Owner, string Shard)>();

            foreach (var path in Directory.GetFiles(folder, "*" + Vsl.FileExtension))
            {
                var stem = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(stem))
                {
                    continue;
                }

                var owner = ResolveConventionalOwner(stem);
                if (owner != null)
                {
                    conventional.Add((owner, stem, GetShardIndex(stem)));
                    continue;
                }

                owner = ResolveAdoptedOwner(path, stem);
                if (owner != null)
                {
                    adopted.Add((owner, stem));
                }
            }

            conventional.Sort((a, b) =>
            {
                var byName = string.Compare(GetFileName(a.Owner), GetFileName(b.Owner), StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.Index.CompareTo(b.Index);
            });
            adopted.Sort((a, b) => string.Compare(a.Shard, b.Shard, StringComparison.OrdinalIgnoreCase));

            foreach (var (owner, shard, _) in conventional)
            {
                index.Add((owner, shard));
            }

            index.AddRange(adopted);
            return index;
        }

        /// <summary>Parses a file whose name claims nothing, to find out which type owns it.</summary>
        private static Type ResolveAdoptedOwner(string absolutePath, string stem)
        {
            long stamp;
            long length;
            try
            {
                var info = new FileInfo(absolutePath);
                stamp = info.LastWriteTimeUtc.Ticks;
                length = info.Length;
            }
            catch (Exception)
            {
                return null;
            }

            if (s_AdoptedOwners.TryGetValue(stem, out var cached) && cached.Stamp == stamp && cached.Length == length)
            {
                return cached.Owner;
            }

            Type owner;
            try
            {
                owner = ResolveOwningType(stem, Read(File.ReadAllText(absolutePath)));
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(VslDataStore)}: could not read {GetShardAssetPath(stem)} - {e.Message}");
                owner = null;
            }

            s_AdoptedOwners[stem] = (stamp, length, owner);
            return owner;
        }

        /// <summary>
        /// The shard files backing a type's document, in the order new entries are offered them.
        /// </summary>
        public static List<string> EnumerateShardNames(Type dataType)
        {
            var shards = new List<string>();
            if (dataType == null)
            {
                return shards;
            }

            foreach (var (owner, shard) in IndexFolder())
            {
                if (owner == dataType)
                {
                    shards.Add(shard);
                }
            }

            return shards;
        }

        /// <summary>Forgets what was learned by parsing, so the next listing looks again.</summary>
        /// <remarks>Only adopted files are cached; conventional ones are recognised by name every time.</remarks>
        public static void InvalidateShardIndex() => s_AdoptedOwners.Clear();

        /// <summary>One shard's entries. An absent file reads as empty.</summary>
        public static List<IData> ReadShard(string shardName)
        {
            var path = GetShardAbsolutePath(shardName);
            return File.Exists(path) ? Read(File.ReadAllText(path)) : new List<IData>();
        }

        /// <summary>The size of a shard on disk, or zero when it does not exist.</summary>
        public static long GetShardLength(string shardName)
        {
            try
            {
                var info = new FileInfo(GetShardAbsolutePath(shardName));
                return info.Exists ? info.Length : 0L;
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        /// <summary>
        /// Which shard each entry belongs in: the one it came from, and for a new entry the first with
        /// room.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Assignment is sticky. An entry stays in the file it was read from for as long as it exists,
        /// so adding one near the front of a document does not cascade every entry after it into a
        /// different file - which is what a plain sequential pack does, and it turns a one-line edit
        /// into a diff the size of the project. The cost of stickiness is that shards drift away from
        /// full as entries are deleted, which <see cref="ShardSizeLimit"/> tolerates and an explicit
        /// rebalance is there to correct.
        /// </para>
        /// <para>
        /// Only unassigned entries are measured, because only they need placing. The rest are accounted
        /// for by the length of the file they are already in.
        /// </para>
        /// </remarks>
        /// <param name="dataType">The document's owning type.</param>
        /// <param name="entries">The document's entries, in order.</param>
        /// <param name="shardOf">
        /// The known assignment, read and updated in place. Entries missing from it are placed.
        /// </param>
        /// <returns>The plan: every shard that will hold something, with its entries in document order.</returns>
        public static List<(string Shard, List<IData> Entries)> AssignShards(
            Type dataType, IReadOnlyList<IData> entries, IDictionary<IData, string> shardOf)
        {
            var known = EnumerateShardNames(dataType);
            if (known.Count == 0)
            {
                known.Add(GetShardName(dataType, 0));
            }

            var order = new List<string>(known);
            var byShard = new Dictionary<string, List<IData>>(StringComparer.OrdinalIgnoreCase);
            var projected = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var shard in known)
            {
                byShard[shard] = new List<IData>();
                projected[shard] = GetShardLength(shard);
            }

            var unplaced = new List<IData>();
            foreach (var entry in entries ?? Array.Empty<IData>())
            {
                if (entry == null)
                {
                    continue;
                }

                if (shardOf != null && shardOf.TryGetValue(entry, out var shard) && byShard.ContainsKey(shard))
                {
                    byShard[shard].Add(entry);
                    continue;
                }

                unplaced.Add(entry);
            }

            foreach (var entry in unplaced)
            {
                var size = MeasureEntry(entry);
                var target = FirstShardWithRoom(order, projected, size);
                if (target == null)
                {
                    target = NextShardName(dataType, order);
                    order.Add(target);
                    byShard[target] = new List<IData>();
                    projected[target] = 0L;
                }

                projected[target] += size;
                byShard[target].Add(entry);
                if (shardOf != null)
                {
                    shardOf[entry] = target;
                }
            }

            // Members are re-sorted into document order rather than left in the order the loops above
            // happened to produce: an entry placed into an earlier shard has to land in that shard's
            // list where the document puts it, or a save would reorder the file for no reason.
            var position = new Dictionary<IData, int>(entries?.Count ?? 0, EntryIdentity.Instance);
            for (var i = 0; entries != null && i < entries.Count; i++)
            {
                if (entries[i] != null)
                {
                    position[entries[i]] = i;
                }
            }

            var plan = new List<(string Shard, List<IData> Entries)>();
            foreach (var shard in order)
            {
                var members = byShard[shard];
                if (members.Count > 1)
                {
                    members.Sort((a, b) => position[a].CompareTo(position[b]));
                }

                plan.Add((shard, members));
            }

            return plan;
        }

        /// <summary>
        /// Re-packs a document's entries from the first shard onward, ignoring where they are now.
        /// </summary>
        /// <remarks>
        /// The counterpart to sticky assignment: deletions leave shards half full and nothing moves an
        /// entry on its own, so this exists to be run deliberately when the layout has drifted far
        /// enough to be worth one large diff.
        /// </remarks>
        public static List<(string Shard, List<IData> Entries)> RebalanceShards(
            Type dataType, IReadOnlyList<IData> entries, IDictionary<IData, string> shardOf)
        {
            shardOf?.Clear();

            var plan = new List<(string Shard, List<IData> Entries)>();
            var current = new List<IData>();
            var used = 0L;
            var index = 0;

            foreach (var entry in entries ?? Array.Empty<IData>())
            {
                if (entry == null)
                {
                    continue;
                }

                var size = MeasureEntry(entry);
                if (current.Count > 0 && used + size > ShardSizeLimit)
                {
                    plan.Add((GetShardName(dataType, index++), current));
                    current = new List<IData>();
                    used = 0L;
                }

                current.Add(entry);
                used += size;
            }

            plan.Add((GetShardName(dataType, index), current));

            if (shardOf != null)
            {
                foreach (var (shard, members) in plan)
                {
                    foreach (var entry in members)
                    {
                        shardOf[entry] = shard;
                    }
                }
            }

            return plan;
        }

        /// <summary>Identity by reference, since <c>ReferenceEqualityComparer</c> is not on this runtime.</summary>
        private sealed class EntryIdentity : IEqualityComparer<IData>
        {
            public static readonly EntryIdentity Instance = new EntryIdentity();

            public bool Equals(IData x, IData y) => ReferenceEquals(x, y);

            public int GetHashCode(IData obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static string FirstShardWithRoom(List<string> order, Dictionary<string, long> projected, long size)
        {
            foreach (var shard in order)
            {
                // An empty shard takes whatever it is given, so an entry larger than the limit gets a
                // file of its own rather than no home at all.
                var current = projected[shard];
                if (current == 0L || current + size <= ShardSizeLimit)
                {
                    return shard;
                }
            }

            return null;
        }

        /// <summary>The lowest unused conventional shard name for a type.</summary>
        private static string NextShardName(Type dataType, List<string> taken)
        {
            for (var index = 1; ; index++)
            {
                var candidate = GetShardName(dataType, index);
                if (!taken.Contains(candidate) && !File.Exists(GetShardAbsolutePath(candidate)))
                {
                    return candidate;
                }
            }
        }

        /// <summary>Roughly how many bytes an entry adds to a document.</summary>
        private static long MeasureEntry(IData entry)
        {
            try
            {
                return Encoding.UTF8.GetByteCount(Vsl.Serialize(entry, entry.GetType(), DocumentContext));
            }
            catch (Exception)
            {
                // A measurement is not worth failing a save over; an unmeasurable entry is treated as
                // small and lands wherever there is room.
                return 0L;
            }
        }

        /// <summary>
        /// Writes a shard, and only if its text actually differs from what is already there.
        /// </summary>
        /// <param name="created">True when the file did not exist and has just been made.</param>
        /// <returns>True when the file was created or rewritten.</returns>
        public static bool WriteShard(string shardName, IReadOnlyList<IData> entries, out bool created)
        {
            var absolute = GetShardAbsolutePath(shardName);
            created = !File.Exists(absolute);

            var text = Write(entries);
            if (!created)
            {
                try
                {
                    // Compared rather than written blind: an unchanged shard must not be touched, or the
                    // asset database reimports it and writing one file instead of all of them buys
                    // nothing.
                    if (string.Equals(File.ReadAllText(absolute), text, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                catch (Exception)
                {
                    // Unreadable: fall through and overwrite it.
                }
            }

            var folder = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(absolute, text, new UTF8Encoding(false));
            return true;
        }

        #endregion

        #region Documents

        /// <summary>Parses a document. Returns an empty list for empty or absent text.</summary>
        public static List<IData> Read(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<IData>();
            }

            var entries = Vsl.Deserialize<List<IData>>(text, DocumentContext);
            if (entries == null)
            {
                return new List<IData>();
            }

            // A '!tag' naming a type that no longer exists reads as null rather than throwing, and a
            // null in the registry would only surface later as a confusing failure.
            entries.RemoveAll(e => e == null);

            // Entries arrive with only what the document held. Anything derived from those members has
            // to be computed now, before the entry is handed to a registry or an editor.
            foreach (var entry in entries)
            {
                if (entry is IDataLoadCallback callback)
                {
                    try
                    {
                        callback.OnDataLoaded();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"{entry.GetType().Name} '{entry.Name}' failed to finish loading - {e.Message}");
                    }
                }
            }

            return entries;
        }

        /// <summary>Serializes entries into a document.</summary>
        public static string Write(IEnumerable<IData> entries)
        {
            var list = entries as List<IData> ?? new List<IData>(entries ?? Enumerable.Empty<IData>());
            return Vsl.Serialize(list, DocumentContext);
        }

        /// <summary>Reads one type's document - every shard of it - straight off disk. Editor-time path.</summary>
        public static List<IData> ReadFromDisk(Type dataType) => ReadFromDisk(dataType, null);

        /// <summary>
        /// Reads one type's document, recording which shard each entry came from.
        /// </summary>
        /// <param name="shardOf">
        /// Receives the origin of every entry read. That record is what makes assignment sticky: the
        /// file an entry came from is the file it goes back to.
        /// </param>
        public static List<IData> ReadFromDisk(Type dataType, Dictionary<IData, string> shardOf)
        {
            var all = new List<IData>();
            if (dataType == null)
            {
                return all;
            }

            foreach (var shard in EnumerateShardNames(dataType))
            {
                foreach (var entry in ReadShard(shard))
                {
                    if (shardOf != null)
                    {
                        shardOf[entry] = shard;
                    }

                    all.Add(entry);
                }
            }

            return all;
        }

        /// <summary>
        /// Reads every authored type's document off disk.
        /// </summary>
        /// <remarks>
        /// This is what the editor builds the registry from. Going through the file system rather
        /// than Addressables means authoring works before any Addressables content has been built,
        /// which is the normal state of a project mid-edit. The folder is walked once and grouped
        /// rather than asked type by type, so a file whose name claims nothing is still read and
        /// attributed to whatever its entries turn out to be.
        /// </remarks>
        public static List<(Type type, int order, List<IData> entries)> ReadAllFromDisk()
        {
            var byType = new Dictionary<Type, List<IData>>();
            var seen = new List<Type>();

            foreach (var (owner, shard) in IndexFolder())
            {
                List<IData> entries;
                try
                {
                    entries = ReadShard(shard);
                }
                catch (Exception e)
                {
                    // One malformed document must not stop every other type from loading.
                    Debug.LogError($"{nameof(VslDataStore)}: could not read {GetShardAssetPath(shard)} - {e.Message}");
                    continue;
                }

                if (entries.Count == 0)
                {
                    continue;
                }

                if (!byType.TryGetValue(owner, out var bucket))
                {
                    bucket = new List<IData>();
                    byType.Add(owner, bucket);
                    seen.Add(owner);
                }

                bucket.AddRange(entries);
            }

            var documents = new List<(Type, int, List<IData>)>();
            foreach (var type in seen)
            {
                documents.Add((type, GetOrder(type), byType[type]));
            }

            return documents;
        }

        /// <summary>
        /// Loads every published document through Addressables. The player path.
        /// </summary>
        /// <param name="handles">Receives the load handles so the caller can release them.</param>
        public static List<(Type type, int order, List<IData> entries)> ReadAllFromAddressables(
            List<AsyncOperationHandle<TextAsset>> handles)
        {
            var documents = new List<(Type, int, List<IData>)>();

            var assets = AddressableAssetUtility.LoadAll<TextAsset>(new object[] { AddressableLabel }, out var loaded);
            if (assets == null)
            {
                return documents;
            }

            if (loaded != null)
            {
                handles?.AddRange(loaded);
            }

            foreach (var asset in assets)
            {
                if (asset == null)
                {
                    continue;
                }

                try
                {
                    var entries = Read(asset.text);
                    if (entries.Count == 0)
                    {
                        continue;
                    }

                    var type = ResolveOwningType(asset.name, entries);
                    documents.Add((type, type == null ? 0 : GetOrder(type), entries));
                }
                catch (Exception e)
                {
                    Debug.LogError($"{nameof(VslDataStore)}: could not read document '{asset.name}' - {e.Message}");
                }
            }

            return documents;
        }

        /// <summary>
        /// Every data document, in whichever way this context can reach them.
        /// </summary>
        /// <remarks>
        /// The editor reads from disk even in play mode: the files are the source of truth, and using
        /// them directly means edits take effect without rebuilding Addressables content. The
        /// Addressables path is what a build runs, and the label it depends on is applied on import by
        /// <c>VslDataPostprocessor</c>, so the two cannot drift apart.
        /// </remarks>
        public static List<(Type type, int order, List<IData> entries)> ReadAll(
            List<AsyncOperationHandle<TextAsset>> handles)
        {
#if UNITY_EDITOR
            return ReadAllFromDisk();
#else
            return ReadAllFromAddressables(handles);
#endif
        }

        #endregion
    }
}
