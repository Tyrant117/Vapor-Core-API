using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VaporEditor.ProjectManagement
{
    [System.Serializable]
    public class FeatureModel
    {
        public string Id;
        public string Name;
        public List<TaskModel> Tasks;

        public FeatureModel(string name)
        {
            Id = System.Guid.NewGuid().ToString();
            Name = name;
            Tasks = new List<TaskModel>();
        }

        public bool IsComplete() => Tasks.All(task => task.Status == TaskStatus.Completed);
    }
}
