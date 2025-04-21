using System.Collections.Generic;
using UnityEngine;

namespace VaporEditor.ProjectManagement
{
    public enum TaskStatus
    {
        NotStarted,
        InProgress,
        Completed,
    }

    public enum TaskType
    {
        Script,
        Texture,
        Material,
        Audio,
        Visual,
        Prefab,
        Data,
        Design,
    }

    [System.Serializable]
    public class TaskModel
    {
        public string Id;
        public string Name;
        public TaskStatus Status;
        public TaskType Type;
        public string Description;
        public List<ChecklistEntryModel> Checklist;

        public bool IsPendingRename { get; set; }
        public int Order { get; set; }

        public TaskModel(string name, bool withPendingRename = false)
        {
            Id = System.Guid.NewGuid().ToString();
            Name = name;
            Status = TaskStatus.NotStarted;
            Checklist = new List<ChecklistEntryModel>();
            IsPendingRename = withPendingRename;
        }
    }
}
