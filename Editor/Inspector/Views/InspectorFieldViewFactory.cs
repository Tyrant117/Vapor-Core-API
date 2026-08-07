using UnityEditor;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Decides which element actually draws a field, and hands it back to the controller.
    /// Everything the choice depends on comes off the property, so the controller never has to reason
    /// about drawing and the drawn element never has to know it has a controller.
    /// </summary>
    internal static class InspectorFieldViewFactory
    {
        public static InspectorTreeElement.TreeView Create(InspectorTreeFieldElement owner)
        {
            var property = owner.Property;

            // Before the generic list path: a list of data extensions is a set of capabilities keyed by
            // type, not an ordered collection, so reordering and duplicate slots do not apply to it.
            if (DataExtensionListView.Matches(property, out var extensionType))
            {
                return new InspectorTreeElement.TreeView(new DataExtensionListView(property, owner, extensionType));
            }

            if (TryGetInlineGroupType(property, out var inlinedGroupType))
            {
                var group = InspectorTreeElement.SurroundWithVaporGroup(inlinedGroupType, property.PropertyPath, property.DisplayName);
                ApplyGroupTooltip(property, group);
                return new InspectorTreeElement.TreeView(group);
            }

            if (ShouldInlineAsManagedReferenceFoldout(property))
            {
                var foldout = InspectorTreeElement.SurroundWithVaporGroup(UIGroupType.Foldout, property.PropertyPath, property.DisplayName);
                ApplyGroupTooltip(property, foldout);
                return new InspectorTreeElement.TreeView(foldout);
            }

            // Fields sitting in a horizontal group get an extra wrapper so the row can lay out sideways
            // without the field itself having to care.
            var wrapInLayoutElement = owner.Group is { Type: UIGroupType.Horizontal };

            if (property.IsWrappedSystemObject)
            {
                var managedReference = SerializedDrawerUtility.DrawManagedReferenceAsField(owner, property, wrapInLayoutElement);
                return managedReference == null
                    ? InspectorTreeElement.TreeView.None
                    : new InspectorTreeElement.TreeView(managedReference);
            }

            var field = new TreePropertyField(property, owner);
            if (!field.IsValid)
            {
                return InspectorTreeElement.TreeView.None;
            }

            if (!wrapInLayoutElement)
            {
                return new InspectorTreeElement.TreeView(field);
            }

            var layoutElement = new VisualElement();
            layoutElement.Add(field);
            return new InspectorTreeElement.TreeView(layoutElement, field);
        }

        /// <summary>
        /// A [DrawWithVapor] type is drawn inline as its own group rather than through a field, so long as
        /// nothing else has already claimed responsibility for drawing it. The group type is read here
        /// rather than off the controller, which only resolves it on the paths that build children.
        /// </summary>
        private static bool TryGetInlineGroupType(InspectorTreeProperty property, out UIGroupType groupType)
        {
            groupType = UIGroupType.Vertical;
            if (!property.TryGetTypeAttribute<DrawWithVaporAttribute>(out var drawWithVapor)
                || property.IsUnityObjectOrSubclass()
                || property.HasCustomDrawer
                || property.IsWrappedSystemObject
                || property.IsManagedReference)
            {
                return false;
            }

            groupType = drawWithVapor.InlinedGroupType;
            return true;
        }

        private static bool ShouldInlineAsManagedReferenceFoldout(InspectorTreeProperty property)
        {
            return property.SerializedPropertyType == SerializedPropertyType.ManagedReference
                   && !property.HasCustomDrawer
                   && !property.NoChildProperties
                   && !property.IsWrappedSystemObject
                   && !property.IsManagedReference;
        }

        private static void ApplyGroupTooltip(InspectorTreeProperty property, VisualElement group)
        {
            if (!property.TryGetAttribute<RichTextTooltipAttribute>(out var richTextTooltip))
            {
                return;
            }

            if (group is StyledFoldout foldout)
            {
                foldout.Label.tooltip = richTextTooltip.Tooltip;
            }
            else
            {
                group.tooltip = richTextTooltip.Tooltip;
            }
        }
    }
}
