using UnityEngine;
using Nyc.Core;

namespace Nyc.Gameplay
{
    /// <summary>
    /// Safe arrival search. Port of findTeleportPosition() (controllers.js:231-303): clamp the
    /// click to the island and its shoreline, test the whole arrival footprint (not just the
    /// centre) against buildings and the other player bodies, then search 4 m rings out to 256 m
    /// in 32 directions before giving up.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TeleportService : MonoBehaviour
    {
        public CollisionWorld collision;
        public ThirdPersonControllerManager manager;

        [Header("Body cylinders (radius, height)")]
        public float personRadius = 0.48f, personHeight = 1.9f;
        public float carRadius = 2.4f, carHeight = 1.9f;
        public float bikeRadius = 1.0f, bikeHeight = 1.9f;
        public float heliRadius = 6.2f, heliHeight = 3.2f;

        public bool TryFindSafe(Vector2 target, float heading, TravelMode mode,
                                TravelController entity, out Vector2 result)
        {
            result = entity != null ? entity.Position : target;
            if (!CityConfig.Ready || collision == null) return false;

            bool heli = mode == TravelMode.Helicopter;
            bool car = mode == TravelMode.Car;
            float radius = heli ? 4.5f : mode == TravelMode.Person ? 0.48f : 0.65f;

            float sinH = Mathf.Sin(heading);
            float cosH = Mathf.Cos(heading);
            float extX = (car ? Mathf.Abs(cosH) * 0.86f + Mathf.Abs(sinH) * 2.18f : radius) + 0.1f;
            float extZ = (car ? Mathf.Abs(sinH) * 0.86f + Mathf.Abs(cosH) * 2.18f : radius) + 0.1f;
            float reach = Mathf.Max(extX, extZ);
            float altitude = heli && entity != null ? Mathf.Clamp(entity.Height, 92f, 380f) : 0f;

            GetBody(mode, out float bodyRadius, out float bodyHeight);

            Vector2 ClampPoint(Vector2 p, out bool ok)
            {
                ok = true;
                Vector4 b = collision.Bounds;
                p.y = Mathf.Clamp(p.y, b.z + (heli ? -6000f : 30f), b.w + (heli ? 6000f : 0f));
                p.x = Mathf.Clamp(p.x, b.x - (heli ? 6000f : 0f), b.y + (heli ? 6000f : 0f));

                if (!heli)
                {
                    CityConfig.IslandBoundsAtZ(p.y, out float west, out float east);
                    west += 8f;
                    east -= 8f;
                    if (west > east) { ok = false; return p; }
                    p.x = Mathf.Clamp(p.x, west, east);
                }
                return p;
            }

            bool Clear(Vector2 p)
            {
                float baseY = heli ? altitude : (manager != null ? manager.GetGroundHeight(p.x, p.y, mode) : 0f);
                float y = heli ? altitude : mode == TravelMode.Person ? baseY + 1f : 1f;

                float cameraBack = Mathf.Cos(0.09f) * (entity != null ? entity.ChaseDistance : 5f);
                float cameraUp = (entity != null ? entity.ChaseHeight : 2f)
                               + Mathf.Sin(0.09f) * (entity != null ? entity.ChaseDistance : 5f) * 0.85f;
                float viewTop = baseY + Mathf.Max(bodyHeight, cameraUp);

                if (manager)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        TravelMode otherMode = (TravelMode)i;
                        TravelController other = manager.Resolve(otherMode);
                        if (other == null || other == entity) continue;

                        GetBody(otherMode, out float oRadius, out float oHeight);
                        float oBottom = other.Height - (otherMode == TravelMode.Helicopter ? 0.7f : 0f);
                        float oTop = other.Height + oHeight;
                        if (oBottom > viewTop + 0.3f || oTop < baseY - (heli ? 0.7f : 0f) - 0.3f) continue;

                        float dx = other.Position.x - p.x;
                        float dz = other.Position.y - p.y;
                        float back = Mathf.Clamp(-dx * sinH - dz * cosH, 0f, cameraBack);
                        float cx = dx + sinH * back;
                        float cz = dz + cosH * back;
                        if (Mathf.Sqrt(cx * cx + cz * cz) < oRadius + Mathf.Max(bodyRadius, 0.5f) + 0.1f)
                            return false;
                    }
                }

                return !collision.Overlaps(p, extX, extZ, y);
            }

            Vector2 origin = ClampPoint(target, out bool originOk);
            if (originOk && Clear(origin)) { result = origin; return true; }

            Vector2 centre = originOk ? origin : target;
            for (float distance = 4f; distance <= 256f; distance += 4f)
            {
                for (int i = 0; i < 32; i++)
                {
                    float angle = i * Mathf.PI / 16f;
                    Vector2 candidate = ClampPoint(
                        new Vector2(centre.x + Mathf.Cos(angle) * distance,
                                    centre.y + Mathf.Sin(angle) * distance), out bool ok);
                    if (!ok) continue;
                    if (!Clear(candidate)) continue;
                    result = candidate;
                    return true;
                }
            }
            return false;
        }

        void GetBody(TravelMode mode, out float radius, out float height)
        {
            switch (mode)
            {
                case TravelMode.Person: radius = personRadius; height = personHeight; break;
                case TravelMode.Car: radius = carRadius; height = carHeight; break;
                case TravelMode.Cyclist: radius = bikeRadius; height = bikeHeight; break;
                default: radius = heliRadius; height = heliHeight; break;
            }
        }

        /// <summary>
        /// Exact preset teleport locations matching TELEPORT_LOCATIONS in city-config.js.
        /// </summary>
        public static bool GetNamedTeleport(string id, out Vector2 pos, out float heading)
        {
            switch (id.ToLowerInvariant())
            {
                case "empire":
                case "esb":
                    pos = new Vector2(CityConfig.AvenueX("5") + 3.6f, CityConfig.StreetZ(36));
                    heading = 0f;
                    return true;
                case "timessq":
                case "times_square":
                    pos = new Vector2(CityConfig.AvenueX("7") + 3.6f, CityConfig.StreetZ(46));
                    heading = 0f;
                    return true;
                case "chrysler":
                    pos = new Vector2(CityConfig.AvenueX("lex") + 3.6f, CityConfig.StreetZ(40));
                    heading = Mathf.PI;
                    return true;
                case "grandcentral":
                case "gct":
                    pos = new Vector2(CityConfig.AvenueX("park") - 8.0f, CityConfig.StreetZ(40));
                    heading = Mathf.PI;
                    return true;
                case "bryant":
                case "bryantpark":
                    pos = new Vector2(CityConfig.AvenueX("6") + 3.6f, CityConfig.StreetZ(41));
                    heading = Mathf.PI * 0.5f;
                    return true;
                case "flatiron":
                    pos = new Vector2(CityConfig.AvenueX("5") + 3.6f, CityConfig.StreetZ(25));
                    heading = 0f;
                    return true;
                case "centralpark":
                    pos = new Vector2(CityConfig.AvenueX("6") + 3.6f, CityConfig.StreetZ(57));
                    heading = Mathf.PI;
                    return true;
                case "owtc":
                case "wtc":
                    pos = new Vector2(-305.7f, 5210f);
                    heading = Mathf.Atan2(-204f + 305.7f, 5255.2f - 5210f);
                    return true;
                default:
                    pos = Vector2.zero;
                    heading = 0f;
                    return false;
            }
        }
    }
}
