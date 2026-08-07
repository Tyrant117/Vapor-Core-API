using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using VaporEditor.Inspector;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Draws the selected entries as a sheet, either way round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cells are the real inspector. Each entry builds an <see cref="InspectorTreeRootElement"/>
    /// as usual and its top-level member elements are then moved into the grid, so a cell keeps the
    /// drawer, the change handling and the validation it would have had in a stacked inspector.
    /// Building a second set of widgets for the grid would have meant two definitions of how a member
    /// is edited, and they would drift.
    /// </para>
    /// <para>
    /// Both orientations are laid out row-major — a list of rows, each a list of cells — rather than
    /// as columns. Flex columns only line up if every cell is the same height, which stops being true
    /// the moment a foldout opens; rows align by construction.
    /// </para>
    /// <para>
    /// Only top-level members become fields. A nested object keeps its foldout inside its own cell
    /// rather than being flattened, which keeps the field set identical for every entry however deep
    /// the type goes.
    /// </para>
    /// </remarks>
    internal static class DataGridView
    {
        private const int FieldColumnWidth = 150;
        private const int ValueColumnWidth = 260;
        private const int CheckboxGutter = 24;
        private const int CellPadding = 4;

        private static readonly Color HeaderBackground = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color GridLine = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color AlternateRow = new Color(1f, 1f, 1f, 0.03f);

        /// <summary>What the grid needs from the window, without knowing about the window.</summary>
        internal sealed class Context
        {
            public IReadOnlyList<IData> Entries;

            /// <summary>True when field names run down the side and entries across the top.</summary>
            public bool FieldsAsRows;

            public bool FlagDifferences;

            /// <summary>Raised with the entry whose row or column was edited.</summary>
            public Action<IData> EntryChanged;

            /// <summary>
            /// Members ticked on the field axis. Empty means the whole entry is in scope.
            /// </summary>
            /// <remarks>
            /// These narrow what <c>Copy Prompt</c> writes; they do not hide anything. Nothing is
            /// ticked by default, which is what makes the button copy complete entries unless asked
            /// otherwise.
            /// </remarks>
            public IReadOnlyCollection<string> FocusedFields;

            /// <summary>Raised when a field's checkbox is ticked or unticked.</summary>
            public Action<string, bool> FieldCheckedChanged;

            /// <summary>Raised by the master checkbox at the origin of the field axis.</summary>
            public Action<bool> AllFieldsCheckedChanged;
        }

        /// <summary>Prefix marking a field that came out of an extension rather than off the type.</summary>
        private const string ExtensionPrefix = "ext:";

        private const string MissingValue = "--";

        private sealed class EntryFields
        {
            public IData Entry;

            /// <summary>Top-level member elements, in the order the inspector drew them.</summary>
            public List<KeyValuePair<string, VisualElement>> Members;

            /// <summary>The hidden tree the members were taken from, kept so it stays their root.</summary>
            public VisualElement Host;
        }

        public static VisualElement Build(Context context)
        {
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { style = { flexGrow = 1f } };

            if (context?.Entries == null || context.Entries.Count == 0)
            {
                return scroll;
            }

            var built = new List<EntryFields>(context.Entries.Count);

            foreach (var entry in context.Entries)
            {
                var tree = new InspectorTreeRootElement(entry, entry.GetType());

                // Kept in the hierarchy, hidden. The elements moved out of it still point at it as
                // their root, and a rebuild walks up to it through its parent.
                var host = new VisualElement { style = { display = DisplayStyle.None } };
                host.Add(tree);

                var members = Harvest(tree);
                ExpandExtensions(entry, members);

                built.Add(new EntryFields { Entry = entry, Members = members, Host = host });
            }

            // The union across the selection, not one entry's members. Two items with different
            // extensions both contribute their own, and whoever lacks one gets a placeholder — so the
            // sheet shows everything without the rows having to line up by accident.
            var fieldOrder = BuildFieldOrder(built);

            var sheet = new VisualElement();
            scroll.Add(sheet);

            var groups = context.FieldsAsRows
                ? BuildFieldsAsRows(sheet, context, built, fieldOrder)
                : BuildFieldsAsColumns(sheet, context, built, fieldOrder);

            if (context.FlagDifferences && built.Count > 1)
            {
                // Deferred a frame: a nested inspector materializes its children as the tree is built,
                // and the fields have to exist before they can be found and marked.
                scroll.schedule.Execute(() => DataComparison.FlagDifferences(groups)).ExecuteLater(1);
            }

            return scroll;
        }

        #region Layouts

        /// <summary>Field names across the top, one row per entry.</summary>
        private static List<IReadOnlyList<TreePropertyField>> BuildFieldsAsColumns(
            VisualElement sheet, Context context, List<EntryFields> built, List<string> fieldOrder)
        {
            var header = Row(HeaderBackground);
            header.Add(MasterCheckboxCell(context, CheckboxGutter));
            foreach (var path in fieldOrder)
            {
                header.Add(FieldHeaderCell(context, path, ValueColumnWidth));
            }

            sheet.Add(header);

            var groups = NewGroups(fieldOrder);

            for (var i = 0; i < built.Count; i++)
            {
                var entryFields = built[i];
                var row = Row(i % 2 == 1 ? AlternateRow : Color.clear);

                // Empty, but it keeps the data rows lined up under the header's checkbox column.
                row.Add(Cell(CheckboxGutter));

                PlaceCells(row, entryFields, fieldOrder, groups, ValueColumnWidth);

                row.Add(entryFields.Host);
                row.RegisterCallback<TreePropertyChangedEvent>(_ => context.EntryChanged?.Invoke(entryFields.Entry));
                sheet.Add(row);
            }

            return groups;
        }

        /// <summary>Field names down the side, one column per entry.</summary>
        private static List<IReadOnlyList<TreePropertyField>> BuildFieldsAsRows(
            VisualElement sheet, Context context, List<EntryFields> built, List<string> fieldOrder)
        {
            var header = Row(HeaderBackground);
            header.Add(MasterCheckboxCell(context, FieldColumnWidth));
            foreach (var entryFields in built)
            {
                header.Add(EntryHeaderCell(entryFields.Entry));
            }

            sheet.Add(header);

            var groups = NewGroups(fieldOrder);

            for (var f = 0; f < fieldOrder.Count; f++)
            {
                var path = fieldOrder[f];
                var row = Row(f % 2 == 1 ? AlternateRow : Color.clear);
                row.Add(FieldHeaderCell(context, path, FieldColumnWidth));

                foreach (var entryFields in built)
                {
                    var cell = Cell(ValueColumnWidth);
                    if (TryTake(entryFields.Members, path, out var element))
                    {
                        cell.Add(element);
                        Collect(groups[f], element);
                    }
                    else
                    {
                        cell.Add(Missing());
                    }

                    row.Add(cell);
                }

                sheet.Add(row);
            }

            // The hidden trees have to stay in the hierarchy, and in this orientation no single row
            // owns an entry — so they hang off the sheet, and the change notification with them.
            foreach (var entryFields in built)
            {
                sheet.Add(entryFields.Host);
            }

            sheet.RegisterCallback<TreePropertyChangedEvent>(evt =>
            {
                var owner = OwnerOf(evt.target as VisualElement, built);
                if (owner != null)
                {
                    context.EntryChanged?.Invoke(owner);
                }
            });

            return groups;
        }

        /// <summary>
        /// Works out which entry a change came from by walking up to the cell that holds it.
        /// </summary>
        /// <remarks>
        /// Needed only in the transposed layout, where an entry's cells are spread across every row
        /// and there is no per-entry container to hang a callback on.
        /// </remarks>
        private static IData OwnerOf(VisualElement target, List<EntryFields> built)
        {
            for (var element = target; element != null; element = element.parent)
            {
                foreach (var entryFields in built)
                {
                    foreach (var member in entryFields.Members)
                    {
                        if (ReferenceEquals(member.Value, element))
                        {
                            return entryFields.Entry;
                        }
                    }
                }
            }

            return null;
        }

        private static void PlaceCells(VisualElement row, EntryFields entryFields, List<string> fieldOrder,
            List<IReadOnlyList<TreePropertyField>> groups, int width)
        {
            for (var f = 0; f < fieldOrder.Count; f++)
            {
                var cell = Cell(width);
                if (TryTake(entryFields.Members, fieldOrder[f], out var element))
                {
                    cell.Add(element);
                    Collect(groups[f], element);
                }
                else
                {
                    cell.Add(Missing());
                }

                row.Add(cell);
            }
        }

        #endregion

        #region Harvesting

        /// <summary>
        /// Finds the element that owns each top-level member of a built tree.
        /// </summary>
        /// <remarks>
        /// Returned as a list, not a dictionary. The order this walks the tree in is the order the
        /// inspector draws the members — after group and draw-order sorting — and that is the field
        /// order, so it cannot be left to a hash table's iteration order.
        /// </remarks>
        private static List<KeyValuePair<string, VisualElement>> Harvest(InspectorTreeRootElement tree)
        {
            var found = new List<KeyValuePair<string, VisualElement>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            tree.Query<TreePropertyField>().ForEach(field =>
            {
                var path = field.Property?.PropertyPath;
                if (string.IsNullOrEmpty(path) || path.Contains('.') || path.Contains('['))
                {
                    // Nested and array-element fields travel with their parent's cell.
                    return;
                }

                if (!seen.Add(path))
                {
                    return;
                }

                // The controlling element rather than the widget: it carries the row framing, the
                // decorators and the conditionals that the widget alone does not.
                var owner = field.GetFirstAncestorOfType<InspectorTreeFieldElement>();
                found.Add(new KeyValuePair<string, VisualElement>(path, (VisualElement)owner ?? field));

                HideRedundantLabel(field);
            });

            return found;
        }

        /// <summary>
        /// Hides a field's own label, since the row or column header already names it.
        /// </summary>
        /// <remarks>
        /// Matched on text rather than position. A compound widget nests labels of its own — the
        /// components of a vector, the parts of a localized reference — and those have to stay.
        /// </remarks>
        private static void HideRedundantLabel(TreePropertyField field)
        {
            var label = field.Q<Label>();
            if (label != null && string.Equals(label.text, field.Property?.DisplayName, StringComparison.Ordinal))
            {
                label.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Replaces an extension list with one field per extension the entry actually carries.
        /// </summary>
        /// <remarks>
        /// Left as a list, the whole set of capabilities would sit inside a single cell and two entries
        /// with different extensions would have nothing to compare. Split out, each extension is its own
        /// field, and an entry without it simply has no cell to fill.
        /// </remarks>
        private static void ExpandExtensions(IData entry, List<KeyValuePair<string, VisualElement>> members)
        {
            for (var i = members.Count - 1; i >= 0; i--)
            {
                var property = FindProperty(members[i].Value);
                if (property == null || !DataExtensionListView.Matches(property, out _))
                {
                    continue;
                }

                members.RemoveAt(i);

                if (property.GetValueSafe(true) is not IList list)
                {
                    continue;
                }

                var inserted = i;
                foreach (var item in list)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var type = item.GetType();
                    var view = new VisualElement();
                    view.Add(new InspectorTreeRootElement(item, type, property.InspectorObject));

                    members.Insert(inserted++, new KeyValuePair<string, VisualElement>(ExtensionPrefix + type.Name, view));
                }
            }
        }

        private static InspectorTreeProperty FindProperty(VisualElement element) =>
            (element as TreePropertyField ?? element.Q<TreePropertyField>())?.Property
            ?? (element as DataExtensionListView)?.Property
            ?? element.Q<DataExtensionListView>()?.Property;

        /// <summary>
        /// The field axis: every field any selected entry has, in first-seen order.
        /// </summary>
        private static List<string> BuildFieldOrder(List<EntryFields> built)
        {
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entryFields in built)
            {
                foreach (var member in entryFields.Members)
                {
                    if (seen.Add(member.Key))
                    {
                        order.Add(member.Key);
                    }
                }
            }

            // Name first, whatever order the type declares its members in. It is what identifies the
            // entry, and a sheet whose first field is not the name is hard to read.
            var nameIndex = order.FindIndex(p => string.Equals(p, nameof(IData.Name), StringComparison.Ordinal));
            if (nameIndex > 0)
            {
                order.RemoveAt(nameIndex);
                order.Insert(0, nameof(IData.Name));
            }

            return order;
        }

        private static string FieldLabel(string path) =>
            path.StartsWith(ExtensionPrefix, StringComparison.Ordinal)
                ? ObjectNames.NicifyVariableName(path[ExtensionPrefix.Length..])
                : ObjectNames.NicifyVariableName(path);

        /// <summary>Stands in for a field the entry has no value for, so the axis still lines up.</summary>
        private static VisualElement Missing() =>
            new Label(MissingValue)
            {
                tooltip = "This entry does not have that extension.",
                style = { opacity = 0.45f, unityTextAlign = TextAnchor.MiddleLeft },
            };

        private static bool TryTake(List<KeyValuePair<string, VisualElement>> members, string path, out VisualElement element)
        {
            foreach (var member in members)
            {
                if (string.Equals(member.Key, path, StringComparison.Ordinal))
                {
                    element = member.Value;
                    return true;
                }
            }

            element = null;
            return false;
        }

        private static List<IReadOnlyList<TreePropertyField>> NewGroups(List<string> fieldOrder)
        {
            var groups = new List<IReadOnlyList<TreePropertyField>>(fieldOrder.Count);
            for (var i = 0; i < fieldOrder.Count; i++)
            {
                groups.Add(new List<TreePropertyField>());
            }

            return groups;
        }

        private static void Collect(IReadOnlyList<TreePropertyField> group, VisualElement element)
        {
            // The outermost widget only. A compound member draws nested fields of its own, and
            // comparing the whole member once says everything comparing its parts would.
            var field = element as TreePropertyField ?? element.Q<TreePropertyField>();
            if (field != null && group is List<TreePropertyField> list)
            {
                list.Add(field);
            }
        }

        #endregion

        #region Chrome

        private static VisualElement Row(Color background) =>
            new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart,
                    borderBottomWidth = 1,
                    borderBottomColor = GridLine,
                    backgroundColor = background,
                },
            };

        private static VisualElement Cell(int width) =>
            new VisualElement
            {
                style =
                {
                    width = width,
                    flexShrink = 0f,
                    paddingLeft = CellPadding,
                    paddingRight = CellPadding,
                    paddingTop = 2,
                    paddingBottom = 2,
                    borderRightWidth = 1,
                    borderRightColor = GridLine,
                },
            };

        private static VisualElement HeaderCell(string text, string tooltip, int width)
        {
            var cell = Cell(width);
            cell.Add(new Label(text)
            {
                tooltip = tooltip,
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                },
            });
            return cell;
        }

        /// <summary>
        /// The checkbox at the origin of the field axis: ticks or clears every field at once.
        /// </summary>
        private static VisualElement MasterCheckboxCell(Context context, int width)
        {
            var all = context.FocusedFields is { Count: > 0 };

            var cell = Cell(width);
            var toggle = new Toggle
            {
                value = all,
                tooltip = "Focus every field, or clear the focus so the prompt covers whole entries.",
                style = { marginLeft = 0, marginRight = 0 },
            };
            toggle.RegisterValueChangedCallback(evt => context.AllFieldsCheckedChanged?.Invoke(evt.newValue));
            cell.Add(toggle);
            return cell;
        }

        /// <summary>
        /// A field's name, with the checkbox that puts it in the prompt's focus.
        /// </summary>
        private static VisualElement FieldHeaderCell(Context context, string path, int width)
        {
            var cell = Cell(width);
            var focused = context.FocusedFields != null && IsFocused(context.FocusedFields, path);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var toggle = new Toggle
            {
                value = focused,
                tooltip = "Tick to narrow Copy Prompt to this field. With nothing ticked the prompt covers whole entries.",
                style = { marginLeft = 0, marginRight = 2, flexShrink = 0f },
            };
            toggle.RegisterValueChangedCallback(evt => context.FieldCheckedChanged?.Invoke(path, evt.newValue));
            row.Add(toggle);

            row.Add(new Label(FieldLabel(path))
            {
                tooltip = path,
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    flexGrow = 1f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                },
            });

            cell.Add(row);
            return cell;
        }

        private static bool IsFocused(IReadOnlyCollection<string> focused, string path)
        {
            foreach (var candidate in focused)
            {
                if (string.Equals(candidate, path, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The per-entry header, when entries are columns.</summary>
        private static VisualElement EntryHeaderCell(IData entry)
        {
            var name = DataDocument.GetName(entry);
            return HeaderCell(string.IsNullOrEmpty(name) ? "<unnamed>" : name, name, ValueColumnWidth);
        }

        #endregion
    }
}
