using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace VaporEditor.ProjectManagement
{
    [System.Serializable]
    public class EpicModel
    {
        public string Id;
        public string Name;
        public bool Archived;
        public List<FeatureModel> Features;

        public static EpicModel Create(string name)
        {
            return new EpicModel
            {
                Id = System.Guid.NewGuid().ToString(),
                Name = name,
                Archived = false,
                Features = new List<FeatureModel>()
            };
        }
    }
}
