namespace VaporEditor.Inspector
{
    public class GenericSearchModel : SearchModelBase
    {
        public bool IsToggled { get; set; }
        
        public GenericSearchModel(string category, string name, bool supportFavorite = true) : base(category, name, supportFavorite)
        {
        }
    }
}