
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using Vapor.Inspector;
#if UNITY_EDITOR_COROUTINES
using Unity.EditorCoroutines.Editor;
#endif

namespace VaporEditor.Inspector
{
    /// <summary>
    /// The dictionary half of a property: what a <c>Dictionary&lt;TKey, TValue&gt;</c> member holds, and
    /// how the inspector edits it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modelled on the array half rather than on anything new. An entry is materialized as two ordinary
    /// properties — one for its key, one for its value — so both are drawn by whatever already draws
    /// their type: a <c>GameplayTag</c> key gets the tag picker, a <c>double</c> value gets a
    /// <c>DoubleField</c>, and a class value gets the same foldout it would get anywhere else. Nothing
    /// here knows what a dictionary is <i>of</i>.
    /// </para>
    /// <para>
    /// A dictionary has no index, which is the one thing an array gives for free. The order the rows are
    /// drawn in is held here, in <see cref="_entryKeys"/>, and every structural change rewrites the
    /// dictionary in that order — so renaming a key leaves its row where it was instead of sending it to
    /// wherever the new hash happens to land.
    /// </para>
    /// </remarks>
    public partial class InspectorTreeProperty
    {
        /// <summary>
        /// What a dictionary is made of, read off its type. The mirror of
        /// <see cref="ArrayReflectionHelper"/>, and just as safe to build before any entry exists.
        /// </summary>
        /// <remarks>
        /// The entries themselves are reached through the non-generic <see cref="IDictionary"/>, which
        /// every <c>Dictionary&lt;,&gt;</c> implements — so reading, writing and removing an entry is a
        /// boxed call rather than a reflected one, and only the key and value types have to be resolved
        /// here at all.
        /// </remarks>
        public class DictionaryReflectionHelper
        {
            public Type DictionaryType { get; }
            public Type KeyType { get; }
            public Type ValueType { get; }

            public DictionaryReflectionHelper(Type dictionaryType)
            {
                DictionaryType = dictionaryType;
                var arguments = dictionaryType.GetGenericArguments();
                KeyType = arguments[0];
                ValueType = arguments[1];
            }
        }

        /// <summary>One entry, as the pair of properties that draw it.</summary>
        public class DictionaryRow
        {
            public InspectorTreeProperty Key { get; internal set; }
            public InspectorTreeProperty Value { get; internal set; }
        }

        /// <summary>Returned instead of null so callers can always foreach. Never mutated.</summary>
        private static readonly List<DictionaryRow> s_NoRows = new();

        // Dictionary
        public bool IsDictionary { get; private set; }

        private DictionaryReflectionHelper _dictionaryHelper;
        public DictionaryReflectionHelper DictionaryHelper => _dictionaryHelper;

        /// <summary>
        /// The keys in the order their rows are drawn, which is also the order the dictionary itself is
        /// kept in. Null until the entries are materialized.
        /// </summary>
        private List<object> _entryKeys;

        private List<DictionaryRow> _dictionaryData;

        public List<DictionaryRow> DictionaryData
        {
            get
            {
                EnsureChildProperties();
                return _dictionaryData ?? s_NoRows;
            }
        }

        /// <inheritdoc cref="ArraySize"/>
        [CreateProperty]
        public int DictionarySize
        {
            get
            {
                if (!IsDictionary)
                {
                    return 0;
                }

                EnsureChildProperties();
                return _dictionaryData?.Count ?? 0;
            }
        }

        // Dictionary Entry
        /// <summary>True when this property is one half of an entry rather than a member of anything.</summary>
        public bool IsDictionaryEntry { get; private set; }

        /// <summary>Which half: the key when true, the value when false.</summary>
        public bool IsEntryKey { get; private set; }

        /// <summary>
        /// The entry's current key or value, snapshotted when the row was built. The mirror of
        /// <see cref="ArrayElementObject"/>: a dictionary cannot hand out a reference to its own storage,
        /// so what is drawn is a copy and what is edited is written back through the owning property.
        /// </summary>
        private object _entryObject;

