using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GunnellSmoke
{
    /// <summary>
    /// RendererFeature URP del humo estilo Gunnell: compute de raymarch a media
    /// resolución + composite fullscreen con rechazo por profundidad.
    ///
    /// Implementación propia para RenderGraph/URP de la técnica pública del
    /// vídeo de Acerola. Sin código copiado del repositorio de referencia.
    /// </summary>
    public class GunnellSmokeFeature : ScriptableRendererFeature
    {
        private const int NoiseResolution = 128;
        private const int MaxLocalLights = 8;
        [SerializeField, HideInInspector] private int debugForce;
        [System.Serializable]
        public class Settings
        {
            [Header("Recursos")]
            public ComputeShader raymarchCompute;
            public Shader compositeShader;

            [Header("Volumen (vacío = primero activo con densidad)")]
            public GunnellSmokeVolume volume;
            [Header("Estilos visuales (4 / 5 / 6 en la demo)")]
            public GunnellSmokeLook realisticLook;
            public GunnellSmokeLook stylizedLook;
            public GunnellSmokeLook ActiveLook => SmokeGameplay.SelectedSmokeStyle == 1
                ? stylizedLook : realisticLook;

            [Header("Resolución del buffer de humo")]
            [Range(1, 4)] public int downsample = 2;
            [Tooltip("Conserva resolución completa cuando el jugador está junto al humo.")]
            public bool fullResolutionNearSmoke;

            [Header("Muchas nubes")]
            [Tooltip("Reduce el buffer con varias nubes; cerca limita la reducción a un nivel sobre el ajuste normal.")]
            public bool adaptiveCrowdResolution = true;
            [Range(4, MaxVolumes)] public int crowdResolutionThreshold = 4;
            [Range(2,4)] public int crowdDownsample = 3;
            [Min(0f)] public float crowdNearDistance = 3f;
            [Tooltip("Con muchas nubes, mezcla toda su iluminación almacenada pero calcula el relieve fino sólo en las dos contribuciones más densas.")]
            public bool adaptiveOverlapShading = true;
            [Tooltip("Reduce emisión y correcciones CPU de partículas adicionales con muchas nubes. El campo volumétrico conserva su simulación.")]
            public bool adaptiveCrowdParticles = true;
            [Tooltip("Reparte la renovación de sombras y luz en más cuadros cuando hay muchas nubes. No retrasa la densidad ni los impactos.")]
            public bool adaptiveCrowdLighting = true;
            [Range(1,MaxVolumes)] public int crowdLightingUpdatesPerFrame = 4;
            [Range(64,192)] public int crowdViewSteps = 128;
            [Range(12,48)] public int crowdShadowSteps = 24;
            [Tooltip("Salta el transporte caro en bloques cuya densidad ya está totalmente restaurada. Las velocidades altas usan el cálculo completo.")]
            public bool sparseWakeTiles = true;

            [Header("Marcha de vista")]
            [Range(8, 256)] public int stepCount = 160;
            [Range(0.02f, 0.3f)] public float stepSize = 0.035f;

            [Header("Marcha de luz")]
            public bool cacheLighting = true;
            [Tooltip("Actualizaciones por segundo de la luz volumétrica. Cámara y luces invalidan la caché; las perturbaciones aumentan su frecuencia.")]
            [Range(15,60)] public int lightingRefreshRate = 30;
            [Tooltip("Presupuesto de nubes cuya luz se renueva por cámara y cuadro. Las nubes nuevas siempre se inicializan.")]
            [Range(1, MaxVolumes)] public int lightingUpdatesPerFrame = 8;
            [Range(1, 16)] public int lightStepCount = 12;
            [Tooltip("Resolución de la luz difusa. El relieve fino conserva las muestras de densidad originales.")]
            [Range(0.75f,1.875f)] public float lightingGridScale = 1.25f;
            [Range(0.05f, 1f)] public float lightStepSize = 0.25f;
            [Range(0f, 4f)] public float shadowDensity = 1.15f;

            [Header("Luces de la escena")]
            public bool sceneAdditionalLights = true;
            [Tooltip("Luces puntuales y focos que más aportan a las nubes visibles.")]
            [Range(0, MaxLocalLights)] public int maxLocalLights = 4;
            [Range(0f, 6f)] public float localLightStrength = 1f;
            [Range(1, 8)] public int localLightSteps = 4;

            [Header("Sombras del entorno")]
            public bool receiveSceneShadows = true;
            public bool castSceneShadows = true;
            [Range(0f, 0.85f)] public float castShadowStrength = 0.58f;

            [Header("Medio")]
            [Tooltip("Variación suave de la densidad interior, usando el ruido volumétrico existente.")]
            [Range(0f, 1f)] public float interiorDetail = 0.4f;
            [Range(0f, 1f)] public float noiseErode = 0.22f;
            [Range(0f, 4f)] public float volumeDensity = 3.5f;
            [Range(0f, 3f)] public float absorption = 0.35f;
            [Range(0f, 3f)] public float scattering = 1.2f;
            [Range(-0.9f, 0.9f)] public float anisotropy;
            /// <summary>
            /// Luz ambiental isotrópica que rellena el cuerpo del humo. En el ref
            /// el color parte de `_SmokeColor` y se le suma el sol; este término es
            /// ese peso base. Sin él el interior queda negro donde el sol no llega.
            /// </summary>
            [Range(0f, 4f)] public float ambient = 0.85f;
            [Header("Luz difusa del humo")]
            public Color skyAmbientTint = new Color(1.04f, 1.04f, 1.02f);
            public Color groundAmbientTint = new Color(0.90f, 0.87f, 0.82f);
            [Tooltip("Aproximación de luz dispersada en el interior, calculada en el volumen de iluminación.")]
            [Range(0f, 1f)] public float multipleScattering = 0.25f;
            [Tooltip("Da relieve a los bordes porosos sin cambiar su opacidad.")]
            [Range(0f, 1f)] public float powderStrength = 0.20f;

            /// <summary>
            /// 0 = Henyey-Greenstein (con `anisotropy`), 2 = Rayleigh (iso).
            /// La escena de referencia usa 2 (Rayleigh): brillo que no depende de
            /// mirar a favor o contra el sol.
            /// </summary>
            [Range(0, 2)] public int phaseFunction = 2;

            public Color sunColor = new Color(0.85f, 0.85f, 0.82f);
            public Color smokeColor = new Color(0.74f, 0.72f, 0.68f);

            [Header("Ruido Worley (se regenera al cambiar)")]
            public int noiseSeed;
            [Range(2, 16)] public int noiseCells = 5;
            [Range(1, 5)] public int noiseOctaves = 5;
            [Range(0f, 2f)] public float noiseAmplitude = 0.62f;
            [Range(0f, 2f)] public float noiseWarp = 0.35f;
            [Range(0f, 1f)] public float shapeDeformation = 0.34f;
            [Tooltip("Escala del Worley en el mundo. Valores bajos producen lóbulos más grandes.")]
            [Range(0.05f, 0.4f)] public float noiseWorldScale = 0.29f;
            [Range(0f, 1f)] public float detailStrength = 0.22f;
            [Tooltip("Contraste suave de los lóbulos con la dirección del sol.")]
            [Range(0f, 1f)] public float lobeContrast = 0.50f;
            [Range(-1f, 1f)] public float noiseAdd;
            public bool noiseInvert = true;

            [Header("Animación")]
            [Tooltip("Velocidad del ruido en metros/s. Una Y negativa hace ascender el humo.")]
            public Vector3 animationDirection = new Vector3(0.025f, -0.11f, 0.020f);
            [Range(0f, 3f)] public float animationStrength = 1f;
            [Tooltip("Desplazamiento máximo de los remolinos, en metros. La rejilla de obstáculos permanece fija.")]
            [Range(0f, 0.8f)] public float turbulenceStrength = 0.46f;
            [Tooltip("Frecuencia espacial de los remolinos, en radianes por metro.")]
            [Range(0.3f, 4f)] public float turbulenceScale = 1.05f;
            [Tooltip("Velocidad de evolución de los remolinos.")]
            [Range(0f, 3f)] public float turbulenceSpeed = 0.72f;

            [Header("Corte")]
            [Range(0f, 0.5f)] public float alphaThreshold = 0.01f;
        }

        public Settings settings = new Settings();

        private GunnellRaymarchPass raymarchPass;
        private GunnellCompositePass compositePass;
        private GunnellSceneShadowPass sceneShadowPass;
        private Material compositeMaterial;
        private ComputeShader compute;
        private int kernelNoise;
        private int kernelRaymarch;
        private int kernelLighting;
        private int kernelLocalLighting;
        private int kernelSceneShadow;
        private RenderTexture noiseTexture;
        private RTHandle noiseVolume;
        private int lastNoiseHash;
        private bool noiseGenerated;

        // INSTANCIA, no estático. Un ScriptableObject ejecuta los inicializadores
        // estáticos al deserializarse (al crear el renderer), y crear ahí objetos
        // nativos revienta con "CreateImpl is not allowed to be called during
        // serialization" -> TypeInitializationException y el renderer no se crea.
        // Es el mismo motivo por el que `compositeBlock` va perezoso, y el patrón
        // que ya usan las features propias del proyecto (SmokeRaymarchPass).
        private readonly ProfilingSampler sampler = new ProfilingSampler("Gunnell Smoke");
        private readonly ProfilingSampler lightingSampler = new ProfilingSampler("Gunnell Smoke Lighting Volume");
        private readonly ProfilingSampler shadowSampler = new ProfilingSampler("Gunnell Smoke Scene Shadows");
        private readonly TextureHandle[] densityScratch = new TextureHandle[MaxVolumes];
        private readonly TextureHandle[] lightingScratch = new TextureHandle[MaxVolumes];
        private readonly TextureHandle[] localLightingScratch = new TextureHandle[MaxVolumes];
        private readonly TextureHandle[] wakeScratch = new TextureHandle[MaxVolumes];

        public override void Create()
        {
            DisposeResources();
            GunnellSmokeParticles.FlowSettings = settings;

                        if (settings.raymarchCompute == null)
            {
                settings.raymarchCompute = Resources.Load<ComputeShader>("GunnellSmoke");
#if UNITY_EDITOR
                if (settings.raymarchCompute == null)
                    settings.raymarchCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/GunnellSmoke/Shaders/GunnellSmoke.compute");
#endif
            }

            if (settings.compositeShader == null)
            {
                settings.compositeShader = Shader.Find("Hidden/GunnellSmoke/Composite");
#if UNITY_EDITOR
                if (settings.compositeShader == null)
                    settings.compositeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/GunnellSmoke/Shaders/GunnellSmokeComposite.shader");
#endif
            }

            if (settings.realisticLook == null)
                settings.realisticLook = Resources.Load<GunnellSmokeLook>("SmokeLook_Realistic");
            if (settings.stylizedLook == null)
                settings.stylizedLook = Resources.Load<GunnellSmokeLook>("SmokeLook_Stylized");

            compute = settings.raymarchCompute;
            if (compute != null && compute.HasKernel("CS_GenerateWorley"))
            {
                kernelNoise = compute.FindKernel("CS_GenerateWorley");
                kernelRaymarch = compute.FindKernel("CS_Raymarch");
                kernelLighting = compute.FindKernel("CS_LightingVolume");
                kernelLocalLighting = compute.FindKernel("CS_LocalLighting");
                kernelSceneShadow = compute.FindKernel("CS_SceneShadow");
            }
            else
            {
                Debug.LogWarning("[Gunnell Smoke] Sin ComputeShader válido: el pase no se encolará.");
            }

            if (settings.compositeShader != null)
                compositeMaterial = CoreUtils.CreateEngineMaterial(settings.compositeShader);

            // Patrón del proyecto (ver SmokeFroxelShadows): RenderTexture 3D con
            // random write + RTHandles.Alloc encima.
            noiseTexture = new RenderTexture(NoiseResolution, NoiseResolution, 0, RenderTextureFormat.RGHalf)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = NoiseResolution,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                useMipMap = true,
                autoGenerateMips = false,
            };
            noiseTexture.Create();
            noiseVolume = RTHandles.Alloc(noiseTexture);
            lastNoiseHash = 0; // fuerza la generación en el primer frame
            noiseGenerated = false;

            // Suscribe al segmento resuelto de DemoWeapon y reserva el buffer de
            // agujeros de bala. Idempotente: si ya estaba suscrito, no hace nada.
            GunnellBulletHoles.Ensure();

            // Los agujeros se actualizan FUERA del render. Ver el comentario de
            // `TickBulletHolesBeforeRender`: subir el buffer con SetData durante
            // AddRenderPasses corrompía la grabación del RenderGraph y el frame
            // salía NEGRO con cualquier agujero vivo.
            RenderPipelineManager.beginContextRendering -= TickBulletHolesBeforeRender;
            RenderPipelineManager.beginContextRendering += TickBulletHolesBeforeRender;
            RenderPipelineManager.beginContextRendering -= PrewarmNoiseBeforeRender;
            RenderPipelineManager.beginContextRendering += PrewarmNoiseBeforeRender;

            raymarchPass = new GunnellRaymarchPass(this);
            compositePass = new GunnellCompositePass(this);
            sceneShadowPass = new GunnellSceneShadowPass(this);
        }

        /// <summary>
        /// Avanza las vidas de los agujeros y sube su buffer ANTES de que el
        /// RenderGraph grabe nada.
        /// <para>
        /// POR QUÉ NO SE PUEDE HACER EN <c>AddRenderPasses</c>: <c>Tick</c> termina
        /// en <c>ComputeBuffer.SetData</c>, que es una subida CPU→GPU inmediata.
        /// Hacerla mientras el RenderGraph está grabando los pases desincroniza la
        /// grabación y el pase de humo se ejecuta sobre un target vacío: el frame
        /// entero salía negro (incluido el cielo) en cuanto había un agujero vivo.
        /// <c>beginContextRendering</c> se dispara antes de grabar, así que el
        /// buffer ya está subido y el grafo grabado después es consistente.
        /// </para>
        /// Una sola vez por frame aunque haya varias cámaras, para que un disparo
        /// no consuma la vida del agujero varias veces.
        /// </summary>
        private static int bulletTickFrame = int.MinValue;

        private static void TickBulletHolesBeforeRender(ScriptableRenderContext context,
                                                        System.Collections.Generic.List<Camera> cameras)
        {
            if (!Application.isPlaying) return;
            if (bulletTickFrame == Time.frameCount) return;
            bulletTickFrame = Time.frameCount;
            GunnellBulletHoles.Ensure();
            GunnellBulletHoles.Tick(Time.deltaTime);
            foreach (var volume in GunnellSmokeVolume.Volumes)
                if (volume != null && volume.isActiveAndEnabled && volume.HasDensity)
                    volume.Wake.Advance(Time.deltaTime, GunnellSmokeParticles.FlowSettings);
        }

        private int GetNoiseSettingsHash()
        {
            int h;
            unchecked
            {
                h = 17;
                h = h * 31 + settings.noiseSeed;
                h = h * 31 + settings.noiseCells;
                h = h * 31 + settings.noiseOctaves;
                h = h * 31 + settings.noiseAmplitude.GetHashCode();
                h = h * 31 + settings.noiseWarp.GetHashCode();
                h = h * 31 + settings.noiseAdd.GetHashCode();
                h = h * 31 + (settings.noiseInvert ? 1 : 0);
            }
            return h;
        }

        private void PrewarmNoiseBeforeRender(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            lightingCache.Trim();
            if (!Application.isPlaying || !NoiseNeedsGeneration || !isActive) return;
            // Prepare the shared billow texture before the first grenade, outside
            // RenderGraph recording, so detonation does not pay this one-time cost.
            var cmd = CommandBufferPool.Get("Gunnell Smoke Prepare Noise");
            cmd.SetComputeTextureParam(compute,kernelNoise,"_RWNoiseTex",noiseTexture);
            cmd.SetComputeIntParam(compute,"_NoiseSeed",settings.noiseSeed);
            cmd.SetComputeIntParam(compute,"_NoiseCells",Mathf.Max(2,settings.noiseCells));
            cmd.SetComputeIntParam(compute,"_NoiseOctaves",Mathf.Clamp(settings.noiseOctaves,1,5));
            cmd.SetComputeFloatParam(compute,"_NoiseAmplitude",settings.noiseAmplitude);
            cmd.SetComputeFloatParam(compute,"_NoiseWarp",settings.noiseWarp);
            cmd.SetComputeFloatParam(compute,"_NoiseAdd",settings.noiseAdd);
            cmd.SetComputeIntParam(compute,"_NoiseInvert",settings.noiseInvert ? 1 : 0);
            cmd.SetComputeIntParam(compute,"_NoiseResolution",NoiseResolution);
            int groups = (NoiseResolution+7)/8;
            cmd.DispatchCompute(compute,kernelNoise,groups,groups,groups);
            cmd.GenerateMips(noiseTexture);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            lastNoiseHash=GetNoiseSettingsHash(); noiseGenerated=true;
        }

        internal bool NoiseNeedsGeneration => compute != null && noiseVolume != null &&
            (!noiseGenerated || GetNoiseSettingsHash() != lastNoiseHash);

        internal bool ResolveNoise(ComputeCommandBuffer cmd, TextureHandle noiseHandle)
        {
            if (compute == null || noiseVolume == null) return false;
            int h = GetNoiseSettingsHash();
            if (noiseGenerated && h == lastNoiseHash) return true;
            lastNoiseHash = h;
            noiseGenerated = true;

            cmd.SetComputeTextureParam(compute, kernelNoise, "_RWNoiseTex", noiseHandle);
            cmd.SetComputeIntParam(compute, "_NoiseSeed", settings.noiseSeed);
            cmd.SetComputeIntParam(compute, "_NoiseCells", Mathf.Max(2, settings.noiseCells));
            cmd.SetComputeIntParam(compute, "_NoiseOctaves", Mathf.Clamp(settings.noiseOctaves, 1, 5));
            cmd.SetComputeFloatParam(compute, "_NoiseAmplitude", settings.noiseAmplitude);
            cmd.SetComputeFloatParam(compute, "_NoiseWarp", settings.noiseWarp);
            cmd.SetComputeFloatParam(compute, "_NoiseAdd", settings.noiseAdd);
            cmd.SetComputeIntParam(compute, "_NoiseInvert", settings.noiseInvert ? 1 : 0);
            cmd.SetComputeIntParam(compute, "_NoiseResolution", NoiseResolution);
            int groups = (NoiseResolution + 7) / 8;
            cmd.DispatchCompute(compute, kernelNoise, groups, groups, groups);
            return true;
        }

        // Matches GUNNELL_MAX_VOLUMES in GunnellSmoke.compute.
        public const int MaxVolumes = 20;

        internal GunnellSmokeVolume ResolveVolume()
        {
            if (settings.volume != null && settings.volume.isActiveAndEnabled && settings.volume.HasDensity)
                return settings.volume;
            var vols = GunnellSmokeVolume.Volumes;
            for (int i = 0; i < vols.Count; i++)
            {
                var v = vols[i];
                if (v != null && v.isActiveAndEnabled && v.HasDensity) return v;
            }
            return null;
        }

        // Select the nearest supported clouds, then use a stable slot order.
        // Raymarch integrates their joint density along each ray; whole clouds
        // are never painted over one another by camera-to-centre distance.
        private static Vector3 sortCamPos;
        private static readonly System.Comparison<GunnellSmokeVolume> sortNearToFar =
            (a, b) => (a.transform.position - sortCamPos).sqrMagnitude.CompareTo(
                      (b.transform.position - sortCamPos).sqrMagnitude);
        private static readonly System.Comparison<GunnellSmokeVolume> sortStable =
            (a, b) => a.GetEntityId().CompareTo(b.GetEntityId());
        private static int[] ResourceIDs(string prefix)
        {
            var ids=new int[MaxVolumes];
            for(int i=0;i<ids.Length;i++)ids[i]=Shader.PropertyToID(prefix+i);
            return ids;
        }
        private static readonly int[] occupancyNames = ResourceIDs("_Occupancy");
        private static readonly int[] wakeNames = ResourceIDs("_Wake");
        private static readonly int[] lightNames = ResourceIDs("_Light");

        private readonly System.Collections.Generic.List<GunnellSmokeVolume> volumeScratch =
            new System.Collections.Generic.List<GunnellSmokeVolume>(MaxVolumes);

        private readonly Plane[] frustumPlanes = new Plane[6];
        private int selectionFrame = -1, selectionMask, selectionRegistryCount;
        private Camera selectionCamera;
        private Matrix4x4 selectionView, selectionProjection;
        private readonly GunnellSmokeLightingCache lightingCache=new GunnellSmokeLightingCache();
        private readonly bool[] lightingUpdateScratch=new bool[MaxVolumes];
        private readonly Vector4[] lightingNeighbourScratch=new Vector4[MaxVolumes];
        private readonly Vector4[] sunLayoutScratch=new Vector4[MaxVolumes];
        private readonly Vector4[] localLayoutScratch=new Vector4[MaxVolumes];
        private readonly Vector4[] lightingTexelScratch=new Vector4[MaxVolumes];

        internal int ResolveVolumes(Camera camera)
        {
            if (camera == null) { volumeScratch.Clear(); return 0; }
            // Reuse only CPU selection within this camera's frame. Each graph
            // still imports resources and snapshots its own fresh TextureHandles.
            var vols = GunnellSmokeVolume.Volumes;
            if (Application.isPlaying && selectionFrame == Time.frameCount && selectionCamera == camera
                && selectionMask == camera.cullingMask && selectionRegistryCount == vols.Count
                && selectionView == camera.worldToCameraMatrix && selectionProjection == camera.projectionMatrix)
                return volumeScratch.Count;
            selectionFrame = Time.frameCount; selectionCamera = camera;
            selectionMask = camera.cullingMask; selectionRegistryCount = vols.Count;
            selectionView = camera.worldToCameraMatrix; selectionProjection = camera.projectionMatrix;
            volumeScratch.Clear();
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            for (int i = 0; i < vols.Count; i++)
            {
                var v = vols[i];
                if (v == null || !v.isActiveAndEnabled || !v.HasDensity) continue;
                if ((camera.cullingMask & (1 << v.gameObject.layer)) == 0) continue;
                var bounds = new Bounds((v.ActiveMinWorld + v.ActiveMaxWorld) * 0.5f,
                    v.ActiveMaxWorld - v.ActiveMinWorld);
                bool visible = GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                if (!visible && settings.castSceneShadows && RenderSettings.sun != null)
                {
                    // Include off-screen gas whose shadow can fall into view.
                    var projected = bounds;
                    projected.Encapsulate(new Bounds(bounds.center + RenderSettings.sun.transform.forward * 24f, bounds.size));
                    visible = GeometryUtility.TestPlanesAABB(frustumPlanes, projected);
                }
                if (visible) volumeScratch.Add(v);
            }
            sortCamPos = camera.transform.position;
            volumeScratch.Sort(sortNearToFar);
            if (volumeScratch.Count > MaxVolumes)
                volumeScratch.RemoveRange(MaxVolumes, volumeScratch.Count - MaxVolumes);
            volumeScratch.Sort(sortStable);
            return volumeScratch.Count;
        }

        private int ResolveDownsample(Camera camera,int count,int normal)
        {
            var s=settings;
            if(!s.adaptiveCrowdResolution||count<Mathf.Max(4,s.crowdResolutionThreshold)||camera==null)return normal;
            float nearSquared=s.crowdNearDistance*s.crowdNearDistance;
            foreach(var volume in volumeScratch)
            {
                var bounds=new Bounds((volume.ActiveMinWorld+volume.ActiveMaxWorld)*.5f,
                    volume.ActiveMaxWorld-volume.ActiveMinWorld);
                if(bounds.SqrDistance(camera.transform.position)<=nearSquared)
                    return Mathf.Max(normal,Mathf.Min(Mathf.Clamp(s.crowdDownsample,2,4),normal+1));
            }
            return Mathf.Max(normal,Mathf.Clamp(s.crowdDownsample,2,4));
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (raymarchPass == null || compositePass == null) return;
            if (compute == null || compositeMaterial == null) return;
            if (renderingData.cameraData.isPreviewCamera || renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            // Los agujeros ya se avanzaron en `TickBulletHolesBeforeRender` (antes
            // de grabar). NO llamar aquí a `Tick`: su `SetData` corrompe la
            // grabación del RenderGraph y el frame sale negro.
            if (ResolveVolume() == null) return;

            renderer.EnqueuePass(raymarchPass);
            renderer.EnqueuePass(sceneShadowPass);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            RenderPipelineManager.beginContextRendering -= TickBulletHolesBeforeRender;
            DisposeResources();
        }

        private void DisposeResources()
        {
            selectionFrame = -1; selectionCamera = null;
            lightingCache.Dispose();
            RenderPipelineManager.beginContextRendering -= PrewarmNoiseBeforeRender;
            raymarchPass = null;
            compositePass = null;
            sceneShadowPass = null;
            CoreUtils.Destroy(compositeMaterial);
            compositeMaterial = null;
            if (noiseVolume != null)
            {
                RTHandles.Release(noiseVolume);
                noiseVolume = null;
            }
            CoreUtils.Destroy(noiseTexture);
            noiseTexture = null;
        }

        // ------------------------------------------------------- pase compute

        private sealed class RaymarchPassData
        {
            public GunnellSmokeFeature feature;
            public GunnellSmokeLook look;
            // Copia propia por grabación: el scratch del feature se reutiliza
            // en el siguiente Record y la ejecución va por detrás.
            public readonly TextureHandle[] densities = new TextureHandle[MaxVolumes];
            public readonly TextureHandle[] lighting = new TextureHandle[MaxVolumes];
            public readonly TextureHandle[] localLighting = new TextureHandle[MaxVolumes];
            public readonly bool[] lightingUpdates = new bool[MaxVolumes];
            public readonly Vector4[] lightingNeighbours = new Vector4[MaxVolumes];
            public readonly Vector4[] sunLayouts = new Vector4[MaxVolumes];
            public readonly Vector4[] localLayouts = new Vector4[MaxVolumes];
            public readonly Vector4[] lightingTexels = new Vector4[MaxVolumes];
            public readonly TextureHandle[] wakes = new TextureHandle[MaxVolumes];
            public readonly Vector3Int[] gridRes = new Vector3Int[MaxVolumes];
            public readonly Vector3Int[] lightRes = new Vector3Int[MaxVolumes];
            public readonly Vector3Int[] localLightRes = new Vector3Int[MaxVolumes];
            public readonly Vector4[] localPositions = new Vector4[MaxLocalLights];
            public readonly Vector4[] localColors = new Vector4[MaxLocalLights];
            public readonly Vector4[] localDirections = new Vector4[MaxLocalLights];
            public readonly Vector4[] localParams = new Vector4[MaxLocalLights];
            public readonly float[] localScores = new float[MaxLocalLights];
            public int localLightCount;
            public readonly Vector3[] gridMins = new Vector3[MaxVolumes];
            public readonly float[] voxelSizes = new float[MaxVolumes];
            public readonly float[] fades = new float[MaxVolumes];
            public readonly Vector3[] origins = new Vector3[MaxVolumes];
            public readonly float[] radii = new float[MaxVolumes];
            public readonly float[] verticalScales = new float[MaxVolumes];
            public readonly float[] dissipations = new float[MaxVolumes];
            public readonly Vector4[] blastSpheres = new Vector4[MaxVolumes * GunnellSmokeVolume.MaxBlasts];
            public readonly Vector4[] blastStates = new Vector4[MaxVolumes * GunnellSmokeVolume.MaxBlasts];
            public readonly Vector4[] blastFlows = new Vector4[MaxVolumes * GunnellSmokeVolume.MaxBlasts];
            public readonly int[] blastCounts = new int[MaxVolumes];
            public readonly Vector4[] minVoxel = new Vector4[MaxVolumes];
            public readonly Vector4[] resFade = new Vector4[MaxVolumes];
            public readonly Vector4[] originRadius = new Vector4[MaxVolumes];
            public readonly Vector4[] shape = new Vector4[MaxVolumes];
            public readonly Vector4[] release = new Vector4[MaxVolumes];
            public readonly Vector4[] activeMins = new Vector4[MaxVolumes];
            public readonly Vector4[] activeMaxs = new Vector4[MaxVolumes];
            public bool voxelMode;
            public int volumeCount;
            public TextureHandle albedo;
            public TextureHandle mask;
            public TextureHandle depth;
            public TextureHandle noise;
            public TextureHandle sceneShadowAtlas;
            public TextureHandle localShadowAtlas;
            public bool localShadowsEnabled;
            public TextureHandle sceneShadow;
            public int shadowCascades;
            public bool sceneShadowsEnabled;
            public int groupsX;
            public int groupsY;
            public Vector2 bufferSize;
            // Instantánea de cámara/sol/tiempo tomada en Record (hilo
            // principal): en Execute (hilo de render) no se toca Camera.
            public Vector3 camPos;
            public Vector3 camFwd;
            public Matrix4x4 cameraToWorld;
            public Matrix4x4 gpuProj;
            public Matrix4x4 worldToCamera;
            public Vector3 sunDir;
            public Vector3 sunColor;
            public Vector3 ambientSky;
            public Vector3 ambientGround;
            public float timeSeconds;
            public int frameIndex;
        }

        private sealed class NoiseMipPassData
        {
            public TextureHandle texture;
        }

        private sealed class NoiseGenerationPassData
        {
            public GunnellSmokeFeature feature;
            public TextureHandle texture;
        }

        private Vector3Int LightingResolution(Vector3Int grid)
        {
            float scale = Mathf.Clamp(settings.lightingGridScale,0.75f,1.875f);
            return new Vector3Int(Mathf.CeilToInt(grid.x * scale),
                Mathf.CeilToInt(grid.y * scale), Mathf.CeilToInt(grid.z * scale));
        }

        private static Vector3Int LocalLightingResolution(Vector3Int grid) => new Vector3Int(
            Mathf.Clamp(Mathf.CeilToInt(grid.x * 0.85f), 1, 64),
            Mathf.Clamp(Mathf.CeilToInt(grid.y * 0.85f), 1, 64),
            Mathf.Clamp(Mathf.CeilToInt(grid.z * 0.85f), 1, 64));

        private static readonly Vector3[] ambientDirections = { Vector3.up, Vector3.down };
        private int LightingInputHash(Camera camera,UniversalLightData lights)
        {
            unchecked
            {
                int hash=GetNoiseSettingsHash();
                void Add(int value){hash=hash*31+value;}
                var s=settings;var look=s.ActiveLook;
                Add(s.absorption.GetHashCode());Add(s.scattering.GetHashCode());Add(s.interiorDetail.GetHashCode());
                Add(s.lightStepCount);Add(s.lightStepSize.GetHashCode());Add(s.shadowDensity.GetHashCode());
                Add(s.ambient.GetHashCode());Add(s.multipleScattering.GetHashCode());Add(s.receiveSceneShadows?1:0);
                Add(s.localLightStrength.GetHashCode());Add(s.localLightSteps);Add(s.maxLocalLights);Add(s.sceneAdditionalLights?1:0);
                Add((look?look.shapeDeformation:s.shapeDeformation).GetHashCode());Add((look?look.noiseWorldScale:s.noiseWorldScale).GetHashCode());
                Add((look?look.detailStrength:s.detailStrength).GetHashCode());Add((look?look.shadowDensity:s.shadowDensity).GetHashCode());
                Add((look?look.stylization:0).GetHashCode());Add((look?look.ambient:s.ambient).GetHashCode());
                Add((look?look.animationDirection:s.animationDirection).GetHashCode());Add((look?look.animationStrength:s.animationStrength).GetHashCode());
                Add((look?look.turbulenceStrength:s.turbulenceStrength).GetHashCode());Add((look?look.turbulenceSpeed:s.turbulenceSpeed).GetHashCode());
                Add((look?look.turbulenceScale:s.turbulenceScale).GetHashCode());Add((look?look.smokeColor:s.smokeColor).GetHashCode());
                Add((look?look.sunColor:s.sunColor).GetHashCode());Add(RenderSettings.ambientIntensity.GetHashCode());
                Add((int)RenderSettings.ambientMode);Add(RenderSettings.ambientLight.GetHashCode());Add(RenderSettings.ambientProbe.GetHashCode());
                Add(RenderSettings.ambientSkyColor.GetHashCode());Add(RenderSettings.ambientGroundColor.GetHashCode());
                Add(camera.projectionMatrix.GetHashCode());Add(lights.visibleLights.Length);
                for(int i=0;i<lights.visibleLights.Length;i++)
                {
                    var light=lights.visibleLights[i];Add(light.localToWorldMatrix.GetHashCode());Add(light.finalColor.GetHashCode());
                    Add(light.range.GetHashCode());Add(light.spotAngle.GetHashCode());Add(light.light?light.light.GetEntityId().GetHashCode():0);
                }
                return hash;
            }
        }
        private static readonly Color[] ambientSamples = new Color[2];
        private int LightingNeighbourHash(int index,int commonHash)
        {
            // Neither sunlight nor local transport samples farther than this
            // bounded reach. Remote clouds cannot alter this lighting history.
            var volume=volumeScratch[index];
            var bounds=new Bounds((volume.ActiveMinWorld+volume.ActiveMaxWorld)*.5f,
                volume.ActiveMaxWorld-volume.ActiveMinWorld);
            float reach=Mathf.Max(1f,settings.lightStepSize*settings.lightStepCount);
            bounds.Expand(reach*2f+.05f);
            int mask=0,hash=commonHash;
            unchecked
            {
                for(int i=0;i<volumeScratch.Count;i++)
                {
                    var neighbour=volumeScratch[i];
                    var other=new Bounds((neighbour.ActiveMinWorld+neighbour.ActiveMaxWorld)*.5f,
                        neighbour.ActiveMaxWorld-neighbour.ActiveMinWorld);
                    if(i!=index&&!bounds.Intersects(other))continue;
                    mask|=1<<i;
                    hash=hash*31+neighbour.GetEntityId().GetHashCode();
                    hash=hash*31+neighbour.GridMinWorld.GetHashCode();
                }
            }
            // All 20 bits remain exact in a float4 constant array.
            lightingNeighbourScratch[index]=new Vector4(mask,0,0,0);
            return hash;
        }
        internal static void SceneAmbient(out Vector3 sky, out Vector3 ground)
        {
            Color up, down;
            if (RenderSettings.ambientMode == AmbientMode.Flat)
            {
                up = down = RenderSettings.ambientLight.linear;
            }
            else if (RenderSettings.ambientMode == AmbientMode.Trilight)
            {
                up = RenderSettings.ambientSkyColor.linear;
                down = RenderSettings.ambientGroundColor.linear;
            }
            else
            {
                // The scene probe already carries the skybox intensity.
                RenderSettings.ambientProbe.Evaluate(ambientDirections, ambientSamples);
                up = ambientSamples[0]; down = ambientSamples[1];
            }
            // Recover the reference look's ambient fill with one exposure for
            // both hemispheres. Bound that exposure and soften the sky tint so
            // ordinary scenes produce gray smoke rather than white or blue gas.
            sky = SmokeAmbientResponse(up);
            ground = SmokeAmbientResponse(down);
        }

        private static Vector3 SmokeAmbientResponse(Color sample)
        {
            var radiance = new Vector3(Mathf.Max(0f, sample.r),
                Mathf.Max(0f, sample.g), Mathf.Max(0f, sample.b));
            float luminance = Vector3.Dot(radiance, new Vector3(0.2126f, 0.7152f, 0.0722f));
            if (luminance <= 0.00001f) return Vector3.zero;

            // The same response feeds the volume and its supplementary wisps.
            // Zero scene illumination still produces zero ambient light.
            Vector3 tint = Vector3.Lerp(Vector3.one, radiance / luminance, 0.15f);
            return tint * Mathf.Clamp01(luminance / 0.2f);
        }

        private Vector3Int LocalLightingResolution(GunnellSmokeVolume volume, UniversalLightData lightData)
        {
            if (!settings.sceneAdditionalLights || settings.maxLocalLights == 0) return Vector3Int.one;
            var bounds = new Bounds((volume.ActiveMinWorld+volume.ActiveMaxWorld)*0.5f,
                volume.ActiveMaxWorld-volume.ActiveMinWorld);
            foreach (var visible in lightData.visibleLights)
            {
                Light light = visible.light;
                if (light == null || !light.isActiveAndEnabled ||
                    (visible.lightType != LightType.Point && visible.lightType != LightType.Spot) ||
                    (light.cullingMask & (1 << volume.gameObject.layer)) == 0) continue;
                if (bounds.SqrDistance(light.transform.position) < visible.range*visible.range)
                    return LocalLightingResolution(volume.GridResolution);
            }
            return Vector3Int.one;
        }

        private void SnapshotLocalLights(RaymarchPassData data, UniversalLightData lightData)
        {
            int limit = settings.sceneAdditionalLights ? Mathf.Clamp(settings.maxLocalLights,0,MaxLocalLights) : 0;
            data.localLightCount = 0;
            if (limit == 0) return;
            for (int index=0; index<lightData.visibleLights.Length; index++)
            {
                var visible = lightData.visibleLights[index];
                Light light = visible.light;
                if (light == null || !light.isActiveAndEnabled ||
                    (visible.lightType != LightType.Point && visible.lightType != LightType.Spot)) continue;
                Vector3 position = light.transform.position;
                float range = Mathf.Max(0.01f, visible.range), nearest = float.PositiveInfinity;
                int cloudMask = 0;
                for (int cloud=0; cloud<data.volumeCount; cloud++)
                {
                    if ((light.cullingMask & (1 << volumeScratch[cloud].gameObject.layer)) == 0) continue;
                    cloudMask |= 1 << cloud;
                    var bounds = new Bounds(((Vector3)data.activeMins[cloud]+(Vector3)data.activeMaxs[cloud])*0.5f,
                        (Vector3)data.activeMaxs[cloud]-(Vector3)data.activeMins[cloud]);
                    nearest = Mathf.Min(nearest, bounds.SqrDistance(position));
                }
                if (nearest >= range * range) continue;
                Color color = visible.finalColor;
                float score = Mathf.Max(color.r,Mathf.Max(color.g,color.b)) / Mathf.Max(1f,nearest);
                if (score <= 0f) continue;
                int slot = data.localLightCount;
                if (slot == limit)
                {
                    slot = 0;
                    for (int i=1;i<limit;i++) if (data.localScores[i] < data.localScores[slot]) slot=i;
                    if (score <= data.localScores[slot]) continue;
                }
                else data.localLightCount++;
                Vector3 direction = light.transform.forward;
                float outer = visible.lightType == LightType.Spot ? Mathf.Cos(light.spotAngle * Mathf.Deg2Rad * 0.5f) : -1f;
                float inner = visible.lightType == LightType.Spot ? Mathf.Cos(light.innerSpotAngle * Mathf.Deg2Rad * 0.5f) : 1f;
                int shadowIndex = index - (lightData.mainLightIndex >= 0 && index > lightData.mainLightIndex ? 1 : 0);
                data.localScores[slot] = score;
                data.localPositions[slot] = new Vector4(position.x,position.y,position.z,range);
                data.localColors[slot] = new Vector4(color.r,color.g,color.b,cloudMask);
                data.localDirections[slot] = new Vector4(direction.x,direction.y,direction.z,outer);
                data.localParams[slot] = new Vector4(inner,visible.lightType == LightType.Spot ? 1f : 0f,
                    light.shadows != LightShadows.None && shadowIndex < lightData.additionalLightsCount ? shadowIndex : -1f,0f);
            }
        }

        private void SnapshotPassData(RaymarchPassData data, UniversalCameraData cameraData, UniversalLightData lightData,
            int volumeCount, TextureHandle noise, int width, int height)
        {
            data.feature = this;
            data.look = settings.ActiveLook;
            data.volumeCount = volumeCount;
            data.voxelMode = SmokeGameplay.SelectedSmokeStyle == 2;
            for (int i = 0; i < volumeCount; i++)
            {
                GunnellSmokeVolume v = volumeScratch[i];
                data.densities[i] = densityScratch[i];
                data.lighting[i] = lightingScratch[i];
                data.localLighting[i] = localLightingScratch[i];
                data.lightingUpdates[i] = lightingUpdateScratch[i];
                data.lightingNeighbours[i] = lightingNeighbourScratch[i];
                data.sunLayouts[i] = sunLayoutScratch[i];
                data.localLayouts[i] = localLayoutScratch[i];
                data.lightingTexels[i] = lightingTexelScratch[i];
                data.wakes[i] = wakeScratch[i];
                data.gridRes[i] = v.GridResolution;
                data.lightRes[i] = LightingResolution(v.GridResolution);
                data.localLightRes[i] = LocalLightingResolution(v,lightData);
                data.gridMins[i] = v.GridMinWorld;
                data.activeMins[i] = v.ActiveMinWorld;
                data.activeMaxs[i] = v.ActiveMaxWorld;
                data.voxelSizes[i] = v.voxelSize;
                data.fades[i] = v.DensityFade;
                data.origins[i] = v.RenderOrigin;
                data.radii[i] = v.ShapeRadius;
                data.verticalScales[i] = v.VerticalScale;
                data.dissipations[i] = v.Dissipation;
                data.blastCounts[i] = v.CopyBlasts(data.blastSpheres, data.blastStates,
                    i * GunnellSmokeVolume.MaxBlasts, data.blastFlows);
                Vector3 min = data.gridMins[i], origin = data.origins[i];
                Vector3Int res = data.gridRes[i];
                data.minVoxel[i] = new Vector4(min.x,min.y,min.z,v.voxelSize);
                data.resFade[i] = new Vector4(res.x,res.y,res.z,Mathf.Clamp01(data.fades[i]));
                data.originRadius[i] = new Vector4(origin.x,origin.y,origin.z,Mathf.Max(0.05f,data.radii[i]));
                data.shape[i] = new Vector4(data.verticalScales[i],data.dissipations[i],
                    data.wakes[i].IsValid() ? 1f : 0f,data.blastCounts[i]);
                data.release[i] = new Vector4(v.Age, v.ReleaseEnergy, v.GrowthProgress,
                    v.HasProceduralExplosionFlow ? 1f : 0f);
            }
            data.noise = noise;
            data.groupsX = (width + 7) / 8;
            data.groupsY = (height + 7) / 8;
            data.bufferSize = new Vector2(width, height);
            Camera cam = cameraData.camera;
            data.camPos = cam != null ? cam.transform.position : cameraData.worldSpaceCameraPos;
            data.camFwd = cam != null ? cam.transform.forward : Vector3.forward;
            data.cameraToWorld = cam != null ? cam.cameraToWorldMatrix : Matrix4x4.identity;
            data.gpuProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true);
            data.worldToCamera = cameraData.GetViewMatrix();
            data.timeSeconds = Time.timeSinceLevelLoad;
            data.frameIndex = Time.frameCount & 1023;
            Light sun = lightData.mainLightIndex >= 0
                ? lightData.visibleLights[lightData.mainLightIndex].light : null;
            data.sunDir = sun != null ? -sun.transform.forward
                : new Vector3(0.45f, 0.85f, -0.28f).normalized;
            Color sunTint = data.look != null ? data.look.sunColor : settings.sunColor;
            // Keep the art direction at the demo's original light intensity,
            // while responding to the actual main light's colour and brightness.
            Color environmentLight = sun != null && sun.enabled ? lightData.visibleLights[lightData.mainLightIndex].finalColor / 1.35f : Color.black;
            data.sunColor = new Vector3(sunTint.r * environmentLight.r,
                sunTint.g * environmentLight.g, sunTint.b * environmentLight.b);
            SceneAmbient(out data.ambientSky, out data.ambientGround);
            SnapshotLocalLights(data, lightData);
        }

        private void SnapshotShadowData(RaymarchPassData data, UniversalResourceData resources,
            UniversalShadowData shadows, UniversalLightData lights)
        {
            data.sceneShadowAtlas = resources.mainShadowsTexture;
            data.localShadowAtlas = resources.additionalShadowsTexture;
            data.localShadowsEnabled = data.localShadowAtlas.IsValid() && shadows.supportsAdditionalLightShadows;
            data.shadowCascades = Mathf.Clamp(shadows.mainLightShadowCascadesCount, 1, 4);
            Light light = lights.mainLightIndex >= 0 ? lights.visibleLights[lights.mainLightIndex].light : null;
            data.sceneShadowsEnabled = data.sceneShadowAtlas.IsValid() && shadows.supportsMainLightShadows
                && light != null && light.shadows != LightShadows.None;
        }

        private sealed class GunnellRaymarchPass : ScriptableRenderPass
        {
            private readonly GunnellSmokeFeature feature;

            public GunnellRaymarchPass(GunnellSmokeFeature feature)
            {
                this.feature = feature;
                // Antes que opacos: el composite (AfterTransparents) lo lee.
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                // Invalidar lo publicado el frame anterior ANTES de cualquier salida
                // temprana. Un TextureHandle sólo es válido dentro del frame en que
                // se creó; si este pase sale por abajo sin grabar y el composite sí
                // se ejecuta, se encontraría el handle del frame anterior y lo
                // enlazaría (textura liberada -> basura o error de RenderGraph).
                feature.lastAlbedo = default;
                feature.lastMask = default;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.isPreviewCamera) return;

                Camera camEarly = cameraData.camera;
                Vector3 camPosEarly = camEarly != null ? camEarly.transform.position : cameraData.worldSpaceCameraPos;
                int volumeCount = feature.ResolveVolumes(cameraData.camera);
                if (volumeCount == 0 || feature.compute == null) return;

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalLightData lights = frameData.Get<UniversalLightData>();
                UniversalShadowData shadows = frameData.Get<UniversalShadowData>();
                if (!resources.activeColorTexture.IsValid()) return;

                Settings s = feature.settings;
                GunnellSmokeParticles.FlowSettings = s;
                int factor = feature.ResolveDownsample(cameraData.camera,volumeCount,Mathf.Clamp(s.downsample,1,4));
                if (s.fullResolutionNearSmoke)
                    for (int i = 0; i < volumeCount; i++)
                        if (Vector3.Distance(camPosEarly, feature.volumeScratch[i].RenderOrigin)
                            < feature.volumeScratch[i].ShapeRadius + 2.5f) { factor = 1; break; }
                RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
                int w = Mathf.Max(8, camDesc.width / factor);
                int h = Mathf.Max(8, camDesc.height / factor);

                RenderTextureDescriptor desc = camDesc;
                desc.width = w;
                desc.height = h;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.useMipMap = false;
                desc.autoGenerateMips = false;
                desc.bindMS = false;
                // El compute los enlaza como UAV (RWTexture2D): sin este flag
                // el driver rechaza el bind y el humo no se dibuja.
                desc.enableRandomWrite = true;
                desc.colorFormat = RenderTextureFormat.ARGBHalf;

                TextureHandle albedo = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, desc, "_GunnellAlbedo", true, FilterMode.Bilinear, TextureWrapMode.Clamp);

                RenderTextureDescriptor mdesc = desc;
                mdesc.colorFormat = RenderTextureFormat.RGHalf;
                TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, mdesc, "_GunnellMask", true, FilterMode.Bilinear, TextureWrapMode.Clamp);

                TextureHandle depth = resources.cameraDepthTexture.IsValid()
                    ? resources.cameraDepthTexture
                    : resources.activeDepthTexture;
                TextureHandle noise = renderGraph.ImportTexture(feature.noiseVolume);
                bool generateNoiseMips = feature.NoiseNeedsGeneration;

                if (generateNoiseMips)
                {
                    using (var builder = renderGraph.AddComputePass<NoiseGenerationPassData>(
                        "Gunnell Smoke Worley Noise", out var data, feature.sampler))
                    {
                        builder.UseTexture(noise, AccessFlags.ReadWrite);
                        builder.AllowPassCulling(false);
                        builder.AllowGlobalStateModification(true);
                        data.feature = feature;
                        data.texture = noise;
                        builder.SetRenderFunc(static (NoiseGenerationPassData d, ComputeGraphContext ctx) =>
                        {
                            if (d.feature != null) d.feature.ResolveNoise(ctx.cmd, d.texture);
                        });
                    }

                    // El compute sólo escribe la mip 0. Generar el resto antes del
                    // raymarch evita aliasing del Worley cuando la nube ocupa pocos píxeles.
                    using (var builder = renderGraph.AddUnsafePass<NoiseMipPassData>(
                        "Gunnell Smoke Noise Mipmaps", out var data))
                    {
                        data.texture = noise;
                        builder.UseTexture(noise, AccessFlags.ReadWrite);
                        builder.SetRenderFunc(static (NoiseMipPassData d, UnsafeGraphContext ctx) =>
                        {
                            RTHandle texture = d.texture;
                            if (texture != null) ctx.cmd.GenerateMips(texture);
                        });
                    }
                }

                int lightingHash=feature.LightingInputHash(cameraData.camera,lights);
                bool anyLightingUpdate=false;
                int refreshBudget=Mathf.Clamp(s.lightingUpdatesPerFrame,1,MaxVolumes);
                if(s.adaptiveCrowdLighting && volumeCount>=Mathf.Max(4,s.crowdResolutionThreshold))
                    refreshBudget=Mathf.Min(refreshBudget,Mathf.Clamp(s.crowdLightingUpdatesPerFrame,1,MaxVolumes));
                int refreshStart=(Time.frameCount%volumeCount)*refreshBudget%volumeCount;
                for (int i = 0; i < volumeCount; i++)
                {
                    GunnellSmokeVolume v = feature.volumeScratch[i];
                    feature.wakeScratch[i] = v.WakeDensityHandle != null
                        ? renderGraph.ImportTexture(v.WakeDensityHandle) : default;
                    feature.densityScratch[i] = renderGraph.ImportTexture(v.DensityHandle);
                    Vector3Int lightRes = feature.LightingResolution(v.GridResolution);
                    Vector3Int localRes = feature.LocalLightingResolution(v,lights);
                    var cached=feature.lightingCache.Acquire(cameraData.camera,v,lightRes,localRes);
                    feature.lightingScratch[i]=renderGraph.ImportTexture(cached.sun);
                    feature.localLightingScratch[i]=feature.lightingScratch[i];
                    feature.sunLayoutScratch[i]=cached.sunLayout;
                    feature.localLayoutScratch[i]=cached.localLayout;
                    feature.lightingTexelScratch[i]=cached.halfTexel;
                    int neighbourHash=feature.LightingNeighbourHash(i,lightingHash);
                    bool allowRefresh=(i-refreshStart+volumeCount)%volumeCount<refreshBudget;
                    feature.lightingUpdateScratch[i]=GunnellSmokeLightingCache.NeedsUpdate(cached,neighbourHash,
                        Application.isPlaying&&s.cacheLighting,v.WakeDensityHandle!=null?Mathf.Max(45,s.lightingRefreshRate):s.lightingRefreshRate,
                        allowRefresh);
                    anyLightingUpdate|=feature.lightingUpdateScratch[i];
                }

                // La iluminación se calcula en espacio de mundo una vez por
                // nube y cámara, en lugar de doce marchas por muestra de vista.
                if(anyLightingUpdate)
                using (var builder = renderGraph.AddComputePass<RaymarchPassData>(
                    "Gunnell Smoke Lighting Volume", out var data, feature.lightingSampler))
                {
                    builder.UseTexture(noise, AccessFlags.Read);
                    for (int i = 0; i < volumeCount; i++)
                    {
                        builder.UseTexture(feature.densityScratch[i], AccessFlags.Read);
                        if(feature.lightingUpdateScratch[i])
                        {
                            builder.UseTexture(feature.lightingScratch[i], AccessFlags.Write);
                        }
                        if (feature.wakeScratch[i].IsValid())
                            builder.UseTexture(feature.wakeScratch[i], AccessFlags.Read);
                    }
                    feature.SnapshotPassData(data, cameraData, lights, volumeCount, noise, w, h);
                    builder.AllowPassCulling(false);
                    feature.SnapshotShadowData(data, resources, shadows, lights);
                    if (!data.sceneShadowAtlas.IsValid()) data.sceneShadowAtlas = renderGraph.defaultResources.defaultShadowTexture;
                    builder.UseTexture(data.sceneShadowAtlas, AccessFlags.Read);
                    if (!data.localShadowAtlas.IsValid()) data.localShadowAtlas = renderGraph.defaultResources.defaultShadowTexture;
                    builder.UseTexture(data.localShadowAtlas, AccessFlags.Read);
                    builder.SetRenderFunc(static (RaymarchPassData d, ComputeGraphContext ctx) =>
                    {
                        if (d.feature != null) d.feature.ExecuteLighting(ctx.cmd, d);
                    });
                }

                using (var builder = renderGraph.AddComputePass<RaymarchPassData>(
                    "Gunnell Smoke Raymarch", out var data, feature.sampler))
                {
                    builder.UseTexture(albedo, AccessFlags.Write);
                    builder.UseTexture(mask, AccessFlags.Write);
                    builder.UseTexture(noise, AccessFlags.Read);
                    for (int i = 0; i < volumeCount; i++)
                    {
                        builder.UseTexture(feature.densityScratch[i], AccessFlags.Read);
                        builder.UseTexture(feature.lightingScratch[i], AccessFlags.Read);
                        if (feature.wakeScratch[i].IsValid())
                            builder.UseTexture(feature.wakeScratch[i], AccessFlags.Read);
                    }
                    if (depth.IsValid()) builder.UseTexture(depth, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    feature.SnapshotPassData(data, cameraData, lights, volumeCount, noise, w, h);
                    data.albedo = albedo;
                    data.mask = mask;
                    data.depth = depth;

                    // Publicar para el composite (mismo frame, se graba después).
                    feature.lastAlbedo = albedo;
                    feature.lastMask = mask;
                    feature.lastBufferSize = new Vector4(w, h, 1f / w, 1f / h);

                    builder.SetRenderFunc(static (RaymarchPassData d, ComputeGraphContext ctx) =>
                    {
                        if (d.feature == null) return;
                        d.feature.ExecuteRaymarch(ctx.cmd, d);
                    });
                }
            }
        }

        internal TextureHandle lastAlbedo;
        internal TextureHandle lastMask;
        private Vector4 lastBufferSize;

        private void SetCommonParameters(ComputeCommandBuffer cmd, RaymarchPassData d, int kernel)
        {
            Settings s = settings;
            GunnellSmokeLook look = d.look;
            cmd.SetComputeIntParam(compute, "_VoxelMode", d.voxelMode ? 1 : 0);

            cmd.SetComputeMatrixParam(compute, "_CameraToWorld", d.cameraToWorld);
            cmd.SetComputeMatrixParam(compute, "_CameraInvProjection", d.gpuProj.inverse);
            Matrix4x4 vp = d.gpuProj * d.worldToCamera;
            cmd.SetComputeMatrixParam(compute, "_CameraInvViewProj", vp.inverse);
            cmd.SetComputeVectorParam(compute, "_CameraWorldPos", d.camPos);
            cmd.SetComputeVectorParam(compute, "_CameraForward", d.camFwd);

            cmd.SetComputeTextureParam(compute, kernel, "_NoiseTex", d.noise);

            // Existing debug count; rendering samples the shared transported density.
            cmd.SetComputeIntParam(compute, "_BulletHoleCount", GunnellBulletHoles.Count);
            cmd.SetComputeVectorArrayParam(compute, "_BlastSpheres", d.blastSpheres);
            cmd.SetComputeVectorArrayParam(compute, "_BlastStates", d.blastStates);
            cmd.SetComputeVectorArrayParam(compute, "_BlastFlows", d.blastFlows);
            cmd.SetComputeFloatParam(compute, "_BulletHoleDepth", GunnellBulletHoles.depth);

            int viewSteps = Mathf.Clamp(s.stepCount, 8, 256);
            // A joint interval can be longer than one cloud. Keep its sample
            // spacing fine enough at intersections, particularly for cel bands.
            // Ray steps are budgeted along the covered union in the shader.
            // Separate distant clouds do not justify forcing the maximum budget.
            if (d.volumeCount > 1) viewSteps = Mathf.Max(viewSteps, Mathf.Min(256,s.stepCount+32));
            if(s.adaptiveCrowdResolution && d.volumeCount>=Mathf.Max(4,s.crowdResolutionThreshold))
                viewSteps=Mathf.Min(viewSteps,Mathf.Clamp(s.crowdViewSteps,64,192));
            if (s.fullResolutionNearSmoke)
                for (int i = 0; i < d.volumeCount; i++)
                    if (Vector3.Distance(d.camPos, d.origins[i]) < d.radii[i] + 2.5f)
                    { viewSteps = Mathf.Max(viewSteps, 256); break; }
            cmd.SetComputeIntParam(compute, "_StepCount", viewSteps);
            cmd.SetComputeIntParam(compute, "_SceneShadowSteps", s.adaptiveCrowdResolution
                && d.volumeCount>=Mathf.Max(4,s.crowdResolutionThreshold) ? Mathf.Clamp(s.crowdShadowSteps,12,48) : 48);
            cmd.SetComputeIntParam(compute, "_OverlapDetailLimit", s.adaptiveOverlapShading
                && d.volumeCount >= Mathf.Max(4,s.crowdResolutionThreshold) ? 2 : 0);
            cmd.SetComputeIntParam(compute, "_NoiseResolution", NoiseResolution);
            cmd.SetComputeFloatParam(compute, "_StepSize", s.stepSize);
            cmd.SetComputeFloatParam(compute, "_InteriorDetail", s.interiorDetail);
            cmd.SetComputeIntParam(compute, "_LightStepCount", Mathf.Clamp(s.lightStepCount, 1, 16));
            cmd.SetComputeFloatParam(compute, "_LightStepSize", s.lightStepSize);
            // Coeficientes por metro. El compute integra la longitud real de
            // cada segmento, incluyendo el paso adaptativo y el último segmento.
            cmd.SetComputeFloatParam(compute, "_VolumeDensity", s.volumeDensity);
            cmd.SetComputeFloatParam(compute, "_Absorption", s.absorption);
            cmd.SetComputeFloatParam(compute, "_Scattering", s.scattering);
            cmd.SetComputeFloatParam(compute, "_ShadowDensity", look != null ? look.shadowDensity : s.shadowDensity);
            cmd.SetComputeFloatParam(compute, "_PhaseG", Mathf.Clamp(s.anisotropy, -0.9f, 0.9f));
            cmd.SetComputeIntParam(compute, "_PhaseFunc", s.phaseFunction == 2 ? 2 : 0);
            // _NoiseErode se reusa como _DensityFalloff del ref: el ref pasa
            // (1 - densityFalloff) al shader y lo usa como umbral del smoothstep
            // que decide donde se disuelve la superficie de la bola. Un valor
            // mayor -> la nube se "come" antes (borde mas suave). Lo invertimos
            // aqui para que el control del editor siga leyendose como "erosion".
            cmd.SetComputeFloatParam(compute, "_DensityFalloff", Mathf.Clamp01(look != null ? look.densityFalloff : 1.0f - s.noiseErode));
            cmd.SetComputeFloatParam(compute, "_ShapeDeformation", Mathf.Clamp01(look != null ? look.shapeDeformation : s.shapeDeformation));
            cmd.SetComputeFloatParam(compute, "_NoiseWorldScale", Mathf.Max(0.01f, look != null ? look.noiseWorldScale : s.noiseWorldScale));
            cmd.SetComputeFloatParam(compute, "_DetailStrength", Mathf.Clamp01(look != null ? look.detailStrength : s.detailStrength));
            cmd.SetComputeFloatParam(compute, "_LobeContrast", Mathf.Clamp01(look != null ? look.lobeContrast : s.lobeContrast));
            cmd.SetComputeFloatParam(compute, "_Ambient", Mathf.Max(0f, look != null ? look.ambient : s.ambient));
            Color sky = look != null ? look.skyAmbientTint : s.skyAmbientTint;
            Color ground = look != null ? look.groundAmbientTint : s.groundAmbientTint;
            cmd.SetComputeVectorParam(compute, "_SkyAmbientTint", Vector3.Scale(new Vector3(sky.r, sky.g, sky.b), d.ambientSky));
            cmd.SetComputeVectorParam(compute, "_GroundAmbientTint", Vector3.Scale(new Vector3(ground.r, ground.g, ground.b), d.ambientGround));
            cmd.SetComputeFloatParam(compute, "_MultipleScattering", Mathf.Clamp01(look != null ? look.multipleScattering : s.multipleScattering));
            cmd.SetComputeFloatParam(compute, "_PowderStrength", Mathf.Clamp01(look != null ? look.powderStrength : s.powderStrength));
            cmd.SetComputeFloatParam(compute, "_Stylization", look != null ? Mathf.Clamp01(look.stylization) : 0f);
            Color shadow = look != null ? look.shadowColor : s.smokeColor;
            Color highlight = look != null ? look.highlightColor : s.smokeColor;
            cmd.SetComputeVectorParam(compute, "_StylizedShadow", new Vector3(shadow.r, shadow.g, shadow.b));
            cmd.SetComputeVectorParam(compute, "_StylizedHighlight", new Vector3(highlight.r, highlight.g, highlight.b));
            cmd.SetComputeFloatParam(compute, "_ExtinctionCoeff",
                Mathf.Max(1e-3f, s.absorption + s.scattering));
            // Ref: extinctionColor = (1,1,1) -> tau/_ExtinctionColor = tau. Se mantiene
            // como vector para que el port del atenuador sea literal.
            cmd.SetComputeVectorParam(compute, "_ExtinctionColor", Vector3.one);
            cmd.SetComputeFloatParam(compute, "_AnimationStrength", look != null ? look.animationStrength : s.animationStrength);
            cmd.SetComputeVectorParam(compute, "_AnimationDirection", look != null ? look.animationDirection : s.animationDirection);
            cmd.SetComputeFloatParam(compute, "_TurbulenceStrength", Mathf.Clamp(look != null ? look.turbulenceStrength : s.turbulenceStrength, 0f, 0.8f));
            cmd.SetComputeFloatParam(compute, "_TurbulenceScale", Mathf.Max(0.3f, look != null ? look.turbulenceScale : s.turbulenceScale));
            cmd.SetComputeFloatParam(compute, "_TurbulenceSpeed", Mathf.Max(0f, look != null ? look.turbulenceSpeed : s.turbulenceSpeed));
            cmd.SetComputeFloatParam(compute, "_TimeSeconds", d.timeSeconds);
            cmd.SetComputeFloatParam(compute, "_AlphaThreshold", s.alphaThreshold);
            cmd.SetComputeIntParam(compute, "_FrameIndex", d.frameIndex);
            cmd.SetComputeIntParam(compute, "_DebugForce", debugForce);
            cmd.SetComputeIntParam(compute, "_SceneShadowsEnabled", d.sceneShadowsEnabled ? 1 : 0);
            cmd.SetComputeIntParam(compute, "_SceneShadowCascadeCount", d.shadowCascades);
            cmd.SetComputeIntParam(compute, "_ReceiveSceneShadows", s.receiveSceneShadows ? 1 : 0);
            cmd.SetComputeFloatParam(compute, "_SmokeCastShadowStrength", s.castShadowStrength);
            if (kernel == kernelLighting || kernel == kernelSceneShadow)
                cmd.SetComputeTextureParam(compute, kernel, "_MainLightShadowmapTexture",
                    d.sceneShadowAtlas);
            cmd.SetComputeVectorParam(compute, "_BufferSize",
                d.bufferSize);

            cmd.SetComputeVectorParam(compute, "_SunDirection", d.sunDir);
            cmd.SetComputeVectorParam(compute, "_SunColor", d.sunColor);
            cmd.SetComputeIntParam(compute, "_LocalLightCount", d.localLightCount);
            cmd.SetComputeFloatParam(compute, "_LocalLightStrength", s.localLightStrength);
            cmd.SetComputeIntParam(compute, "_LocalLightSteps", Mathf.Clamp(s.localLightSteps,1,8));
            cmd.SetComputeIntParam(compute, "_LocalShadowsEnabled", d.localShadowsEnabled && s.receiveSceneShadows ? 1 : 0);
            cmd.SetComputeVectorArrayParam(compute, "_LocalPositions", d.localPositions);
            cmd.SetComputeVectorArrayParam(compute, "_LocalColors", d.localColors);
            cmd.SetComputeVectorArrayParam(compute, "_LocalDirections", d.localDirections);
            cmd.SetComputeVectorArrayParam(compute, "_LocalParams", d.localParams);
            if (kernel == kernelLocalLighting)
                cmd.SetComputeTextureParam(compute, kernel, "_AdditionalLightsShadowmapTexture", d.localShadowAtlas);
            Color smokeTint = look != null ? look.smokeColor : s.smokeColor;
            cmd.SetComputeVectorParam(compute, "_SmokeColor",
                new Vector3(smokeTint.r, smokeTint.g, smokeTint.b));

        }

        private void SetVolumeResources(ComputeCommandBuffer cmd, RaymarchPassData d, int kernel)
        {
            cmd.SetComputeIntParam(compute, "_VolumeCount", d.volumeCount);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeMinVoxel", d.minVoxel);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeResFade", d.resFade);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeOriginRadius", d.originRadius);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeShape", d.shape);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeRelease", d.release);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeActiveMin", d.activeMins);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeActiveMax", d.activeMaxs);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeLightingNeighbours", d.lightingNeighbours);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeSunLayout", d.sunLayouts);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeLocalLayout", d.localLayouts);
            cmd.SetComputeVectorArrayParam(compute, "_VolumeLightingTexel", d.lightingTexels);
            // Bind every shader resource even for unused slots. Count gates
            // their reads; the first volume is a valid harmless fallback.
            for (int i = 0; i < MaxVolumes; i++)
            {
                int slot = i < d.volumeCount ? i : 0;
                cmd.SetComputeTextureParam(compute, kernel, occupancyNames[i], d.densities[slot]);
                cmd.SetComputeTextureParam(compute, kernel, wakeNames[i], d.wakes[slot].IsValid()
                    ? d.wakes[slot] : d.densities[slot]);
                if (kernel == kernelRaymarch)
                {
                    cmd.SetComputeTextureParam(compute, kernel, lightNames[i], d.lighting[slot]);
                }
            }
        }

        private void ExecuteLighting(ComputeCommandBuffer cmd, RaymarchPassData d)
        {
            SetCommonParameters(cmd, d, kernelLighting);
            SetVolumeResources(cmd, d, kernelLighting);
            for (int i = 0; i < d.volumeCount; i++)
            {
                if(!d.lightingUpdates[i])continue;
                cmd.SetComputeIntParam(compute, "_LightingIndex", i);
                Vector3Int res = d.lightRes[i];
                cmd.SetComputeVectorParam(compute, "_LightGridRes", new Vector3(res.x, res.y, res.z));
                cmd.SetComputeTextureParam(compute, kernelLighting, "_RWLightVolume", d.lighting[i]);
                cmd.DispatchCompute(compute, kernelLighting, (res.x + 3) / 4, (res.y + 3) / 4, (res.z + 3) / 4);
            }
            SetCommonParameters(cmd, d, kernelLocalLighting);
            SetVolumeResources(cmd, d, kernelLocalLighting);
            for (int i=0;i<d.volumeCount;i++)
            {
                if(!d.lightingUpdates[i])continue;
                cmd.SetComputeIntParam(compute,"_LightingIndex",i);
                Vector3Int res = d.localLightRes[i];
                cmd.SetComputeVectorParam(compute,"_LocalGridRes",new Vector3(res.x,res.y,res.z));
                cmd.SetComputeIntParam(compute,"_LocalLightZOffset",d.lightRes[i].z);
                cmd.SetComputeTextureParam(compute,kernelLocalLighting,"_RWLocalLight",d.localLighting[i]);
                cmd.DispatchCompute(compute,kernelLocalLighting,(res.x+3)/4,(res.y+3)/4,(res.z+3)/4);
            }
        }

        private void ExecuteRaymarch(ComputeCommandBuffer cmd, RaymarchPassData d)
        {
            SetCommonParameters(cmd, d, kernelRaymarch);
            SetVolumeResources(cmd, d, kernelRaymarch);
            if (d.depth.IsValid()) cmd.SetComputeTextureParam(compute, kernelRaymarch, "_DepthTex", d.depth);
            cmd.SetComputeTextureParam(compute, kernelRaymarch, "_SmokeAlbedo", d.albedo);
            cmd.SetComputeTextureParam(compute, kernelRaymarch, "_SmokeMask", d.mask);
            cmd.DispatchCompute(compute, kernelRaymarch, d.groupsX, d.groupsY, 1);
        }

        // ------------------------------------------------------ pase composite

        private struct ParticleDraw
        {
            public ParticleSystemRenderer wisps,nozzle;
            public TextureHandle localLight;
            public Vector4 localLayout,halfTexel;
        }

        private sealed class CompositePassData
        {
            public readonly GunnellSmokeVolume[] volumes = new GunnellSmokeVolume[MaxVolumes];
            public readonly TextureHandle[] localLighting = new TextureHandle[MaxVolumes];
            public readonly System.Collections.Generic.List<ParticleDraw> particleDraws =
                new System.Collections.Generic.List<ParticleDraw>(MaxVolumes);
            public int volumeCount;
            public int cullingMask;
            public bool voxelMode;
            public Material material;
            public TextureHandle albedo;
            public TextureHandle mask;
            public TextureHandle depth;
            public Vector4 bufferSize;
        }

        // Sin inicializador inline: en un ScriptableObject, crear objetos nativos
        // en el inicializador estático revienta con TypeInitializationException
        // y rompe la creación del renderer. Se crea perezoso en Record (hilo
        // principal, permitido).
        private static MaterialPropertyBlock compositeBlock;
        private static MaterialPropertyBlock wispCompositeBlock;
        private static MaterialPropertyBlock shadowCompositeBlock;

        private sealed class GunnellSceneShadowPass : ScriptableRenderPass
        {
            private readonly GunnellSmokeFeature feature;
            public GunnellSceneShadowPass(GunnellSmokeFeature feature)
            {
                this.feature = feature;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                if (!feature.settings.castSceneShadows || feature.settings.castShadowStrength <= 0f
                    || !feature.lastAlbedo.IsValid()) return;
                var camera = frameData.Get<UniversalCameraData>();
                var resources = frameData.Get<UniversalResourceData>();
                var lights = frameData.Get<UniversalLightData>();
                if (lights.mainLightIndex < 0 || !resources.activeColorTexture.IsValid()) return;
                int count = feature.ResolveVolumes(camera.camera);
                if (count == 0) return;
                var depth = resources.cameraDepthTexture.IsValid() ? resources.cameraDepthTexture : resources.activeDepthTexture;
                if (!depth.IsValid()) return;
                int factor=feature.ResolveDownsample(camera.camera,count,2);
                int width = Mathf.Max(8, camera.cameraTargetDescriptor.width / factor);
                int height = Mathf.Max(8, camera.cameraTargetDescriptor.height / factor);
                var shadow = graph.CreateTexture(new TextureDesc(width, height)
                {
                    name = "Gunnell Smoke Receiver Shadows", colorFormat = GraphicsFormat.R16G16_SFloat,
                    enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                });
                for (int i = 0; i < count; i++)
                {
                    var v = feature.volumeScratch[i];
                    feature.densityScratch[i] = graph.ImportTexture(v.DensityHandle);
                    feature.wakeScratch[i] = v.WakeDensityHandle != null ? graph.ImportTexture(v.WakeDensityHandle) : default;
                }
                var noise = graph.ImportTexture(feature.noiseVolume);
                using (var builder = graph.AddComputePass<RaymarchPassData>(
                    "Gunnell Smoke Projected Shadows", out var data, feature.shadowSampler))
                {
                    feature.SnapshotPassData(data, camera, lights, count, noise, width, height);
                    feature.SnapshotShadowData(data, resources, frameData.Get<UniversalShadowData>(), lights);
                    if (!data.sceneShadowAtlas.IsValid()) data.sceneShadowAtlas = graph.defaultResources.defaultShadowTexture;
                    data.depth = depth; data.sceneShadow = shadow; data.albedo = feature.lastAlbedo;
                    builder.UseTexture(depth, AccessFlags.Read);
                    builder.UseTexture(noise, AccessFlags.Read);
                    builder.UseTexture(shadow, AccessFlags.Write);
                    builder.UseTexture(data.albedo, AccessFlags.Read);
                    builder.UseTexture(data.sceneShadowAtlas, AccessFlags.Read);
                    for (int i = 0; i < count; i++)
                    {
                        builder.UseTexture(data.densities[i], AccessFlags.Read);
                        if (data.wakes[i].IsValid()) builder.UseTexture(data.wakes[i], AccessFlags.Read);
                    }
                    builder.SetRenderFunc(static (RaymarchPassData d, ComputeGraphContext ctx) =>
                    {
                        var f = d.feature;
                        f.SetCommonParameters(ctx.cmd, d, f.kernelSceneShadow);
                        f.SetVolumeResources(ctx.cmd, d, f.kernelSceneShadow);
                        ctx.cmd.SetComputeTextureParam(f.compute, f.kernelSceneShadow, "_DepthTex", d.depth);
                        ctx.cmd.SetComputeTextureParam(f.compute, f.kernelSceneShadow, "_SmokeSceneShadow", d.sceneShadow);
                        ctx.cmd.SetComputeTextureParam(f.compute, f.kernelSceneShadow, "_ViewSmokeAlbedo", d.albedo);
                        ctx.cmd.DispatchCompute(f.compute, f.kernelSceneShadow, d.groupsX, d.groupsY, 1);
                    });
                }
                using (var builder = graph.AddRasterRenderPass<CompositePassData>(
                    "Gunnell Smoke Shadow Composite", out var data, feature.shadowSampler))
                {
                    data.material = feature.compositeMaterial; data.mask = shadow; data.depth = depth;
                    data.bufferSize = new Vector4(width,height,1f/width,1f/height);
                    builder.UseTexture(shadow, AccessFlags.Read);
                    builder.UseTexture(depth, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
                    {
                        shadowCompositeBlock ??= new MaterialPropertyBlock();
                        shadowCompositeBlock.Clear();
                        shadowCompositeBlock.SetTexture("_GunnellSceneShadow", (Texture)d.mask);
                        shadowCompositeBlock.SetVector("_GunnellBufferSize", d.bufferSize);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity,d.material,1,MeshTopology.Triangles,3,1,shadowCompositeBlock);
                    });
                }
            }
        }

        private sealed class GunnellCompositePass : ScriptableRenderPass
        {
            private readonly GunnellSmokeFeature feature;

            public GunnellCompositePass(GunnellSmokeFeature feature)
            {
                this.feature = feature;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!feature.lastAlbedo.IsValid() || !feature.lastMask.IsValid()) return;
                compositeBlock ??= new MaterialPropertyBlock();
                wispCompositeBlock ??= new MaterialPropertyBlock();
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (!resources.activeColorTexture.IsValid()) return;

                TextureHandle depth = resources.cameraDepthTexture.IsValid()
                    ? resources.cameraDepthTexture
                    : resources.activeDepthTexture;

                using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                    "Gunnell Smoke Composite", out var data, feature.sampler))
                {
                    data.material = feature.compositeMaterial;
                    data.voxelMode = SmokeGameplay.SelectedSmokeStyle == 2;
                    data.albedo = feature.lastAlbedo;
                    data.mask = feature.lastMask;
                    data.depth = depth;
                    data.bufferSize = feature.lastBufferSize;
                    data.cullingMask = frameData.Get<UniversalCameraData>().camera.cullingMask;
                    data.volumeCount = feature.ResolveVolumes(frameData.Get<UniversalCameraData>().camera);
                    for (int i=0;i<data.volumeCount;i++)
                    {
                        data.volumes[i] = feature.volumeScratch[i];
                        data.localLighting[i] = feature.localLightingScratch[i];
                        builder.UseTexture(data.localLighting[i], AccessFlags.Read);
                    }

                    builder.UseTexture(data.albedo, AccessFlags.Read);
                    builder.UseTexture(data.mask, AccessFlags.Read);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    data.particleDraws.Clear();
                    if(!data.voxelMode)
                    foreach (var volume in GunnellSmokeVolume.Volumes)
                    {
                        if(volume==null||(data.cullingMask&(1<<volume.gameObject.layer))==0)continue;
                        var particles=volume.Particles;
                        if(particles==null||particles.ParticleCount==0)continue;
                        var wisps=particles.WispRenderer;
                        var nozzle=particles.NozzleRenderer;
                        // DrawRenderer bypasses the ordinary renderer culling.
                        // Snapshot only cards whose actual world bounds are visible.
                        if(wisps!=null&&!GeometryUtility.TestPlanesAABB(feature.frustumPlanes,wisps.bounds))wisps=null;
                        if(nozzle!=null&&!GeometryUtility.TestPlanesAABB(feature.frustumPlanes,nozzle.bounds))nozzle=null;
                        if(wisps==null&&nozzle==null)continue;
                        TextureHandle localLight=default;
                        Vector4 localLayout=default,halfTexel=default;
                        for(int i=0;i<data.volumeCount;i++)
                            if(data.volumes[i]==volume)
                            {
                                localLight=data.localLighting[i];localLayout=feature.localLayoutScratch[i];
                                halfTexel=feature.lightingTexelScratch[i];break;
                            }
                        data.particleDraws.Add(new ParticleDraw{wisps=wisps,nozzle=nozzle,localLight=localLight,
                            localLayout=localLayout,halfTexel=halfTexel});
                        if(volume.DensityHandle!=null)
                            builder.UseTexture(renderGraph.ImportTexture(volume.DensityHandle),AccessFlags.Read);
                    }
                    if (depth.IsValid()) builder.UseTexture(depth, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (CompositePassData d, RasterGraphContext ctx) =>
                    {
                        if (d.material == null) return;
                        compositeBlock.Clear();
                        compositeBlock.SetTexture("_GunnellAlbedo", (Texture)d.albedo);
                        compositeBlock.SetTexture("_GunnellMask", (Texture)d.mask);
                        compositeBlock.SetVector("_GunnellBufferSize", d.bufferSize);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0,
                            MeshTopology.Triangles, 3, 1, compositeBlock);
                        if (d.voxelMode) return;
                        // Wisps blend after the gas. Bind this camera's current
                        // graph textures so dense gas also occludes embedded cards.
                        foreach (var draw in d.particleDraws)
                        {
                            var wisps = draw.wisps;
                            var nozzle = draw.nozzle;
                            Texture localLight = draw.localLight.IsValid() ? (Texture)draw.localLight : null;
                            if (wisps != null && wisps.enabled && wisps.gameObject.activeInHierarchy)
                            {
                                wisps.GetPropertyBlock(wispCompositeBlock);
                                wispCompositeBlock.SetVector("_WispLocalLightLayout",draw.localLayout);
                                wispCompositeBlock.SetVector("_WispLocalLightTexel",draw.halfTexel);
                                wispCompositeBlock.SetFloat("_WispLocalLightReady",localLight != null ? 1f : 0f);
                                if (localLight != null) wispCompositeBlock.SetTexture("_WispLocalLight",localLight);
                                if (wisps.sharedMaterial != null && wisps.sharedMaterial.IsKeywordEnabled("_WISP_GAS_COMPOSITE"))
                                {
                                    wispCompositeBlock.SetTexture("_WispGasAlbedo", (Texture)d.albedo);
                                    wispCompositeBlock.SetTexture("_WispGasMask", (Texture)d.mask);
                                    wispCompositeBlock.SetFloat("_WispGasReady", 1f);
                                }
                                wisps.SetPropertyBlock(wispCompositeBlock);
                                ctx.cmd.DrawRenderer(wisps, wisps.sharedMaterial, 0, 0);
                            }
                            if (nozzle != null && nozzle.enabled && nozzle.gameObject.activeInHierarchy)
                            {
                                nozzle.GetPropertyBlock(wispCompositeBlock);
                                wispCompositeBlock.SetVector("_WispLocalLightLayout",draw.localLayout);
                                wispCompositeBlock.SetVector("_WispLocalLightTexel",draw.halfTexel);
                                wispCompositeBlock.SetFloat("_WispLocalLightReady",localLight != null ? 1f : 0f);
                                if (localLight != null) wispCompositeBlock.SetTexture("_WispLocalLight",localLight);
                                nozzle.SetPropertyBlock(wispCompositeBlock);
                                ctx.cmd.DrawRenderer(nozzle, nozzle.sharedMaterial, 0, 0);
                            }
                        }
                    });
                }
            }
        }
    }
}
