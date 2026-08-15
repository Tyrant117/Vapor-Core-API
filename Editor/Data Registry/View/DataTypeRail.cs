using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// The left rail: every authored type as a coloured bar, with its family underneath.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-built rather than a <see cref="TreeView"/>. Several types are expanded at once in normal
    /// use, and a stack of default foldouts gives no way to tell at a glance which rows belong to
    /// which type — hence the colour stripe and the solid bar, which a tree view's own item template
    /// cannot express.
    /// </para>
    /// <para>
    /// One document per type, so a bar is the thing you select. A family's concrete types appear
    /// beneath it as filters over that one document rather than as documents of their own.
    /// </para>
    /// </remarks>
    internal sealed class DataTypeRail : VisualElement
    {
        private const string ExpandedPrefix = "Vapor.DataTypes.Expanded.";
        private const int RowHeight = 20;
        private const int BarHeight = 22;

        private static readonly Color HoverRow = new Color(1f, 1f, 1f, 0.06f);

        /// <summary>Raised with the type now selected, or null when the selection was cleared.</summary>
        public event Action<Type> SelectionChanged;

        /// <summary>
        /// Raised with the concrete type the entry list should narrow to, or null for the whole family.
        /// </summary>
        public event Action<Type> TypeFilterChanged;

        /// <summary>
        /// Asked before the selection changes. Returning false leaves it alone — this is what lets the
        /// window prompt about unsaved changes and have the rail respect a cancel.
        /// </summary>
        public Func<bool> ConfirmChange { get; set; }

        public Type SelectedType { get; private set; }

        public Type TypeFilter { get; private set; }

        private readonly ToolbarSearchField _search;
        private readonly ScrollView _body;

        public DataTypeRail()
        {
            style.flexGrow = 1f;

            var toolbar = new Toolbar();
            _search = new ToolbarSearchField
            {
                tooltip = "Filter types by name.",
                style = { flexGrow = 1f, flexShrink = 1f, flexBasis = 0f, minWidth = 40 },
            };
            _search.RegisterValueChangedCallback(_ => Rebuild());
            toolbar.Add(_search);
            Add(toolbar);

            _body = new ScrollView { style = { flexGrow = 1f } };
            Add(_body);
        }

        #region Building

        public void Refresh() => Rebuild();

        private void Rebuild()
        {
            _body.Clear();

            var query = _search.value;
            var any = false;

            foreach (var type in VslDataStore.GetAuthoredTypes())
            {
                var concrete = VslDataStore.GetConcreteTypes(type);

                // A search matches the family name or any of its concrete types, so looking for
                // "OneShot" finds it without having to know which family it belongs to.
                if (!Matches(VslDataStore.GetDisplayName(type), query) &&
                    !concrete.Exists(c => Matches(c.Name, query)))
                {
                    continue;
                }

                any = true;
                _body.Add(BuildTypeBar(type, concrete));

                // A type with a window of its own is a link out of here; its family is that window's
                // business, so there is nothing to expand.
                if (DataAuthoringWindows.HasWindow(type))
                {
                    continue;
                }

                if (concrete.Count < 2 || (!IsExpanded(type) && string.IsNullOrEmpty(query)))
                {
                    continue;
                }

                foreach (var subType in concrete)
                {
                    _body.Add(BuildTypeFilterRow(type, subType));
                }
            }

            if (!any)
            {
                _body.Add(new Label(string.IsNullOrEmpty(query)
                    ? "No data types are marked [DataAuthoring]."
                    : "Nothing matches that search.")
                {
                    style = { opacity = 0.6f, marginTop = 8, marginLeft = 6, whiteSpace = WhiteSpace.Normal },
                });
            }
        }

        private VisualElement BuildTypeBar(Type type, List<Type> concrete)
        {
            var color = VslDataStore.GetColor(type);
            var selected = SelectedType == type;
            var hasWindow = DataAuthoringWindows.TryGetWindow(type, out var windowType);
            var hasFamily = !hasWindow && concrete.Count > 1;
            var expanded = hasFamily && (IsExpanded(type) || !string.IsNullOrEmpty(_search.value));
            var exists = File.Exists(VslDataStore.GetAbsolutePath(type));

            var location = exists
                ? $"{type.FullName}\n{VslDataStore.GetAssetPath(type)}"
                : $"{type.FullName}\n{VslDataStore.GetAssetPath(type)} (created on save)";

            var bar = new VisualElement
            {
                tooltip = hasWindow
                    ? $"{location}\nAuthored in its own window ({ObjectNames.NicifyVariableName(windowType.Name)}). Click to open it."
                    : location,
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = BarHeight,
                    flexShrink = 0f,
                    marginTop = 2,
                    paddingLeft = 4,
                    paddingRight = 4,
                    backgroundColor = new Color(color.r, color.g, color.b, selected ? 0.38f : 0.16f),
                    borderLeftWidth = 3,
                    borderLeftColor = color,
                },
            };

            // Only a family has anything to expand; a lone type gets a spacer so the labels still line
            // up down the rail.
            bar.Add(hasFamily
                ? Arrow(expanded, color)
                : new VisualElement { style = { width = 6, flexShrink = 0f, marginLeft = 2 } });

            bar.Add(new Label(VslDataStore.GetDisplayName(type))
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginLeft = 4,
                    flexGrow = 1f,
                    opacity = exists ? 1f : 0.7f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                },
            });

            if (hasFamily)
            {
                bar.Add(new Label($"{concrete.Count}")
                {
                    tooltip = $"{concrete.Count} types in this family",
                    style = { opacity = 0.6f, flexShrink = 0f },
                });
            }

            if (hasWindow)
            {
                // Says where the click goes. Text rather than a glyph, for the same reason the arrow
                // is drawn from borders: it cannot come out as a missing character.
                bar.Add(new Label("Window")
                {
                    style =
                    {
                        opacity = 0.6f,
                        flexShrink = 0f,
                        fontSize = 10,
                        unityFontStyleAndWeight = FontStyle.Italic,
                    },
                });
            }

            bar.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                if (hasWindow)
                {
                    // Not selected here — this window has nothing to show for it. The open document,
                    // whatever it is, stays exactly as it was.
                    DataAuthoringWindows.Open(type);
                }
                else
                {
                    OnTypeClicked(type, hasFamily);
                }

                evt.StopPropagation();
            });

            return bar;
        }

        /// <summary>
        /// A triangle drawn from borders rather than a glyph, so it cannot come out as a missing
        /// character in whatever font the editor is using.
        /// </summary>
        private static VisualElement Arrow(bool expanded, Color color)
        {
            var arrow = new VisualElement { style = { width = 0, height = 0, flexShrink = 0f, marginLeft = 2 } };

            if (expanded)
            {
                arrow.style.borderLeftWidth = 4;
                arrow.style.borderRightWidth = 4;
                arrow.style.borderTopWidth = 6;
                arrow.style.borderLeftColor = Color.clear;
                arrow.style.borderRightColor = Color.clear;
                arrow.style.borderTopColor = color;
            }
            else
            {
                arrow.style.borderTopWidth = 4;
                arrow.style.borderBottomWidth = 4;
                arrow.style.borderLeftWidth = 6;
                arrow.style.borderTopColor = Color.clear;
                arrow.style.borderBottomColor = Color.clear;
                arrow.style.borderLeftColor = color;
            }

            return arrow;
        }

        /// <summary>
        /// One concrete type of a family, indented by how far it sits below the root.
        /// </summary>
        /// <remarks>
        /// A filter, not a destination: it narrows the entry list and decides what the add button
        /// creates, but the document is the family's either way. Clicking the active one again clears
        /// it, which is how you get back to seeing the whole family.
        /// </remarks>
        private VisualElement BuildTypeFilterRow(Type root, Type subType)
        {
            var active = TypeFilter == subType;
            var color = VslDataStore.GetColor(root);
            var depth = VslDataStore.GetDepth(subType, root);

            var row = new VisualElement
            {
                tooltip = subType.FullName,
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = RowHeight,
                    flexShrink = 0f,
                    paddingLeft = 14 + depth * 10,
                    backgroundColor = active ? new Color(color.r, color.g, color.b, 0.28f) : Color.clear,
                },
            };

            row.Add(new VisualElement
            {
                style =
                {
                    width = 5,
                    height = 5,
                    marginRight = 5,
                    flexShrink = 0f,
                    backgroundColor = active ? color : new Color(color.r, color.g, color.b, 0.5f),
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                },
            });

            row.Add(new Label(ObjectNames.NicifyVariableName(subType.Name))
            {
                style =
                {
                    flexGrow = 1f,
                    opacity = active ? 1f : 0.8f,
                    unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                },
            });

            if (!active)
            {
                row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = HoverRow);
                row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = Color.clear);
            }

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                // Selecting a type of a family that is not open opens it first, so the filter always
                // has something to filter.
                if (SelectedType != root && !SelectType(root))
                {
                    evt.StopPropagation();
                    return;
                }

                TypeFilter = active ? null : subType;
                Rebuild();
                TypeFilterChanged?.Invoke(TypeFilter);
                evt.StopPropagation();
            });

            return row;
        }

        #endregion

        #region Selection

        private void OnTypeClicked(Type type, bool hasFamily)
        {
            if (SelectedType == type)
            {
                // Already open, so the click is about showing or hiding its family.
                if (hasFamily)
                {
                    SetExpanded(type, !IsExpanded(type));
                    Rebuild();
                }

                return;
            }

            if (SelectType(type) && hasFamily)
            {
                SetExpanded(type, true);
            }

            Rebuild();
        }

        /// <summary>
        /// Opens a type, clearing any filter left over from the last one. False when the user backed
        /// out of the unsaved-changes prompt.
        /// </summary>
        private bool SelectType(Type type)
        {
            if (ConfirmChange != null && !ConfirmChange())
            {
                return false;
            }

            SelectedType = type;

            // The filter named a concrete type of the family being left behind.
            if (TypeFilter != null && !type.IsAssignableFrom(TypeFilter))
            {
                TypeFilter = null;
                TypeFilterChanged?.Invoke(null);
            }

            SelectionChanged?.Invoke(type);
            return true;
        }

        /// <summary>
        /// Opens a named type, expanding whatever family holds it. For jumping to an entry.
        /// </summary>
        /// <remarks>
        /// Goes through the same path a click does, so an unsaved document still gets its prompt —
        /// arriving from somewhere else is not a reason to lose edits.
        /// </remarks>
        public void Select(Type type)
        {
            if (type == null || SelectedType == type)
            {
                return;
            }

            // Not this window's to open; the caller should have gone to the dedicated one.
            if (DataAuthoringWindows.HasWindow(type))
            {
                return;
            }

            if (SelectType(type))
            {
                SetExpanded(type, true);
            }

            Rebuild();
        }

        /// <summary>Opens the first type this window draws itself, so it has something on screen.</summary>
        public void SelectFirst()
        {
            foreach (var type in VslDataStore.GetAuthoredTypes())
            {
                if (DataAuthoringWindows.HasWindow(type))
                {
                    continue;
                }

                SelectType(type);
                Rebuild();
                return;
            }

            Rebuild();
        }

        #endregion

        #region State

        private static bool Matches(string value, string query) =>
            string.IsNullOrEmpty(query) ||
            (!string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

        // SessionState rather than EditorPrefs: which families are open is a property of what is being
        // worked on right now, not a setting worth carrying between sessions.
        private static bool IsExpanded(Type type) => SessionState.GetBool(ExpandedPrefix + type.Name, true);

        private static void SetExpanded(Type type, bool value) => SessionState.SetBool(ExpandedPrefix + type.Name, value);

        #endregion
    }
}