        /// <summary>
        /// A dictionary entry's key or value.
        /// </summary>
        /// <remarks>
        /// Deliberately not given a <see cref="DisplayName"/>: the row's two columns are what say which
        /// half is which, and a label on each would eat the width the fields need. That is also why an
        /// entry answers to <c>[HideLabel]</c> whether or not the member it belongs to carries one.
        /// </remarks>
        public InspectorTreeProperty(InspectorTreeObject root, InspectorTreeProperty parentProperty, Type entryType, object entryObject, int index, bool isKey, string path)
        {
            InspectorObject = root;
            ParentProperty = parentProperty;
            PropertyType = entryType;
            _entryObject = entryObject;
            PropertyPath = path;
            ElementIndex = index;
            IsDictionaryEntry = true;
            IsEntryKey = isKey;
            PropertyName = isKey ? $"Key[{index}]" : $"Value[{index}]";
            DisplayName = string.Empty;

            PropertyInfoType = MemberInfoType.Field;
            ParentType = parentProperty.PropertyType;
            HasParentProperty = true;

            IsUnitySerializedProperty = InspectorObject.IsUnityObject;
            SerializedPropertyType = TypeToSerializedPropertyType(PropertyType);
            SerializedPropertyNumericType = TypeToSerializedPropertyNumericType(PropertyType);

            IsArray = IsArrayOrList(PropertyType);
            if (IsArray)
            {
                _arrayHelper = CreateArrayHelper(PropertyType);
            }

            IsDictionary = IsDictionaryType(PropertyType);
            if (IsDictionary)
            {
                _dictionaryHelper = new DictionaryReflectionHelper(PropertyType);
            }

            IsStruct = PropertyType.IsValueType && !PropertyType.IsPrimitive;
            if (IsStruct)
            {
                _cachedStructObject = GetValue(true);
            }

            // Not inherited from the owning member the way an array element's is: the member is a
            // dictionary, and a drawer that claimed it would have drawn the whole thing rather than
            // leaving these rows to be built.
            HasCustomDrawer = !TypeHasAttribute<IgnoreCustomDrawerAttribute>() && !HasAttribute<IgnoreCustomDrawerAttribute>() &&
                              SerializedDrawerUtility.HasUsableCustomPropertyDrawer(PropertyType, SerializedPropertyType == SerializedPropertyType.ManagedReference, IsUnitySerializedProperty);
            NoChildProperties = HasIgnoreChildNodes();
        }

        /// <summary>True for <c>Dictionary&lt;,&gt;</c> and anything derived from it.</summary>
        public static bool IsDictionaryType(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Materializes a property pair per entry, in <see cref="_entryKeys"/> order.
        /// </summary>
        /// <remarks>
        /// The key order is re-read from the dictionary on every rebuild rather than kept across one.
        /// Every structural edit rewrites the dictionary in row order first, so what comes back is the
        /// order the rows were already in — and an edit made anywhere else is picked up rather than
        /// fought with.
        /// </remarks>
        private void BuildDictionaryProperties()
        {
            if (GetValueSafe() is not IDictionary dictionary)
            {
                Debug.LogWarning($"{PropertyPath} has a non-initialized dictionary, but it is serialized. This shouldn't happen.");
                _entryKeys = new List<object>();
                _dictionaryData = new List<DictionaryRow>();
                return;
            }

            _entryKeys = new List<object>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                _entryKeys.Add(entry.Key);
            }

            _dictionaryData = new List<DictionaryRow>(_entryKeys.Count);
            for (var i = 0; i < _entryKeys.Count; i++)
            {
                var key = _entryKeys[i];
                var keyPath = $"{PropertyPath}.Dictionary.key[{i}]";
                var valuePath = $"{PropertyPath}.Dictionary.value[{i}]";

                var keyProperty = new InspectorTreeProperty(InspectorObject, this, _dictionaryHelper.KeyType, key, i, true, keyPath);
                var valueProperty = new InspectorTreeProperty(InspectorObject, this, _dictionaryHelper.ValueType, dictionary[key], i, false, valuePath);

                _dictionaryData.Add(new DictionaryRow { Key = keyProperty, Value = valueProperty });
                InspectorObject.AddToMap(keyPath, keyProperty);
                InspectorObject.AddToMap(valuePath, valueProperty);
            }
        }

        #region - Dictionary -
        /// <summary>The key or the value of the entry at <paramref name="index"/>, or null when there is none.</summary>
        public object GetEntryObjectAt(int index, bool key)
        {
            if (!IsDictionary || GetValueSafe() is not IDictionary dictionary)
            {
                return null;
            }

