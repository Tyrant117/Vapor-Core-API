using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Vapor;
using Vapor.GameplayTags;
using Vapor.Serialization;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Keeps references pointing at an entry that has been renamed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Data refers to data by name — a <see cref="GameplayTag"/> is the hash of one — because a name is
    /// legible in a document and something a model can write from context, which an opaque id is not.
    /// The cost of that choice is that renaming an entry silently orphans everything pointing at it.
    /// This pays that cost off at the point it is incurred: a rename rewrites the references to it, the
    /// same way renaming a symbol rewrites its call sites.
    /// </para>
    /// <para>
    /// Only <see cref="GameplayTag"/> and <see cref="GameplayTagContainer"/> are rewritten. A tag is a
    /// name hash by definition, so replacing one is unambiguous; a bare <c>uint</c> may hold a key or
    /// may hold a count, and rewriting one on the chance it is a reference would corrupt the other.
    /// A type that wants its references maintained should declare them as tags.
    /// </para>
    /// </remarks>
    internal static class DataRenameService
    {
        /// <summary>Guards against a reference cycle in a hand-authored document.</summary>
        private const int MaxDepth = 12;

        /// <summary>One entry's key before and after a rename.</summary>
        internal readonly struct Rename
        {
            public readonly uint OldKey;
            public readonly uint NewKey;
            public readonly string NewName;

            public Rename(uint oldKey, uint newKey, string newName)
            {
                OldKey = oldKey;
                NewKey = newKey;
                NewName = newName;
            }
        }

        /// <summary>
        /// Rewrites every reference to a renamed entry, in memory and on disk.
        /// </summary>
        /// <param name="renames">What was renamed.</param>
        /// <param name="openDocuments">
        /// Documents the window currently has open. Their entries are rewritten in place and marked
        /// dirty rather than being read from disk, so unsaved edits are not thrown away.
        /// </param>
        /// <returns>How many references were rewritten.</returns>
        public static int Propagate(IReadOnlyList<Rename> renames, IReadOnlyList<DataDocument> openDocuments)
        {
            if (renames == null || renames.Count == 0)
            {
                return 0;
            }

            var rewritten = 0;
            var openTypes = new HashSet<Type>();

            foreach (var document in openDocuments ?? Array.Empty<DataDocument>())
            {
                openTypes.Add(document.DataType);

                var changed = RewriteAll(document.Entries, renames);
                if (changed > 0)
                {
                    // Untargeted on purpose: a rename can land in any entry, so every shard of an open
                    // document has to be reconsidered.
                    document.SetDirty();
                    rewritten += changed;
                }
            }

            // Everything else is read, rewritten and written back only if it actually referred to the
            // renamed entry - so an unrelated document is never rewritten just because a rename happened.
            // Skipped by type rather than by path: a document is however many files it is spread over,
            // and all of them are the open window's to write.
            foreach (var type in VslDataStore.GetAuthoredTypes())
            {
                if (!openTypes.Contains(type))
                {
                    rewritten += RewriteOnDisk(type, renames);
                }
            }

            return rewritten;
        }

        /// <summary>
        /// Rewrites a closed document's references, one shard at a time.
        /// </summary>
        /// <remarks>
        /// Per shard rather than per document so a rename costs the files that actually mention the
        /// renamed entry, not every file of every type that mentions it once.
        /// </remarks>
        private static int RewriteOnDisk(Type dataType, IReadOnlyList<Rename> renames)
        {
            var total = 0;

            foreach (var shard in VslDataStore.EnumerateShardNames(dataType))
            {
                var assetPath = VslDataStore.GetShardAssetPath(shard);

                List<IData> entries;
                try
                {
                    entries = VslDataStore.ReadShard(shard);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Could not check {assetPath} for renamed references - {e.Message}");
                    continue;
                }

                var changed = RewriteAll(entries, renames);
                if (changed == 0)
                {
                    continue;
                }

                try
                {
                    File.WriteAllText(VslDataStore.GetShardAbsolutePath(shard), VslDataStore.Write(entries),
                        new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    Debug.LogError($"Could not update references in {assetPath} - {e.Message}");
                    continue;
                }

                total += changed;
            }

            return total;
        }

        #region Walking

        private static int RewriteAll(IEnumerable<IData> entries, IReadOnlyList<Rename> renames)
        {
            var changed = 0;
            foreach (var entry in entries ?? Array.Empty<IData>())
            {
                if (entry != null)
                {
                    changed += Rewrite(entry, entry.GetType(), renames, 0);
                }
            }

            return changed;
        }

        /// <summary>
        /// Walks an object's serialized members, replacing any tag that names a renamed entry.
        /// </summary>
        /// <remarks>
        /// Driven by <see cref="VslTypeSchema"/> rather than by plain reflection, so it visits exactly
        /// the members a document stores. A reference the format does not persist is not a reference
        /// anything can be broken by.
        /// </remarks>
        private static int Rewrite(object target, Type type, IReadOnlyList<Rename> renames, int depth)
        {
            if (target == null || depth > MaxDepth)
            {
                return 0;
            }

            var changed = 0;

            foreach (var member in VslTypeSchema.Get(type).Members)
            {
                object value;
                try
                {
                    value = member.GetValue(target);
                }
                catch (Exception)
                {
                    continue;
                }

                if (TryRewriteValue(ref value, member.MemberType, renames, depth, out var memberChanged))
                {
                    member.SetValue(target, value);
                }

                changed += memberChanged;
            }

            return changed;
        }

        /// <summary>
        /// Rewrites one value. Returns true when the value itself was replaced and has to be written
        /// back — which a struct always does, since what was read was a copy.
        /// </summary>
        private static bool TryRewriteValue(ref object value, Type declaredType, IReadOnlyList<Rename> renames,
            int depth, out int changed)
        {
            changed = 0;

            if (value is GameplayTag tag)
            {
                foreach (var rename in renames)
                {
                    if (tag.Key == rename.OldKey)
                    {
                        value = new GameplayTag(rename.NewKey);
                        changed = 1;
                        return true;
                    }
                }

                return false;
            }

            if (value is GameplayTagContainer container)
            {
                var tags = container.Tags;
                for (var i = 0; i < tags.Count; i++)
                {
                    foreach (var rename in renames)
                    {
                        if (tags[i].Key == rename.OldKey)
                        {
                            tags[i] = new GameplayTag(rename.NewKey);
                            changed++;
                            break;
                        }
                    }
                }

                return false;
            }

            if (value is IList list && declaredType != typeof(string))
            {
                var elementType = declaredType.IsArray
                    ? declaredType.GetElementType()
                    : declaredType.IsGenericType ? declaredType.GetGenericArguments()[0] : null;

                for (var i = 0; i < list.Count; i++)
                {
                    var element = list[i];
                    if (TryRewriteValue(ref element, elementType ?? element?.GetType() ?? typeof(object), renames, depth + 1, out var elementChanged))
                    {
                        // A list of structs hands back copies, so an edited element has to be put back.
                        list[i] = element;
                    }

                    changed += elementChanged;
                }

                return false;
            }

            if (value is string || value == null || declaredType.IsPrimitive || declaredType.IsEnum)
            {
                return false;
            }

            // Anything else with serialized members of its own - a nested object, an extension, a
            // blend clip - is walked for tags the same way.
            var runtimeType = value.GetType();
            if (VslTypeSchema.Get(runtimeType).Members.Length == 0)
            {
                return false;
            }

            changed = Rewrite(value, runtimeType, renames, depth + 1);

            // Written back only for a value type, where what was walked was a copy of the original.
            return changed > 0 && runtimeType.IsValueType;
        }

        #endregion
    }
}
