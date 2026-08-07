using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
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
            var found = VaporTypeCache.GetTypesDerivedFrom<IData>()
                .Where(t => !t.IsInterface && !t.IsAbstract && root.IsAssignableFrom(t) && GetDocumentOwner(t) == root)
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

        /// <summary>Project-relative asset path of a type's document, for the asset database.</summary>
        public static string GetAssetPath(Type dataType) =>
            $"{RelativeFolder}/{GetFileName(dataType)}{Vsl.FileExtension}";

        /// <summary>Absolute path of a type's document, for plain file IO.</summary>
        public static string GetAbsolutePath(Type dataType) =>
            $"{AbsoluteFolder}/{GetFileName(dataType)}{Vsl.FileExtension}";

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

            // Nothing to read: fall back to a file name that matches a type's document.
            if (string.IsNullOrEmpty(documentName))
            {
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(documentName);
            return GetAuthoredTypes().FirstOrDefault(t =>
                string.Equals(GetFileName(t), name, StringComparison.OrdinalIgnoreCase));
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

            var entries = Vsl.Deserialize<List<IData>>(text);
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
            return Vsl.Serialize(list);
        }

        /// <summary>Reads one type's document straight off disk. Editor-time path.</summary>
        public static List<IData> ReadFromDisk(Type dataType)
        {
            if (dataType == null)
            {
                return new List<IData>();
            }

            var path = GetAbsolutePath(dataType);
            return File.Exists(path) ? Read(File.ReadAllText(path)) : new List<IData>();
        }

        /// <summary>
        /// Reads every authored type's document off disk.
        /// </summary>
        /// <remarks>
        /// This is what the editor builds the registry from. Going through the file system rather
        /// than Addressables means authoring works before any Addressables content has been built,
        /// which is the normal state of a project mid-edit.
        /// </remarks>
        public static List<(Type type, int order, List<IData> entries)> ReadAllFromDisk()
        {
            var documents = new List<(Type, int, List<IData>)>();

            foreach (var type in GetAuthoredTypes())
            {
                List<IData> entries;
                try
                {
                    entries = ReadFromDisk(type);
                }
                catch (Exception e)
                {
                    // One malformed document must not stop every other type from loading.
                    Debug.LogError($"{nameof(VslDataStore)}: could not read {GetAssetPath(type)} - {e.Message}");
                    continue;
                }

                if (entries.Count > 0)
                {
                    documents.Add((type, GetOrder(type), entries));
                }
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

            var assets = AddressableAssetUtility.LoadAll<TextAsset>(null, new object[] { AddressableLabel }, out var loaded);
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
