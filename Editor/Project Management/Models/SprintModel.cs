using System;
using System.Collections.Generic;

namespace VaporEditor.ProjectManagement
{
    [Serializable]
    public class SprintModel
    {
        public string Id;
        public int Sprint;
        public bool Archived;
        public List<FeatureModel> Features;

        public string Name => $"Sprint {Sprint}";
        
        public event Action<SprintModel> Changed;

        public SprintModel()
        {
            Id = Guid.NewGuid().ToString();
            Features = new List<FeatureModel>();
        }
        
        public void Loaded()
        {
            foreach (var f in Features)
            {
                f.Changed += OnFeatureChanged;
                f.Loaded();
            }
        }
        
        public void AddFeature(FeatureModel feature)
        {
            feature.Changed += OnFeatureChanged;
            feature.Order = Features.Count;
            Features.Add(feature);
        }

        public void RemoveFeature(FeatureModel feature)
        {
            feature.Changed -= OnFeatureChanged;
            Features.Remove(feature);
            Changed?.Invoke(this);
        }
        
        private void OnFeatureChanged(FeatureModel obj)
        {
            Changed?.Invoke(this);
        }
    }
}
