using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;
using VaporEditor.ProjectManagement;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class ChecklistEntryView : VisualElement
    {
        private readonly TaskWindowView _view;
        private readonly ChecklistEntryModel _model;

        public ChecklistEntryView()
        {
            this.LoadUxmlFromResourcePath("Styles/ChecklistEntryView");
        }

        public ChecklistEntryView(TaskWindowView view, ChecklistEntryModel model) : this()
        {
            _view = view;
            _model = model;

            var toggle = this.Q<Toggle>();
            toggle.SetValueWithoutNotify(model.Checked);
            toggle.RegisterValueChangedCallback(evt =>
            {
                model.SetChecked(evt.newValue);
                _view.UpdateChecklistCount();
            });
            
            var textField = this.Q<TextField>();
            textField.SetValueWithoutNotify(model.Description);
            textField.RegisterValueChangedCallback(evt => model.SetDescription(evt.newValue));
            
            var deleteButton = this.Q<Button>();
            deleteButton.clicked += () =>
            {
                _view.DeleteChecklistEntry(model);
            };
        }
    }
}
