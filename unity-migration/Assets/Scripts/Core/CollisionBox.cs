using UnityEngine;

namespace Nyc.Core
{
    /// <summary>
    /// One static horizontal collider: an AABB with a top height, optionally an oriented box
    /// (buildings on non-grid streets, which have rotated frontage frames).
    /// Produced by ManhattanMeshBuilder and consumed by CollisionWorld.
    /// </summary>
    public struct CollisionBox
    {
        public float MinX, MaxX, MinZ, MaxZ, MaxY;
        public bool HasObb;
        public float Cx, Cz, Hx, Hz, Cs, Sn;
    }
}
