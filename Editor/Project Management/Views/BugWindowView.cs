using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class BugWindowView : VisualElement
    {
        private TaskEditorWindow _window;
        private TextField _header;
        private TextField _description;
        private TextField _reproduction;
        
        private BugModel _model;
        

        public BugWindowView()
        {
            this.ConstructFromResourcePath("Styles/BugWindowView");
        }

        public void Init(TaskEditorWindow window)
        {
            _window = window;
            _header = this.Q<TextField>("Header");
            _header.RegisterValueChangedCallback(OnRenameTask);
            this.Q<Button>("Close").clicked += OnCloseClicked;
            
            _description = this.Q<TextField>("Description");
            _description.RegisterValueChangedCallback(OnChangeDescription);
            _description.Q<TextElement>().enableRichText = true;
            
            _reproduction = this.Q<TextField>("Reproduction");
            _reproduction.RegisterValueChangedCallback(OnChangeReproduction);
            _reproduction.Q<TextElement>().enableRichText = true;
        }

        public void Show(BugModel model)
        {
            _model = model;

            _header.SetValueWithoutNotify(model.Name);
            _description.SetValueWithoutNotify(model.Description);
            _reproduction.SetValueWithoutNotify(model.Reproduction);
            
            this.Show();
        }
        
        private void OnCloseClicked()
        {
            _window.HideBugWindow();
            this.Hide();
        }
        
        
        private void OnRenameTask(ChangeEvent<string> evt)
        {
            _model.Rename(evt.newValue);
        }

        private void OnChangeDescription(ChangeEvent<string> evt)
        {
            _model.SetDescription(evt.newValue);
        }

        private void OnChangeReproduction(ChangeEvent<string> evt)
        {
            _model.SetReproduction(evt.newValue);
        }
    }
}
