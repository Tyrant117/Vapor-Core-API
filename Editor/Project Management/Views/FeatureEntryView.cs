using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class FeatureEntryView : VisualElement
    {
        private readonly FeatureModel _model;
        
        private VisualElement _icon;
        public VisualElement Icon
        {
            get
            {
                _icon ??= this.Q<VisualElement>("StatusIcon");
                return _icon;
            }
        }
        
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
            this.ConstructFromResourcePath("Styles/FeatureEntryView");
        }

        public FeatureEntryView(TaskEditorWindow window, FeatureModel model) : this()
        {
            var window1 = window;
            _model = model;
            Label.text = _model.Name;
            Icon.style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("GreenCheckmark@2x").image);
            CheckComplete();

            var context = new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Rename", _ =>
                {
                    StartRename();
                });
                evt.menu.AppendAction("Delete", _ => { window1.RemoveFeature(_model); });
            });
            this.AddManipulator(context);

            Text.RegisterValueChangedCallback(evt =>
            {
                _model.Rename(evt.newValue);
                window1.RenameFeature(_model);
            });
            Text.RegisterCallback<FocusOutEvent>(_ =>
            {
                Text.Hide();
                Label.Show();
            });
            
            RegisterCallback<FocusInEvent>(evt =>
            {
                window1.SelectFeature(_model);
                evt.StopPropagation();
            });
            
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.F2)
                {
                    StartRename();
                }

                if (evt.keyCode == KeyCode.Delete)
                {
                    window1.RemoveFeature(_model);
                }
                evt.StopPropagation();
            });
            
            if (_model.IsPendingRename)
            {
                _model.IsPendingRename = false;
                schedule.Execute(StartRename).ExecuteLater(100);
            }
        }

        public void CheckComplete()
        {
            Icon.SetDisplay(_model.Tasks.All(m => m.Status == TaskStatus.Completed));
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
