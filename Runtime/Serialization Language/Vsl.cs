using System;
using System.IO;
using System.Text;

namespace Vapor.Serialization
{
    /// <summary>
    /// Reads and writes the Vapor Serialization Language.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A text format for Unity whose primary author is an AI and whose secondary reader is a human.
    /// See <c>SPEC.md</c> next to this file for the language itself.
    /// </para>
    /// <example>
    /// <code>
    /// var text = Vsl.Serialize(player);
    /// var copy = Vsl.Deserialize&lt;Player&gt;(text);
    /// Vsl.Populate(existingPlayer, text);
    /// </code>
    /// </example>
    /// </remarks>
    public static class Vsl
    {
        /// <summary>The language version this build writes.</summary>
        public const int Version = 1;

        /// <summary>The conventional file extension for a VSL document.</summary>
        public const string FileExtension = ".vsl";

        #region Serialize

        public static string Serialize<T>(T value) => Serialize(value, null);

        public static string Serialize<T>(T value, VslContext context)
        {
            context ??= VslContext.Default;
            context.ResetDepth();

            var writer = new VslWriter(context);
            try
            {
                writer.WriteHeader();
                VslFormatterRegistry.Get<T>().Write(ref writer, value, context);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// Serializes through the runtime type rather than the static one. Use when all you have is
        /// <see cref="object"/>.
        /// </summary>
        public static string Serialize(object value, Type declaredType, VslContext context = null)
        {
            context ??= VslContext.Default;
            context.ResetDepth();

            var type = declaredType ?? value?.GetType();
            if (type == null)
            {
                return context.Options.EmitHeader ? $"@vsl {Version}{context.Options.NewLine}{context.Options.NewLine}null" : "null";
            }

            var writer = new VslWriter(context);
            try
            {
                writer.WriteHeader();
                VslFormatterRegistry.Get(type).WriteObject(ref writer, value, context);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        #endregion

        #region Deserialize

        public static T Deserialize<T>(string text) => Deserialize<T>(text, null);

        public static T Deserialize<T>(string text, VslContext context)
        {
            if (string.IsNullOrEmpty(text))
            {
                return default;
            }

            context ??= VslContext.Default;
            context.ResetDepth();

            var reader = new VslReader(text.AsSpan(), context);
            reader.ReadHeader();
            return VslFormatterRegistry.Get<T>().Read(ref reader, context);
        }

        public static object Deserialize(string text, Type type, VslContext context = null)
        {
            if (string.IsNullOrEmpty(text) || type == null)
            {
                return null;
            }

            context ??= VslContext.Default;
            context.ResetDepth();

            var reader = new VslReader(text.AsSpan(), context);
            reader.ReadHeader();
            return VslFormatterRegistry.Get(type).ReadObject(ref reader, context);
        }

        /// <summary>
        /// Reads a document into an existing instance, leaving members the document does not mention
        /// untouched.
        /// </summary>
        /// <remarks>
        /// This is the one that matters for loading save data into a live <c>MonoBehaviour</c> or
        /// <c>ScriptableObject</c>, where the instance already exists and must not be replaced.
        /// </remarks>
        public static void Populate(object target, string text, VslContext context = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            context ??= VslContext.Default;
            context.ResetDepth();

            var reader = new VslReader(text.AsSpan(), context);
            reader.ReadHeader();

            if (reader.TryReadTypeTag(out _))
            {
                // The tag names what wrote the document; the caller has already chosen the target.
            }

            VslReflection.Populate(ref reader, target, context);
        }

        #endregion

        #region Files

        public static void WriteToFile<T>(string path, T value, VslContext context = null)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, Serialize(value, context), new UTF8Encoding(false));
        }

        public static T ReadFromFile<T>(string path, VslContext context = null) =>
            File.Exists(path) ? Deserialize<T>(File.ReadAllText(path), context) : default;

        public static void PopulateFromFile(object target, string path, VslContext context = null)
        {
            if (File.Exists(path))
            {
                Populate(target, File.ReadAllText(path), context);
            }
        }

        #endregion
    }
}
