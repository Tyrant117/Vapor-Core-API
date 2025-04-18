using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public abstract class InspectorBaseEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var inspector = new VisualElement();
            inspector.Add(DrawScript());
            InsertBeforeGraph(inspector);

            var so = new InspectorTreeObject(serializedObject);
            var tree = new InspectorTreeRootElement(so);
            tree.DrawToScreen(inspector);

            InsertAfterGraph(inspector);
            return inspector;
        }

        protected VisualElement DrawScript()
        {
            var script = new PropertyField(serializedObject.FindProperty("m_Script"));
            var hide = target.GetType().IsDefined(typeof(HideMonoScriptAttribute));
            script.style.display = hide ? DisplayStyle.None : DisplayStyle.Flex;
            script.SetEnabled(false);
            return script;
        }

        protected virtual void InsertBeforeGraph(VisualElement inspector)
        {

        }

        protected virtual void InsertAfterGraph(VisualElement inspector)
        {

        }        
    }
}