            EnsureChildProperties();
            if (index < 0 || index >= _entryKeys.Count)
            {
                return null;
            }

            var entryKey = _entryKeys[index];
            return key ? entryKey : dictionary[entryKey];
        }

        /// <summary>True when the dictionary already holds this key.</summary>
        public bool ContainsEntryKey(object key)
        {
            return key != null && GetValueSafe() is IDictionary dictionary && dictionary.Contains(key);
        }

        /// <summary>
        /// Re-keys an entry in place.
        /// </summary>
        /// <remarks>
        /// A key that is already taken is refused rather than merged — merging would silently destroy
        /// whichever entry lost — and the rows are rebuilt, which puts the widget back to the key it was
        /// showing before the edit.
        /// </remarks>
        public void SetEntryKeyAt(int index, object newKey)
        {
            if (!IsDictionary || GetValueSafe() is not IDictionary dictionary)
            {
                return;
            }

            EnsureChildProperties();
            if (index < 0 || index >= _entryKeys.Count)
            {
                return;
            }

            newKey = newKey == null ? null : CastToType(newKey, _dictionaryHelper.KeyType);
            var oldKey = _entryKeys[index];
            if (Equals(oldKey, newKey))
            {
                return;
            }

            if (newKey == null)
            {
                Debug.LogWarning($"{PropertyPath} cannot use a null key.");
                RebuildDictionary();
                return;
            }

            if (dictionary.Contains(newKey))
            {
                Debug.LogWarning($"{PropertyPath} already has an entry keyed '{DescribeKey(newKey)}'. The rename was not applied.");
                RebuildDictionary();
                return;
            }

            var values = ReadValuesInRowOrder(dictionary);
            _entryKeys[index] = newKey;
            RewriteInRowOrder(dictionary, values);

            InspectorObject.ApplyModifiedProperties();
            RebuildDictionary();
        }

        /// <summary>
        /// Writes an entry's value. Structurally nothing changes, so the rows are left alone — rebuilding
        /// them here would tear down the widget being typed into.
        /// </summary>
        public void SetEntryValueAt(int index, object value)
        {
            if (!IsDictionary || GetValueSafe() is not IDictionary dictionary)
            {
                return;
            }

            EnsureChildProperties();
            if (index < 0 || index >= _entryKeys.Count)
            {
                return;
            }

            // A cleared object field sends null, which a value-typed dictionary cannot be handed - it
            // unboxes what it is given. Null means "back to nothing" either way.
            var valueType = _dictionaryHelper.ValueType;
            dictionary[_entryKeys[index]] = value == null
                ? valueType.IsValueType ? Activator.CreateInstance(valueType) : null
                : CastToType(value, valueType);

            InspectorObject.ApplyModifiedProperties();
        }

        /// <summary>Adds an entry under a key nothing is using yet.</summary>
        public void AddEntry()
        {
            if (!IsDictionary || GetValueSafe() is not IDictionary dictionary)
            {
                return;
            }

            EnsureChildProperties();
            if (!TryCreateFreeKey(dictionary, out var key))
            {
                Debug.LogWarning($"{PropertyPath} already has an entry keyed '{DescribeKey(DefaultEntryObject(_dictionaryHelper.KeyType))}'. " +
                                 $"Name that one before adding another.");
                return;
            }

            var values = ReadValuesInRowOrder(dictionary);
            values.Add(DefaultEntryObject(_dictionaryHelper.ValueType));
            _entryKeys.Add(key);
            RewriteInRowOrder(dictionary, values);

            InspectorObject.ApplyModifiedProperties();
            RebuildDictionary();
        }

        public void RemoveLastEntry()
        {
            EnsureChildProperties();
            RemoveEntryAt(_entryKeys == null ? -1 : _entryKeys.Count - 1);
        }

        public void RemoveEntryAt(int index)
        {
            if (!IsDictionary || GetValueSafe() is not IDictionary dictionary)
            {
                return;
            }

            EnsureChildProperties();
            if (index < 0 || index >= _entryKeys.Count)
            {
                return;
            }

            var values = ReadValuesInRowOrder(dictionary);
            values.RemoveAt(index);
            _entryKeys.RemoveAt(index);
            RewriteInRowOrder(dictionary, values);

            InspectorObject.ApplyModifiedProperties();
            RebuildDictionary();
        }

