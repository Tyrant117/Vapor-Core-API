using System;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
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
    /// The default: the Vapor inspector over the entry, which is what makes a new data type
    /// authorable with no editor code at all.
    /// </summary>
    /// <remarks>
    /// The inspector draws <c>[VslSerialize]</c> members whatever their access modifier, so what it
    /// shows is exactly what the document stores. <paramref name="onChanged"/> is raised from the
    /// tree's own change event rather than per-field, so it covers nested objects and list elements
    /// too.
    /// </remarks>
    public sealed class DefaultDataAuthoringView : IDataAuthoringView
    {
        public VisualElement CreateInspector(DataDocument document, IData entry, Action onChanged)
        {
            var root = new VisualElement();
            root.Add(new InspectorTreeRootElement(entry, entry.GetType()));
            root.RegisterCallback<TreePropertyChangedEvent>(_ => onChanged?.Invoke());
            return root;
        }
    }

    public static class DataAuthoringViewFactory
    {
        private static readonly DefaultDataAuthoringView s_Default = new DefaultDataAuthoringView();

        /// <summary>
        /// Resolves the view a data type asks for, falling back to the schema-driven editor.
        /// </summary>
        /// <remarks>
        /// The attribute carries a bare <see cref="Type"/> because it lives in the runtime assembly
        /// and cannot name an editor interface, so the contract can only be checked here.
        /// </remarks>
        public static IDataAuthoringView Resolve(Type dataType)
        {
            var declared = VslDataStore.GetAuthoring(dataType)?.View;
            if (declared == null)
            {
                return s_Default;
            }

            if (!typeof(IDataAuthoringView).IsAssignableFrom(declared))
            {
                Debug.LogError($"[DataAuthoring] on {dataType.Name} names {declared.Name} as its view, " +
                               $"but that does not implement {nameof(IDataAuthoringView)}. Using the default view.");
                return s_Default;
            }

            try
            {
                return (IDataAuthoringView)Activator.CreateInstance(declared, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not create {declared.Name} for {dataType.Name}: {e.Message}. Using the default view.");
                return s_Default;
            }
        }
    }
}
