
namespace Vapor.Keys
{
    /// <summary>
    /// Base interface that gives unique integer key functionality.
    /// </summary>
    public interface IKey
    {
        /// <summary>
        /// The unique key.
        /// </summary>
        int Key { get; }

        /// <summary>
        /// Method that refreshes the value of the key before a rebuild.
        /// </summary>
        void ForceRefreshKey();

        /// <summary>
        /// The display name of the key shown in dropdowns.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// If true, the key will be excluded when rebuilding.
        /// </summary>
        bool IsDeprecated { get; }

        /// <summary>
        /// Chesk if the key is valid. Usually used for determining if the name is valid.
        /// </summary>
        /// <returns>Whether the key is valid.</returns>
        bool ValidKey();
    }
}
