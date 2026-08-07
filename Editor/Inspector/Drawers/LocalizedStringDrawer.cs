using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Picks a <see cref="LocalizedString"/> as a table plus an entry within it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity's own drawer for this type is built on <see cref="SerializedProperty"/>, so it cannot draw
    /// a <see cref="LocalizedString"/> that lives on a plain C# object — which is exactly where the
    /// data registry keeps them. This one covers both: <see cref="CreateVaporPropertyGUI"/> reads and
    /// writes through the property tree, and <see cref="CreatePropertyGUI"/> keeps working for
    /// serialized objects so registering it project-wide costs nothing.
    /// </para>
    /// <para>
    /// Both paths write the table by collection name and the entry by key rather than by GUID and
    /// numeric id. The two forms are equivalent to the runtime, and the readable one is what makes a
    /// serialized document legible and hand-editable — the whole point of storing this in VSL.
    /// </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(LocalizedString), true)]
    public class LocalizedStringDrawer : VaporPropertyDrawer
    {
        private const string NoneLabel = "None";

        // Unity's field names on LocalizedReference / TableReference / TableEntryReference.
        private const string TablePath = "m_TableReference.m_TableCollectionName";
        private const string KeyPath = "m_TableEntryReference.m_Key";
        private const string KeyIdPath = "m_TableEntryReference.m_KeyId";

        #region Vapor tree

        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            var current = field.Property.GetValue<LocalizedString>();

            return BuildRow(
                field.Property.DisplayName,
                TableNameOf(current),
                KeyOf(current),
                (table, key) =>
                {
                    var previous = field.Property.GetValue<LocalizedString>();
                    var next = string.IsNullOrEmpty(table) && string.IsNullOrEmpty(key)
                        ? null
                        : new LocalizedString(table, key);
                    field.MarkDirtyWithValue(previous, next);
                });
        }

        #endregion

        #region Serialized objects

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var tableProperty = property.FindPropertyRelative(TablePath);
            var keyProperty = property.FindPropertyRelative(KeyPath);
            var keyIdProperty = property.FindPropertyRelative(KeyIdPath);

            if (tableProperty == null || keyProperty == null || keyIdProperty == null)
            {
                // A layout change in the package would land here rather than throwing mid-inspector.
                return new PropertyField(property);
            }

            var owner = property.serializedObject;

            return BuildRow(
                property.displayName,
                tableProperty.stringValue,
                keyProperty.stringValue,
                (table, key) =>
                {
                    owner.Update();
                    tableProperty.stringValue = table ?? string.Empty;
                    keyProperty.stringValue = key ?? string.Empty;

                    // The id wins over the key in TableEntryReference.OnAfterDeserialize, so a stale
                    // one would silently outrank the key just chosen.
                    keyIdProperty.longValue = SharedTableData.EmptyId;
                    owner.ApplyModifiedProperties();
                });
        }

        #endregion

        #region UI

        /// <summary>
        /// Label, table dropdown, entry dropdown. <paramref name="apply"/> receives the collection
        /// name and entry key, either of which may be empty.
        /// </summary>
        private static VisualElement BuildRow(string label, string table, string key, Action<string, string> apply)
        {
            var collections = Collections();

            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 1,
                    marginBottom = 1,
                },
            };

            row.Add(new Label(label)
            {
                tooltip = "Localized text, chosen as a string table collection and an entry within it.",
                style =
                {
                    minWidth = 120,
                    flexShrink = 0,
                    unityTextAlign = TextAnchor.MiddleLeft,
                },
            });

            var tableNames = new List<string> { NoneLabel };
            tableNames.AddRange(collections.Keys);

            var tableField = new DropdownField
            {
                choices = tableNames,
                value = string.IsNullOrEmpty(table) ? NoneLabel : table,
                tooltip = collections.Count == 0
                    ? "No string table collections exist in this project yet."
                    : "String table collection.",
                style = { flexGrow = 1f, flexBasis = 0f, marginRight = 2 },
            };

            var keyField = new DropdownField
            {
                choices = KeyChoices(collections, table),
                value = string.IsNullOrEmpty(key) ? NoneLabel : key,
                tooltip = "Entry within the selected table.",
                style = { flexGrow = 1f, flexBasis = 0f },
            };

            // A key the project no longer has still has to show, or opening an inspector on stale data
            // would quietly rewrite it to None.
            EnsurePresent(keyField, key);
            EnsurePresent(tableField, table);

            tableField.RegisterValueChangedCallback(evt =>
            {
                var selectedTable = Normalize(evt.newValue);
                keyField.choices = KeyChoices(collections, selectedTable);

                // Keys are scoped to their table, so one that does not exist in the new table is gone.
                var selectedKey = keyField.choices.Contains(keyField.value) ? Normalize(keyField.value) : null;
                keyField.SetValueWithoutNotify(string.IsNullOrEmpty(selectedKey) ? NoneLabel : selectedKey);

                apply(selectedTable, selectedKey);
            });

            keyField.RegisterValueChangedCallback(evt =>
                apply(Normalize(tableField.value), Normalize(evt.newValue)));

            row.Add(tableField);
            row.Add(keyField);
            return row;
        }

        private static void EnsurePresent(DropdownField field, string value)
        {
            if (!string.IsNullOrEmpty(value) && !field.choices.Contains(value))
            {
                field.choices.Add(value);
                field.SetValueWithoutNotify(value);
                field.tooltip += $"\n'{value}' is not in this project.";
            }
        }

        private static List<string> KeyChoices(Dictionary<string, StringTableCollection> collections, string table)
        {
            var choices = new List<string> { NoneLabel };
            if (!string.IsNullOrEmpty(table) && collections.TryGetValue(table, out var collection))
            {
                choices.AddRange(collection.SharedData.Entries.Select(e => e.Key));
            }

            return choices;
        }

        private static Dictionary<string, StringTableCollection> Collections()
        {
            var map = new Dictionary<string, StringTableCollection>();

            // Guarded: a project without localization settings has no collections, and asking for them
            // must not be the reason an inspector fails to draw.
            try
            {
                foreach (var collection in LocalizationEditorSettings.GetStringTableCollections())
                {
                    if (collection != null && !string.IsNullOrEmpty(collection.TableCollectionName))
                    {
                        map[collection.TableCollectionName] = collection;
                    }
                }
            }
            catch (Exception)
            {
                // Leaves the dropdowns empty, which the tooltip already explains.
            }

            return map;
        }

        private static string Normalize(string value) => value == NoneLabel ? null : value;

        private static string TableNameOf(LocalizedString value) =>
            value == null ? null : value.TableReference.TableCollectionName;

        private static string KeyOf(LocalizedString value) =>
            value == null ? null : value.TableEntryReference.Key;

        #endregion
    }
}
