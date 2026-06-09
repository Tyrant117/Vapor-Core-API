namespace Vapor.NetworkObjects
{
    public interface ISubObjectOwner
    {
        void OnSubObjectSpawned(VaporNetworkObject subObject);
        void OnSubObjectDespawned(VaporNetworkObject subObject);
    }
}