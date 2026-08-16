using UnityEngine;
using Vapor.Networking;

namespace Vapor.GameplayFramework
{
    /// <summary>A pose: where something happened, or where to aim.</summary>
    [NetworkSerializable]
    public partial struct PoseEventData : IGameplayEventData
    {
        public Pose Pose;

        public PoseEventData(Pose pose) => Pose = pose;
        public PoseEventData(Vector3 position, Quaternion rotation) => Pose = new Pose(position, rotation);
    }
}
