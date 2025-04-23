using System;

namespace VaporEditor.ProjectManagement
{
    [Serializable]
    public class BugModel
    {
        public string Id;
        public string Name;
        public int Order;
        public TaskStatus Status;
        public string Description;
        public string Reproduction;
        
        public bool IsPendingRename { get; set; }
        
        public event Action<BugModel> Changed;
        
        public BugModel(string name, bool withPendingRename = false)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            Status = TaskStatus.NotStarted;
            IsPendingRename = withPendingRename;
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
        
        public void SetDescription(string newDescription)
        {
            Description = newDescription;
            Changed?.Invoke(this);
        }
        
        public void SetReproduction(string newReproduction)
        {
            Reproduction = newReproduction;
            Changed?.Invoke(this);
        }
    }
}
