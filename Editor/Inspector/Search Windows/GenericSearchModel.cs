namespace VaporEditor.Inspector
{
    public class GenericSearchModel : SearchModelBase
    {
        public bool IsToggled { get; set; }

        public GenericSearchModel(string uniqueName, string category, string name, bool supportFavorite = true) : base(uniqueName, category, name, supportFavorite) { }
    }
}