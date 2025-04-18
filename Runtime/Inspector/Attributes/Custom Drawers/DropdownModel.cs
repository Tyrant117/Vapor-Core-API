namespace Vapor.Inspector
{
    public readonly struct DropdownModel
    {
        public readonly string Category;
        public readonly string Name;
        public readonly object Value;

        public DropdownModel(string name, object value)
        {
            Category = null;
            Name = name;
            Value = value;
        }

        public DropdownModel(string category, string name, object value)
        {
            Category = category;
            Name = name;
            Value = value;
        }
    }
}