using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
using Vapor.Inspector;
using VaporEditor;
using VaporEditor.Inspector;

namespace VaporEditor.ProjectManagement
{
    public class TaskEditorWindow : EditorWindow
    {
        [MenuItem("Vapor/Project Management/Tasks")]
        public static void ShowExample()
        {
            TaskEditorWindow wnd = GetWindow<TaskEditorWindow>();
            wnd.titleContent = new GUIContent("Tasks");
            wnd.minSize = new Vector2(800, 600);
        }

        [SerializeField] private VisualTreeAsset _visualTreeAsset;

        private EpicModel _currentEpic;
        private FeatureModel _currentFeature;

        // Features
        private ListView _featureList;
        private Label _featureCount;
        private Button _addFeature;

        private ListView _notStartedList;
        private Button _addTask;
        private Label _notStartedCount;
        
        private ListView _inProgressList;
        private Label _inProgressCount;
        
        private ListView _completedList;
        private Label _completedCount;


        public void CreateGUI()
        {
            _visualTreeAsset.CloneTree(rootVisualElement);

            string fullDirectoryPath = Path.Combine(Application.dataPath, FolderSetupUtility.TASK_RELATIVE_PATH);
            string fullFilePath = Path.Combine(fullDirectoryPath, "Tasks.json").Replace("\\", "/");
            Debug.Log(fullDirectoryPath);

            // If file doesn't exist, create it with default content
            if (!File.Exists(fullFilePath))
            {
                var defaultProj = EpicModel.Create("Default");
                string defaultJson = JsonUtility.ToJson(defaultProj, true);
                File.WriteAllText(fullFilePath, defaultJson);
            }

            // Load and deserialize
            string json = File.ReadAllText(fullFilePath);
            _currentEpic = JsonUtility.FromJson<EpicModel>(json);

            SetupFeatureList();
            SetupNotStartedList();
            SetupInProgressList();
            SetupCompletedList();
        }

        #region Feature List
        private void SetupFeatureList()
        {
            _featureList = rootVisualElement.Q<ListView>("FeatureList");
            _featureCount = rootVisualElement.Q<Label>("FeatureCount");
            _addFeature = rootVisualElement.Q<Button>("AddFeature");

            _featureList.makeItem = () => new VisualElement();
            _featureList.bindItem = (element, i) =>
            {
                element.Clear();
                if (_featureList.itemsSource is not List<FeatureModel> featureModels)
                {
                    return;
                }

                var feature = featureModels[i];
                var entry = new FeatureEntryView(this, feature);
                element.Add(entry);
            };
            _featureList.itemsSource = _currentEpic.Features;

            _addFeature.clicked += () =>
            {
                _currentEpic.Features.Add(new FeatureModel("Placeholder"));
                UpdateFeatureCount();
                _featureList.Rebuild();
            };

            UpdateFeatureCount();
        }

        public void RemoveFeature(FeatureModel model)
        {
            _currentEpic.Features.Remove(model);
            UpdateFeatureCount();
            _featureList.Rebuild();
        }

        public void RenameFeature(FeatureModel model)
        {
            _featureList.Rebuild();
        }

        private void UpdateFeatureCount()
        {
            int count = _currentEpic.Features.Count;
            int completed = _currentEpic.Features.Count(feature => feature.IsComplete());
            _featureCount.text = $"{completed}/{count}";
        }

        public void SelectFeature(FeatureModel model)
        {
            _currentFeature = model;
            
            RebuildNotStartedList();

            RebuildInProgressList();

            RebuildCompletedList();
        }
        private void RebuildNotStartedList()
        {
            // _notStartedList.Clear();
            _notStartedList.itemsSource = _currentFeature.Tasks.Where(t => t.Status == TaskStatus.NotStarted).ToList();
            // _notStartedList.Rebuild();
        }
        
        private void RebuildInProgressList()
        {
            // _inProgressList.Clear();
            _inProgressList.itemsSource = _currentFeature.Tasks.Where(t => t.Status == TaskStatus.InProgress).ToList();
            // _inProgressList.Rebuild();
        }
        
        private void RebuildCompletedList()
        {
            // _completedList.Clear();
            _completedList.itemsSource = _currentFeature.Tasks.Where(t => t.Status == TaskStatus.Completed).ToList();
            // _completedList.Rebuild();
        }
        
        #endregion
        

        private void SetupNotStartedList()
        {
            _notStartedList = rootVisualElement.Q<ListView>("NotStarted");
            _notStartedCount = rootVisualElement.Q<Label>("NotStartedCount");
            _addTask = rootVisualElement.Q<Button>("AddTask");
            _notStartedList.makeItem = () => new VisualElement();
            _notStartedList.bindItem = (element, i) =>
            {
                element.Clear();
                if (_notStartedList.itemsSource is not List<TaskModel> taskModels)
                {
                    return;
                }

                var task = taskModels[i];
                var entry = new TaskEntryView(this, task);
                element.Add(entry);
            };
            
            _addTask.clicked += () =>
            {
                if (_currentFeature == null)
                {
                    return;
                }

                _currentFeature.Tasks.Add(new TaskModel("Placeholder", true));
                RebuildNotStartedList();
                _notStartedList.schedule.Execute(() => _notStartedList.ScrollToItem(-1)).ExecuteLater(100);
            };
        }

        private void SetupInProgressList()
        {
            _inProgressList = rootVisualElement.Q<ListView>("InProgress");
            _inProgressCount = rootVisualElement.Q<Label>("InProgressCount");
            _inProgressList.makeItem = () => new VisualElement();
            _inProgressList.bindItem = (element, i) =>
            {
                element.Clear();
                if (_inProgressList.itemsSource is not List<TaskModel> taskModels)
                {
                    return;
                }

                var task = taskModels[i];
                var entry = new TaskEntryView(this, task);
                element.Add(entry);
            };
        }

        private void SetupCompletedList()
        {
            _completedList = rootVisualElement.Q<ListView>("Completed");
            _completedCount = rootVisualElement.Q<Label>("CompletedCount");
            _completedList.makeItem = () => new VisualElement();
            _completedList.bindItem = (element, i) =>
            {
                element.Clear();
                if (_completedList.itemsSource is not List<TaskModel> taskModels)
                {
                    return;
                }

                var task = taskModels[i];
                var entry = new TaskEntryView(this, task);
                element.Add(entry);
            };
        }

        public void TaskComplete(TaskModel model)
        {
            switch (model.Status)
            {
                case TaskStatus.NotStarted:
                    model.Status = TaskStatus.InProgress;
                    RebuildNotStartedList();
                    RebuildInProgressList();
                    break;
                case TaskStatus.InProgress:
                    model.Status = TaskStatus.Completed;
                    RebuildInProgressList();
                    RebuildCompletedList();
                    break;
            }
        }

        public void RenameTask(TaskModel model, string listViewName)
        {
            switch (listViewName)
            {
                case "NotStarted":
                    RebuildNotStartedList();
                    break;
                case "InProgress":
                    RebuildInProgressList();
                    break;
                case "Completed":
                    RebuildCompletedList();
                    break;
            }
        }
    }
}
