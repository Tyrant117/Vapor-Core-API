using System.Collections.Generic;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    public abstract class VaporNetworkBehaviour : NetworkBehaviour, ISubObjectOwner
    {
        protected List<VaporNetworkObject> SubObjects;

        public virtual void OnSubObjectSpawned(VaporNetworkObject subObject)
        {
            SubObjects ??= new List<VaporNetworkObject>();
            SubObjects.Add(subObject);
        }
        
        public virtual void OnSubObjectDespawned(VaporNetworkObject subObject)
        {
            SubObjects.Remove(subObject);
        }
    }
}
