using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    /// <summary>What a drawn element is to a filter walking over it.</summary>
    public enum InspectorFilterRole
    {
        /// <summary>A member. Hidden or shown whole, with everything nested inside it.</summary>
        Row,

        /// <summary>
        /// Layout around members — a group box, a section. Counts as a row when rows are collected, so
        /// hiding it takes its heading too, but a search filters <i>through</i> it rather than testing
        /// it, so a match narrows to the member inside rather than to the box around it.
        /// </summary>
        Group,

        /// <summary>The wrapper an object's own tree comes in. Not a row; its members are the rows.</summary>
        Container,
    }

    /// <summary>An element the filter can recognise while walking a drawn inspector.</summary>
    public interface IInspectorFilterElement
    {
        InspectorFilterRole FilterRole { get; }
    }

    /// <summary>An element a search can match by name.</summary>
    public interface IInspectorFilterField
    {
        /// <summary>
        /// The names this element answers to — a display name, a member name. Adding none means it
        /// never matches, which is what an array element does: they are named for their position, so
        /// matching them would turn any list into a wall of hits.
        /// </summary>
        void CollectFilterNames(List<string> names);
    }

    /// <summary>
    /// The engine behind the filter bar: which parts of a drawn inspector are showing, what the search
    /// narrows them to, and how to put it all back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is written back to the object. A part that is off only hides drawn elements, so a
    /// filtered inspector and an unfiltered one edit the same data and neither marks a document dirty.
    /// The model never builds the inspector either — it is handed elements that are already drawn and
    /// only decides which of them are displayed, which is what lets one engine serve a sectioned actor
    /// template, a flat data entry, and a live actor drawn in a build.
    /// </para>
    /// <para>
    /// Hiding remembers what a row's <c>display</c> was rather than forcing it back to
    /// <see cref="DisplayStyle.Flex"/> afterwards: a row can already be hidden by its own
    /// <c>[ShowIf]</c>, and clearing a search must not be what reveals it.
    /// </para>
    /// <para>
    /// The chrome — chips, a search field, a + button — lives with whoever is drawing. In the editor
    /// that is a toolbar search field and editor styling; in a build it is plain UI Toolkit. The part
    /// worth sharing is this: what a chip means, what a query matches, and the bookkeeping that makes
    /// both undoable.
    /// </para>
    /// </remarks>
    public class InspectorFilterModel
    {
        /// <summary>
        /// One region a chip shows or hides. Either a wrapper element with the rows inside it, or a
        /// bare list of rows that share no wrapper.
        /// </summary>
        public sealed class Target
        {
            /// <summary>
            /// Optional. Hidden whole when the target is, which is what takes a section's header down
            /// with its fields. Without one, the rows are hidden individually.
            /// </summary>
            public VisualElement Root;

            /// <summary>Optional. The rows are the outermost inspector elements under it.</summary>
            public VisualElement Container;

            /// <summary>Optional. The rows, when they share no container worth naming.</summary>
            public List<VisualElement> Rows;

            /// <summary>Optional. Whether the box these rows sit in is open.</summary>
            public Func<bool> IsExpanded;

            /// <summary>Optional. Opens the box a search matched into, and closes it again after.</summary>
            public Action<bool> SetExpanded;

            internal bool Shown = true;
        }

        /// <summary>One chip, and everything it stands for.</summary>
        public sealed class Part
        {
            public string Label;
            public string Tooltip;
            public Color Accent;

            /// <summary>
            /// Identity across rebuilds. A chip that was toggled off stays off when the parts are
            /// rebuilt and one with the same key comes back.
            /// </summary>
            public string Key;

            /// <summary>
            /// True for the boxes under <see cref="GroupHeader"/>, so the header can follow them.
            /// </summary>
            public bool InGroup;

            public bool Visible = true;

            public readonly List<Target> Targets = new();
        }

        private readonly List<Part> _parts = new();
        private readonly List<Foldout> _expandedFoldouts = new();
        private readonly List<Target> _expandedBoxes = new();
        private readonly List<(VisualElement Element, StyleEnum<DisplayStyle> Display)> _hidden = new();
        private readonly HashSet<VisualElement> _hiddenSet = new();
        private readonly List<IInspectorFilterField> _fieldScratch = new();
        private readonly List<string> _nameScratch = new();

        private string _query = string.Empty;

        public InspectorFilterModel()
        {
            EmptyHint = new Label
            {
                style = { opacity = 0.6f, marginTop = 6, marginBottom = 6, whiteSpace = WhiteSpace.Normal, display = DisplayStyle.None },
            };
        }

        /// <summary>
        /// Says why nothing is on screen when nothing is. Not a child of any bar — add it wherever the
        /// filtered content would have been, so it reads as the content's absence rather than as more
        /// chrome.
        /// </summary>
        public Label EmptyHint { get; }

        /// <summary>
        /// Optional. A header over the boxes that belongs to no chip — the "Components" rule, the
        /// "Extensions" label. Follows the parts marked <see cref="Part.InGroup"/>, and with no such
        /// parts at all it stays up while nothing is being searched, so whatever stands in for an
        /// empty stack has something to sit under.
        /// </summary>
        public VisualElement GroupHeader { get; set; }

        public IReadOnlyList<Part> Parts => _parts;

        public string Query => _query;

        /// <summary>The parts or their visibility changed; whoever draws the chips should redraw them.</summary>
        public event Action ChipsChanged;

        /// <summary>
        /// Replaces the parts. One whose <see cref="Part.Key"/> matches a part that was toggled off
        /// comes back toggled off, so adding a component does not undo what the user was looking at.
        /// </summary>
        public void SetParts(IEnumerable<Part> parts)
        {
            var wasHidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in _parts)
            {
                if (!part.Visible && !string.IsNullOrEmpty(part.Key))
                {
                    wasHidden.Add(part.Key);
                }
            }

            // The elements the old parts pointed at may be gone; anything still hidden or expanded on
            // their behalf is put back before they are dropped.
            RestoreEverything();

            _parts.Clear();
            foreach (var part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(part.Key) && wasHidden.Contains(part.Key))
                {
                    part.Visible = false;
                }

                _parts.Add(part);
            }

            ChipsChanged?.Invoke();
            Apply();
        }

        #region Parts

        /// <summary>Whether every part is on, which is what the "All" chip reflects.</summary>
        public bool IsShowingEverything
        {
            get
            {
                foreach (var part in _parts)
                {
                    if (!part.Visible)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// A plain click solos, because that is what the bar is for; the same click on the part that
        /// is already alone widens back out. Additive builds a set up one part at a time instead.
        /// </summary>
        public void ClickChip(Part part, bool additive = false)
        {
            if (part == null)
            {
                return;
            }

            if (additive)
            {
                part.Visible = !part.Visible;
            }
            else
            {
                var alreadyAlone = part.Visible;
                foreach (var other in _parts)
                {
                    if (other != part && other.Visible)
                    {
                        alreadyAlone = false;
                        break;
                    }
                }

                foreach (var other in _parts)
                {
                    other.Visible = alreadyAlone || other == part;
                }
            }

            ChipsChanged?.Invoke();
            Apply();
        }

        /// <summary>The chip at <paramref name="index"/>, counting the parts only — "All" is not one.</summary>
        public void ClickChip(int index, bool additive = false)
        {
            if (index >= 0 && index < _parts.Count)
            {
                ClickChip(_parts[index], additive);
            }
        }

        /// <summary>Whether anything the chip at <paramref name="index"/> stands for is on screen.</summary>
        public bool IsPartShown(int index)
        {
            if (index < 0 || index >= _parts.Count)
            {
                return false;
            }

            foreach (var target in _parts[index].Targets)
            {
                if (target.Shown)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Every part on. The search is left alone.</summary>
        public void ShowEverything()
        {
            foreach (var part in _parts)
            {
                part.Visible = true;
            }

            ChipsChanged?.Invoke();
            Apply();
        }

        /// <summary>Back to the whole object: every part on, no search.</summary>
        public virtual void Reset()
        {
            _query = string.Empty;
            ShowEverything();
        }

        /// <summary>Sets the search text as if it had been typed. Chrome keeps its field in step.</summary>
        public virtual void Search(string query)
        {
            _query = query?.Trim() ?? string.Empty;
            Apply();
        }

        /// <summary>Sets the query without re-applying, for chrome that is about to apply anyway.</summary>
        protected void SetQuery(string query) => _query = query?.Trim() ?? string.Empty;

        #endregion

        #region Filtering

        /// <summary>Re-applies the parts and the search to what is drawn.</summary>
        public void Apply()
        {
            // Undone first, so a change of query is always applied to an untouched inspector however it
            // changed - narrowed, widened or cleared.
            RestoreEverything();

            if (_parts.Count == 0)
            {
                EmptyHint.style.display = DisplayStyle.None;
                return;
            }

            var searching = _query.Length > 0;
            var anyShown = false;

            foreach (var part in _parts)
            {
                foreach (var target in part.Targets)
                {
                    if (!part.Visible)
                    {
                        target.Shown = false;
                    }
                    else if (!searching)
                    {
                        // Nothing to do to the rows: the restore above already put back what the last
                        // query hid, and a row's own [ShowIf] decides the rest.
                        target.Shown = true;
                    }
                    else
                    {
                        target.Shown = FilterTarget(target);
                    }

                    if (!target.Shown)
                    {
                        HideTarget(target);
                    }

                    anyShown |= target.Shown;
                }
            }

            if (GroupHeader != null)
            {
                anyShown |= ApplyGroupHeader(searching);
            }

            EmptyHint.text = searching
                ? $"Nothing named like \"{_query}\" in what the chips are showing."
                : "Everything is hidden. Press All to bring it back.";
            EmptyHint.style.display = anyShown ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// The header belongs to no chip. It follows the boxes under it, and with no boxes at all it
        /// stays up while nothing is being searched, so whatever stands in for them has something to
        /// sit under.
        /// </summary>
        private bool ApplyGroupHeader(bool searching)
        {
            var grouped = 0;
            var shown = false;

            foreach (var part in _parts)
            {
                if (!part.InGroup)
                {
                    continue;
                }

                grouped++;
                foreach (var target in part.Targets)
                {
                    shown |= target.Shown;
                }
            }

            var showHeader = grouped == 0 ? !searching : shown;
            if (!showHeader)
            {
                Hide(GroupHeader);
            }

            return showHeader;
        }

        private bool FilterTarget(Target target)
        {
            var any = FilterRows(RowsOf(target));

            if (any && target.SetExpanded != null && target.IsExpanded?.Invoke() == false)
            {
                // A hit inside a folded box is no use folded. Not persisted: the box goes back to the
                // way it was left as soon as the search clears.
                _expandedBoxes.Add(target);
                target.SetExpanded(true);
            }

            return any;
        }

        private void HideTarget(Target target)
        {
            if (target.Root != null)
            {
                Hide(target.Root);
                return;
            }

            // No wrapper to take down, so the rows go one at a time. Anything a search already hid is
            // skipped by Hide, so its remembered display is the one from before the filter touched it.
            foreach (var row in RowsOf(target))
            {
                Hide(row);
            }
        }

        /// <summary>
        /// Narrows a set of rows to those that match, hiding the rest. A group is layout rather than a
        /// member, so it is filtered through rather than tested — and a group left with nothing shown
        /// inside it is hidden too, or its heading would sit over an empty space.
        /// </summary>
        private bool FilterRows(List<VisualElement> rows)
        {
            var any = false;

            foreach (var row in rows)
            {
                var match = RoleOf(row) == InspectorFilterRole.Group
                    ? FilterRows(DirectRows(row))
                    : RowMatches(row);

                if (!match)
                {
                    Hide(row);
                }

                any |= match;
            }

            return any;
        }

        private bool RowMatches(VisualElement row)
        {
            _fieldScratch.Clear();
            CollectFields(row, _fieldScratch);

            // Copied out: expanding a foldout can rebuild children, and the walk is done with them.
            var fields = _fieldScratch.ToArray();
            var matched = false;

            foreach (var field in fields)
            {
                if (!FieldMatches(field))
                {
                    continue;
                }

                matched = true;

                // A nested hit shows its row too, but folded away it would read as a false positive, so
                // every foldout between the two is opened for as long as the search lasts.
                if (field is VisualElement element)
                {
                    ExpandFoldoutsUpTo(element, row);
                }
            }

            return matched;
        }

        private bool FieldMatches(IInspectorFilterField field)
        {
            if (field == null)
            {
                return false;
            }

            _nameScratch.Clear();
            field.CollectFilterNames(_nameScratch);

            foreach (var name in _nameScratch)
            {
                if (Matches(name, _query))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectFields(VisualElement element, List<IInspectorFilterField> fields)
        {
            if (element == null)
            {
                return;
            }

            if (element is IInspectorFilterField field)
            {
                fields.Add(field);
            }

            foreach (var child in element.Children())
            {
                CollectFields(child, fields);
            }
        }

        private void ExpandFoldoutsUpTo(VisualElement element, VisualElement stop)
        {
            for (var current = element; current != null && current != stop; current = current.parent)
            {
                if (current is Foldout { value: false } foldout)
                {
                    _expandedFoldouts.Add(foldout);
                    foldout.value = true;
                }
            }
        }

        private void Hide(VisualElement element)
        {
            if (element == null || !_hiddenSet.Add(element))
            {
                return;
            }

            _hidden.Add((element, element.style.display));
            element.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Undoes everything the last pass did — the elements it hid, the foldouts and the boxes it
        /// opened — leaving the inspector as if the filter had never touched it.
        /// </summary>
        private void RestoreEverything()
        {
            for (var i = _hidden.Count - 1; i >= 0; i--)
            {
                _hidden[i].Element.style.display = _hidden[i].Display;
            }

            _hidden.Clear();
            _hiddenSet.Clear();

            foreach (var foldout in _expandedFoldouts)
            {
                foldout.value = false;
            }

            _expandedFoldouts.Clear();

            foreach (var box in _expandedBoxes)
            {
                box.SetExpanded?.Invoke(false);
            }

            _expandedBoxes.Clear();
        }

        #endregion

        #region Walking what is drawn

        private static InspectorFilterRole RoleOf(VisualElement element) =>
            element is IInspectorFilterElement known ? known.FilterRole : InspectorFilterRole.Container;

        private static List<VisualElement> RowsOf(Target target) =>
            target.Rows ?? DirectRows(target.Container);

        private static List<VisualElement> DirectRows(VisualElement container)
        {
            var rows = new List<VisualElement>();
            CollectRows(container, rows);
            return rows;
        }

        /// <summary>
        /// The rows under an element: the outermost inspector elements below it. A nested object
        /// counts as one row, not as its contents, so hiding a row takes its whole subtree with it. A
        /// container is not a row — it is the wrapper a nested object's own tree comes in, and its
        /// members are the rows.
        /// </summary>
        /// <remarks>
        /// A group is a row here, because hiding one has to take its heading with it. Where what is
        /// wanted is the members rather than the layout around them — filtering a group's contents, or
        /// carving one member out of a group — the caller descends through it.
        /// </remarks>
        public static void CollectRows(VisualElement container, List<VisualElement> rows)
        {
            if (container == null)
            {
                return;
            }

            foreach (var child in container.Children())
            {
                if (RoleOf(child) == InspectorFilterRole.Container)
                {
                    CollectRows(child, rows);
                }
                else
                {
                    rows.Add(child);
                }
            }
        }

        /// <summary>
        /// The rows under an element with one of them carved out: every row that does not hold
        /// <paramref name="carveOut"/> is collected whole, and the one that does is descended into so
        /// its siblings are collected instead of it. Returns the row that owns the carve-out.
        /// </summary>
        /// <remarks>
        /// What splits an object with an extension list in two. Everything the inspector draws for one
        /// object sits under a single implicit group, so without this the list and the fields around
        /// it are one row and neither can be shown without the other. Only groups are descended
        /// through: the member that draws the list is where it stops, and that member is what the
        /// caller hides when no extension is on screen.
        /// </remarks>
        public static VisualElement CollectRowsExcept(VisualElement container, VisualElement carveOut, List<VisualElement> rows)
        {
            VisualElement owner = null;

            foreach (var row in DirectRows(container))
            {
                if (carveOut == null || (row != carveOut && !row.Contains(carveOut)))
                {
                    rows.Add(row);
                    continue;
                }

                // The row holds it. A group is layout, so its other members are what should be
                // collected; anything else is the member that draws it, and the search stops there.
                owner = RoleOf(row) == InspectorFilterRole.Group && row != carveOut
                    ? CollectRowsExcept(row, carveOut, rows) ?? row
                    : row;
            }

            return owner;
        }

        /// <summary>
        /// Fuzzy, case-insensitive: every character of the query in order, not necessarily adjacent,
        /// with spaces in the query ignored. "entk" finds Enabled Ticks; "net" finds Networked.
        /// </summary>
        public static bool Matches(string candidate, string query)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            var at = 0;
            foreach (var wanted in query)
            {
                if (wanted == ' ')
                {
                    continue;
                }

                var found = false;
                while (at < candidate.Length)
                {
                    if (char.ToLowerInvariant(candidate[at++]) == char.ToLowerInvariant(wanted))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
