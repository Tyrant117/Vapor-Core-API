using Unity.Netcode;
using UnityEngine;

namespace Vapor.GameplayFramework
{
    public struct PoseEventData : IGameplayEventData
    {
        public Pose Pose;
        
        public void Serialize(FastBufferWriter writer, bool fullPacket)
        {
            writer.WriteValueSafe(Pose);
        }
        
        public void Deserialize(FastBufferReader reader, out bool fullPacket)
        {
            fullPacket = true;
            reader.ReadValueSafe(out Pose);
        }
    }
}