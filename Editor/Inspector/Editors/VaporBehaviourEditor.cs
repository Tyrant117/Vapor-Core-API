using UnityEditor;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VaporBehaviour), true)]
    public class VaporBehaviourEditor : InspectorBaseEditor
    {
        /// <summary>
        /// Runs the type's <see cref="DrawHandlesAttribute"/> handler, once per selected object.
        /// </summary>
        /// <remarks>
        /// Unity has not been consistent across versions about whether it calls this once per editor or
        /// once per target with <see cref="Editor.target"/> advanced each time. Drawing the whole
        /// selection only on the pass where <c>target</c> is the first entry is correct under both, and
        /// does not depend on knowing which one this editor version does.
        /// </remarks>
        protected virtual void OnSceneGUI()
        {
            // The editor can outlive its target by a frame when the object is deleted from the scene.
            if (target == null)
            {
                return;
            }

            var binding = DrawHandlesBinding.Get(target.GetType());
            if (!binding.HasHandler)
            {
                return;
            }

            if (targets.Length > 1 && !ReferenceEquals(target, targets[0]))
            {
                return;
            }

            var view = SceneView.currentDrawingSceneView;
            foreach (var selected in targets)
            {
                binding.Invoke(selected, view);
            }
        }

        /// <summary>
        /// A misnamed or wrongly-shaped handles method is otherwise a silent no-op — the handles simply
        /// never appear, with nothing to say why. It belongs above the fields because it is a fault in
        /// the type rather than in any one of them.
        /// </summary>
        protected override void InsertBeforeGraph(VisualElement inspector)
        {
            base.InsertBeforeGraph(inspector);

            var binding = DrawHandlesBinding.Get(target.GetType());
            if (binding.HasError)
            {
                inspector.Add(ResolverBinding.CreateErrorBox(binding.Error));
            }
        }
    }
}
