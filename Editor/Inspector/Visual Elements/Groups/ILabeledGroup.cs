using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public interface IElementGroup { }

    public interface ILabeledGroup : IElementGroup
    {
        public Label Label { get; }
    }
}
