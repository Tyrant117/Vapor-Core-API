using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vapor;
using Vapor.Serialization;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Builds the block of text the <c>Copy Prompt</c> button puts on the clipboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// VSL was designed for an AI to author, and this is the other half of that: the model needs to
    /// know which file to edit, what shape it is, and what is currently in it. Everything here is
    /// something it would otherwise have to be told by hand or guess at.
    /// </para>
    /// <para>
    /// The entries are included as real VSL rather than as a description of them. The writer emits the
    /// <c>[VslComment]</c> documentation alongside every member, so the pasted block doubles as the
    /// schema — which is the property the format was built around.
    /// </para>
    /// </remarks>
    public static class DataPromptBuilder
    {
        private const string SpecPath = "Assets/Vapor Serialization Language/Runtime/SPEC.md";

        public static string Build(DataDocument document, IReadOnlyList<IData> selection,
            IReadOnlyCollection<string> focusedFields = null)
        {
            if (document == null)
            {
                return string.Empty;
            }

            var type = document.DataType;
            var sb = new StringBuilder();

            sb.AppendLine("I'm editing Vapor game data, stored in VSL (Vapor Serialization Language).");
            sb.AppendLine();

            sb.AppendLine($"Data type: {VslDataStore.GetDisplayName(type)}  ({type.FullName})");
            sb.AppendLine($"File: {document.AssetPath}");

            // Said plainly because it changes what a useful answer looks like: a model asked to add a
            // hundred entries can put them in a file of their own rather than restating an existing one.
            if (document.ShardCount > 1)
            {
                sb.AppendLine($"This document is spread across {document.ShardCount} files in {VslDataStore.RelativeFolder}.");
            }

            sb.AppendLine($"New entries may go in a new .vsl file in {VslDataStore.RelativeFolder}; any file there whose");
            sb.AppendLine("entries are of this type is loaded as part of this document, whatever it is called.");

            // A family shares one document, so the model needs to know what else may legitimately
            // appear in it and which tag names those entries.
            var concrete = VslDataStore.GetConcreteTypes(type);
            if (concrete.Count > 1)
            {
                sb.AppendLine($"Entry types: {string.Join(", ", concrete.Select(c => "!" + c.Name))}");
            }

            sb.AppendLine($"Format spec: {SpecPath}");
            sb.AppendLine();

            sb.AppendLine("The file's root is a sequence of entries. Each carries its own !tag, so a subclass can");
            sb.AppendLine("sit alongside its base type. The # comments document what each member means. Members not");
            sb.AppendLine("mentioned keep their current value, and an unknown member is ignored rather than failing.");
            sb.AppendLine();

            var subject = selection is { Count: > 0 } ? selection : document.Entries.ToList();
            var isSelection = selection is { Count: > 0 } && selection.Count < document.Entries.Count;
            var focused = focusedFields is { Count: > 0 };

            var members = focused ? ResolveMembers(type, focusedFields) : null;
            if (focused && members.Count == 0)
            {
                // Every ticked field turned out not to be a serialized member. Writing an object with
                // no members at all would be a silently useless prompt.
                focused = false;
            }

            sb.AppendLine(isSelection
                ? $"Selected {subject.Count} of {document.Entries.Count} entries:"
                : $"All {subject.Count} entries:");

            if (focused)
            {
                sb.AppendLine();
                sb.AppendLine($"Only these fields are in scope: {string.Join(", ", members.Select(m => m.Name))}.");
                sb.AppendLine("The other members are left out of the excerpt below on purpose. Do not add them back —");
                sb.AppendLine("a member a document does not mention keeps whatever value it already had.");
            }

            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine((focused ? WriteMembers(subject, members) : VslDataStore.Write(subject)).TrimEnd());
            sb.AppendLine("```");

            if (isSelection)
            {
                sb.AppendLine();
                sb.AppendLine("These are an excerpt. Leave the rest of the file alone.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Maps the ticked member names onto the type's schema, in the order the schema declares them.
        /// </summary>
        /// <remarks>
        /// The grid ticks C# member names; the schema knows them by the name they are written under.
        /// <see cref="VslTypeSchema.Find"/> is the mapping — the same one the reader uses, so a field
        /// ticked in the editor and a member named in a document mean the same thing.
        /// </remarks>
        private static List<VslMember> ResolveMembers(Type type, IReadOnlyCollection<string> focusedFields)
        {
            var schema = VslTypeSchema.Get(type);
            var wanted = new HashSet<VslMember>();

            foreach (var path in focusedFields)
            {
                var member = schema.Find(path.AsSpan());
                if (member != null && member.IsIn(VslProfiles.Template))
                {
                    wanted.Add(member);
                }
            }

            // Declaration order, not tick order: the excerpt should read like the file it came from.
            return schema.Members.Where(wanted.Contains).ToList();
        }

        /// <summary>
        /// Writes the entries carrying only the members in scope.
        /// </summary>
        /// <remarks>
        /// A partial entry is a legal document, not a lossy one: reading it back leaves every member it
        /// does not mention alone. That is what makes a focused excerpt safe to hand to a model and
        /// paste straight back.
        /// </remarks>
        private static string WriteMembers(IReadOnlyList<IData> entries, List<VslMember> members)
        {
            // The document's own profile, so a nested object writes exactly what the file would.
            var context = VslDataStore.DocumentContext;
            var writer = new VslWriter(context);

            try
            {
                writer.WriteHeader();
                writer.BeginSequence();

                foreach (var entry in entries)
                {
                    writer.WriteTypeTag(VslTypeRegistry.GetTag(entry.GetType()));
                    writer.BeginObject();

                    foreach (var member in members)
                    {
                        if (member.Comment != null)
                        {
                            writer.WriteComment(member.Comment);
                        }

                        writer.WriteMember(member.Name);
                        member.Formatter.WriteObject(ref writer, member.GetValue(entry), context);
                    }

                    writer.EndObject();
                }

                writer.EndSequence();
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>A one-line summary of what was copied, for the console.</summary>
        public static string Describe(DataDocument document, IReadOnlyList<IData> selection)
        {
            if (document == null)
            {
                return "Nothing to copy.";
            }

            var count = selection is { Count: > 0 } ? selection.Count : document.Entries.Count;
            var noun = count == 1 ? "entry" : "entries";
            return $"Copied a prompt for {count} {VslDataStore.GetDisplayName(document.DataType)} {noun} to the clipboard.";
        }
    }
}
