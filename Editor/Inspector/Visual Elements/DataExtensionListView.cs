using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Draws a list of <see cref="IDataExtension"/> the way the inspector draws components: a stack of
    /// removable boxes, with an <c>Add Extension</c> button underneath.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extension is a capability an entry either has or does not, which is nothing like the ordered,
    /// duplicating collection the generic list drawer is built for — index reordering and duplicate
    /// entries are both meaningless here. So the list is keyed by type: one of each, added by picking a
    /// type rather than by growing an array and then choosing what goes in the slot.
    /// </para>
    /// <para>
    /// Recognised by shape rather than by a specific type, so any data type that carries extensions
    /// gets this for free.
    /// </para>
    /// </remarks>
    internal class DataExtensionListView : VisualElement
    {
        /// <summary>The list this view draws. Read by the comparison sheet, which splits it into fields.</summary>
        public InspectorTreeProperty Property => _property;

        private readonly InspectorTreeProperty _property;
        private readonly InspectorTreeElement _owner;
        private readonly Type _elementType;
        private readonly DataExtensionFilterAttribute _filter;
        private VisualElement _body;

        /// <summary>
        /// True when this property is a list of data extensions, and hands back its element type.
        /// </summary>
        public static bool Matches(InspectorTreeProperty property, out Type elementType)
        {
            elementType = null;

            if (property is not { IsArray: true } || property.PropertyType == null)
            {
                return false;
            }

            elementType = property.PropertyType.IsArray
                ? property.PropertyType.GetElementType()
                : property.PropertyType.GetGenericArguments().FirstOrDefaultSafe();

            return elementType != null && typeof(IDataExtension).IsAssignableFrom(elementType);
        }

        public DataExtensionListView(InspectorTreeProperty property, InspectorTreeElement owner, Type elementType)
        {
            _property = property;
            _owner = owner;
            _elementType = elementType;
            _filter = property.TryGetAttribute<DataExtensionFilterAttribute>(out var filter) ? filter : null;

            style.flexGrow = 1f;
            style.marginTop = 2;
            style.marginBottom = 2;

            Rebuild();
        }

        private void Rebuild()
        {
            Clear();

            Add(new Label(_property.DisplayName)
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 },
            });

            _body = new VisualElement();
            Add(_body);

            var list = Current();
            if (list == null || list.Count == 0)
            {
                _body.Add(new Label("No extensions.")
                {
                    style = { opacity = 0.6f, marginLeft = 4, marginBottom = 2 },
                });
            }
            else
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is IDataExtension extension)
                    {
                        _body.Add(BuildBox(extension));
                    }
                }
            }

            var add = new Button(ShowPicker) { text = "Add Extension" };
            add.style.marginTop = 2;
            Add(add);
        }

        private VisualElement BuildBox(IDataExtension extension)
        {
            var type = extension.GetType();

            var box = new VisualElement
            {
                style =
                {
                    marginBottom = 3,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingBottom = 3,
                    backgroundColor = new Color(1f, 1f, 1f, 0.03f),
                    borderLeftWidth = 2,
                    borderLeftColor = new Color(0.4f, 0.6f, 0.85f, 0.8f),
                },
            };

            var header = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 },
            };

            header.Add(new Label(ObjectNames.NicifyVariableName(type.Name))
            {
                tooltip = type.FullName,
                style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1f },
            });

            header.Add(new Button(() => Remove(type))
            {
                text = "-",
                tooltip = $"Remove {type.Name}.",
                style = { width = 20, flexShrink = 0f, marginLeft = 0, marginRight = 0 },
            });

            box.Add(header);

            // The extension is a plain C# object, so it draws through the same reflected inspector as
            // any nested member - which is what gives it its drawers and its change handling.
            box.Add(new InspectorTreeRootElement(extension, type, _property.InspectorObject));
            return box;
        }

        #region Mutation

        private IList Current() => _property.GetValueSafe(true) as IList;

        private void ShowPicker()
        {
            var taken = new HashSet<Type>();
            var list = Current();
            if (list != null)
            {
                foreach (var item in list)
                {
                    if (item != null)
                    {
                        taken.Add(item.GetType());
                    }
                }
            }

            // The candidates are known here, so the picker is handed them rather than left to filter the
            // whole project down to them. That is what lets it show a flat, readable list.
            var host = _property.GetParentObject() as IExtensionHostFilter;
            var provider = TypeSearchProvider.ForTypes(
                model => Add(model.Type),
                ReflectionUtility.GetAssignableTypesOf(_elementType).Where(candidate => IsOffered(candidate, taken, host)));

            var position = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? new Vector2(200, 200));
            TypeSearchWindow.Show(position, position, provider, false, true, TypeDisplayNames.GetDisplayName(_elementType));
        }

        /// <summary>
        /// Whether the picker should offer a type: a concrete extension of the right family, that the
        /// filter allows, that the owning entry accepts, and that is not already on this entry.
        /// </summary>
        /// <remarks>
        /// The owner has the last word. A family root can declare what its members may carry, but
        /// only the entry knows what it is — a controller and a pawn share one extension type and
        /// take different components — so an owner implementing <see cref="IExtensionHostFilter"/>
        /// narrows the list further.
        /// </remarks>
        private bool IsOffered(Type candidate, HashSet<Type> taken, IExtensionHostFilter host) =>
            candidate is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
            && _elementType.IsAssignableFrom(candidate)
            && (_filter == null || _filter.Allows(candidate))
            && (host == null || host.AcceptsExtension(candidate))
            && !taken.Contains(candidate);

        private void Add(Type type)
        {
            var list = Current();
            if (list == null)
            {
                list = (IList)InspectorTreeProperty.GetDefaultValue(_property.SerializedPropertyType, _property.PropertyType);
                if (list == null)
                {
                    Debug.LogError($"Could not create the list backing {_property.PropertyPath}.");
                    return;
                }
            }

            object instance;
            try
            {
                // nonPublic: an extension's parameterless constructor exists for the serializer and is
                // not meant to be part of its public surface.
                instance = Activator.CreateInstance(type, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not create {type.Name}: {e.Message}");
                return;
            }

            if (instance is IDataExtension extension)
            {
                extension.Owner = _property.GetParentObject() as IData;
            }

            list.Add(instance);
            Commit(list);
        }

        private void Remove(Type type)
        {
            var list = Current();
            if (list == null)
            {
                return;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i]?.GetType() == type)
                {
                    list.RemoveAt(i);
                }
            }

            Commit(list);
        }

        /// <summary>
        /// Writes the list back and redraws.
        /// </summary>
        /// <remarks>
        /// Without the array rebuild: this view owns its own layout and rebuilds it here, so the
        /// generic list machinery has nothing to rebuild and its deferred redraw would only tear this
        /// down and put it back.
        /// </remarks>
        private void Commit(IList list)
        {
            var arrayType = _property.PropertyType;
            if (arrayType.IsArray)
            {
                var array = Array.CreateInstance(_elementType, list.Count);
                list.CopyTo(array, 0);
                _property.SetValueWithoutArrayRebuild(array);
            }
            else
            {
                _property.SetValueWithoutArrayRebuild(list);
            }

            using (var evt = TreePropertyChangedEvent.GetPooled())
            {
                evt.target = this;
                SendEvent(evt);
            }

            Rebuild();
        }

        #endregion
    }

    internal static class TypeArrayExtensions
    {
        /// <summary>First generic argument, or null. Saves a Linq using in a hot path.</summary>
        public static Type FirstOrDefaultSafe(this Type[] types) => types is { Length: > 0 } ? types[0] : null;
    }
}
