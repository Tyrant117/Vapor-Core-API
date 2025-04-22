using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;
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

        private ProjectTrackerModel _currentProjectTracker;
        private SprintModel _currentSprint;
        private FeatureModel _currentFeature;

        // Menu
        private VisualElement _bugTracker;
        private VisualElement _tasks;
        private DropdownField _sprintDropdown;
        
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

        private TaskWindowView _taskWindow;

        
        // Bugs
        private ListView _bugsNotStartedList;
        private Label _bugsNotStartedCount;
        private Button _addBug;
        
        private ListView _bugsInProgressList;
        private Label _bugsInProgressCount;
        
        private ListView _bugsCompletedList;
        private Label _bugsCompletedCount;
        
        private BugWindowView _bugWindow;
        private VisualElement _underlay;


        public void CreateGUI()
        {
            _visualTreeAsset.CloneTree(rootVisualElement);

            string fullDirectoryPath = Path.Combine(Application.dataPath, FolderSetupUtility.TASK_RELATIVE_PATH);
            string fullFilePath = Path.Combine(fullDirectoryPath, "ProjectTracker.json").Replace("\\", "/");

            // If file doesn't exist, create it with default content
            if (!File.Exists(fullFilePath))
            {
                var defaultProj = ProjectTrackerModel.Create(Application.productName);
                string defaultJson = JsonUtility.ToJson(defaultProj, true);
                File.WriteAllText(fullFilePath, defaultJson);
            }

            // Load and deserialize
            string json = File.ReadAllText(fullFilePath);
            _currentProjectTracker = ProjectTrackerModel.Load(json);
            _currentProjectTracker.Changed += OnProjectTrackerChanged;

            _sprintDropdown = rootVisualElement.Q<DropdownField>("Sprints");
            _sprintDropdown.choices.Clear();
            var ss = _currentProjectTracker.Sprints.ToList();
            ss.Reverse();
            foreach (var sprint in ss)
            {
                _sprintDropdown.choices.Add(sprint.Name);
            }

            _sprintDropdown.index = 0;
            _currentSprint = _currentProjectTracker.Sprints.First(s => s.Name == _sprintDropdown.value);
            _sprintDropdown.RegisterValueChangedCallback(OnSprintChanged);

            var addSprint = rootVisualElement.Q<Button>("AddSprint");
            addSprint.clicked += OnAddSprint;
            var archive = rootVisualElement.Q<Button>("ArchiveSprint");
            archive.clicked += OnArchiveSprint;
            archive.Q<VisualElement>("Icon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_Package Manager").image);

            var bugs = rootVisualElement.Q<ToolbarToggle>("Bugs");
            bugs.RegisterValueChangedCallback(ToggleBugs);
            bugs.Q<VisualElement>("Icon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_Debug").image);

            _tasks = rootVisualElement.Q<VisualElement>("Tasks");
            SetupFeatureList();
            SetupNotStartedList();
            SetupInProgressList();
            SetupCompletedList();

            SetupBugTracker();

            _underlay = rootVisualElement.Q<VisualElement>("Underlay");
            _underlay.RegisterCallback<PointerDownEvent>(OnUnderlayPressed);
            _taskWindow = rootVisualElement.Q<TaskWindowView>();
            _taskWindow.Init(this);
            
            _bugWindow = rootVisualElement.Q<BugWindowView>();
            _bugWindow.Init(this);
        }

        private void OnSprintChanged(ChangeEvent<string> evt)
        {
            _currentSprint = _currentProjectTracker.Sprints.First(s => s.Name == evt.newValue);
            _currentFeature = null;
            RefreshAll();
        }

        private void OnAddSprint()
        {
            _currentProjectTracker.AddSprint(new SprintModel());
            _sprintDropdown.choices.Clear();
            var sprintModels = _currentProjectTracker.Sprints.ToList();
            sprintModels.Reverse();
            foreach (var sprint in sprintModels)
            {
                _sprintDropdown.choices.Add(sprint.Name);
            }

            _sprintDropdown.value = _currentProjectTracker.Sprints[^1].Name;
        }

        private void OnArchiveSprint()
        {
            _currentProjectTracker.Archive(_currentSprint);
            _sprintDropdown.choices.Clear();
            var sprintModels = _currentProjectTracker.Sprints.ToList();
            sprintModels.Reverse();
            foreach (var sprint in sprintModels)
            {
                _sprintDropdown.choices.Add(sprint.Name);
            }

            _sprintDropdown.value = _currentProjectTracker.Sprints[^1].Name;
        }

        

        private void OnProjectTrackerChanged(ProjectTrackerModel projectTracker)
        {
            Save(projectTracker);
        }

        public void Save(ProjectTrackerModel projectTracker)
        {
            string fullDirectoryPath = Path.Combine(Application.dataPath, FolderSetupUtility.TASK_RELATIVE_PATH);
            string fullFilePath = Path.Combine(fullDirectoryPath, "ProjectTracker.json").Replace("\\", "/");
            string defaultJson = JsonUtility.ToJson(projectTracker, true);
            File.WriteAllText(fullFilePath, defaultJson);
        }

        private void ToggleBugs(ChangeEvent<bool> evt)
        {
            _tasks.SetDisplay(!evt.newValue);
            _bugTracker.SetDisplay(evt.newValue);
        }

        private void RefreshAll()
        {
            _featureList.itemsSource = _currentSprint.Features;
            _featureList.selectedIndex = -1;
            UpdateFeatureCount();

            RebuildNotStartedList();
            RebuildInProgressList();
            RebuildCompletedList();
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
            _featureList.itemsSource = _currentSprint.Features;

            _addFeature.clicked += () =>
            {
                _currentSprint.AddFeature(new FeatureModel("", true));
                UpdateFeatureCount();
                _featureList.Rebuild();
                _featureList.schedule.Execute(() => _featureList.ScrollToItem(-1)).ExecuteLater(100);
            };

            UpdateFeatureCount();
        }

        public void RemoveFeature(FeatureModel model)
        {
            _currentSprint.RemoveFeature(model);
            UpdateFeatureCount();
            _featureList.Rebuild();
        }

        public void RenameFeature(FeatureModel model)
        {
            _featureList.Rebuild();
        }

        private void UpdateFeatureCount()
        {
            int count = _currentSprint.Features.Count;
            int completed = _currentSprint.Features.Count(feature => feature.IsComplete());
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
            if (_currentFeature == null)
            {
                _notStartedList.itemsSource = null;
                _notStartedCount.text = "0";
            }
            else
            {
                _notStartedList.itemsSource = _currentFeature.Tasks.Where(t => t.Status == TaskStatus.NotStarted).OrderBy(t => t.Order).ToList();
                _notStartedCount.text = $"{_notStartedList.itemsSource.Count}";
            }
        }

        private void RebuildInProgressList()
        {
            if (_currentFeature == null)
            {
                _inProgressList.itemsSource = null;
                _inProgressCount.text = "0";
            }
            else
            {
                _inProgressList.itemsSource = _currentFeature.Tasks.Where(t => t.Status == TaskStatus.InProgress).OrderBy(t => t.Order).ToList();
                _inProgressCount.text = $"{_inProgressList.itemsSource.Count}";
            }
        }

        private void RebuildCompletedList()
        {
            if (_currentFeature == null)
            {
                _completedList.itemsSource = null;
                _completedCount.text = "0";
            }
            else
            {
                _completedList.itemsSource = _currentFeature.Tasks.Where(t => t.Status == TaskStatus.Completed).OrderBy(t => t.Order).ToList();
                _completedCount.text = $"{_completedList.itemsSource.Count}";
            }
        }

        #endregion

        #region - Sprint-

        private void SetupNotStartedList()
        {
            _notStartedList = rootVisualElement.Q<ListView>("NotStarted");
            _notStartedCount = rootVisualElement.Q<Label>("NotStartedCount");
            rootVisualElement.Q<VisualElement>("NotStartedIcon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_redLight").image);
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
            _notStartedList.itemIndexChanged += (src, dst) =>
            {
                var list = (List<TaskModel>)_notStartedList.itemsSource;
                int idx = 0;
                foreach (var item in list)
                {
                    item.Order = idx;
                    idx++;
                }
                Save(_currentProjectTracker);
                _notStartedList.schedule.Execute(() => _notStartedList.Query<TaskEntryView>().ForEach(v => v.Q<VisualElement>("Outline").Blur())).ExecuteLater(100);
            };

            _addTask.clicked += () =>
            {
                if (_currentFeature == null)
                {
                    return;
                }

                _currentFeature.AddTask(new TaskModel("", true));
                RebuildNotStartedList();
                _notStartedList.schedule.Execute(() => _notStartedList.ScrollToItem(-1)).ExecuteLater(100);
            };
        }

        private void SetupInProgressList()
        {
            _inProgressList = rootVisualElement.Q<ListView>("InProgress");
            _inProgressCount = rootVisualElement.Q<Label>("InProgressCount");
            rootVisualElement.Q<VisualElement>("InProgressIcon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_orangeLight").image);
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
            _inProgressList.itemIndexChanged += (src, dst) =>
            {
                var list = (List<TaskModel>)_inProgressList.itemsSource;
                int idx = 0;
                foreach (var item in list)
                {
                    item.Order = idx;
                    idx++;
                }
                Save(_currentProjectTracker);
                _inProgressList.schedule.Execute(() => _inProgressList.Query<TaskEntryView>().ForEach(v => v.Q<VisualElement>("Outline").Blur())).ExecuteLater(100);
            };
        }

        private void SetupCompletedList()
        {
            _completedList = rootVisualElement.Q<ListView>("Completed");
            _completedCount = rootVisualElement.Q<Label>("CompletedCount");
            rootVisualElement.Q<VisualElement>("CompletedIcon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_greenLight").image);
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
            _completedList.itemIndexChanged += (src, dst) =>
            {
                var list = (List<TaskModel>)_completedList.itemsSource;
                int idx = 0;
                foreach (var item in list)
                {
                    item.Order = idx;
                    idx++;
                }

                Save(_currentProjectTracker);
                _completedList.schedule.Execute(() => _completedList.Query<TaskEntryView>().ForEach(v => v.Q<VisualElement>("Outline").Blur())).ExecuteLater(100);
            };
        }

        public void TaskComplete(TaskModel model)
        {
            switch (model.Status)
            {
                case TaskStatus.NotStarted:
                    model.Order = _inProgressList.itemsSource?.Count ?? 1;
                    model.SetStatus(TaskStatus.InProgress);
                    RebuildNotStartedList();
                    RebuildInProgressList();
                    break;
                case TaskStatus.InProgress:
                    model.Order = _completedList.itemsSource?.Count ?? 1;
                    model.SetStatus(TaskStatus.Completed);
                    RebuildInProgressList();
                    RebuildCompletedList();
                    break;
            }
        }

        public void RemoveTask(TaskModel model, string listViewName)
        {
            _currentFeature.RemoveTask(model);
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

        public void ShowTask(TaskModel model)
        {
            if (model != null)
            {
                _underlay.Show();
                _taskWindow.Show(model);
            }
        }

        public void HideTaskWindow()
        {
            _underlay.Hide();
            RebuildNotStartedList();
            RebuildInProgressList();
            RebuildCompletedList();
        }


        #endregion

        #region - Bug Tracker -
        private void SetupBugTracker()
        {
            _bugTracker = rootVisualElement.Q<VisualElement>("BugTracker");
            SetupBugsNotStartedList();
            SetupBugsInProgressList();
            SetupBugsCompletedList();
        }
        
        private void SetupBugsNotStartedList()
        {
            _bugsNotStartedList = rootVisualElement.Q<ListView>("BugsNotStarted");
            _bugsNotStartedCount = rootVisualElement.Q<Label>("BugsNotStartedCount");
            rootVisualElement.Q<VisualElement>("BugsNotStartedIcon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_redLight").image);
            _addBug = rootVisualElement.Q<Button>("AddBug");
            _bugsNotStartedList.makeItem = () => new VisualElement();
            _bugsNotStartedList.bindItem = (element, i) =>
            {
                element.Clear();
                if (_bugsNotStartedList.itemsSource is not List<BugModel> bugModels)
                {
                    Debug.Log("No bugs found");
                    return;
                }

                var bug = bugModels[i];
                var entry = new BugEntryView(this, bug);
                element.Add(entry);
            };

            _addBug.clicked += () =>
            {
                _currentProjectTracker.BugTracker.AddBug(new BugModel("", true));
                RebuildBugsNotStartedList();
                _bugsNotStartedList.schedule.Execute(() => _bugsNotStartedList.ScrollToItem(-1)).ExecuteLater(100);
            };

            RebuildBugsNotStartedList();
        }

        private void SetupBugsInProgressList()
        {
            _bugsInProgressList = rootVisualElement.Q<ListView>("BugsInProgress");
            _bugsInProgressCount = rootVisualElement.Q<Label>("BugsInProgressCount");
            rootVisualElement.Q<VisualElement>("BugsInProgressIcon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_orangeLight").image);
            _bugsInProgressList.makeItem = () => new VisualElement();
            _bugsInProgressList.bindItem = (element, i) =>
            {
                element.Clear();
                if (_bugsInProgressList.itemsSource is not List<BugModel> bugModels)
                {
                    return;
                }

                var bug = bugModels[i];
                var entry = new BugEntryView(this, bug);
                element.Add(entry);
            };

            RebuildBugsInProgressList();
        }

        private void SetupBugsCompletedList()
        {
            _bugsCompletedList = rootVisualElement.Q<ListView>("BugsCompleted");
            _bugsCompletedCount = rootVisualElement.Q<Label>("BugsCompletedCount");
            rootVisualElement.Q<VisualElement>("BugsCompletedIcon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_greenLight").image);
            _bugsCompletedList.makeItem = () => new VisualElement();
            _bugsCompletedList.bindItem = (element, i) =>
            {
                element.Clear();
                if (_bugsCompletedList.itemsSource is not List<BugModel> bugModels)
                {
                    return;
                }

                var bug = bugModels[i];
                var entry = new BugEntryView(this, bug);
                element.Add(entry);
            };

            RebuildBugsCompletedList();
        }

        private void RebuildBugsNotStartedList()
        {
            _bugsNotStartedList.itemsSource = _currentProjectTracker.BugTracker.Bugs.Where(t => t.Status == TaskStatus.NotStarted).ToList();
            _bugsNotStartedCount.text = $"{_bugsNotStartedList.itemsSource.Count}";
        }

        private void RebuildBugsInProgressList()
        {
            _bugsInProgressList.itemsSource = _currentProjectTracker.BugTracker.Bugs.Where(t => t.Status == TaskStatus.InProgress).ToList();
            _bugsInProgressCount.text = $"{_bugsInProgressList.itemsSource.Count}";
        }

        private void RebuildBugsCompletedList()
        {
            _bugsCompletedList.itemsSource = _currentProjectTracker.BugTracker.Bugs.Where(t => t.Status == TaskStatus.Completed).ToList();
            _bugsCompletedCount.text = $"{_bugsCompletedList.itemsSource.Count}";
        }

        public void RenameBug(BugModel model, string listViewName)
        {
            switch (listViewName)
            {
                case "BugsNotStarted":
                    RebuildBugsNotStartedList();
                    break;
                case "BugsInProgress":
                    RebuildBugsInProgressList();
                    break;
                case "BugsCompleted":
                    RebuildBugsCompletedList();
                    break;
            }
        }
        
        public void ShowBug(BugModel model)
        {
            if (model != null)
            {
                _underlay.Show();
                _bugWindow.Show(model);
            }
        }

        public void RemoveBug(BugModel model, string listViewName)
        {
            _currentProjectTracker.BugTracker.RemoveBug(model);
            switch (listViewName)
            {
                case "BugsNotStarted":
                    RebuildBugsNotStartedList();
                    break;
                case "BugsInProgress":
                    RebuildBugsInProgressList();
                    break;
                case "BugsCompleted":
                    RebuildBugsCompletedList();
                    break;
            }
        }

        public void BugComplete(BugModel model)
        {
            switch (model.Status)
            {
                case TaskStatus.NotStarted:
                    model.SetStatus(TaskStatus.InProgress);
                    RebuildBugsNotStartedList();
                    RebuildBugsInProgressList();
                    break;
                case TaskStatus.InProgress:
                    model.SetStatus(TaskStatus.Completed);
                    RebuildBugsInProgressList();
                    RebuildBugsCompletedList();
                    break;
            }
        }
        
        public void HideBugWindow()
        {
            _underlay.Hide();
            RebuildBugsNotStartedList();
            RebuildBugsInProgressList();
            RebuildBugsCompletedList();
        }
        #endregion


        private void OnUnderlayPressed(PointerDownEvent evt)
        {
            if (_taskWindow.IsOpen())
            {
                HideTaskWindow();
                _taskWindow.Hide();
            }

            if (_bugWindow.IsOpen())
            {
                HideBugWindow();
                _bugWindow.Hide();
            }
            
            _underlay.Hide();
        }
    }
}
