using System.Runtime.CompilerServices;
using UnityEngine;

namespace Nyc.Core
{
    /// <summary>
    /// Single source of truth for the three.js -> Unity coordinate conversion.
    ///
    /// Source frame (js/city-config.js): 1 unit = 1 m, +X = Manhattan-grid east,
    /// +Z = grid south (north = -Z), origin at 5th Ave &amp; 42nd St centreline,
    /// axes turned 29.02 deg from true north.
    ///
    /// Unity is left-handed with +Z forward, the source is right-handed with +Z toward
    /// the viewer, therefore the only conversion that preserves chirality is z -> -z.
    /// After conversion north is +Z in Unity, which is also Unity's convention.
    /// </summary>
    public static class CityFrame
    {
        public const float BearingDeg      = 29.02f;
        public const float SidewalkHeight  = 0.22f;
        public const float WaterLevel      = -2.2f;
        public const float RoadSurfaceY    = 0.03f;
        public const float ParkSurfaceY    = 0.06f;
        public const float LotInset        = 4.6f;   // property line in from the curb (15 ft)

        // Detail cut: everything north of this z (~129th St) is low-detail borough geometry.
        public const float DefaultCutZ     = -7090f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, y, -z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ToUnity(Vector3 src) => new Vector3(src.x, src.y, -src.z);

        /// <summary>Directions convert exactly like positions (reflection about the XY plane).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 DirToUnity(Vector3 src) => new Vector3(src.x, src.y, -src.z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ToUnityXZ(float x, float z) => new Vector2(x, -z);

        /// <summary>
        /// Source heading: 0 faces +Z (south), PI faces north. Unity yaw: 0 faces +Z.
        /// yaw = 180 - heading.
        /// </summary>
        public static float HeadingToYawDeg(float headingRad) => 180f - headingRad * Mathf.Rad2Deg;

        public static float YawToHeadingRad(float yawDeg) => (180f - yawDeg) * Mathf.Deg2Rad;

        /// <summary>
        /// Source heading (0 = +Z south, PI = north) -> Unity rotation.
        ///
        /// The conversion z -> -z means a model authored facing +Z in the source faces -Z in
        /// Unity. Actor prefabs therefore MUST face -Z (which is exactly what a glTF import
        /// produces). With that convention the Unity yaw is simply the negated heading.
        /// If you author a prefab facing +Z, add a 180 deg child rotation.
        /// </summary>
        public static Quaternion HeadingToRotation(float headingRad) =>
            Quaternion.Euler(0f, -headingRad * Mathf.Rad2Deg, 0f);

        /// <summary>Kept for geometry authored with a source-frame +Y rotation.</summary>
        public static Quaternion YawQuaternion(float sourceRad) => HeadingToRotation(sourceRad);
    }

    /// <summary>
    /// Deterministic 32-bit pseudo-random number generator (Mulberry32).
    /// Used for procedural vegetation, street details, and building generation.
    /// </summary>
    public struct Mulberry32
    {
        uint state;
        public Mulberry32(uint s) { state = s; }

        public float NextFloat()
        {
            unchecked
            {
                state += 0x6D2B79F5u;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296f;
            }
        }

        public float Range(float a, float b) => a + (b - a) * NextFloat();
        public int Range(int a, int b) => a + (int)(NextFloat() * (b - a + 1));
        public int NextInt() => (int)(NextFloat() * int.MaxValue);
        public int NextInt(int max) => (int)(NextFloat() * max);
        public int NextInt(int min, int max) => Range(min, max);
    }
}
