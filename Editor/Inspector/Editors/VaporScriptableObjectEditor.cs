using UnityEditor;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VaporScriptableObject), true)]
    public class VaporScriptableObjectEditor : InspectorBaseEditor
    {
    }
}
