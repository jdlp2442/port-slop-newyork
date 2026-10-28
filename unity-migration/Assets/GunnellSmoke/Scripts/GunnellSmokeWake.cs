using UnityEngine;
using UnityEngine.Rendering;

namespace GunnellSmoke
{
    /// <summary>
    /// Persistent local density fraction and velocity. One shared solver per cloud;
    /// shots inject once and the surrounding gas transports/mixes the cleared wake.
    /// The base cloud shape and collision field remain authoritative.
    /// </summary>
    public sealed class GunnellSmokeWake : System.IDisposable
    {
        private readonly GunnellSmokeVolume volume;
        private ComputeShader shader;
        private readonly RenderTexture[] density = new RenderTexture[2];
        private readonly RenderTexture[] velocity = new RenderTexture[2];
        private readonly RenderTexture[] pressure = new RenderTexture[2];
        private readonly RTHandle[] densityHandles = new RTHandle[2];
        private RenderTexture divergence;
        private RenderTexture predicted;
        private RenderTexture vorticity;
        private RenderTexture densityTiles, expandedDensityTiles;
        private Vector3Int tileRes;
        private readonly int[] tileDimensions = new int[3];
        private Vector3Int res, flowRes;
        private Vector3 size, cell, flowCell;
        private readonly int[] dimensions = new int[3];
        private readonly int[] dispatchMin = new int[3], dispatchSize = new int[3];
        private int d, v, p, lastInjectionTick = -1;
        private int clear, clearDensity, inject, injectVelocity, injectExplosion, injectExplosionVelocity;
        private int advectVelocity, divergenceKernel, pressureKernel, project, advectDensity, correctDensity;
        private int computeVorticity, confineVorticity;
        private int classifyDensityTiles, expandDensityTiles;
        private int injectMotion, injectMotionVelocity;
        private struct Motion
        {
            public int source, kind;
            public Vector3 a, b, flow, sweep;
            public float radius, clearing, duration, seed, queuedAt;
        }
        // One shared flow field per cloud. Coalesce repeated samples from each
        // actor; crowds never allocate one solver or a growing queue per actor.
        private readonly Motion[] motions = new Motion[32];
        private int motionCount, motionCursor;
        private float accumulator, time, remaining, blastRemaining, vorticityRemaining;
        private int blastCursor;
        private struct PendingBlast
        {
            public Vector4 centerRadius;
            public Vector4 flowStrength;
            public int seed;
            public float reach;
            public float elapsed;
            public bool pending;
        }
        private readonly PendingBlast[] blasts = new PendingBlast[GunnellSmokeVolume.MaxBlasts];
        private readonly Vector4[] recoverySpheres = new Vector4[GunnellSmokeVolume.MaxBlasts];
        private readonly Vector4[] recoveryFlows = new Vector4[GunnellSmokeVolume.MaxBlasts];
        private readonly Vector4[] recoveryStates = new Vector4[GunnellSmokeVolume.MaxBlasts];
        private const float BlastOpeningDuration = 0.50f;
        private const float BlastResponseDuration = 7.2f;
        private const float StepTime = 1f / 30f;
        public RTHandle DensityHandle => Active ? densityHandles[d] : null;
        public bool Active => density[0] != null && remaining > 0;
        public Vector3Int Resolution => res;
        public bool HasExplosionResponse => Active && (blastRemaining > 0f || HasPendingBlast());

        private bool HasPendingBlast()
        {
            for (int i = 0; i < blasts.Length; i++)
                if (blasts[i].pending) return true;
            return false;
        }

        public GunnellSmokeWake(GunnellSmokeVolume owner) { volume = owner; }

