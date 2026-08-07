namespace Vapor
{
    /// <summary>
    /// Data that has state to derive once a document has been read into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A type built in code can do its derivation in a constructor. One read from a document cannot:
    /// the serializer creates it empty and fills its members in afterwards, so anything computed from
    /// those members — a cached id, a back-reference, an entry added to another registry — has nowhere
    /// to happen. This is that place.
    /// </para>
    /// <para>
    /// Called by <see cref="VslDataStore.Read"/> for every entry it parses, which means it runs on
    /// each read rather than once. An implementation has to be safe to call again.
    /// </para>
    /// </remarks>
    public interface IDataLoadCallback
    {
        void OnDataLoaded();
    }
}
