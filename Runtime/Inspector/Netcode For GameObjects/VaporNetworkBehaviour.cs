#if VAPOR_NETCODE
using Unity.Netcode;
#endif

namespace Vapor.Inspector.Netcode
{
#if VAPOR_NETCODE
    public abstract class VaporNetworkBehaviour : NetworkBehaviour
    {
        
    }
#endif
}
