using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Keys
{
    /// <summary>
    /// Helper class to serialize and deserialize key data to json.
    /// </summary>
    [System.Serializable]
    public class JsonConfigDefinition
    {
        /// <summary>
        /// The folder path
        /// </summary>
        public string FolderPath;
        /// <summary>
        /// The containing namespace
        /// </summary>
        public string NamespaceName;
        /// <summary>
        /// The class name
        /// </summary>
        public string DefinitionName;
        /// <summary>
        /// True if a custom order should be assigned instead of <see cref="IKey"/> values.
        /// </summary>
        public bool CustomOrder;
        /// <summary>
        /// The staring value when <see cref="CustomOrder"/> is true.
        /// </summary>
        public int StartingValue;
        /// <summary>
        /// If the <see cref="StartingValue"/> should count up or down when <see cref="CustomOrder"/> is true.
        /// </summary>
        public int OrderDirection;
        /// <summary>
        /// If True, the value "None" will be created.
        /// </summary>
        public bool CreateNone;
        /// <summary>
        /// List of all the enums that are created.
        /// </summary>
        public List<string> EnumContent;

        /// <summary>
        /// Writes the config to a json file at the path supplied.
        /// </summary>
        /// <param name="def">The file to write</param>
        /// <param name="path">The full system path to write to including the filename and extension</param>
        /// <example>
        /// <code>
        /// JsonConfigDefinition.ToJson(new JsonConfigDefinition(), $"{Application.dataPath}/ParentFolder/SubFolder/fileName.json");
        /// </code>
        /// </example>
        public static void ToJson(JsonConfigDefinition def, string path)
        {
            var jsonString = JsonUtility.ToJson(def, true);
            System.IO.File.WriteAllText(path, jsonString);
        }

        /// <summary>
        ///  Reads the json file and returns the config file.
        /// </summary>
        /// <param name="directoryPath">The full system directory path to read from.</param>
        /// <param name="fileName">The filename to read, including the extension</param>
        /// <returns>The deserialized <see cref="JsonConfigDefinition"/></returns>
        /// <example>
        /// <code>
        /// var definition = JsonConfigDefinition.FromJson($"{Application.dataPath}/ParentFolder/SubFolder", "fileName.json");
        /// </code>
        /// </example>
        public static JsonConfigDefinition FromJson(string directoryPath, string fileName)
        {
            var jsonString = System.IO.File.ReadAllText($"{directoryPath}/{fileName}");
            return JsonUtility.FromJson<JsonConfigDefinition>(jsonString);
        }
    }
}
