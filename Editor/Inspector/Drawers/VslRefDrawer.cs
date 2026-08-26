using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
using Vapor.UIComponents;
using VaporEditor.Inspector;

namespace VaporEditor.DataRegistry
{
    /// <summary>
    /// Draws a <see cref="VslRef{T}"/> as a dropdown of the entries that exist for its target type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The point of the reference is that the field knows what it wants.</b> Referring to another entry
    /// by <c>GameplayTag</c> means a picker over every tag in the project and no way to tell whether the
    /// one chosen is even the right kind of thing. <see cref="VslRef{T}"/> carries its target type, so this
    /// can offer exactly the entries that would resolve and nothing else — a ship fit picking a hull is
    /// shown hulls.
    /// </para>
    /// <para>
    /// <b>Names, not keys.</b> The list holds each entry's dotted name because that is what the reference
    /// serializes and what a person recognises; the hash is derived on assignment. An entry whose target
    /// has been deleted or renamed still shows the name it was pointed at, marked missing, rather than
    /// silently reading as empty — a dangling reference the author cannot see is one they cannot fix.
    /// </para>
    /// <para>
    /// Rebuilt whenever the registry changes, because the Data Types window is precisely where somebody
    /// adds the entry this field is waiting for, and a dropdown that needed a domain reload to notice
    /// would be worse than a text box.
    /// </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(VslRef<>), true)]
    public class VslRefDrawer : VaporPropertyDrawer
    {
        private const string NoneLabel = "(none)";

        private TreePropertyField _field;
        private DropdownField _dropdown;
        private Type _targetType;

        public override VisualElement CreateVaporPropertyGUI(TreePropertyField field)
        {
            _field = field;
            _targetType = TargetTypeOf(field.Property.PropertyType);

            var group = new Group("my=4px") { Align = Align.Stretch };

            if (!field.Property.HasAttribute<HideLabelAttribute>())
            {
                var labelContainer = new StyledElement(StyleHelper.GetInspectorLabelStyle() + " mr=2 pr=2")
                {
                    style =
                    {
                        alignItems = Align.Center,
                        flexDirection = FlexDirection.Row,
                        justifyContent = Justify.FlexStart,
                    },
                };

                labelContainer.AddChild(new Text(field.Property.DisplayName, "mr=6 fg=1 ov=hidden tt=ellipsis ta=middleleft"));
                labelContainer.AddChild(new HelpUrlView(
                    field.Property.TryGetAttribute<RichTextTooltipAttribute>(out var tooltip) ? tooltip.Tooltip : null));
                group.Add(labelContainer);
            }

            _dropdown = new DropdownField { style = { flexGrow = 1f, marginLeft = 0, marginRight = 0 } };
            _dropdown.RegisterValueChangedCallback(OnChanged);

            Rebuild();

            // The Data Types window is exactly where somebody adds the entry this field is waiting for.
            GlobalDataRegistry.OnRegistryChanged += Rebuild;
            _dropdown.RegisterCallback<DetachFromPanelEvent>(_ => GlobalDataRegistry.OnRegistryChanged -= Rebuild);

            group.AddChild(_dropdown);
            return group;
        }

        /// <summary>The <c>T</c> of a <see cref="VslRef{T}"/>, or null if this is not one.</summary>
        private static Type TargetTypeOf(Type refType)
        {
            while (refType != null)
            {
                if (refType.IsGenericType && refType.GetGenericTypeDefinition() == typeof(VslRef<>))
                {
                    return refType.GetGenericArguments()[0];
                }

                refType = refType.BaseType;
            }

            return null;
        }

        private void Rebuild()
        {
            if (_dropdown == null || _field == null)
            {
                return;
            }

            var choices = new List<string> { NoneLabel };
            if (_targetType != null)
            {
                choices.AddRange(GlobalDataRegistry.GetAll()
                    .Where(entry => entry != null && _targetType.IsInstanceOfType(entry))
                    .Select(entry => entry.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .OrderBy(name => name, StringComparer.Ordinal));
            }

            string current = CurrentName();

            // A reference whose target has gone still shows what it was pointed at. Silently reading as
            // empty would hide the breakage from the one person able to fix it.
            if (!string.IsNullOrEmpty(current) && !choices.Contains(current))
            {
                choices.Insert(1, current);
            }

            _dropdown.choices = choices;
            _dropdown.SetValueWithoutNotify(string.IsNullOrEmpty(current) ? NoneLabel : current);
            MarkMissing(!string.IsNullOrEmpty(current) && !Exists(current));
        }

        private void MarkMissing(bool missing)
        {
            _dropdown.style.color = missing ? new StyleColor(new Color(0.95f, 0.45f, 0.45f)) : new StyleColor(StyleKeyword.Null);
            _dropdown.tooltip = missing ? "This entry no longer exists. Pick another, or re-create it." : null;
        }

        private bool Exists(string name) =>
            _targetType != null && GlobalDataRegistry.GetAll()
                .Any(entry => entry != null && _targetType.IsInstanceOfType(entry) && entry.Name == name);

        /// <summary>Reads the reference's name through its public surface, whatever <c>T</c> is.</summary>
        /// <remarks>
        /// Reflection rather than a cast, because the drawer is registered against the open generic and
        /// cannot name the closed type it is drawing. One property read per redraw on an editor field.
        /// </remarks>
        private string CurrentName()
        {
            object value = _field.Property.GetValue<object>();
            return value?.GetType().GetProperty(nameof(VslRef<GameplayTagStub>.Name))?.GetValue(value) as string;
        }

        private void OnChanged(ChangeEvent<string> evt)
        {
            string chosen = evt.newValue == NoneLabel ? null : evt.newValue;

            var refType = _field.Property.PropertyType;
            object built = chosen == null
                ? Activator.CreateInstance(refType)
                : Activator.CreateInstance(refType, chosen);

            _field.MarkDirtyWithValue(built, built);
            MarkMissing(chosen != null && !Exists(chosen));
        }

        /// <summary>Only ever used to name a property in <c>nameof</c>. Never constructed.</summary>
        private sealed class GameplayTagStub : IData
        {
            public string Name => null;

            public uint Key => 0;
        }
    }
}
