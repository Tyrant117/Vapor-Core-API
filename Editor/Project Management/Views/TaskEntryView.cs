using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class TaskEntryView : VisualElement
    {
        private Label _label;
        public Label Label
        {
            get
            {
                _label ??= this.Q<Label>();
                return _label;
            }
        }
        
        private TextField _text;
        public TextField Text
        {
            get
            {
                _text ??= this.Q<TextField>();
                return _text;
            }
        }

        public override bool focusable => true;
        
        private readonly TaskEditorWindow _window;
        private readonly TaskModel _model;

        public TaskEntryView()
        {
            this.LoadUxmlFromResourcePath("Styles/TaskEntryView");
        }

        public TaskEntryView(TaskEditorWindow window, TaskModel model) : this()
        {
            _window = window;
            _model = model;
            Label.text = model.Name;

            if (model.Status == TaskStatus.Completed)
            {
                this.Q<Button>().Hide();
            }
            else
            {
                this.Q<Button>().clicked += OnCompleteClicked;
            }
            
            Text.RegisterValueChangedCallback(evt =>
            {
                _model.Name = evt.newValue;
                var lv = GetFirstAncestorOfType<ListView>();
                _window.RenameTask(_model, lv.name);
            });
            Text.RegisterCallback<FocusOutEvent>(evt =>
            {
                Text.Hide();
                Label.Show();
            });

            if (_model.IsPendingRename)
            {
                _model.IsPendingRename = false;
                schedule.Execute(StartRename).ExecuteLater(100);
            }
        }

        private void OnCompleteClicked()
        {
            _window.TaskComplete(_model);
        }

        public void StartRename()
        {
            Label.Hide();
            Text.SetValueWithoutNotify(_model.Name);
            Text.Show();
            schedule.Execute(() => Text.Focus()).ExecuteLater(100);
        }
    }
}
