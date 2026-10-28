using UnityEngine;

namespace GunnellSmoke
{
    [CreateAssetMenu(menuName = "Gunnell Smoke/Look preset")]
    public sealed class GunnellSmokeLook : ScriptableObject
    {
        [Range(0f, 1f)] public float stylization;
        [Header("Forma")]
        [Range(0f, 1f)] public float shapeDeformation = 0.20f;
        [Tooltip("Frecuencia de los lóbulos en el mundo. Menor valor produce volutas más amplias.")]
        [Range(0.05f, 0.4f)] public float noiseWorldScale = 0.20f;
        [Range(0f, 1f)] public float detailStrength = 0.17f;
        [Range(0.75f, 0.99f)] public float densityFalloff = 0.89f;
        [Header("Luz y color")]
        public Color smokeColor = new Color(0.68f, 0.68f, 0.68f);
        [ColorUsage(false, true)] public Color sunColor = new Color(0.85f, 0.85f, 0.82f);
        public Color skyAmbientTint = Color.white;
        public Color groundAmbientTint = new Color(0.94f, 0.94f, 0.92f);
        public Color shadowColor = new Color(0.20f, 0.32f, 0.35f);
        public Color highlightColor = new Color(0.92f, 0.91f, 0.78f);
        [Range(0f, 4f)] public float ambient = 0.85f;
        [Range(0f, 1f)] public float lobeContrast = 0.34f;
        [Range(0f, 1f)] public float multipleScattering = 0.32f;
        [Range(0f, 1f)] public float powderStrength = 0.12f;
        [Range(0f, 4f)] public float shadowDensity = 0.95f;
        [Header("Movimiento")]
        public Vector3 animationDirection = new Vector3(0.035f, -0.09f, 0.028f);
        [Range(0f, 3f)] public float animationStrength = 1f;
        [Range(0f, 0.8f)] public float turbulenceStrength = 0.40f;
        [Range(0.3f, 4f)] public float turbulenceScale = 1.15f;
        [Range(0f, 3f)] public float turbulenceSpeed = 0.60f;
        [Header("Wisps")]
        [Tooltip("Material compartido opcional. Si falta, se conserva el atlas direccional procedural.")]
        public Material wispMaterial;
        [Range(0.25f, 1.5f)] public float wispSize = 0.85f;
        [Range(0f, 1f)] public float wispOpacity = 0.14f;
    }
}
