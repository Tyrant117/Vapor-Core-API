using System.Collections.Generic;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    [UxmlElement]
    public partial class DropdownElement : ComboBox<object>
    {
        [UxmlAttribute]
        public string LabelName { get => Label.text; set => Label.text = value; }
        [UxmlAttribute]
        public bool MultiSelect { get; set; }
        
        public DropdownElement()
        {
            SetMultiSelect(MultiSelect);
        }

        public DropdownElement(string label, int selectedIndex, List<string> choices, List<object> values, bool multiSelect, bool noCopy = false) : base(label, selectedIndex, choices, values, null,
            multiSelect, noCopy: noCopy)
        {
            
        }
    }
}