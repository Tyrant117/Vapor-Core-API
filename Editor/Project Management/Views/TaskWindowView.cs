using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Vapor.Inspector;
using System.Linq;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class TaskWindowView : VisualElement
    {
        private TaskEditorWindow _window;
        private TaskModel _model;

        private VisualElement _typeIcon;
        private TextField _header;
        private EnumField _taskType;
        private TextField _description;
        private ListView _checklist;
        
        private Label _checklistLabel;
        public Label ChecklistLabel
        {
            get
            {
                _checklistLabel ??= this.Q<Label>("ChecklistCount");
                return _checklistLabel;
            }
        }
        

        public TaskWindowView()
        {
            this.ConstructFromResourcePath("Styles/TaskWindowView");
        }

        public void Init(TaskEditorWindow window)
        {
            _window = window;
            _header = this.Q<TextField>("Header");
            _header.RegisterValueChangedCallback(OnRenameTask);
            _typeIcon = this.Q<VisualElement>("TypeIcon");
            
            this.Q<Button>("Close").clicked += OnCloseClicked;
            
            _taskType = this.Q<EnumField>();
            _taskType.RegisterValueChangedCallback(OnChangeTaskType);
            
            _description = this.Q<TextField>("Description");
            _description.RegisterValueChangedCallback(OnChangeDescription);
            _description.Q<TextElement>().enableRichText = true;

            this.Q<Button>("AddChecklistEntry").clicked += OnAddChecklistEntryClicked;
            _checklist = this.Q<ListView>();
            _checklist.makeItem = () => new VisualElement();
            _checklist.bindItem = (element, i) =>
            {
                element.Clear();
                if (_checklist.itemsSource is not List<ChecklistEntryModel> checklistEntryModels)
                {
                    return;
                }

                var checklist = checklistEntryModels[i];
                var entry = new ChecklistEntryView(this, checklist);
                element.Add(entry);
            };
        }

        private void OnAddChecklistEntryClicked()
        {
            _model.AddChecklistEntry(new ChecklistEntryModel());
            ChecklistLabel.text = $"{_model.Checklist.Count(m => m.Checked)}/{_model.Checklist.Count}";
            _checklist.Rebuild();
        }

        private void OnChangeDescription(ChangeEvent<string> evt)
        {
            _model.SetDescription(evt.newValue);
        }

        private void OnChangeTaskType(ChangeEvent<Enum> evt)
        {
            _model.SetType((TaskType)evt.newValue);
            _typeIcon.style.backgroundImage = IconUtility.GetTaskBackground(_model.Type);
        }

        private void OnRenameTask(ChangeEvent<string> evt)
        {
            _model.Rename(evt.newValue);
        }

        public void Show(TaskModel model)
        {
            _model = model;

            _typeIcon.style.backgroundImage = IconUtility.GetTaskBackground(_model.Type);
            _header.SetValueWithoutNotify(model.Name);
            _taskType.SetValueWithoutNotify(model.Type);
            _description.SetValueWithoutNotify(model.Description);
            _checklist.itemsSource = _model.Checklist;
            ChecklistLabel.text = $"{_model.Checklist.Count(m => m.Checked)}/{_model.Checklist.Count}";
            
            this.Show();
        }

        

        private void OnCloseClicked()
        {
            _window.HideTaskWindow();
            this.Hide();
        }

        public void DeleteChecklistEntry(ChecklistEntryModel model)
        {
            var idx = _model.Checklist.IndexOf(model);
            if (idx == -1)
            {
                return;
            }

            _model.RemoveChecklistEntryAt(idx);
            ChecklistLabel.text = $"{_model.Checklist.Count(m => m.Checked)}/{_model.Checklist.Count}";
            _checklist.Rebuild();
        }

        public void UpdateChecklistCount()
        {
            ChecklistLabel.text = $"{_model.Checklist.Count(m => m.Checked)}/{_model.Checklist.Count}";
        }
    }
}
