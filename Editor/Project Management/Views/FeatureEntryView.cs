using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class FeatureEntryView : VisualElement
    {
        private readonly TaskEditorWindow _window;
        private readonly FeatureModel _model;
        
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

        public FeatureEntryView()
        {
            this.LoadUxmlFromResourcePath("Styles/FeatureEntryView");
        }

        public FeatureEntryView(TaskEditorWindow window, FeatureModel model) : this()
        {
            _window = window;
            _model = model;
            Label.text = _model.Name;

            var context = new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Rename", action =>
                {
                    Label.Hide();
                    Text.SetValueWithoutNotify(_model.Name);
                    Text.visible = true;
                    schedule.Execute(() => Text.Focus()).ExecuteLater(30);
                });
                evt.menu.AppendAction("Delete", action => { _window.RemoveFeature(_model); });
            });
            this.AddManipulator(context);

            Text.RegisterValueChangedCallback(evt =>
            {
                _model.Name = evt.newValue;
                _window.RenameFeature(_model);
            });
            Text.RegisterCallback<FocusOutEvent>(evt =>
            {
                Text.visible = false;
                Label.Show();
            });
            
            RegisterCallback<FocusInEvent>(evt =>
            {
                _window.SelectFeature(_model);
            });
            
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.F2)
                {
                    Label.Hide();
                    Text.SetValueWithoutNotify(_model.Name);
                    Text.visible = true;
                    schedule.Execute(() => Text.Focus()).ExecuteLater(100);
                }
                evt.StopPropagation();
            });
        }
    }
}
