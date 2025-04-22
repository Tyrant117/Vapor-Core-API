using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace VaporEditor.ProjectManagement
{
    [System.Serializable]
    public class ProjectTrackerModel
    {
        public string Id;
        public string Name;
        public bool Archived;
        public List<SprintModel> Sprints;
        public BugTrackerModel BugTracker;

        public event Action<ProjectTrackerModel> Changed;

        public static ProjectTrackerModel Create(string name)
        {
            return new ProjectTrackerModel
            {
                Id = System.Guid.NewGuid().ToString(),
                Name = name,
                Archived = false,
                Sprints = new List<SprintModel>() { new() { Sprint = 1 } },
                BugTracker = new BugTrackerModel(),
            };
        }

        public static ProjectTrackerModel Load(string json)
        {
            var model = JsonUtility.FromJson<ProjectTrackerModel>(json);
            model.Loaded();
            return model;
        }

        private void Loaded()
        {
            foreach (var s in Sprints)
            {
                s.Changed += OnSprintChanged;
                s.Loaded();
            }

            BugTracker.Changed += OnBugTrackerChanged;
            BugTracker.Loaded();
        }

        public void Rename(string name)
        {
            Name = name;
            Changed?.Invoke(this);
        }

        public void Archive(SprintModel currentSprint)
        {
            Archived = true;
            Changed?.Invoke(this);
        }

        public void AddSprint(SprintModel sprint)
        {
            sprint.Changed += OnSprintChanged;
            sprint.Sprint = Sprints.Count == 0 ? 1 : Sprints[^1].Sprint + 1;
            if (Sprints.Count > 0)
            {
                var last = Sprints[^1];
                foreach (var f in last.Features)
                {
                    if(!f.IsComplete())
                    {
                        sprint.AddFeature(f.Copy());
                    }
                }
            }

            Sprints.Add(sprint);
            Changed?.Invoke(this);
        }

        public void RemoveSprint(SprintModel sprint)
        {
            sprint.Changed -= OnSprintChanged;
            Sprints.Remove(sprint);
            Changed?.Invoke(this);
        }
        
        private void OnSprintChanged(SprintModel obj)
        {
            Changed?.Invoke(this);
        }
        
        private void OnBugTrackerChanged(BugTrackerModel obj)
        {
            Changed?.Invoke(this);
        }
    }
}