        private RenderTexture Allocate(string name, RenderTextureFormat format, Vector3Int resolution)
        {
            var texture = new RenderTexture(resolution.x, resolution.y, 0, format)
            {
                name = name, dimension = TextureDimension.Tex3D, volumeDepth = resolution.z,
                enableRandomWrite = true, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        private bool Ensure()
        {
            if (density[0] != null) return true;
            shader = Resources.Load<ComputeShader>("GunnellSmokeWake");
            if (shader == null) return false;
            size = volume.GridMaxWorld - volume.GridMinWorld;
            // Resolve the ~0.35 m bullet wake, within a bounded per-cloud budget.
            float voxel = 0.12f;
            float cells = size.x * size.y * size.z / (voxel * voxel * voxel);
            if (cells > 900000f) voxel *= Mathf.Pow(cells / 900000f, 1f / 3f);
            res = new Vector3Int(Mathf.Clamp(Mathf.CeilToInt(size.x / voxel), 4, 128),
                Mathf.Clamp(Mathf.CeilToInt(size.y / voxel), 4, 128),
                Mathf.Clamp(Mathf.CeilToInt(size.z / voxel), 4, 128));
            cell = new Vector3(size.x / res.x, size.y / res.y, size.z / res.z);
            flowRes = new Vector3Int(Mathf.Max(4,(res.x+2)/3), Mathf.Max(4,(res.y+2)/3), Mathf.Max(4,(res.z+2)/3));
            flowCell = new Vector3(size.x / flowRes.x, size.y / flowRes.y, size.z / flowRes.z);
            dimensions[0] = res.x; dimensions[1] = res.y; dimensions[2] = res.z;
            clear = shader.FindKernel("Clear"); inject = shader.FindKernel("Inject");
            clearDensity = shader.FindKernel("ClearDensity"); injectVelocity = shader.FindKernel("InjectVelocity");
            injectExplosion = shader.FindKernel("InjectExplosion");
            injectExplosionVelocity = shader.FindKernel("InjectExplosionVelocity");
            injectMotion = shader.FindKernel("InjectMotion");
            injectMotionVelocity = shader.FindKernel("InjectMotionVelocity");
            advectVelocity = shader.FindKernel("AdvectVelocity"); divergenceKernel = shader.FindKernel("Divergence");
            pressureKernel = shader.FindKernel("Pressure"); project = shader.FindKernel("Project");
            advectDensity = shader.FindKernel("AdvectDensity");
            correctDensity = shader.FindKernel("CorrectDensity");
            computeVorticity = shader.FindKernel("ComputeVorticity");
            confineVorticity = shader.FindKernel("ConfineVorticity");
            classifyDensityTiles = shader.FindKernel("ClassifyDensityTiles");
            expandDensityTiles = shader.FindKernel("ExpandDensityTiles");
            tileRes = new Vector3Int((res.x+7)/8,(res.y+7)/8,(res.z+7)/8);
            tileDimensions[0]=tileRes.x; tileDimensions[1]=tileRes.y; tileDimensions[2]=tileRes.z;
            densityTiles = Allocate("Smoke wake occupied tiles",RenderTextureFormat.RFloat,tileRes);
            expandedDensityTiles = Allocate("Smoke wake tile halo",RenderTextureFormat.RFloat,tileRes);
            for (int i = 0; i < 2; i++)
            {
                // x: density fraction; y: deficit-weighted mixing age. Keeping
                // age in the transported field lets old wakes recover while
                // new shots elsewhere continue to disturb this cloud.
                density[i] = Allocate("Smoke wake density and mixing age", RenderTextureFormat.RGHalf, res);
                velocity[i] = Allocate("Smoke wake velocity", RenderTextureFormat.ARGBHalf, flowRes);
                pressure[i] = Allocate("Smoke wake pressure", RenderTextureFormat.RHalf, flowRes);
                densityHandles[i] = RTHandles.Alloc(density[i]);
            }
            divergence = Allocate("Smoke wake divergence", RenderTextureFormat.RHalf, flowRes);
            predicted = Allocate("Smoke wake transport prediction", RenderTextureFormat.RGHalf, res);
            return true;
        }

        private void Parameters(GunnellSmokeFeature.Settings settings)
        {
            shader.SetInts("_Resolution", dimensions);
            shader.SetVector("_Min", volume.GridMinWorld);
            shader.SetVector("_Size", size); shader.SetVector("_Cell", cell);
            // Coordinate advection has the opposite sign to world-space gas motion.
            var look = settings != null ? settings.ActiveLook : null;
            Vector3 wind = settings != null ? -settings.animationDirection * settings.animationStrength : Vector3.up * 0.1f;
            if (look != null) wind = -look.animationDirection * look.animationStrength;
            shader.SetVector("_Wind", wind);
            shader.SetVector("_FlowOrigin", volume.RenderOrigin);
            shader.SetVector("_FlowParameters", new Vector4(
                look != null ? look.turbulenceStrength : settings != null ? settings.turbulenceStrength : .4f,
                look != null ? look.turbulenceScale : settings != null ? settings.turbulenceScale : 1.15f,
                look != null ? look.turbulenceSpeed : settings != null ? settings.turbulenceSpeed : .6f,
                look != null ? look.animationStrength : settings != null ? settings.animationStrength : 1f));
            shader.SetFloat("_FlowTime", Time.timeSinceLevelLoad);
            shader.SetFloat("_Time", time); shader.SetFloat("_Dt", StepTime);
        }

        private void Dispatch(int kernel, bool fine = false)
        {
            Vector3Int resolution = fine ? res : flowRes;
            dimensions[0] = resolution.x; dimensions[1] = resolution.y; dimensions[2] = resolution.z;
            shader.SetInts("_Resolution", dimensions);
            shader.SetVector("_Cell", fine ? cell : flowCell);
            if (kernel != clearDensity) shader.SetTexture(kernel, "_Collision", volume.CollisionTexture);
            shader.Dispatch(kernel, (resolution.x + 3) / 4, (resolution.y + 3) / 4, (resolution.z + 3) / 4);
        }

        private void ResetField()
        {
            for (int i = 0; i < 2; i++)
            {
                shader.SetTexture(clearDensity, "_DensityOut", density[i]);
                Dispatch(clearDensity, true);
                shader.SetTexture(clear, "_VelocityOut", velocity[i]);
                shader.SetTexture(clear, "_PressureOut", pressure[i]);
                Dispatch(clear);
            }
            d = v = p = 0;
            accumulator = 0;
        }

        private void InjectRegion(int kernel, Bounds bounds, bool fine)
        {
            Vector3Int resolution = fine ? res : flowRes;
            Vector3 spacing = fine ? cell : flowCell;
            Vector3 lower = bounds.min - volume.GridMinWorld, upper = bounds.max - volume.GridMinWorld;
            for (int axis = 0; axis < 3; axis++)
            {
                dispatchMin[axis] = Mathf.Clamp(Mathf.FloorToInt(lower[axis] / spacing[axis]), 0, resolution[axis]);
                int end = Mathf.Clamp(Mathf.CeilToInt(upper[axis] / spacing[axis]), 0, resolution[axis]);
                dispatchSize[axis] = end - dispatchMin[axis];
                if (dispatchSize[axis] <= 0) return;
                dimensions[axis] = resolution[axis];
            }
            shader.SetInts("_Resolution", dimensions); shader.SetVector("_Cell", spacing);
            shader.SetInts("_DispatchMin", dispatchMin); shader.SetInts("_DispatchSize", dispatchSize);
            shader.SetTexture(kernel, "_Collision", volume.CollisionTexture);
            shader.Dispatch(kernel, (dispatchSize[0]+3)/4, (dispatchSize[1]+3)/4, (dispatchSize[2]+3)/4);
        }

        /// <summary>Called before RenderGraph recording, once per simulation frame.</summary>
        public void Advance(float dt, GunnellSmokeFeature.Settings settings)
        {
            if (!volume.HasDensity) return;
            var bounds = new Bounds((volume.GridMinWorld + volume.GridMaxWorld) * 0.5f,
                volume.GridMaxWorld - volume.GridMinWorld);
            bool fresh = lastInjectionTick != GunnellBulletHoles.TickTotal && GunnellBulletHoles.InjectionIntersects(bounds);
            lastInjectionTick = GunnellBulletHoles.TickTotal;
            bool hasPendingBlast = HasPendingBlast();
            if (!fresh && !Active && !hasPendingBlast && motionCount == 0)
            {
                // Release the large fine grids as soon as the recovery ends.
                // This runs before any camera records imports of these handles.
                if(density[0]!=null)Dispose();
                return;
            }
            bool restart = !Active;
            if (!Ensure()) return;
            Parameters(settings);
            if (restart) ResetField();
            for (int i = 0; i < motionCount; i++)
            {
                var motion = motions[i];
                if (Time.time - motion.queuedAt > 0.35f) continue;
                shader.SetVector("_MotionA", new Vector4(motion.a.x,motion.a.y,motion.a.z,motion.radius));
                shader.SetVector("_MotionB", new Vector4(motion.b.x,motion.b.y,motion.b.z,motion.clearing));
                shader.SetVector("_MotionFlow", new Vector4(motion.flow.x,motion.flow.y,motion.flow.z,motion.duration));
                shader.SetVector("_MotionSweep", new Vector4(motion.sweep.x,motion.sweep.y,motion.sweep.z,motion.seed));
                shader.SetFloat("_MotionKind", motion.kind);
                var region = new Bounds(motion.a, Vector3.zero);
                region.Encapsulate(motion.b);
                region.Encapsulate(motion.a-motion.sweep*0.5f);
                region.Encapsulate(motion.b+motion.sweep*0.5f);
                region.Expand(motion.radius*3.2f);
                shader.SetTexture(injectMotion,"_DensityOut",density[d]);
                InjectRegion(injectMotion,region,true);
                shader.SetTexture(injectMotionVelocity,"_VelocityOut",velocity[v]);
                InjectRegion(injectMotionVelocity,region,false);
                remaining = Mathf.Max(remaining,6f);
            }
            motionCount = 0;
            if (fresh)
            {
                shader.SetBuffer(inject, "_Shots", GunnellBulletHoles.InjectionBuffer);
                shader.SetBuffer(injectVelocity, "_Shots", GunnellBulletHoles.InjectionBuffer);
                shader.SetTexture(inject, "_DensityOut", density[d]);
                shader.SetTexture(injectVelocity, "_VelocityOut", velocity[v]);
                // Inject only the capsule's local bounding region, never all
                // shots at every cell of the cloud during a burst.
                for (int shot = 0; shot < GunnellBulletHoles.InjectionCount; shot++)
                {
                    shader.SetInt("_ShotIndex", shot);
                    Bounds region = GunnellBulletHoles.InjectionBounds(shot);
                    InjectRegion(inject, region, true);
                    InjectRegion(injectVelocity, region, false);
                }
                remaining = 12f;
            }
            if (hasPendingBlast)
            {
                EnsureVorticity();
            }
            float elapsed = Mathf.Clamp(dt, 0, StepTime * 2);
            remaining = Mathf.Max(0, remaining - elapsed);
            blastRemaining = Mathf.Max(0, blastRemaining - elapsed);
            vorticityRemaining = Mathf.Max(0, vorticityRemaining - elapsed);
            accumulator += elapsed;
            while (accumulator >= StepTime)
            {
                accumulator -= StepTime;
                time += StepTime;
                shader.SetFloat("_Time", time);
                shader.SetFloat("_FlowTime", Time.timeSinceLevelLoad - accumulator);
                // Move the pressure shell with the clearing front, then retain
                // each blast's clock for the returning flow. The recovery does
                // not reset when a bullet is fired elsewhere in the cloud.
                for (int i = 0; i < blasts.Length; i++)
                {
                    PendingBlast blast = blasts[i];
                    if (blast.centerRadius.w > 0f) blast.elapsed += StepTime;
                    bool response = blast.centerRadius.w > 0f && blast.elapsed < BlastResponseDuration;
                    recoverySpheres[i] = blast.centerRadius;
                    recoveryFlows[i] = new Vector4(blast.flowStrength.x,blast.flowStrength.y,blast.flowStrength.z,
                        (blast.seed & 1023) * 0.013f);
                    recoveryStates[i] = new Vector4(blast.elapsed,Mathf.Clamp01(blast.flowStrength.w/9f),0f,response?1f:0f);
                    if (!blast.pending) { blasts[i] = blast; continue; }
                    float progress = Mathf.SmoothStep(0f, 1f, blast.elapsed / BlastOpeningDuration);
                    Vector4 expanding = blast.centerRadius;
                    expanding.w = Mathf.Lerp(0.2f, blast.centerRadius.w, progress);
                    shader.SetVector("_ImpulseCenterRadius", expanding);
                    shader.SetVector("_ImpulseFlowStrength", blast.flowStrength);
                    shader.SetInt("_ImpulseSeed", blast.seed);
                    Vector3 center = blast.centerRadius;
                    Bounds cutRegion = new Bounds(center, Vector3.one * (expanding.w * 2f + 0.8f));
                    shader.SetTexture(injectExplosion, "_DensityOut", density[d]);
                    InjectRegion(injectExplosion, cutRegion, true);

                    float reach = Mathf.Min(Mathf.Max(expanding.w * 1.5f, 0.5f), Mathf.Max(blast.reach,blast.centerRadius.w*1.5f));
                    Bounds flowRegion = new Bounds(center, Vector3.one * (reach * 2f));
                    shader.SetFloat("_ImpulseReach", reach);
                    shader.SetFloat("_ImpulseKickScale", StepTime * 12f);
                    shader.SetTexture(injectExplosionVelocity, "_VelocityOut", velocity[v]);
                    InjectRegion(injectExplosionVelocity, flowRegion, false);
                    blast.pending = blast.elapsed < BlastOpeningDuration;
                    blasts[i] = blast;
                }
                shader.SetVectorArray("_RecoverySpheres", recoverySpheres);
                shader.SetVectorArray("_RecoveryFlows", recoveryFlows);
                shader.SetVectorArray("_RecoveryStates", recoveryStates);
                shader.SetTexture(advectVelocity, "_Velocity", velocity[v]);
                shader.SetTexture(advectVelocity, "_VelocityOut", velocity[1-v]);
                Dispatch(advectVelocity); v = 1-v;
                if (vorticityRemaining > 0f && vorticity != null)
                {
                    shader.SetTexture(computeVorticity, "_Velocity", velocity[v]);
                    shader.SetTexture(computeVorticity, "_VorticityOut", vorticity);
                    Dispatch(computeVorticity);
                    shader.SetTexture(confineVorticity, "_Velocity", velocity[v]);
                    shader.SetTexture(confineVorticity, "_Vorticity", vorticity);
                    shader.SetTexture(confineVorticity, "_VelocityOut", velocity[1-v]);
                    shader.SetFloat("_VorticityStrength", 1.8f);
                    Dispatch(confineVorticity); v = 1-v;
                }
                shader.SetTexture(divergenceKernel, "_Velocity", velocity[v]);
                shader.SetTexture(divergenceKernel, "_DivergenceOut", divergence);
                Dispatch(divergenceKernel);
                for (int iteration = 0; iteration < 8; iteration++)
                {
                    shader.SetTexture(pressureKernel, "_Pressure", pressure[p]);
                    shader.SetTexture(pressureKernel, "_Divergence", divergence);
                    shader.SetTexture(pressureKernel, "_PressureOut", pressure[1-p]);
                    Dispatch(pressureKernel); p = 1-p;
                }
                shader.SetTexture(project, "_Pressure", pressure[p]);
                shader.SetTexture(project, "_Velocity", velocity[v]);
                shader.SetTexture(project, "_VelocityOut", velocity[1-v]);
                Dispatch(project); v = 1-v;
                bool useTiles = settings == null || settings.sparseWakeTiles;
                shader.SetInt("_UseDensityTiles",useTiles ? 1 : 0);
                if(useTiles)
                {
                    // Classify the CURRENT field after every injection. The halo
                    // includes source cells for advection, interpolation and mixing.
                    dimensions[0]=res.x; dimensions[1]=res.y; dimensions[2]=res.z;
                    shader.SetInts("_Resolution",dimensions);
                    shader.SetInts("_TileResolution",tileDimensions);
                    shader.SetTexture(classifyDensityTiles,"_Density",density[d]);
                    shader.SetTexture(classifyDensityTiles,"_DensityTilesOut",densityTiles);
                    shader.Dispatch(classifyDensityTiles,tileRes.x,tileRes.y,tileRes.z);
                    shader.SetTexture(expandDensityTiles,"_DensityTiles",densityTiles);
                    shader.SetTexture(expandDensityTiles,"_DensityTilesOut",expandedDensityTiles);
                    shader.Dispatch(expandDensityTiles,(tileRes.x+3)/4,(tileRes.y+3)/4,(tileRes.z+3)/4);
                }
                shader.SetTexture(advectDensity, "_Velocity", velocity[v]);
                shader.SetTexture(advectDensity,"_DensityTiles",expandedDensityTiles);
                shader.SetTexture(advectDensity, "_Density", density[d]);
                shader.SetTexture(advectDensity, "_DensityOut", predicted);
                Dispatch(advectDensity, true);
                shader.SetTexture(correctDensity, "_Velocity", velocity[v]);
                shader.SetTexture(correctDensity,"_DensityTiles",expandedDensityTiles);
                shader.SetTexture(correctDensity, "_Density", density[d]);
                shader.SetTexture(correctDensity, "_Predicted", predicted);
                shader.SetTexture(correctDensity, "_DensityOut", density[1-d]);
                Dispatch(correctDensity, true); d = 1-d;
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < 2; i++)
            {
                densityHandles[i]?.Release(); densityHandles[i] = null;
                Release(ref density[i]); Release(ref velocity[i]); Release(ref pressure[i]);
            }
            Release(ref divergence);
            Release(ref predicted);
            Release(ref vorticity);
            Release(ref densityTiles); Release(ref expandedDensityTiles);
            remaining = accumulator = 0;
            motionCount = motionCursor = 0;
            blastRemaining = vorticityRemaining = 0;
            System.Array.Clear(blasts, 0, blasts.Length);
        }

        public void AddExplosion(Vector3 point, float radius, float reach, float strength, Vector3 flow)
        {
            int slot = blastCursor++ % blasts.Length;
            int seed = unchecked(Mathf.RoundToInt(Vector3.Dot(point, new Vector3(17.13f, 31.71f, 13.97f)) * 997f));
            blasts[slot] = new PendingBlast
            {
                centerRadius = new Vector4(point.x, point.y, point.z, Mathf.Max(0.2f, radius)),
                flowStrength = new Vector4(flow.normalized.x, flow.normalized.y, flow.normalized.z, Mathf.Max(0f, strength)),
                seed = seed,
                reach = Mathf.Max(0f, reach),
                pending = true,
            };
            blastRemaining = BlastResponseDuration;
            remaining = Mathf.Max(remaining, BlastResponseDuration);
            vorticityRemaining = 5.8f;
        }

        /// <summary>Local partial clearing and drag. kind 0: moving body, 1: knife sweep.
        /// Submitted on the CPU, dispatched only before RenderGraph recording.</summary>
        public void AddMotion(int source, int kind, Vector3 a, Vector3 b, float radius,
            Vector3 flow, float clearing, float duration, float seed, Vector3 sweep)
        {
            int slot = -1;
            for (int i = 0; i < motionCount; i++)
                if (motions[i].source == source && motions[i].kind == kind) { slot = i; break; }
            if (slot < 0) slot = motionCount < motions.Length ? motionCount++ : motionCursor++ % motions.Length;
            motions[slot] = new Motion { source=source,kind=kind,a=a,b=b,
                radius=Mathf.Max(0.08f,radius),flow=Vector3.ClampMagnitude(flow,6f),
                clearing=Mathf.Clamp(clearing,0f,0.75f),duration=Mathf.Clamp(duration,0.01f,0.15f),
                seed=seed,sweep=sweep,queuedAt=Time.time };
        }

        private void EnsureVorticity()
        {
            if (vorticity == null)
                vorticity = Allocate("Smoke explosion vorticity", RenderTextureFormat.ARGBHalf, flowRes);
        }
        private static void Release(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release(); CoreUtils.Destroy(texture); texture = null;
        }
    }
}
