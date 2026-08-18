using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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

            List<IData> entries;
            try
            {
                entries = VslDataStore.ReadFromDisk(dataType);
            }
            catch (Exception e)
            {
                // Surfaced rather than thrown: the window has to stay usable so the file can be fixed
                // by hand, and an empty document here would silently overwrite it on the next save.
                Debug.LogError($"Could not read {VslDataStore.GetAssetPath(dataType)} - {e.Message}");
                return null;
            }

            return new DataDocument(dataType, entries);
        }

        public void Revert()
        {
            Entries = VslDataStore.ReadFromDisk(DataType);
            SnapshotKeys();
            IsDirty = false;
        }

        public void SetDirty() => IsDirty = true;

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
            IsDirty = true;
            return copy;
        }

        public void Remove(IData entry)
        {
            if (entry != null && Entries.Remove(entry))
            {
                IsDirty = true;
            }
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
        /// Writes the document and rebuilds the registry from it.
        /// </summary>
        /// <remarks>
        /// The rebuild is what makes the edit visible everywhere else immediately: the tag tree, the
        /// key dropdowns and the manifest generators all rebuild off
        /// <see cref="GlobalDataRegistry.OnRegistriesBuilt"/>.
        /// </remarks>
        public bool Save()
        {
            // Renames are propagated first, so the file that comes out already points at the new name —
            // including this one, which may well refer to the entry renamed in it.
            PropagateRenames();

            var absolute = VslDataStore.GetAbsolutePath(DataType);
            var existed = File.Exists(absolute);

            try
            {
                var folder = Path.GetDirectoryName(absolute);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(absolute, VslDataStore.Write(Entries), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not write {AssetPath} - {e.Message}");
                return false;
            }

            IsDirty = false;
            SnapshotKeys();

            // Brought into the asset database before rebuilding: that is what runs the importer and
            // applies the addressable label, and the rebuild is what the rest of the editor listens
            // to. A file the database has never seen needs the full refresh; after that, importing
            // the one path is enough.
            if (existed)
            {
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            }
            else
            {
                AssetDatabase.Refresh();
            }

            GlobalDataRegistry.Initialize();
            return true;
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
