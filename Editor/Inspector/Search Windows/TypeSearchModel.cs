using System;

namespace VaporEditor.Inspector
{
    public class TypeSearchModel : SearchModelBase
    {
        public Type Type { get; set; }
        
        public TypeSearchModel(string category, string name, bool supportFavorite, Type type) : base(category, name, supportFavorite)
        {
            Type = type;
        }
    }
}