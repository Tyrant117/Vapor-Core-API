using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Vapor.Unsafe;

namespace Vapor.Keys
{
    /// <summary>
    /// Generates lightweight compile-time key classes (a set of <c>public const uint</c> fields) from string
    /// names. The values are xxHash32 of the name — the same key space as <c>GameplayTag</c> / <c>IData</c> —
    /// so the generated constants are just a typed, autocompletable view over that space. The emitted classes
    /// are plain (no interfaces, no runtime dependencies); inspector pickers and the tag tree source from the
    /// data registry directly (see <c>DataKeyUtility</c>), not from these classes.
    /// </summary>
    public static class KeyGenerator
    {
        public const string RELATIVE_KEY_PATH = "Assets/Vapor/Keys/Definitions";
        public const string NAMESPACE_NAME = "VaporKeyDefinitions";
        public const string KEYS_CATEGORY_NAME = "KEY_CATEGORY";

        #region - Keys -
        /// <summary>
        /// A helper struct linking all the data of a key together
        /// </summary>
        public readonly struct KeyValuePair
        {
            public readonly string DisplayName;
            public readonly string VariableName;
            public readonly string Guid;
            public readonly uint Key;

            public bool IsValid() => !DisplayName.EmptyOrNull();

            public KeyValuePair(string name, uint key, string guid)
            {
                DisplayName = name;
                VariableName = Regex.Replace(name, " ", "").Replace(".", "_").Replace("-", "_");
                Guid = guid;
                Key = key;
            }

            public string GetFormat(int placeholderIndex)
            {
                var vName = VariableName.Length > 0 ? VariableName : "Placeholder_" + placeholderIndex;
                return $"public const uint {vName} = {Key};";
            }
        }

        /// <summary>
        /// Returns a <see cref="KeyValuePair"/> from any string.
        /// </summary>
        public static KeyValuePair StringToKeyValuePair(string key)
        {
            return new KeyValuePair(key, key.Hash32(), string.Empty);
        }

        public static KeyValuePair StringToKeyValuePair(string key, string guid)
        {
            return new KeyValuePair(key, key.Hash32(), guid);
        }

        /// <summary>
        /// Derives the generated keys-class name and category for a data <paramref name="type"/> using the same
        /// rules as the data-key generator. Shared so the compiled .cs generation and the text-manifest
        /// generation always agree on names (e.g. <c>AttributeData</c> -&gt; script <c>AttributeKeys</c>, category <c>Attributes</c>).
        /// </summary>
        public static (string scriptName, string category) DeriveScriptAndCategory(Type type, KeyOptionsAttribute keyOptions)
        {
            var scriptName = type.Name;
            scriptName = scriptName.Replace("Scriptable", "").Replace("Data", "").Replace("Key", "");
            scriptName = scriptName.EndsWith("SO") ? scriptName[..^2] : scriptName;
            scriptName = scriptName.EndsWith("So") ? scriptName[..^2] : scriptName;
            scriptName = scriptName.EndsWith("s") ? scriptName[..^1] : scriptName;

            var category = keyOptions?.Category ?? $"{scriptName}s";
            scriptName = $"{scriptName}Keys";
            return (scriptName, category);
        }

        /// <summary>
        /// The types a key set exists for: <paramref name="type"/> itself and every <see cref="IData"/>
        /// class it derives from, nearest first.
        /// </summary>
        /// <remarks>
        /// A family authored under one root — actors, say: <c>Pawn</c> and <c>PlayerController</c>
        /// under <c>Actor</c> — must generate one key class and one dropdown category for the whole
        /// family, or a picker asking for "an actor" would only ever see the entries whose runtime
        /// type is exactly <c>Actor</c>. Every level gets a set of its own, so a picker can also ask
        /// for "a pawn". Callers that used to take only the direct base type widen through here.
        /// </remarks>
        public static IEnumerable<Type> WithKeyAncestors(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.IsClass && typeof(IData).IsAssignableFrom(t))
                {
                    yield return t;
                }
            }
        }

        /// <inheritdoc cref="WithKeyAncestors(Type)"/>
        public static List<Type> WithKeyAncestors(IEnumerable<Type> types)
        {
            var seen = new HashSet<Type>();
            var result = new List<Type>();
            foreach (var type in types)
            {
                foreach (var t in WithKeyAncestors(type))
                {
                    if (seen.Add(t))
                    {
                        result.Add(t);
                    }
                }
            }

            return result;
        }
        #endregion

#if UNITY_EDITOR
        #region Format Keys
        /// <summary>
        /// Formats a list of <see cref="KeyValuePair"/> into a plain <c>public static class</c> of
        /// <c>const uint</c> fields. Only rewritten when content changes, so re-running key generation without
        /// data changes never triggers a recompile of the key-definitions assembly.
        /// </summary>
        public static void FormatKeyFiles(string relativeFilePath, string namespaceName, string scriptName, string category, List<KeyValuePair> keys)
        {
            var filePath = $"{FileUtility.ConvertRelativeToFullPath(relativeFilePath)}/{scriptName}.cs".Replace("\\", "/");

            StringBuilder sb = new();

            sb.Append("//\t* THIS SCRIPT IS AUTO-GENERATED *\n");

            sb.Append($"namespace {namespaceName}\n");
            sb.Append("{\n");
            sb.Append($"\tpublic static class {scriptName}\n");
            sb.Append("\t{\n");

            FormatCategory(sb, category.EmptyOrNull() ? scriptName : category);

            for (int i = 0; i < keys.Count; i++)
            {
                if (!keys[i].IsValid())
                {
                    continue;
                }

                int pIndex = i;
                sb.Append("\t\t");
                sb.Append(keys[i].GetFormat(pIndex));
                sb.Append("\n");
            }

            sb.Append("\t}\n");
            sb.Append("}");

            FileUtility.WriteAllTextIfChanged(filePath, sb.ToString());
        }

        private static void FormatCategory(StringBuilder sb, string category)
        {
            sb.Append($"\t\tpublic const string {KEYS_CATEGORY_NAME} = \"{category}\";\n\n");
        }
        #endregion
#endif
    }
}
