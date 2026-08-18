using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Serialization;

namespace Vapor.Inspector
{
    /// <summary>One edit a runtime inspector is asking to make.</summary>
    public sealed class VslInspectorChange
    {
        /// <summary>The object declaring <see cref="Member"/>, resolved at the moment of the edit.</summary>
        public object Owner;

        public VslMember Member;

        /// <summary>
        /// The route from the drawn root, in the same dotted-with-indices grammar the actor paths use,
        /// so a host can resolve the same value on a different copy of the object.
        /// </summary>
        public string Path;

        public object OldValue;
        public object NewValue;
    }

    /// <summary>
    /// One member of an object, drawn: a gutter, a name, and either a control or a foldout holding the
    /// members underneath.
    /// </summary>
    /// <remarks>
    /// The owner is resolved through <see cref="OwnerProvider"/> rather than captured, so a nested
    /// object that is replaced wholesale — a transform reassigned, a component swapped — does not leave
    /// the rows below it writing into an object nothing points at any more.
    /// </remarks>
    public sealed class VslInspectorRow : VisualElement, IInspectorFilterElement, IInspectorFilterField
    {
        /// <summary>The route from the drawn root to this member.</summary>
        public string Path { get; internal set; }

        /// <summary>The member this row draws; null for a list element, which is a value without a member.</summary>
        public VslMember Member { get; internal set; }

        /// <summary>The name shown, spaced out from the member's own.</summary>
        public string DisplayName { get; internal set; }

        /// <summary>True for a list element. Named for its position, so a search never matches it.</summary>
        public bool IsElement { get; internal set; }

        /// <summary>Reserved space to the left of the name, for a host to put its own controls in.</summary>
        public VisualElement Gutter { get; internal set; }

        public Label NameLabel { get; internal set; }

        /// <summary>The rows below this one, for a member that was nested into. Null for a leaf.</summary>
        public VisualElement Content { get; internal set; }

        /// <summary>The last value read. What a poll compares against, and what a nested row reads from.</summary>
        public object CurrentValue { get; internal set; }

        internal Func<object> OwnerProvider;
        internal Action<object> Display;

        /// <summary>Re-reads this row and shows what it found, for a host that changed the value behind the control.</summary>
        public void Reload()
        {
            CurrentValue = Read();
            Display?.Invoke(CurrentValue);
        }
        internal Foldout Fold;
        internal bool DrawnAsList;

        public InspectorFilterRole FilterRole => InspectorFilterRole.Row;

        void IInspectorFilterField.CollectFilterNames(List<string> names)
        {
            if (IsElement)
            {
                return;
            }

            names.Add(DisplayName);
            if (Member != null)
            {
                names.Add(Member.Name);
            }
        }

        /// <summary>The object this row's member hangs off, right now.</summary>
        public object Owner => OwnerProvider?.Invoke();

        /// <summary>Reads the member's value off its owner.</summary>
        public object Read()
        {
            var owner = Owner;
            return owner == null || Member == null ? CurrentValue : Member.GetValue(owner);
        }

        /// <summary>Whether the pointer or keyboard is inside this row, so a poll should leave it alone.</summary>
        public bool HasFocus
        {
            get
            {
                var focused = panel?.focusController?.focusedElement as VisualElement;
                return focused != null && (focused == this || Contains(focused));
            }
        }
    }

    /// <summary>
    /// An inspector built from a type's VSL schema, drawable in a player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor's reflected inspector cannot come along into a build — it lives in an editor
    /// assembly and is built on <c>SerializedObject</c>. What can come along is the schema VSL already
    /// derives for every serialized type: the members, their declared types, their
    /// <c>[VslComment]</c> documentation and which profiles they belong to. That is enough to draw a
    /// working inspector, and it has the useful property of showing exactly what a document would
    /// store.
    /// </para>
    /// <para>
    /// Nothing is written directly. Every edit goes through <see cref="ValueWriter"/>, so a host that
    /// wants to record the change first — mark it unsaved, keep the value it displaced — is not
    /// fighting a control that already committed it.
    /// </para>
    /// <para>
    /// Rows implement the filter interfaces, so the same chips and member search that narrow the editor
    /// windows narrow this one.
    /// </para>
    /// </remarks>
    public class VslInspectorElement : VisualElement
    {
        /// <summary>How deep nesting goes before a value is drawn as text instead.</summary>
        public const int DefaultMaxDepth = 5;

