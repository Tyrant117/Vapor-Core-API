using System;

namespace VaporEditor.Inspector
{
    public class TypeSearchModel : SearchModelBase
    {
        public Type Type { get; set; }

        public TypeSearchModel(string category, string name, bool supportFavorite, Type type) : base($"{category}/{name}", category, name, supportFavorite)
        {
            Type = type;
        }

        /// <summary>
        /// For a flat picker, where the category is empty and cannot make the name unique.
        /// </summary>
        /// <remarks>
        /// The unique name keys the favourites list, so two types whose display names collide once
        /// their shared suffix is stripped would otherwise share a favourite. Passing the full type
        /// name keeps them apart no matter how they are displayed.
        /// </remarks>
        public TypeSearchModel(string uniqueName, string category, string name, bool supportFavorite, Type type)
            : base(uniqueName, category, name, supportFavorite)
        {
            Type = type;
        }
    }
}