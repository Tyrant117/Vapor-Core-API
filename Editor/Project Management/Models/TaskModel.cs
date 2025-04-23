using System;
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
        public int Order;
        public List<ChecklistEntryModel> Checklist;
        
        public event Action<TaskModel> Changed;

        public bool IsPendingRename { get; set; }
        

        public TaskModel(string name, bool withPendingRename = false)
        {
            Id = System.Guid.NewGuid().ToString();
            Name = name;
            Status = TaskStatus.NotStarted;
            Checklist = new List<ChecklistEntryModel>();
            IsPendingRename = withPendingRename;
        }
        
        public void Loaded()
        {
            foreach (var c in Checklist)
            {
                c.Changed += OnChecklistEntryChanged;
            }
        }

        public void Rename(string newName)
        {
            Name = newName;
            Changed?.Invoke(this);
        }

        public void SetStatus(TaskStatus newStatus)
        {
            Status = newStatus;
            Changed?.Invoke(this);
        }

        public void SetType(TaskType newType)
        {
            Type = newType;
            Changed?.Invoke(this);
        }

        public void SetDescription(string newDescription)
        {
            Description = newDescription;
            Changed?.Invoke(this);
        }

        public void AddChecklistEntry(ChecklistEntryModel checklistEntry)
        {
            checklistEntry.Changed += OnChecklistEntryChanged;
            checklistEntry.Order = Checklist.Count;
            Checklist.Add(checklistEntry);
            Changed?.Invoke(this);
        }

        public void RemoveChecklistEntry(ChecklistEntryModel checklistEntry)
        {
            checklistEntry.Changed -= OnChecklistEntryChanged;
            Checklist.Remove(checklistEntry);
            Changed?.Invoke(this);
        }

        public void RemoveChecklistEntryAt(int index)
        {
            var entry = Checklist[index];
            entry.Changed -= OnChecklistEntryChanged;
            Checklist.RemoveAt(index);
            Changed?.Invoke(this);
        }

        private void OnChecklistEntryChanged(ChecklistEntryModel obj)
        {
            Changed?.Invoke(this);
        }

        public TaskModel Copy()
        {
            var task = new TaskModel(Name)
            {
                Status = Status,
                Type = Type,
                Description = Description,
            };
            foreach (var entry in Checklist)
            {
                task.AddChecklistEntry(new ChecklistEntryModel
                {
                    Description = entry.Description,
                    Checked = entry.Checked,
                });
            }
            return task;
        }
    }
}