        private static readonly Color RowRule = new Color(1f, 1f, 1f, 0.05f);

        private readonly List<VslInspectorRow> _rows = new();
        private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

        private object _target;
        private Type _declaredType;

        public VslInspectorElement()
        {
            style.flexDirection = FlexDirection.Column;
        }

        /// <summary>Which profiles' members are drawn. All of them by default.</summary>
        public VslProfiles Profiles { get; set; } = VslProfiles.All;

        public int MaxDepth { get; set; } = DefaultMaxDepth;

        /// <summary>How wide the name column is, so several inspectors can line up.</summary>
        public float LabelWidth { get; set; } = 140f;

        /// <summary>How wide the gutter is. Zero for a host that wants none.</summary>
        public float GutterWidth { get; set; } = 0f;

        /// <summary>Top-level members to leave out, by VSL name. For a host drawing them itself.</summary>
        public ICollection<string> ExcludedMembers => _excluded;

        /// <summary>
        /// Applies an edit. Returns false to reject it, in which case the control goes back to what the
        /// object actually holds. Defaults to writing the member directly.
        /// </summary>
        public Func<VslInspectorChange, bool> ValueWriter { get; set; }

        /// <summary>Called once per row as it is built, for a host adding its own gutter controls.</summary>
        public Action<VslInspectorRow> DecorateRow { get; set; }

        /// <summary>Rows this answers true for are left alone by <see cref="Refresh"/>.</summary>
        public Func<VslInspectorRow, bool> SuppressRefresh { get; set; }

        /// <summary>Every row drawn, in the order they appear.</summary>
        public IReadOnlyList<VslInspectorRow> Rows => _rows;

        public object Target => _target;

        /// <summary>Draws an object. Safe to call again with a different one.</summary>
        public void Rebuild(object target, Type declaredType = null)
        {
            _target = target;
            _declaredType = declaredType ?? target?.GetType();

            Clear();
            _rows.Clear();

            if (target == null)
            {
                Add(new Label("Nothing to show.") { style = { opacity = 0.6f } });
                return;
            }

            BuildMembers(this, () => _target, _declaredType, string.Empty, 0, new List<object> { target });
        }

        /// <summary>
        /// Re-reads what is drawn. Rows being typed into, and rows the host is holding, are left alone;
        /// a list whose length changed is rebuilt, since its rows no longer line up with its elements.
        /// </summary>
        public void Refresh()
        {
            var rebuilt = false;

            foreach (var row in _rows)
            {
                if (row.DrawnAsList && ListLengthChanged(row))
                {
                    rebuilt = true;
                    break;
                }
            }

            if (rebuilt)
            {
                Rebuild(_target, _declaredType);
                return;
            }

            foreach (var row in _rows)
            {
                if (row.Display == null || row.HasFocus || SuppressRefresh?.Invoke(row) == true)
                {
                    continue;
                }

                object value = row.Read();
                if (Equals(value, row.CurrentValue))
                {
                    continue;
                }

                row.CurrentValue = value;
                row.Display(value);
            }
        }

        private static bool ListLengthChanged(VslInspectorRow row) =>
            row.Read() is IList list ? list.Count != (row.CurrentValue as IList)?.Count : row.CurrentValue is IList;

        #region - Building -

        private void BuildMembers(VisualElement container, Func<object> ownerProvider, Type type, string path, int depth, List<object> ancestors)
        {
            var schema = VslTypeSchema.Get(type);

            foreach (var member in schema.Members)
            {
                if (!member.IsIn(Profiles))
                {
                    continue;
                }

                if (depth == 0 && _excluded.Contains(member.Name))
                {
                    continue;
                }

                container.Add(BuildRow(ownerProvider, member, path, depth, ancestors));
            }
        }

