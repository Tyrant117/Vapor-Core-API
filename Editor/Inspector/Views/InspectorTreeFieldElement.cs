using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public class InspectorTreeFieldElement : InspectorTreeElement
    {
        public InspectorTreeFieldElement(InspectorTreeElement parentElement, InspectorTreeProperty property)
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

            // Row framing first so the separator lands above the field, then the field, then the rules
            // that decide whether the row is shown at all.
            ApplyRowDecorators();
            InitializeView();
            ApplyRowConditionals();
            WireArrayEntryName();

            RegisterCallback<TreePropertyChangedEvent>(OnTreePropertyChanged);
        }

        /// <summary>
        /// Keeps an [ArrayEntryName] header in step with the values it is derived from.
        /// <para>
        /// Editing an element used to rebuild the whole list, which refreshed the label as a side effect.
        /// That rebuild destroyed the widget being typed into, so it is gone - the label subscribes to its
        /// own contents instead, which costs nothing until something actually changes.
        /// </para>
        /// </summary>
        private void WireArrayEntryName()
        {
            if (!Property.IsArrayElement || !Property.HasArrayEntryName || View is not ILabeledGroup labeledGroup)
            {
                return;
            }

            ForEachDescendantField(field =>
                field.ValueChanged += (_, _, _) => labeledGroup.Label.text = Property.ResolveDisplayName());
        }

        protected override TreeView BuildView()
        {
            return InspectorFieldViewFactory.Create(this);
        }

        private void OnTreePropertyChanged(TreePropertyChangedEvent evt)
        {
            //InspectorDebug.Log($"TreePropertyChanged: {evt.target} - {Property.PropertyName}");
            if (TryGetAttribute<OnValueChangedAttribute>(out var ovc))
            {
                var methodInfo = ReflectionUtility.GetMethod(Property.GetParentsParentType(), ovc.MethodName);
                if (methodInfo != null)
                {
                    var t = Property.PropertyType;
                    var @new = TreePropertyField.CastToType(Property.GetValue(), t);

                    var target = Property.IsArrayElement ? Property.ParentProperty.GetParentObject() : Property.GetParentObject();
                    if (methodInfo.IsStatic)
                    {
                        target = null;
                    }
                    
                    int length = methodInfo.GetParameters().Length;
                    switch (length)
                    {
                        case 3:
                        {
                            var tpf = evt.target as TreePropertyField;
                            methodInfo.Invoke(target, new[] { tpf?.Property.PropertyName, evt.Previous, @new });
                            break;
                        }
                        case 2:
                        {
                            methodInfo.Invoke(target, new[] { null, @new });
                            break;
                        }
                        case 1:
                        {
                            methodInfo.Invoke(target, new[] { @new });
                            break;
                        }
                        default:
                            InspectorDebug.Log(target);
                            InspectorDebug.Log(methodInfo.Name);
                            methodInfo.Invoke(target, new object[] { });
                            break;
                    }
                    
                    if (ovc.RebuildTree)
                    {
                        Root.RebuildAndRedraw();
                    }
                }
            }
        }
    }
}
