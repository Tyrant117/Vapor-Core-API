using System.Linq;
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
                _label ??= this.Q<Label>("Name");
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
        
        private VisualElement _icon;
        public VisualElement Icon
        {
            get
            {
                _icon ??= this.Q<VisualElement>("Icon");
                return _icon;
            }
        }
        
        private Label _checklist;
        public Label Checklist
        {
            get
            {
                _checklist ??= this.Q<Label>("ChecklistCount");
                return _checklist;
            }
        }

        public override bool focusable => true;
        
        private readonly TaskEditorWindow _window;
        private readonly TaskModel _model;

        public TaskEntryView()
        {
            this.ConstructFromResourcePath("Styles/TaskEntryView");
        }

        public TaskEntryView(TaskEditorWindow window, TaskModel model) : this()
        {
            _window = window;
            _model = model;
            Label.text = model.Name;
            Icon.style.backgroundImage = IconUtility.GetTaskBackground(_model.Type);
            if (_model.Checklist.Count == 0)
            {
                Checklist.Hide();
            }
            else
            {
                Checklist.text = $"{_model.Checklist.Count(m => m.Checked)}/{_model.Checklist.Count}";
            }

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
                _model.Rename(evt.newValue);
                var lv = GetFirstAncestorOfType<ListView>();
                _window.RenameTask(_model, lv.name);
            });
            Text.RegisterCallback<FocusOutEvent>(evt =>
            {
                Text.Hide();
                Label.Show();
                evt.StopPropagation();
            });
            
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    if (evt.clickCount == 2)
                    {
                        _window.ShowTask(_model);
                    }
                }
                evt.StopPropagation();
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                switch (evt.keyCode)
                {
                    case KeyCode.Delete:
                        var lv = GetFirstAncestorOfType<ListView>();
                        _window.RemoveTask(_model, lv.name);
                        break;
                    case KeyCode.F2:
                        StartRename();
                        break;
                }
                evt.StopPropagation();
            });
            
            var context = new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Rename", _ =>
                {
                    StartRename();
                });
                evt.menu.AppendAction("Delete", _ =>
                {
                    var lv = GetFirstAncestorOfType<ListView>();
                    _window.RemoveTask(_model, lv.name);
                });
            });
            this.AddManipulator(context);

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