        private VslInspectorRow BuildRow(Func<object> ownerProvider, VslMember member, string parentPath, int depth, List<object> ancestors)
        {
            string path = string.IsNullOrEmpty(parentPath) ? member.Name : parentPath + "." + member.Name;
            object owner = ownerProvider();
            object value = owner == null ? null : member.GetValue(owner);

            var row = new VslInspectorRow
            {
                Path = path,
                Member = member,
                DisplayName = Prettify(member.Name),
                OwnerProvider = ownerProvider,
                CurrentValue = value,
                tooltip = member.Comment,
            };

            _rows.Add(row);
            Layout(row, depth);

            var control = VslInspectorControls.Create(member.MemberType, value,
                newValue => Commit(row, newValue), out var display);

            if (control != null)
            {
                row.Display = display;
                BuildLeaf(row, control);
            }
            else if (value is IList list && CanNest(member.MemberType, depth, value, ancestors))
            {
                BuildList(row, list, path, depth, ancestors);
            }
            else if (CanNest(member.MemberType, depth, value, ancestors))
            {
                BuildNested(row, () => row.CurrentValue, ValueTypeOf(value, member.MemberType), path, depth, ancestors);
            }
            else
            {
                BuildReadOnly(row, value, member.MemberType);
            }

            DecorateRow?.Invoke(row);
            return row;
        }

        /// <summary>
        /// Whether a value should be nested into rather than printed. Depth is the blunt guard; the
        /// ancestor check is the sharp one — an object graph that points back at something already on
        /// the way down would otherwise be drawn until the depth ran out, which is a long way to go for
        /// a cycle.
        /// </summary>
        private bool CanNest(Type declaredType, int depth, object value, List<object> ancestors)
        {
            if (value == null || depth >= MaxDepth)
            {
                return false;
            }

            if (value is IList)
            {
                return true;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return false;
            }

            foreach (var ancestor in ancestors)
            {
                if (ReferenceEquals(ancestor, value))
                {
                    return false;
                }
            }

            return VslTypeSchema.Get(type).Members.Length > 0 || declaredType == null;
        }

        private static Type ValueTypeOf(object value, Type declared) => value?.GetType() ?? declared;

        private void BuildLeaf(VslInspectorRow row, VisualElement control)
        {
            control.style.flexGrow = 1f;
            control.style.flexShrink = 1f;
            control.style.marginLeft = 0;
            control.style.marginRight = 0;

            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20;

            row.Add(BuildGutter(row));
            row.Add(BuildLabel(row));
            row.Add(control);
        }

        private void BuildReadOnly(VslInspectorRow row, object value, Type type)
        {
            var text = new Label(Describe(value, type))
            {
                style = { flexGrow = 1f, flexShrink = 1f, opacity = 0.65f, whiteSpace = WhiteSpace.Normal },
                tooltip = $"{type?.Name} has no runtime editor here.",
            };

            row.Display = v => text.text = Describe(v, type);
            BuildLeaf(row, text);
        }

        private void BuildNested(VslInspectorRow row, Func<object> valueProvider, Type type, string path, int depth, List<object> ancestors)
        {
            var fold = BuildFold(row, row.DisplayName);
            ancestors.Add(valueProvider());
            BuildMembers(fold.contentContainer, valueProvider, type, path, depth + 1, ancestors);
            ancestors.RemoveAt(ancestors.Count - 1);
        }

        private void BuildList(VslInspectorRow row, IList list, string path, int depth, List<object> ancestors)
        {
            row.DrawnAsList = true;
            var fold = BuildFold(row, $"{row.DisplayName}  ({list.Count})");

            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                var element = list[index];
                string elementPath = path + "[" + index + "]";

                var elementRow = new VslInspectorRow
                {
                    Path = elementPath,
                    DisplayName = ElementName(element, index),
                    IsElement = true,
                    OwnerProvider = () => list,
                    CurrentValue = element,
                };

                _rows.Add(elementRow);
                Layout(elementRow, depth + 1);

                if (element != null && CanNest(element.GetType(), depth + 1, element, ancestors))
                {
                    ancestors.Add(element);
                    var elementFold = BuildFold(elementRow, elementRow.DisplayName);
                    BuildMembers(elementFold.contentContainer, () => elementRow.CurrentValue, element.GetType(), elementPath, depth + 2, ancestors);
                    ancestors.RemoveAt(ancestors.Count - 1);
                }
                else
                {
                    BuildReadOnly(elementRow, element, element?.GetType());
                }

                DecorateRow?.Invoke(elementRow);
                fold.contentContainer.Add(elementRow);
            }
        }

