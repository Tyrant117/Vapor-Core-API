using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using Vapor;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Marks an <see cref="EditorWindow"/> as the dedicated authoring tool for one data type and the
    /// family beneath it. The <c>Data Types</c> window still lists the type, but opens this window
    /// instead of drawing its entries inline — for data whose authoring wants a tool of its own,
    /// like actors.
    /// </summary>
    /// <remarks>
    /// Declared on the window rather than on the data type: the data type lives in a runtime
    /// assembly, which cannot name an editor window, and an editor-side attribute is discovered
    /// through <see cref="TypeCache"/> without either assembly knowing about the other. The window
    /// is expected to implement <see cref="IDataAuthoringWindow"/> so a caller can land on an entry.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class DataAuthoringWindowAttribute : Attribute
    {
        /// <summary>The authored data type this window edits. Subclasses sharing its document are covered.</summary>
        public Type DataType { get; }

        public DataAuthoringWindowAttribute(Type dataType) => DataType = dataType;
    }

    /// <summary>
    /// What a dedicated authoring window offers the rest of the editor: a way to open on an entry.
    /// </summary>
    public interface IDataAuthoringWindow
    {
        /// <summary>
        /// Selects <paramref name="entry"/> — matched by key, since the window loads its own copy of
        /// the document — or does nothing more than show the window when it is null.
        /// </summary>
        void ShowEntry(IData entry);
    }

    /// <summary>Resolves and opens the dedicated authoring window of a data type, when it has one.</summary>
    /// <remarks>Statics are cleaned on code load, so a window added or removed is seen after the next compile.</remarks>
    [AutoStaticsCleanup]
    public static partial class DataAuthoringWindows
    {
        private static Dictionary<Type, Type> s_ByDataType;

        /// <summary>
        /// The window declared for <paramref name="dataType"/>'s document owner, or null when the type
        /// is authored inline in the <c>Data Types</c> window.
        /// </summary>
        public static bool TryGetWindow(Type dataType, out Type windowType)
        {
            windowType = null;
            if (dataType == null)
            {
                return false;
            }

            EnsureCollected();

            // Looked up by the family's root, so a subclass authored in its base type's document opens
            // the same window the base does.
            var owner = VslDataStore.GetDocumentOwner(dataType);
            return (owner != null && s_ByDataType.TryGetValue(owner, out windowType))
                   || s_ByDataType.TryGetValue(dataType, out windowType);
        }

        public static bool HasWindow(Type dataType) => TryGetWindow(dataType, out _);

        /// <summary>
        /// Opens the window for <paramref name="dataType"/> and, when the window supports it, lands on
        /// <paramref name="entry"/>. Returns the window, or null when the type has none.
        /// </summary>
        public static EditorWindow Open(Type dataType, IData entry = null)
        {
            if (!TryGetWindow(dataType, out var windowType))
            {
                return null;
            }

            var window = EditorWindow.GetWindow(windowType);
            if (window == null)
            {
                return null;
            }

            window.Show();
            window.Focus();

            if (window is IDataAuthoringWindow authoring)
            {
                authoring.ShowEntry(entry);
            }

            return window;
        }

        private static void EnsureCollected()
        {
            if (s_ByDataType != null)
            {
                return;
            }

            s_ByDataType = new Dictionary<Type, Type>();

            foreach (var windowType in TypeCache.GetTypesWithAttribute<DataAuthoringWindowAttribute>())
            {
                if (!typeof(EditorWindow).IsAssignableFrom(windowType) || windowType.IsAbstract)
                {
                    Debug.LogError($"[{nameof(DataAuthoringWindowAttribute)}] on {windowType.Name}, which is not a concrete {nameof(EditorWindow)}. Ignored.");
                    continue;
                }

                foreach (var attribute in windowType.GetCustomAttributes(typeof(DataAuthoringWindowAttribute), false))
                {
                    var dataType = ((DataAuthoringWindowAttribute)attribute).DataType;
                    if (dataType == null)
                    {
                        continue;
                    }

                    if (s_ByDataType.TryGetValue(dataType, out var existing) && existing != windowType)
                    {
                        Debug.LogWarning($"Both {existing.Name} and {windowType.Name} claim to author {dataType.Name}. Using {existing.Name}.");
                        continue;
                    }

                    s_ByDataType[dataType] = windowType;
                }
            }
        }
    }
}
