// URP / rendering stubs used only to type-check LightingSystem.cs. NOT part of the deliverable.
using System;

namespace UnityEngine
{
    public class ScriptableObject : Object
    {
        public HideFlags hideFlags;
        public static T CreateInstance<T>() where T : ScriptableObject => null;
    }

    public enum HideFlags { None, HideAndDontSave }

    public class CreateAssetMenuAttribute : Attribute
    {
        public string menuName, fileName;
    }

    public class ExecuteAlways : Attribute { }
    public class RequireComponent : Attribute { public RequireComponent(Type a) { } public RequireComponent(Type a, Type b) { } }

    public enum LightShadows { None, Hard, Soft }
    public class Light : Component
    {
        public Color color;
        public float intensity;
        public LightShadows shadows;
        public float shadowBias, shadowNormalBias;
        public LightShadow shadow;
    }
    public class LightShadow { public float strength; }

    public enum AmbientMode { Skybox, Trilight, Flat, Custom }
    public enum FogMode { Linear, Exponential, ExponentialSquared }

    public static class RenderSettings
    {
        public static AmbientMode ambientMode;
        public static Color ambientSkyColor, ambientEquatorColor, ambientGroundColor;
        public static float ambientIntensity;
        public static bool fog;
        public static FogMode fogMode;
        public static Color fogColor;
        public static float fogDensity;
        public static float reflectionIntensity;
        public static Material skybox;
    }

    public class ReflectionProbe : Component { public void RenderProbe() { } }

    public static class DynamicGI { public static void UpdateEnvironment() { } }

    public class Camera : Component { public static Camera main; public float fieldOfView; }

    public static class Time
    {
        public static float time, deltaTime, unscaledDeltaTime;
        public static double timeAsDouble;
    }
}

namespace UnityEngine.Rendering
{
    public class RenderPipelineAsset : ScriptableObject { }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline; }

    public class Volume : Component
    {
        public VolumeProfile sharedProfile;
        public VolumeProfile profile;
    }

    public class VolumeProfile : ScriptableObject
    {
        public bool TryGet<T>(out T component) where T : class { component = null; return false; }
        public T Add<T>(bool overrides = false) where T : Universal.VolumeComponent, new() => new T();
    }

    public class VolumeParameter<T>
    {
        public T value;
        public void Override(T v) { value = v; }
    }
    public class BoolParameter : VolumeParameter<bool>
    {
        public BoolParameter(bool v, bool overrideState = false) { }
    }
    public class ClampedFloatParameter : VolumeParameter<float>
    {
        public ClampedFloatParameter(float min, float max, bool overrideState = false) { }
    }
    public class FloatParameter : VolumeParameter<float>
    {
        public FloatParameter(float v, bool overrideState = false) { }
    }
    public class ColorParameter : VolumeParameter<Color>
    {
        public ColorParameter(Color c, bool overrideState = false) { }
    }
    public class TonemappingModeParameter : VolumeParameter<TonemappingMode>
    {
        public TonemappingModeParameter(TonemappingMode m, bool overrideState = false) { }
    }
    public enum TonemappingMode { None, Neutral, ACES }
}

namespace UnityEngine.Rendering.Universal
{
    using UnityEngine.Rendering;

    public class Bloom : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0, 10);
        public ClampedFloatParameter threshold = new ClampedFloatParameter(0, 10);
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0, 1);
        public ColorParameter tint = new ColorParameter(Color.white);
    }

    public class ColorAdjustments : VolumeComponent
    {
        public FloatParameter postExposure = new FloatParameter(0);
        public ClampedFloatParameter contrast = new ClampedFloatParameter(-100, 100);
        public ClampedFloatParameter saturation = new ClampedFloatParameter(-100, 100);
        public ColorParameter colorFilter = new ColorParameter(Color.white);
    }

    public class Tonemapping : VolumeComponent
    {
        public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None);
    }

    public class Vignette : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0, 1);
        public ClampedFloatParameter smoothness = new ClampedFloatParameter(0.01f, 1);
        public BoolParameter rounded = new BoolParameter(false);
    }

    public class VolumeComponent : ScriptableObject { }

    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public float shadowDistance;
    }
}
