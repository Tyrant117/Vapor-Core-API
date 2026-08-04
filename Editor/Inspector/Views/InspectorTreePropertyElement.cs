using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// Draws a C# property exposed with [ShowInInspector] as a read-only row. Properties are computed
    /// rather than stored, so the row displays a value and never writes one back.
    /// </summary>
    public class InspectorTreePropertyElement : InspectorTreeElement
    {
        public InspectorTreePropertyElement(InspectorTreeElement parentElement, InspectorTreeProperty property)
        {
            Root = parentElement.Root;
            Parent = parentElement;
            IsRoot = false;

            InspectorObject = property.InspectorObject;
            Property = property;
            HasProperty = true;

            FindGroupsAndDrawOrder();
            BuildChildren();
            BuildGroupNodes();

            ApplyRowDecorators();
            InitializeView();
            ApplyRowConditionals();

            // Disabled after the view exists so it covers whatever was built, and applied to the whole row
            // rather than the input so inline decorations grey out with it.
            SetEnabled(false);
        }

        protected override TreeView BuildView()
        {
            // Dynamic properties keep polling their source; a plain one is read once when the row is built,
            // which is all a constant needs and costs nothing per frame.
            var dynamic = Property.TryGetAttribute<ShowInInspectorAttribute>(out var atr) && atr.Dynamic;

            var field = new TreePropertyField(Property, this, dynamic
                ? TreePropertyField.ValueSource.Polled
                : TreePropertyField.ValueSource.ReadOnce);

            return field.IsValid ? new TreeView(field) : TreeView.None;
        }
    }
}
