using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using Vapor;
using Vapor.Serialization;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// One VSL document — everything of one type, and of the types beneath it — while it is open for
    /// editing.
    /// </summary>
    /// <remarks>
    /// Owns the in-memory entries and the dirty flag, and is the only thing that touches the file.
    /// Every mutation goes through here so the window never has to know how an entry is constructed,
    /// copied, or named — all of which are reflection problems, because <see cref="IData"/> types keep
    /// their state behind private setters.
    /// </remarks>
    public sealed class DataDocument
    {
        /// <summary>The member every <see cref="IData"/> has, and the one the list is keyed on.</summary>
        private const string NameMember = "name";

        public Type DataType { get; }

        public List<IData> Entries { get; private set; }

        public bool IsDirty { get; private set; }

        public string AssetPath => VslDataStore.GetAssetPath(DataType);

        public string DisplayName => VslDataStore.GetDisplayName(DataType);

        /// <summary>
        /// Each entry's key as it was last written, so a rename can be spotted by comparing.
        /// </summary>
        /// <remarks>
        /// Keyed by reference: two entries can legitimately share a name mid-edit, and the question
        /// here is what happened to <em>this</em> entry rather than to one that looks like it.
        /// </remarks>
        private readonly Dictionary<IData, uint> _keysWhenLoaded = new Dictionary<IData, uint>(EntryIdentity.Instance);

        /// <summary>
        /// Which shard file each entry belongs to.
        /// </summary>
        /// <remarks>
        /// Populated from where an entry was read and consulted when it is written, which is the whole
        /// of what makes shard assignment sticky. Keyed by reference for the same reason as
        /// <see cref="_keysWhenLoaded"/>: the question is about this object, not one that looks like it.
        /// </remarks>
        private readonly Dictionary<IData, string> _shardOf = new Dictionary<IData, string>(EntryIdentity.Instance);

        /// <summary>Shards known to differ from disk, so a save only has to consider these.</summary>
        private readonly HashSet<string> _dirtyShards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Entries added since the last save, which have no shard yet.
        /// </summary>
        /// <remarks>
        /// Tracked so an edit to one of them is recognised as "not placed yet" rather than as "not from
        /// this document". The two look identical from <see cref="_shardOf"/> alone, and they need
        /// opposite answers: the first is already going to be written, the second means the assumption
        /// behind targeted marking does not hold and every shard has to be reconsidered.
        /// </remarks>
        private readonly HashSet<IData> _pendingPlacement = new HashSet<IData>(EntryIdentity.Instance);

        /// <summary>
        /// Set when something changed without saying where, so every shard is reconsidered.
        /// </summary>
        /// <remarks>
        /// The safe answer, and the one an untargeted <see cref="SetDirty()"/> gets. Reconsidering a
        /// shard still compares its text before writing, so over-marking costs a serialize and never a
        /// write; under-marking would lose an edit, which is why the doubt resolves this way.
        /// </remarks>
        private bool _allShardsDirty;

        /// <summary>
        /// How many files this document's entries are currently spread across.
        /// </summary>
        /// <remarks>
        /// Answered from the in-memory assignment rather than by listing the folder, because the status
        /// line asks on every keystroke.
        /// </remarks>
        public int ShardCount
        {
            get
            {
                if (_shardOf.Count == 0)
                {
                    return 1;
                }

                var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var shard in _shardOf.Values)
                {
                    distinct.Add(shard);
                }

                return distinct.Count;
            }
        }

        /// <summary>
        /// Identity by reference, since <see cref="ReferenceEqualityComparer"/> is not available on
        /// this runtime.
        /// </summary>
        [NoAutoStaticsCleanup]
        private sealed class EntryIdentity : IEqualityComparer<IData>
        {
            public static readonly EntryIdentity Instance = new EntryIdentity();

            public bool Equals(IData x, IData y) => ReferenceEquals(x, y);

            public int GetHashCode(IData obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private DataDocument(Type dataType, List<IData> entries)
        {
            DataType = dataType;
            Entries = entries;
            SnapshotKeys();
        }

        private void SnapshotKeys()
        {
            _keysWhenLoaded.Clear();
            foreach (var entry in Entries)
            {
                if (entry != null)
                {
                    _keysWhenLoaded[entry] = entry.Key;
                }
            }
        }

        /// <summary>
        /// Entries whose key has changed since the document was last read or written.
        /// </summary>
        /// <remarks>
        /// Collected at save rather than as the name is typed. A rename rewrites references across
        /// every document in the project, and doing that per keystroke would rewrite the whole project
        /// once per character.
        /// </remarks>
        internal void CollectRenames(List<DataRenameService.Rename> into)
        {
            foreach (var entry in Entries)
            {
                if (entry == null || !_keysWhenLoaded.TryGetValue(entry, out var previous))
                {
                    continue;
                }

                if (previous != 0 && entry.Key != 0 && previous != entry.Key)
                {
                    into.Add(new DataRenameService.Rename(previous, entry.Key, entry.Name));
                }
            }
        }

        public static DataDocument Load(Type dataType)
        {
            if (dataType == null)
            {
                return null;
            }

            var document = new DataDocument(dataType, new List<IData>());
            return document.Read() ? document : null;
        }

        /// <summary>Reads every shard, recording where each entry came from. False when unreadable.</summary>
        private bool Read()
        {
            _shardOf.Clear();
            _pendingPlacement.Clear();

            try
            {
                Entries = VslDataStore.ReadFromDisk(DataType, _shardOf);
            }
            catch (Exception e)
            {
                // Surfaced rather than thrown: the window has to stay usable so the file can be fixed
                // by hand, and an empty document here would silently overwrite it on the next save.
                Debug.LogError($"Could not read {VslDataStore.GetAssetPath(DataType)} - {e.Message}");
                return false;
            }

            SnapshotKeys();
            _dirtyShards.Clear();
            _allShardsDirty = false;
            IsDirty = false;
            return true;
        }

        /// <summary>
        /// Throws away the in-memory edits and reads the document back off disk.
        /// </summary>
        /// <remarks>
        /// The registry is re-pointed at what was just read, because a save hands it these very
        /// objects rather than copies of them. That is what makes an edit visible everywhere the moment
        /// it is saved — and it means the objects a revert discards are objects the registry is holding,
        /// so leaving them there would keep answering lookups with changes the file does not have.
        /// </remarks>
        public void Revert()
        {
            if (Read())
            {
                GlobalDataRegistry.ReplaceDocument(DataType, Entries);
            }
        }

        /// <summary>Marks the document changed without saying which shard, so all are reconsidered.</summary>
        public void SetDirty()
        {
            IsDirty = true;
            _allShardsDirty = true;
        }

        /// <summary>
        /// Marks the shard holding <paramref name="entry"/> changed, so a save leaves the rest alone.
        /// </summary>
        /// <remarks>
        /// The path an inspector edit takes. Editing one field of one entry should cost one file
        /// rewrite no matter how large the document has grown, and that is only possible if the edit
        /// says which entry it touched.
        /// </remarks>
        public void SetDirty(IData entry)
        {
            IsDirty = true;

            if (entry == null)
            {
                _allShardsDirty = true;
                return;
            }

            if (_shardOf.TryGetValue(entry, out var shard))
            {
                _dirtyShards.Add(shard);
                return;
            }

            // Added since the last save, so it has no shard yet; the one it lands in is marked once the
            // save has placed it.
            if (_pendingPlacement.Contains(entry))
            {
                return;
            }

            // Some other document's entry, or one this document does not know it holds. Nothing useful
            // can be said about which file it belongs to, so nothing is assumed about the others either.
            _allShardsDirty = true;
        }

        #region Mutation

        /// <summary>
        /// Creates an entry of <paramref name="concreteType"/>, or of this document's type when none
        /// is given, and appends it.
        /// </summary>
        public IData Add(string name, Type concreteType = null)
        {
            var type = concreteType ?? DataType;

            // nonPublic: true — a data type's parameterless constructor exists for the serializer and
            // is not meant to be part of its public surface.
            if (Activator.CreateInstance(type, true) is not IData entry)
            {
                Debug.LogError($"{type.Name} could not be created as an {nameof(IData)}.");
                return null;
            }

            SetName(entry, name);

            // Finished the way a read entry is, so a fresh entry and a loaded one are the same kind of
            // object to whatever draws or spawns them. The contract makes this idempotent.
            (entry as IDataLoadCallback)?.OnDataLoaded();

            Entries.Add(entry);
            _pendingPlacement.Add(entry);
            IsDirty = true;
            return entry;
        }

        /// <summary>
        /// Deep-copies an entry by round-tripping it through VSL.
        /// </summary>
        /// <remarks>
        /// Copying through the serializer rather than by reflection means a duplicate is exactly what
        /// the original would have been after a save and reload — including which members are carried
        /// at all — instead of a second, subtly different notion of what an entry contains.
        /// </remarks>
        public IData Duplicate(IData source)
        {
            if (source == null)
            {
                return null;
            }

            var type = source.GetType();
            var copy = RoundTrip(source, type);
            if (copy == null)
            {
                return null;
            }

            SetName(copy, MakeUniqueName(GetName(source)));
            Entries.Add(copy);
            _pendingPlacement.Add(copy);
            IsDirty = true;
            return copy;
        }

        public void Remove(IData entry)
        {
            if (entry == null || !Entries.Remove(entry))
            {
                return;
            }

            // The file it was in has to be rewritten without it, so mark that one before forgetting
            // where it lived.
            if (_shardOf.Remove(entry, out var shard))
            {
                _dirtyShards.Add(shard);
            }

            _pendingPlacement.Remove(entry);
            IsDirty = true;
        }

        /// <summary>
        /// Serializes entries as a document fragment, for the clipboard.
        /// </summary>
        /// <remarks>
        /// VSL rather than a private clipboard format, so a copy can be pasted into the <c>.vsl</c>
        /// file itself, into another project, or read by whatever produced the data in the first
        /// place. It also means text authored elsewhere pastes straight in.
        /// </remarks>
        public static string Copy(IEnumerable<IData> entries) => VslDataStore.Write(entries);

        /// <summary>
        /// Appends whatever of <paramref name="text"/> belongs in this document, renaming anything
        /// whose name is already taken. Returns what was added.
        /// </summary>
        public List<IData> Paste(string text)
        {
            var added = new List<IData>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return added;
            }

            List<IData> parsed;
            try
            {
                parsed = VslDataStore.Read(text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"The clipboard does not hold readable {Vsl.FileExtension} data - {e.Message}");
                return added;
            }

            foreach (var entry in parsed)
            {
                // Entries of another type are skipped rather than rejected wholesale: pasting a mixed
                // fragment should still bring in the part that belongs here.
                if (entry == null || !DataType.IsInstanceOfType(entry))
                {
                    continue;
                }

                SetName(entry, MakeUniqueName(GetName(entry)));
                Entries.Add(entry);
                _pendingPlacement.Add(entry);
                added.Add(entry);
            }

            if (added.Count > 0)
            {
                IsDirty = true;
            }

            return added;
        }

        /// <summary>
        /// Copies entries of this type that are already in <see cref="GlobalDataRegistry"/> — the ones
        /// a code registry built — into the document, skipping any key it already holds.
        /// </summary>
        /// <remarks>
        /// The first half of migrating a code registry. The second half is deleting that registry, so
        /// the two sources do not both claim the same key.
        /// </remarks>
        public int ImportRegistered()
        {
            var present = new HashSet<uint>(Entries.Select(e => e.Key));
            var imported = 0;

            foreach (var data in GlobalDataRegistry.GetAll().ToList())
            {
                if (data == null || !DataType.IsInstanceOfType(data) || !present.Add(data.Key))
                {
                    continue;
                }

                var copy = RoundTrip(data, data.GetType());
                if (copy == null)
                {
                    continue;
                }

                Entries.Add(copy);
                _pendingPlacement.Add(copy);
                imported++;
            }

            if (imported > 0)
            {
                IsDirty = true;
            }

            return imported;
        }

        /// <summary>
        /// A copy made the way the file would make it: written and read under the document profile,
        /// then finished the way a loaded entry is. Runtime-only state does not survive, exactly as
        /// it would not survive a save.
        /// </summary>
        private static IData RoundTrip(IData source, Type type)
        {
            var text = Vsl.Serialize(source, type, VslDataStore.DocumentContext);
            if (Vsl.Deserialize(text, type, VslDataStore.DocumentContext) is not IData copy)
            {
                return null;
            }

            (copy as IDataLoadCallback)?.OnDataLoaded();
            return copy;
        }

        #endregion

        #region Names

        /// <summary>
        /// Reads and writes the <c>Name</c> member through the VSL schema.
        /// </summary>
        /// <remarks>
        /// <see cref="IData.Name"/> is read-only by design — on <c>GameplayTagData</c> its setter is
        /// what derives <see cref="IData.Key"/> — so the editor cannot assign it directly. The schema
        /// is the right way in: it is the same accessor the serializer uses, so renaming in the window
        /// and renaming in the file do exactly the same thing.
        /// </remarks>
        public static string GetName(IData entry) => entry?.Name ?? string.Empty;

        public static bool SetName(IData entry, string name)
        {
            if (entry == null)
            {
                return false;
            }

            var member = VslTypeSchema.Get(entry.GetType()).Find(NameMember.AsSpan());
            if (member == null)
            {
                Debug.LogError($"{entry.GetType().Name} has no serialized '{NameMember}' member, so it cannot be renamed here.");
                return false;
            }

            member.SetValue(entry, name);
            return true;
        }

        private string MakeUniqueName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = DataType.Name;
            }

            var taken = new HashSet<string>(Entries.Select(GetName), StringComparer.Ordinal);
            if (!taken.Contains(baseName))
            {
                return baseName;
            }

            for (var i = 1; ; i++)
            {
                var candidate = $"{baseName}.{i}";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>
        /// A name that is not already used, for a newly added entry, under the branch the type
        /// declares in <see cref="DataAuthoringAttribute.NamePrefix"/>.
        /// </summary>
        /// <param name="concreteType">
        /// The type being added. A family shares its root's prefix, so the concrete type only matters
        /// when it declares one of its own.
        /// </param>
        public string NextNewName(Type concreteType = null)
        {
            var type = concreteType ?? DataType;
            var prefix = VslDataStore.GetAuthoring(type) != null
                ? VslDataStore.GetNamePrefix(type)
                : VslDataStore.GetNamePrefix(DataType);

            return MakeUniqueName($"{prefix}.New");
        }

        #endregion

        #region Saving

        /// <summary>
        /// Writes the shards that changed and hands the result back to the registry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three things keep this cheap as a project grows. Only shards holding an edited entry are
        /// serialized, and a shard whose text turns out identical is not written at all. Only a file the
        /// asset database has never seen is imported, so the usual save never enters the import pipeline.
        /// And only this document is re-ingested, through
        /// <see cref="GlobalDataRegistry.ReplaceDocument"/>, rather than every source being reloaded to
        /// discover that none of the others moved.
        /// </para>
        /// <para>
        /// The edit is visible everywhere else immediately all the same: the tag tree and the key
        /// dropdowns drop their caches on
        /// <see cref="GlobalDataRegistry.OnRegistryChanged"/> and rebuild when next read.
        /// </para>
        /// </remarks>
        public bool Save() => Write(false);

        /// <summary>
        /// Re-packs the document from the first shard onward, then saves.
        /// </summary>
        /// <remarks>
        /// Sticky assignment never moves an entry, so deleting a lot of content leaves shards standing
        /// half empty and new entries keep landing in the first one with room rather than filling the
        /// gaps. This is the deliberate correction: one large diff, run when it is wanted, instead of
        /// small ones creeping in on every save.
        /// </remarks>
        public bool Rebalance() => Write(true);

        private bool Write(bool rebalance)
        {
            VslSaveDiagnostics.Begin(rebalance ? $"Rebalance {DisplayName}" : $"Save {DisplayName}");
            try
            {
                // Renames are propagated first, so the files that come out already point at the new name —
                // including these, which may well refer to the entry renamed in them.
                using (VslSaveDiagnostics.Measure("renames"))
                {
                    PropagateRenames();
                }

                List<(string Shard, List<IData> Entries)> plan;
                var newlyPlaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (VslSaveDiagnostics.Measure("assign"))
                {
                    if (rebalance)
                    {
                        plan = VslDataStore.RebalanceShards(DataType, Entries, _shardOf);
                        _allShardsDirty = true;
                    }
                    else
                    {
                        // Noted before the assignment runs, because afterwards every entry has a shard
                        // and there is no way to tell which ones just got one.
                        var unassigned = new List<IData>();
                        foreach (var entry in Entries)
                        {
                            if (entry != null && !_shardOf.ContainsKey(entry))
                            {
                                unassigned.Add(entry);
                            }
                        }

                        plan = VslDataStore.AssignShards(DataType, Entries, _shardOf);

                        // Whatever the assignment just placed is new to its file, so that file is dirty
                        // even though nobody said so when the entry was created.
                        foreach (var entry in unassigned)
                        {
                            if (_shardOf.TryGetValue(entry, out var placed))
                            {
                                newlyPlaced.Add(placed);
                            }
                        }
                    }
                }

                var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var created = new List<string>();

                using (VslSaveDiagnostics.Measure("write"))
                {
                    foreach (var (shard, entries) in plan)
                    {
                        planned.Add(shard);

                        // The first shard is always written, even empty: it is the document's file, and a
                        // project that has deleted everything should still have one rather than appearing
                        // never to have had any data.
                        var isPrimary = string.Equals(shard, VslDataStore.GetShardName(DataType, 0), StringComparison.OrdinalIgnoreCase);
                        if (entries.Count == 0 && !isPrimary)
                        {
                            continue;
                        }

                        if (!_allShardsDirty && !_dirtyShards.Contains(shard) && !newlyPlaced.Contains(shard)
                            && File.Exists(VslDataStore.GetShardAbsolutePath(shard)))
                        {
                            continue;
                        }

                        try
                        {
                            if (VslDataStore.WriteShard(shard, entries, out var madeFile) && madeFile)
                            {
                                created.Add(shard);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Could not write {VslDataStore.GetShardAssetPath(shard)} - {e.Message}");
                            return false;
                        }
                    }
                }

                using (VslSaveDiagnostics.Measure("prune"))
                {
                    DeleteEmptiedShards(plan, planned);
                }

                IsDirty = false;
                _dirtyShards.Clear();
                _pendingPlacement.Clear();
                _allShardsDirty = false;
                SnapshotKeys();

                // Only a file the asset database has never seen needs telling about. An existing shard
                // whose bytes changed is picked up by the next refresh, and forcing that import here cost
                // a synchronous round trip through the importer plus an addressables settings write on
                // every save — for a label that was already on the entry and could not have come off.
                if (created.Count > 0)
                {
                    using (VslSaveDiagnostics.Measure("import"))
                    {
                        VslDataStore.InvalidateShardIndex();
                        AssetDatabase.Refresh();
                    }
                }

                // One document changed, so one document is re-ingested. The code registries and the
                // addressable assets cannot have been affected by writing a .vsl, and re-reading every
                // other document to find that out is what a save used to spend most of its time on.
                GlobalDataRegistry.ReplaceDocument(DataType, Entries);
                return true;
            }
            finally
            {
                VslSaveDiagnostics.End();
            }
        }

        /// <summary>Removes shard files this document no longer puts anything in.</summary>
        private void DeleteEmptiedShards(List<(string Shard, List<IData> Entries)> plan, HashSet<string> planned)
        {
            var primary = VslDataStore.GetShardName(DataType, 0);

            var empty = new List<string>();
            foreach (var (shard, entries) in plan)
            {
                if (entries.Count == 0 && !string.Equals(shard, primary, StringComparison.OrdinalIgnoreCase))
                {
                    empty.Add(shard);
                }
            }

            // A file that backed this document before the save but is in no part of the plan is gone the
            // same way - it held entries that have since been deleted.
            foreach (var shard in VslDataStore.EnumerateShardNames(DataType))
            {
                if (!planned.Contains(shard) && !string.Equals(shard, primary, StringComparison.OrdinalIgnoreCase))
                {
                    empty.Add(shard);
                }
            }

            if (empty.Count == 0)
            {
                return;
            }

            foreach (var shard in empty)
            {
                var assetPath = VslDataStore.GetShardAssetPath(shard);
                var absolute = VslDataStore.GetShardAbsolutePath(shard);
                if (!File.Exists(absolute))
                {
                    continue;
                }

                if (!AssetDatabase.DeleteAsset(assetPath))
                {
                    // Not in the database - a file written this session and never imported. Take the meta
                    // with it, or the next refresh resurrects an entry for something that is not there.
                    try
                    {
                        File.Delete(absolute);
                        if (File.Exists(absolute + ".meta"))
                        {
                            File.Delete(absolute + ".meta");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Could not remove the emptied shard {assetPath} - {e.Message}");
                    }
                }
            }

            VslDataStore.InvalidateShardIndex();
        }

        /// <summary>
        /// Repoints every reference to an entry whose name changed since this document was read.
        /// </summary>
        private void PropagateRenames()
        {
            var renames = new List<DataRenameService.Rename>();
            CollectRenames(renames);

            if (renames.Count == 0)
            {
                return;
            }

            var rewritten = DataRenameService.Propagate(renames, new[] { this });
            if (rewritten > 0)
            {
                var noun = rewritten == 1 ? "reference" : "references";
                var renamed = string.Join(", ", renames.Select(r => r.NewName));
                Debug.Log($"Repointed {rewritten} {noun} after renaming {renamed}.");
            }
        }

        #endregion
    }
}
