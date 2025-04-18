using System;
using System.Collections.Generic;
using System.Linq;

namespace VaporEditor.Inspector
{
    public abstract class SearchModelBase
    {
        // Required
        public string Category { get; }
        public string Name { get; }
        public bool SupportFavorite { get; private set; }
        
        // Optional
        public List<string> Synonyms { get; private set; }

        // User Data
        public object UserData { get; private set; }

        public string GetFullName() => $"{Category}/{Name}";

        protected SearchModelBase(string category, string name, bool supportFavorite = true)
        {
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
