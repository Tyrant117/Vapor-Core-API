using System.Reflection;
using UnityEditor;
using Vapor.Inspector;
#if VAPOR_NETCODE
using Vapor.Inspector.Netcode;
#endif

namespace VaporEditor.Inspector.Netcode
{
#if VAPOR_INSPECTOR && VAPOR_NETCODE
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VaporNetworkBehaviour), true)]
    public class VaporNetworkBehaviourEditor : InspectorBaseEditor
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
