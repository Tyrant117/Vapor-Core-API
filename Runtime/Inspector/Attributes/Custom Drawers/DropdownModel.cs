namespace Vapor.Inspector
{
    public readonly struct DropdownModel
    {
        public readonly string Category;
        public readonly string Name;
        public readonly string Tooltip;
        public readonly object Value;

        public DropdownModel(string name, object value, string tooltip)
        {
            Category = null;
            Name = name;
            Value = value;
            Tooltip = tooltip;
        }

        public DropdownModel(string category, string name, object value, string tooltip)
        {
            Category = category;
            Name = name;
            Value = value;
            Tooltip = tooltip;
        }
    }
}