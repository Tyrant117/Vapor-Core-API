using System.Reflection;
using UnityEditor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
#if VAPOR_INSPECTOR
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VaporBehaviour), true)]
    public class VaporBehaviourEditor : InspectorBaseEditor
    {
        protected virtual void OnSceneGUI()
        {
            var atr = target.GetType().GetCustomAttribute<DrawHandlesAttribute>();
            if (atr != null)
            {
                var methodInfo = ReflectionUtility.GetMethod(target.GetType(), atr.MethodName);
                methodInfo.Invoke(target, null);
            }
        }
    }
#endif
}
