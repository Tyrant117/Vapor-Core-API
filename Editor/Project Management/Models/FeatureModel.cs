using System;
using System.Collections.Generic;
using System.Linq;

namespace VaporEditor.ProjectManagement
{
    [Serializable]
    public class FeatureModel
    {
        public string Id;
        public string Name;
        public int Order;
        public List<TaskModel> Tasks;
        
        public bool IsPendingRename { get; set; }

        public event Action<FeatureModel> Changed;

        public FeatureModel(string name, bool withPendingRename = false)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            Tasks = new List<TaskModel>();
            IsPendingRename = withPendingRename;
        }

        public void Loaded()
        {
            foreach (var t in Tasks)
            {
                t.Changed += OnTaskChanged;
                t.Loaded();
            }
        }

        public bool IsComplete() => Tasks.All(task => task.Status == TaskStatus.Completed);

        public void Rename(string newName)
        {
            Name = newName;
            Changed?.Invoke(this);
        }

        public void AddTask(TaskModel task)
        {
            task.Changed += OnTaskChanged;
            task.Order = Tasks.Count;
            Tasks.Add(task);
            Changed?.Invoke(this);
        }

        public void RemoveTask(TaskModel task)
        {
            task.Changed -= OnTaskChanged;
            Tasks.Remove(task);
            Changed?.Invoke(this);
        }
        
        private void OnTaskChanged(TaskModel obj)
        {
            Changed?.Invoke(this);
        }

        public FeatureModel Copy()
        {
            var ft = new FeatureModel(Name);
            foreach (var task in Tasks)
            {
                ft.AddTask(task.Copy());
            }
            return ft;
        }
    }
}
