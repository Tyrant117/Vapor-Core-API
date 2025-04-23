using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class BugEntryView : VisualElement
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

        public override bool focusable => true;

        private readonly TaskEditorWindow _window;
        private readonly BugModel _model;

        public BugEntryView()
        {
            this.ConstructFromResourcePath("Styles/BugEntryView");
        }
        
        public BugEntryView(TaskEditorWindow window, BugModel model) : this()
        {
            _window = window;
            _model = model;
            Label.text = model.Name;
            if (model.Status == TaskStatus.Completed)
            {
                this.Q<Button>("Complete").Hide();
            }
            else
            {
                this.Q<Button>("Complete").clicked += OnCompleteClicked;
            }
            
            Text.RegisterValueChangedCallback(evt =>
            {
                _model.Rename(evt.newValue);
                var lv = GetFirstAncestorOfType<ListView>();
                _window.RenameBug(_model, lv.name);
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
                        _window.ShowBug(_model);
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
                        _window.RemoveBug(_model, lv.name);
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
                    _window.RemoveBug(_model, lv.name);
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
            _window.BugComplete(_model);
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
