using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public abstract class VaporPropertyDrawer : PropertyDrawer
    {
        public abstract VisualElement CreateVaporPropertyGUI(TreePropertyField field);
    }
}
