using System;
using System.Collections.Generic;
using UnityEngine;

namespace VaporEditor.ProjectManagement
{
    [System.Serializable]
    public class BugTrackerModel
    {
        public List<BugModel> Bugs;

        public event Action<BugTrackerModel> Changed;

        public void Loaded()
        {
            foreach (var b in Bugs)
            {
                b.Changed += OnBugChanged;
            }
        }
        
        public void AddBug(BugModel model)
        {
            model.Changed += OnBugChanged;
            Bugs.Add(model);
            Changed?.Invoke(this);
        }

        private void OnBugChanged(BugModel model)
        {
            Changed?.Invoke(this);
        }

        public void RemoveBug(BugModel model)
        {
            model.Changed -= OnBugChanged;
            Bugs.Remove(model);
            Changed?.Invoke(null);
        }
    }
}
