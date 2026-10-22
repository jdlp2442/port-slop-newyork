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
        public ClampedFloatParameter(float value, float min, float max, bool overrideState = false) { }
    }
    public class MinFloatParameter : VolumeParameter<float>
    {
        public MinFloatParameter(float v, float min, bool overrideState = false) { }
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

    public struct RenderTargetIdentifier
    {
        public int id;
        public string name;
        public RenderTargetIdentifier(int id) { this.id = id; this.name = null; }
        public RenderTargetIdentifier(string name) { this.id = 0; this.name = name; }
        public static implicit operator RenderTargetIdentifier(string name) => new RenderTargetIdentifier(name);
        public static implicit operator RenderTargetIdentifier(int id) => new RenderTargetIdentifier(id);
    }

    public static class CommandBufferPool
    {
        public static CommandBuffer Get(string name = "") => new CommandBuffer { name = name };
        public static void Release(CommandBuffer buffer) { }
    }

    public class CommandBuffer : IDisposable
    {
        public string name;
        public void Blit(RenderTargetIdentifier src, RenderTargetIdentifier dst, Material mat = null, int pass = -1) { }
        public void SetGlobalTexture(string name, RenderTargetIdentifier val) { }
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier val) { }
        public void SetGlobalVector(string name, Vector4 val) { }
        public void SetGlobalFloat(string name, float val) { }
        public void SetGlobalColor(string name, Color val) { }
        public void GetTemporaryRT(int nameID, int width, int height, int depthBuffer = 0, FilterMode filter = FilterMode.Point) { }
        public void ReleaseTemporaryRT(int nameID) { }
        public void Clear() { }
        public void Dispose() { }
    }
}

namespace UnityEngine.Rendering.Universal
{
    using UnityEngine.Rendering;

    public enum RenderPassEvent
    {
        BeforeRendering = 0,
        BeforeRenderingShadows = 50,
        AfterRenderingShadows = 100,
        BeforeRenderingPrePasses = 150,
        AfterRenderingPrePasses = 200,
        BeforeRenderingGbuffer = 210,
        AfterRenderingGbuffer = 220,
        BeforeRenderingDeferredLights = 230,
        AfterRenderingDeferredLights = 240,
        BeforeRenderingOpaques = 250,
        AfterRenderingOpaques = 300,
        BeforeRenderingSkybox = 350,
        AfterRenderingSkybox = 400,
        BeforeRenderingTransparents = 450,
        AfterRenderingTransparents = 500,
        BeforeRenderingPostProcessing = 550,
        AfterRenderingPostProcessing = 600,
        AfterRendering = 1000
    }

    public abstract class ScriptableRenderPass
    {
        public RenderPassEvent renderPassEvent;
        public virtual void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) { }
        public virtual void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
        public virtual void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) { }
        public virtual void OnCameraCleanup(CommandBuffer cmd) { }
    }

    public struct RenderingData
    {
        public CameraData cameraData;
        public LightData lightData;
    }

    public struct CameraData
    {
        public Camera camera;
        public RenderTextureDescriptor cameraTargetDescriptor;
        public bool isSceneViewCamera;
        public Matrix4x4 worldToCameraMatrix;
    }

    public struct LightData
    {
        public int mainLightIndex;
        public bool supportsMainLightShadows;
    }

    public struct RenderTextureDescriptor
    {
        public int width;
        public int height;
        public int depthBufferBits;
        public int msaaSamples;
    }

    public struct ScriptableRenderContext
    {
        public void ExecuteCommandBuffer(CommandBuffer cmd) { }
    }

    public abstract class ScriptableRendererFeature : ScriptableObject
    {
        public abstract void Create();
        public abstract void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData);
        public virtual void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData) { }
        protected virtual void Dispose(bool disposing) { }
    }

    public class ScriptableRenderer
    {
        public RenderTargetIdentifier cameraColorTarget;
        public RenderTargetIdentifier cameraDepthTarget;
        public void EnqueuePass(ScriptableRenderPass pass) { }
    }

    public class Bloom : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0, 10);
        public ClampedFloatParameter threshold = new ClampedFloatParameter(0, 10);
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0, 1);
        public ColorParameter tint = new ColorParameter(Color.white);
        public ClampedFloatParameter clamp = new ClampedFloatParameter(65472f, 0f, 65472f);
        public BoolParameter highQualityFiltering = new BoolParameter(false);
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

    public enum DepthOfFieldMode { Off, Gaussian, Bokeh }

    public class DepthOfField : VolumeComponent
    {
        public VolumeParameter<DepthOfFieldMode> mode = new VolumeParameter<DepthOfFieldMode>();
        public MinFloatParameter gaussianStart = new MinFloatParameter(10f, 0f);
        public MinFloatParameter gaussianEnd = new MinFloatParameter(30f, 0f);
        public ClampedFloatParameter gaussianMaxRadius = new ClampedFloatParameter(1f, 0.5f, 1.5f);
        public BoolParameter highQualitySampling = new BoolParameter(false);
    }

    public class VolumeComponent : ScriptableObject { }

    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public float shadowDistance;
    }
}
