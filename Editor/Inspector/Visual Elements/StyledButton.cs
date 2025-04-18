using System;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class StyledButton : Button, IStyledElement
    {
        private int _size;

        public StyledButton(int size) : base()
        {
            _size = size;
            Style();
        }

        public StyledButton(int size, Action clickEvent) : base(clickEvent)
        {
            _size = size;
            Style();
        }

        public void Style()
        {
            styleSheets.Add(IStyledElement.s_Style);
            AddToClassList("styledButton");
            style.minHeight = _size + 4;
            style.height = _size + 4;
        }
    }
}
