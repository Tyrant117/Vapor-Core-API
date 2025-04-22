using System;

namespace VaporEditor.ProjectManagement
{
    [Serializable]
    public class ChecklistEntryModel
    {
        public string Description;
        public bool Checked;
        
        public event Action<ChecklistEntryModel> Changed;

        public void SetDescription(string description)
        {
            Description = description;
            Changed?.Invoke(this);
        }

        public void SetChecked(bool checkedState)
        {
            Checked = checkedState;
            Changed?.Invoke(this);
        }
    }
}
