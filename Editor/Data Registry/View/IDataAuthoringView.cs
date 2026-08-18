using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
using VaporEditor.Inspector;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Draws the right-hand pane of the <c>Data Types</c> window for one entry.
    /// </summary>
    /// <remarks>
    /// The extension point behind <see cref="DataAuthoringAttribute.View"/>. A data type whose shape
    /// the reflected inspector cannot express well — a curve editor, a table, a graph — supplies its
    /// own implementation and gets the list pane, the document and the save handling for free.
    /// </remarks>
    public interface IDataAuthoringView
    {
        /// <summary>
        /// Builds the editor for <paramref name="entry"/>. Called again whenever the selection
        /// changes, so an implementation may hold per-entry state.
        /// </summary>
        /// <param name="onChanged">
        /// Must be invoked after every edit. This is what marks the document dirty; an implementation
        /// that forgets it will silently lose the user's work on the next save.
        /// </param>
        VisualElement CreateInspector(DataDocument document, IData entry, Action onChanged);
    }

    /// <summary>
    /// Chrome the window pins above the scrolling editor, for a view that has some.
    /// </summary>
    /// <remarks>
    /// Read straight after <see cref="IDataAuthoringView.CreateInspector"/>, because a header is
    /// usually built from what that call drew — the filter bar's chips are one per component the
    /// inspector turned out to have. A view that wants no header simply does not implement this.
    /// </remarks>
    public interface IDataAuthoringHeader
    {
        VisualElement PinnedHeader { get; }
    }

    /// <summary>
    /// The default: the Vapor inspector over the entry, which is what makes a new data type
    /// authorable with no editor code at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inspector draws <c>[VslSerialize]</c> members whatever their access modifier, so what it
    /// shows is exactly what the document stores. <c>onChanged</c> is raised from the tree's own
    /// change event rather than per-field, so it covers nested objects and list elements too.
    /// </para>
    /// <para>
    /// An entry that carries extensions also gets a <see cref="InspectorFilterBar"/>: a chip for the
    /// entry's own fields, one per extension, and the <c>Add Extension</c> button as a + on the bar.
    /// An entry with no extensions is a short flat list of fields and gets nothing.
    /// </para>
    /// </remarks>
    public sealed class DefaultDataAuthoringView : IDataAuthoringView, IDataAuthoringHeader
    {
        private InspectorFilterBar _bar;
        private DataExtensionListView _extensions;
        private VisualElement _extensionsRow;
        private InspectorFilterModel.Part _entryPart;
        private IData _entry;

        /// <summary>The filter bar, when the entry earned one. Null otherwise.</summary>
        public VisualElement PinnedHeader => _bar;

        public VisualElement CreateInspector(DataDocument document, IData entry, Action onChanged)
        {
            _bar = null;
            _extensions = null;
            _entry = entry;

            var root = new VisualElement();
            var tree = new InspectorTreeRootElement(entry, entry.GetType());
            root.Add(tree);
            root.RegisterCallback<TreePropertyChangedEvent>(_ => onChanged?.Invoke());

            // The first extension list is the one the bar takes over. A type with two of them is not a
            // shape the chips can express, so any others keep their own header and Add button and are
            // covered by the entry's chip along with the rest of its fields.
            _extensions = tree.Q<DataExtensionListView>();
            if (_extensions == null)
            {
                return root;
            }

            _extensions.SuppressAddButton();

            _bar = new InspectorFilterBar();
            _bar.SetAddAction(_extensions.ShowAddPicker, "Add an extension to this entry. Only the kinds it does not already carry are offered.");
            _extensions.Rebuilt += RebuildParts;

            root.Insert(0, _bar.EmptyHint);
            RebuildParts(tree);

            return root;
        }

        private void RebuildParts() => RebuildParts(null);

        /// <summary>
        /// One chip for the entry — every top-level field except the extension list — and one per
        /// extension. Rebuilt whenever an extension is added or removed, because the chips are that
        /// list.
        /// </summary>
        /// <remarks>
        /// The tree is passed on the first call only. After that the rows have not moved — an
        /// extension being added rebuilds the list view inside its row, not the row — so the entry's
        /// part is kept as it was and only the extension chips are made again.
        /// </remarks>
        private void RebuildParts(VisualElement tree)
        {
            if (_bar == null || _extensions == null)
            {
                return;
            }

            var parts = new List<InspectorFilterModel.Part>();

            if (tree != null)
            {
                // The member that draws the list belongs to the extension chips, not to the entry's, so
                // it is carved out of the entry's rows - through the implicit group the inspector puts
                // every ungrouped member in, which would otherwise make the list and the fields around
                // it one indivisible row. It is also what the bar takes down when no extension is on
                // screen, so the label and the "no extensions" line under it go with it.
                var rows = new List<VisualElement>();
                _extensionsRow = InspectorFilterBar.CollectRowsExcept(tree, _extensions, rows);

                _entryPart = new InspectorFilterModel.Part
                {
                    Label = _entry.GetType().Name,
                    Tooltip = $"{_entry.GetType().FullName}\nThe entry's own fields.",
                    Accent = VslDataStore.GetColor(_entry.GetType()),
                    Key = "entry",
                };
                _entryPart.Targets.Add(new InspectorFilterModel.Target { Rows = rows });
            }

            if (_entryPart != null)
            {
                parts.Add(_entryPart);
            }

            _bar.GroupHeader = _extensionsRow ?? _extensions;

            foreach (var box in _extensions.Boxes)
            {
                var captured = box;
                var part = new InspectorFilterModel.Part
                {
                    Label = box.Type.Name,
                    Tooltip = box.Type.FullName,
                    Accent = DataExtensionListView.BoxAccent,
                    Key = box.Type.FullName,
                    InGroup = true,
                };

                part.Targets.Add(new InspectorFilterModel.Target
                {
                    Root = box.Root,
                    Container = box.Content,
                    IsExpanded = () => captured.IsExpanded,
                    SetExpanded = expanded => captured.SetExpanded(expanded, false),
                });

                parts.Add(part);
            }

            _bar.SetParts(parts);
        }
    }

    public static class DataAuthoringViewFactory
    {
        /// <summary>
        /// Resolves the view a data type asks for, falling back to the schema-driven editor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The attribute carries a bare <see cref="Type"/> because it lives in the runtime assembly
        /// and cannot name an editor interface, so the contract can only be checked here.
        /// </para>
        /// <para>
        /// A fresh instance every time, the default included. The interface says a view may hold
        /// per-entry state, and the default one does — the filter bar it built for the entry it just
        /// drew — which a shared instance would hand to the next window that asked.
        /// </para>
        /// </remarks>
        public static IDataAuthoringView Resolve(Type dataType)
        {
            var declared = VslDataStore.GetAuthoring(dataType)?.View;
            if (declared == null)
            {
                return new DefaultDataAuthoringView();
            }

            if (!typeof(IDataAuthoringView).IsAssignableFrom(declared))
            {
                Debug.LogError($"[DataAuthoring] on {dataType.Name} names {declared.Name} as its view, " +
                               $"but that does not implement {nameof(IDataAuthoringView)}. Using the default view.");
                return new DefaultDataAuthoringView();
            }

            try
            {
                return (IDataAuthoringView)Activator.CreateInstance(declared, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not create {declared.Name} for {dataType.Name}: {e.Message}. Using the default view.");
                return new DefaultDataAuthoringView();
            }
        }
    }
}
