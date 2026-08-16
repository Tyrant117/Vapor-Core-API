using UnityEngine;
using Vapor.Networking;

namespace Vapor.GameplayFramework
{
    /// <summary>
    /// A hit: the network object that was hit (an actor, usually; unset when the hit was on
    /// scenery), the point and the surface normal.
    /// </summary>
    [NetworkSerializable]
    public partial struct RaycastHitEventData : IGameplayEventData
    {
        public VaporNetworkObjectReference Target;
        public Vector3 Point;
        public Vector3 Normal;

        public RaycastHitEventData(VaporNetworkObjectReference target, Vector3 point, Vector3 normal)
        {
            Target = target;
            Point = point;
            Normal = normal;
        }

        public bool HasTarget => !Target.IsNone;
    }
}
