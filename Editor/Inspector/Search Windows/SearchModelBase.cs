using System.Collections.Generic;

namespace VaporEditor.Inspector
{
    public abstract class SearchModelBase
    {
        // Required
        public string UniqueName { get; }
        public string Category { get; }
        public string Name { get; }
        public bool SupportFavorite { get; private set; }
        
        // Optional
        public List<string> Synonyms { get; private set; }
        public string Tooltip { get; set; }

        // User Data
        public object UserData { get; private set; }

        public string GetFullName() => $"{Category}/{Name}";

        protected SearchModelBase(string uniqueName, string category, string name, bool supportFavorite = true)
        {
            UniqueName = uniqueName;
            Category = category;
            Name = name;
            SupportFavorite = supportFavorite;
        }

        public SearchModelBase WithSynonyms(params string[] synonyms)
        {
            if (synonyms == null)
            {
                return this;
            }
            
            Synonyms ??= new List<string>();
            Synonyms.AddRange(synonyms);
            return this;
        }

        public SearchModelBase WithUserData(object userData)
        {
            UserData = userData;
            return this;
        }
    }
}
