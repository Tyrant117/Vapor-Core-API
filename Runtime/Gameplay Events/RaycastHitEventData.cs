using Unity.Netcode;
using UnityEngine;

namespace Vapor.GameplayFramework
{
    public struct RaycastHitEventData : IGameplayEventData
    {
        public NetworkBehaviourReference ActorReference;
        public Vector3 Point;
        public Vector3 Normal;

        public void Serialize(FastBufferWriter writer, bool fullPacket)
        {
            writer.WriteValueSafe(ActorReference);
            writer.WriteValueSafe(Point);
            writer.WriteValueSafe(Normal);
        }

        public void Deserialize(FastBufferReader reader, out bool fullPacket)
        {
            fullPacket = true;
            reader.ReadValueSafe(out ActorReference);
            reader.ReadValueSafe(out Point);
            reader.ReadValueSafe(out Normal);
        }
    }
}