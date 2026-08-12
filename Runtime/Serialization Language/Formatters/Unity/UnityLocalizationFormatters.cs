using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes a <see cref="LocalizedString"/> as the pair that identifies it — <c>(table, entry)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this the reflection path would write <c>{}</c>: <see cref="LocalizedString"/> is a
    /// plain class rather than a <c>UnityEngine.Object</c>, and none of its state is public or
    /// carries a VSL attribute, so the member selection rules pick nothing and the binding is lost
    /// silently.
    /// </para>
    /// <para>
    /// Both halves of the reference have two forms. A table is either a collection name or a GUID,
    /// and an entry is either a key name or a numeric key id. The GUID form is written with the
    /// <c>GUID:</c> prefix Unity itself uses when it serializes the reference, so the text matches
    /// what the same value looks like in a <c>.asset</c> file.
    /// </para>
    /// </remarks>
    [NoAutoStaticsCleanup]
    public sealed class LocalizedStringFormatter : VslFormatter<LocalizedString>
    {
        private const string GuidPrefix = "GUID:";

        public static readonly LocalizedStringFormatter Instance = new LocalizedStringFormatter();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in LocalizedString value, VslContext context)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var table = Coherent(value.TableReference);
            var entry = Coherent(value.TableEntryReference);

            if (table.ReferenceType == TableReference.Type.Empty && entry.ReferenceType == TableEntryReference.Type.Empty)
            {
                writer.WriteNull();
                return;
            }

            writer.BeginTuple();
            WriteTable(ref writer, table);
            WriteEntry(ref writer, entry);
            writer.EndTuple();
        }

        /// <summary>
        /// Recovers a reference whose <c>ReferenceType</c> has gone stale.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>ReferenceType</c> is not serialized: Unity derives it in <c>OnAfterDeserialize</c> from
        /// the backing fields. Anything that writes those fields directly — an inspector editing the
        /// <c>[SerializeField]</c> members, a hand-patched asset — therefore leaves a reference that
        /// holds a real table name but still reports itself as empty, and writing it would silently
        /// discard the binding.
        /// </para>
        /// <para>
        /// Only run when the reference already claims to be empty. A populated one is coherent by
        /// construction, and re-deriving it there would destroy a GUID reference whose backing string
        /// has not been written yet.
        /// </para>
        /// </remarks>
        private static TableReference Coherent(TableReference reference)
        {
            if (reference.ReferenceType != TableReference.Type.Empty)
            {
                return reference;
            }

            reference.OnAfterDeserialize();
            return reference;
        }

        /// <inheritdoc cref="Coherent(TableReference)"/>
        private static TableEntryReference Coherent(TableEntryReference reference)
        {
            if (reference.ReferenceType != TableEntryReference.Type.Empty)
            {
                return reference;
            }

            reference.OnAfterDeserialize();
            return reference;
        }

        public override LocalizedString Read(ref VslReader reader, VslContext context)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            reader.ReadTupleStart();
            var table = ReadTable(ref reader);
            var entry = ReadEntry(ref reader);
            reader.ReadTupleEnd();

            return new LocalizedString(table, entry);
        }

        private static void WriteTable(ref VslWriter writer, in TableReference table)
        {
            switch (table.ReferenceType)
            {
                case TableReference.Type.Name:
                    writer.WriteString(table.TableCollectionName);
                    break;

                case TableReference.Type.Guid:
                    // "N" — 32 digits, no separators. The same format Unity writes.
                    writer.WriteString(GuidPrefix + table.TableCollectionNameGuid.ToString("N"));
                    break;

                default:
                    writer.WriteNull();
                    break;
            }
        }

        private static void WriteEntry(ref VslWriter writer, in TableEntryReference entry)
        {
            switch (entry.ReferenceType)
            {
                case TableEntryReference.Type.Name:
                    writer.WriteString(entry.Key);
                    break;

                case TableEntryReference.Type.Id:
                    writer.WriteInt64(entry.KeyId);
                    break;

                default:
                    writer.WriteNull();
                    break;
            }
        }

        private static TableReference ReadTable(ref VslReader reader)
        {
            if (reader.AtEnd() || reader.TryReadNull())
            {
                return default;
            }

            var name = reader.ReadString();
            if (string.IsNullOrEmpty(name))
            {
                return default;
            }

            // A GUID reference survives a table rename, so it has to round trip as one rather than
            // degrading into a collection name that happens to start with "GUID:".
            if (name.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(name.Substring(GuidPrefix.Length), out var guid))
            {
                return guid;
            }

            return name;
        }

        private static TableEntryReference ReadEntry(ref VslReader reader)
        {
            if (reader.AtEnd() || reader.TryReadNull())
            {
                return default;
            }

            if (reader.PeekKind() == VslValueKind.Number)
            {
                return reader.ReadInt64();
            }

            return reader.ReadString();
        }
    }
}
