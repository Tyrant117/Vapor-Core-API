using UnityEngine;

namespace Vapor
{
    public interface IDatabaseInitialize
    {
        /// <summary>
        /// This method is called once when this item is loaded into the database at runtime.
        /// </summary>
        void InitializedInDatabase();

        /// <summary>
        /// This method is called after all items have been loaded and initialized in the database at runtime.
        /// </summary>
        void PostInitializedInDatabase();
    }
}
