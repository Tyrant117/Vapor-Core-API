using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    internal interface IStyledElement
    {
        readonly static StyleSheet s_Style = Resources.Load<StyleSheet>("StyledVisualElements");

        void Style();
    }
}