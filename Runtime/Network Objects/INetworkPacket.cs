using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    [TypeCache]
    public interface INetworkPacket
    {
        void Serialize(FastBufferWriter writer, bool fullPacket);
        void Deserialize(FastBufferReader reader, out bool fullPacket);
    }
}
