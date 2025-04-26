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
        protected MethodInfo DrawHandlesMethodInfo;
        
        protected virtual void OnSceneGUI()
        {
            if (DrawHandlesMethodInfo != null)
            {
                DrawHandlesMethodInfo.Invoke(target, null);
            }
            else
            {
                var atr = target.GetType().GetCustomAttribute<DrawHandlesAttribute>();
                if (atr == null)
                {
                    return;
                }

                DrawHandlesMethodInfo = ReflectionUtility.GetMethod(target.GetType(), atr.MethodName);
                DrawHandlesMethodInfo?.Invoke(target, null);
            }
        }
    }
#endif
}