        /// <summary>
        /// A foldout inside the row, with the gutter beside it rather than inside it. A real
        /// <see cref="Foldout"/> rather than a hand-drawn arrow, because the member search opens the
        /// foldouts between itself and a nested hit, and that is the control it knows how to open.
        /// </summary>
        private Foldout BuildFold(VslInspectorRow row, string title)
        {
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;

            var fold = new Foldout { text = title, value = false, style = { flexGrow = 1f, flexShrink = 1f } };

            row.Fold = fold;
            row.Content = fold.contentContainer;
            row.Add(BuildGutter(row));
            row.Add(fold);
            return fold;
        }

        private VisualElement BuildGutter(VslInspectorRow row)
        {
            row.Gutter = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexEnd,
                    width = GutterWidth,
                    minHeight = 18,
                    flexShrink = 0f,
                },
            };

            return row.Gutter;
        }

        private Label BuildLabel(VslInspectorRow row)
        {
            row.NameLabel = new Label(row.DisplayName)
            {
                tooltip = row.tooltip,
                style =
                {
                    width = LabelWidth,
                    flexShrink = 0f,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    paddingRight = 4,
                },
            };

            return row.NameLabel;
        }

        private static void Layout(VslInspectorRow row, int depth)
        {
            row.style.marginLeft = depth == 0 ? 0 : 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = RowRule;
        }

        #endregion

        #region - Committing -

        /// <summary>
        /// Hands an edit to the host and, if it takes it, keeps the row in step. A rejected edit puts
        /// the control back to what the object actually holds rather than leaving a value on screen
        /// that nothing stored.
        /// </summary>
        private void Commit(VslInspectorRow row, object newValue)
        {
            var change = new VslInspectorChange
            {
                Owner = row.Owner,
                Member = row.Member,
                Path = row.Path,
                OldValue = row.CurrentValue,
                NewValue = newValue,
            };

            bool applied = ValueWriter != null ? ValueWriter(change) : WriteDirect(change);
            if (applied)
            {
                row.CurrentValue = newValue;
                return;
            }

            object actual = row.Read();
            row.CurrentValue = actual;
            row.Display?.Invoke(actual);
        }

        private static bool WriteDirect(VslInspectorChange change)
        {
            if (change.Owner == null || change.Member == null)
            {
                return false;
            }

            try
            {
                change.Member.SetValue(change.Owner, change.NewValue);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{change.Path} rejected {change.NewValue}: {e.Message}");
                return false;
            }
        }

        #endregion

        #region - Text -

        /// <summary>A member's name with the words separated, so 'sendRateHz' reads as 'Send Rate Hz'.</summary>
        public static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var text = new StringBuilder(name.Length + 4);
            int start = name[0] == '_' ? 1 : 0;

            for (int i = start; i < name.Length; i++)
            {
                char c = name[i];
                if (i > start && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                {
                    text.Append(' ');
                }

                text.Append(i == start ? char.ToUpperInvariant(c) : c);
            }

            return text.ToString();
        }

        private static string ElementName(object element, int index) =>
            element == null ? $"[{index}]  null" : $"[{index}]  {element.GetType().Name}";

        /// <summary>What a value looks like when there is no control for it.</summary>
        private static string Describe(object value, Type type)
        {
            if (value == null)
            {
                return "null";
            }

            try
            {
                string document = Vsl.Serialize(value, type ?? value.GetType(), VslContext.For(VslProfiles.All)).Trim();
                return document.Length > 200 ? document[..200] + "…" : document;
            }
            catch (Exception)
            {
                return value.ToString();
            }
        }

        #endregion
    }
}