        private List<object> ReadValuesInRowOrder(IDictionary dictionary)
        {
            var values = new List<object>(_entryKeys.Count);
            foreach (var key in _entryKeys)
            {
                values.Add(dictionary[key]);
            }

            return values;
        }

        /// <summary>
        /// Empties the dictionary and fills it again in row order.
        /// </summary>
        /// <remarks>
        /// The whole point of the rewrite. A dictionary enumerates in the order its buckets were filled,
        /// so removing an entry and adding another lands the new one in the hole the old one left — which
        /// is what made rows jump around when a key was edited. Refilling from empty makes what is
        /// enumerated exactly what is drawn.
        /// </remarks>
        private void RewriteInRowOrder(IDictionary dictionary, List<object> values)
        {
            dictionary.Clear();
            for (var i = 0; i < _entryKeys.Count; i++)
            {
                dictionary[_entryKeys[i]] = values[i];
            }
        }

        /// <summary>
        /// A key the dictionary is not using: the type's default when that is free, then the next one
        /// after it for a key that can be counted through.
        /// </summary>
        /// <remarks>
        /// Anything else — a tag, a struct — gets one shot at its default and is then refused, because
        /// there is no next value to invent that would mean anything. In practice that reads as "finish
        /// naming the entry you just added", since as soon as the empty row is given a key the default is
        /// free again.
        /// </remarks>
        private bool TryCreateFreeKey(IDictionary dictionary, out object key)
        {
            var keyType = _dictionaryHelper.KeyType;
            key = DefaultEntryObject(keyType);
            if (key != null && !dictionary.Contains(key))
            {
                return true;
            }

            if (keyType == typeof(string))
            {
                for (var i = 1; i < 1000; i++)
                {
                    key = $"New Key {i}";
                    if (!dictionary.Contains(key))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (keyType.IsEnum)
            {
                // Every name the enum has, in order. There is no counting past the last one: a value the
                // enum does not declare is not a key anyone meant to write.
                foreach (var declared in Enum.GetValues(keyType))
                {
                    if (!dictionary.Contains(declared))
                    {
                        key = declared;
                        return true;
                    }
                }

                return false;
            }

            if (!IsCountableKey(keyType))
            {
                return false;
            }

            for (var i = 1L; i < 1000L; i++)
            {
                key = Convert.ChangeType(i, keyType, CultureInfo.InvariantCulture);
                if (!dictionary.Contains(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCountableKey(Type keyType)
        {
            if (keyType.IsEnum)
            {
                return false;
            }

            return Type.GetTypeCode(keyType) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
        }

        /// <summary>
        /// What a fresh key or value of <paramref name="entryType"/> starts out as.
        /// </summary>
        /// <remarks>
        /// Cast to the exact type rather than taken as the inspector's default gives it. That default is
        /// the one a widget would start from — an <c>int</c> zero for anything integral, a <c>float</c>
        /// zero for anything fractional — and a dictionary reached through <see cref="IDictionary"/>
        /// unboxes what it is handed, so an <c>int</c> zero offered to a <c>Dictionary&lt;uint, double&gt;</c>
        /// throws rather than converting.
        /// </remarks>
        private static object DefaultEntryObject(Type entryType)
        {
            var value = GetDefaultValueForArrayElement(entryType);
            return value == null ? null : CastToType(value, entryType);
        }

        private static string DescribeKey(object key) => key?.ToString() ?? "null";

        /// <summary>
        /// Rebuilds the rows and redraws them, a frame later.
        /// </summary>
        /// <remarks>The dictionary twin of the array's delayed rebuild, and deferred for the same reason.</remarks>
        private void RebuildDictionary()
        {
#if UNITY_EDITOR_COROUTINES
            EditorCoroutineUtility.StartCoroutine(DelayedBuildDictionaryProperties(), this);
#else
            _childrenBuilt = true;
            BuildDictionaryProperties();
            RequireRedraw.Invoke();
#endif
        }

#if UNITY_EDITOR_COROUTINES
        private IEnumerator DelayedBuildDictionaryProperties()
        {
            yield return null;
            // Marked built as well, so a later DictionaryData access doesn't rebuild what we just rebuilt.
            _childrenBuilt = true;
            BuildDictionaryProperties();
            RequireRedraw.Invoke();
        }
#endif
        #endregion
    }
}
