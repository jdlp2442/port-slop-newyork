using UnityEngine;

namespace Nyc.Rendering
{
    /// <summary>Cached shader property ids for the global city uniforms.</summary>
    public static class NycShaderIDs
    {
        public static readonly int SunColor        = Shader.PropertyToID("_NycSunColor");
        public static readonly int SkyTopColor     = Shader.PropertyToID("_NycSkyTopColor");
        public static readonly int SkyHorizonColor = Shader.PropertyToID("_NycSkyHorizonColor");
        public static readonly int SunDir          = Shader.PropertyToID("_NycSunDir");
        public static readonly int CpBounds        = Shader.PropertyToID("_NycCpBounds");
        public static readonly int IslandBounds    = Shader.PropertyToID("_NycIslandBounds");
        public static readonly int InteriorGlow    = Shader.PropertyToID("_NycInteriorGlow");
        public static readonly int RoadWetness     = Shader.PropertyToID("_NycRoadWetness");
        public static readonly int CanyonBounce    = Shader.PropertyToID("_NycCanyonBounce");
        public static readonly int Time            = Shader.PropertyToID("_NycTime");

        // Sky material
        public static readonly int Turbidity       = Shader.PropertyToID("_Turbidity");
        public static readonly int Rayleigh        = Shader.PropertyToID("_Rayleigh");
        public static readonly int MieCoefficient  = Shader.PropertyToID("_MieCoefficient");
        public static readonly int MieG            = Shader.PropertyToID("_MieDirectionalG");
        public static readonly int SkySunDir       = Shader.PropertyToID("_SunDir");
        public static readonly int SkyExposure     = Shader.PropertyToID("_Exposure");
        public static readonly int NightFactor     = Shader.PropertyToID("_NightFactor");
        public static readonly int NightColor      = Shader.PropertyToID("_NightColor");
        public static readonly int CloudCoverage   = Shader.PropertyToID("_CloudCoverage");
        public static readonly int CloudDensity    = Shader.PropertyToID("_CloudDensity");
        public static readonly int CloudElevation  = Shader.PropertyToID("_CloudElevation");
        public static readonly int CloudScale      = Shader.PropertyToID("_CloudScale");
    }
}
