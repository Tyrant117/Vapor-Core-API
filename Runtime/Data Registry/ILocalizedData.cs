using UnityEngine.Localization;

namespace Vapor
{
    public interface ILocalizedData : IData
    {
        public LocalizedString LocalizedName { get; set; }
        public LocalizedString LocalizedDescription { get; set; }
    }
}