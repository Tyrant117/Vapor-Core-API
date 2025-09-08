using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.Keys
{
    /// <summary>
    /// The base struct that contains a key. Optionally links to the guid of an object and can be used for remapping key values if that objects key changes.
    /// Has a custom drawer for selecting a key from a dropdown and be decorated with the <see cref="ValueDropdownAttribute"/>.
    /// <example>
    /// <para>How to implement the custom dropdown.</para>
    /// <code>
    /// [Serializable]
    /// public class DropdownDrawerExample
    /// {
    ///     [SerializeField, ValueDropdown("@GetCustomKeys")]
    ///     private KeyDropdownValue _exampleDropdown;
    ///     [SerializeField, ValueDropdown("CustomKeysCategory",ValueDropdownAttribute.FilterType.Category)]
    ///     private KeyDropdownValue _exampleDropdownCategory;
    ///     [SerializeField, ValueDropdown("CustomKeysType", ValueDropdownAttribute.FilterType.TypeName)]
    ///     private KeyDropdownValue _exampleDropdownType;
    ///
    ///     private IEnumerable GetCustomKeys()
    ///     {
    ///         return new List&lt;(string, KeyDropdownValue)> { "None", new KeyDropdownValue() };
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [Serializable, IgnoreChildNodes]
    public struct KeyDropdownValue : IEquatable<KeyDropdownValue>, IEquatable<uint>
    {
        public static implicit operator uint(KeyDropdownValue kdv) => kdv.Key;
        public static bool operator ==(KeyDropdownValue left, KeyDropdownValue right) => left.Equals(right);
        public static bool operator !=(KeyDropdownValue left, KeyDropdownValue right) => !(left == right);

        /// <summary>
        /// The guid of the object linked to this key.
        /// </summary>
        public string Guid;

        /// <summary>
        /// The unique key.
        /// </summary>
        public uint Key;

        /// <summary>
        /// The nice display name.
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// If true, this is a "None" key.
        /// </summary>
        public readonly bool IsNone => Key == 0;

        /// <summary>
        /// Creates a new KeyDropdownValue.
        /// </summary>
        /// <param name="guid">The guid of the linked object (can be empty)</param>
        /// <param name="key">the unique key</param>
        /// <param name="displayName">The displayed name of the key</param>
        public KeyDropdownValue(string guid, uint key, string displayName)
        {
            Guid = guid;
            Key = key;
            DisplayName = displayName;
        }

        /// <summary>
        /// Returns the "None" KeyDropdownValue.
        /// </summary>
        public static KeyDropdownValue None => new(null, 0, "None");

        [Conditional("UNITY_EDITOR")]
        public void Select()
        {
#if UNITY_EDITOR
            if (Guid == string.Empty) return;
            
            var refVal = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(Guid));
            RuntimeEditorUtility.Ping(refVal);
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public void Remap()
        {
#if UNITY_EDITOR
            if (Guid == string.Empty) return;
            var refVal = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(Guid));
            
            if (refVal is not IKey rfk) return;
            rfk.ForceRefreshKey();
            Key = rfk.Key;
            RuntimeEditorUtility.DirtyAndSave(refVal);
#endif
        }

        public readonly override string ToString() => $"Key: {Key} belonging to [{Guid}] with name [{DisplayName}]";

        public readonly bool Equals(KeyDropdownValue other) => Key == other.Key;

        public bool Equals(uint other) => Key == other;
        public readonly override int GetHashCode() => (int)Key;
        
    }
}
