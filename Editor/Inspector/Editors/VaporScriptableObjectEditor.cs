using UnityEditor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
#if VAPOR_INSPECTOR
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VaporScriptableObject), true)]
    public class VaporScriptableObjectEditor : InspectorBaseEditor
    {
    }
#endif
}
