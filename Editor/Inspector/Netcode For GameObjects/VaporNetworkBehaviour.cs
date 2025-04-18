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
