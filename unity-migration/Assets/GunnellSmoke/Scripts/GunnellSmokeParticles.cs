using UnityEngine;
using UnityEngine.Rendering;

namespace GunnellSmoke
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class GunnellSmokeParticles : MonoBehaviour
    {
        internal static GunnellSmokeFeature.Settings FlowSettings;
        [SerializeField, HideInInspector] private GunnellSmokeVolume volume;
        [SerializeField, HideInInspector] private ParticleSystem nozzle;
        [SerializeField, HideInInspector] private ParticleSystem wisps;
        private ParticleSystem.Particle[] nozzleBuffer;
        private ParticleSystem.Particle[] wispBuffer;
        private float wispCredit;
        private float flowClock, propertiesClock;
        private uint randomState;
        private MaterialPropertyBlock wispProperties;
        private GunnellSmokeLook previousLook;
        private Material defaultWispMaterial;
        internal ParticleSystemRenderer WispRenderer { get; private set; }
        internal ParticleSystemRenderer NozzleRenderer { get; private set; }

        public int ParticleCount => (nozzle != null ? nozzle.particleCount : 0)
            + (wisps != null ? wisps.particleCount : 0);

        public void Initialize(GunnellSmokeVolume owner)
        {
            volume = owner;
            randomState = unchecked((uint)owner.GetEntityId().GetHashCode() * 747796405u + 2891336453u);
            if (randomState == 0) randomState = 1;
            Material material = Resources.Load<Material>("GunnellSmokeWisps");
            if (material == null) return;
            defaultWispMaterial = material;
            int budget = Mathf.Clamp(owner.particleBudget, 32, 256);
            nozzle = Configure(nozzle, "SmokeNozzle", 48, material, true);
            wisps = Configure(wisps, "SmokeEdgeWisps", budget, material, false);
            NozzleRenderer = nozzle.GetComponent<ParticleSystemRenderer>();
            WispRenderer = wisps.GetComponent<ParticleSystemRenderer>();
            wispProperties ??= new MaterialPropertyBlock();
            ApplyWispMaterial(FlowSettings != null ? FlowSettings.ActiveLook : null);
            UpdateRenderProperties();
            nozzleBuffer = new ParticleSystem.Particle[48];
            wispBuffer = new ParticleSystem.Particle[budget];
            wispCredit = 0f;
            flowClock = propertiesClock = 0f;
        }

        private ParticleSystem Configure(ParticleSystem system, string childName,
            int budget, Material material, bool jet)
        {
            if (system == null)
            {
                Transform child = transform.Find(childName);
                if (child == null)
                {
                    var go = new GameObject(childName);
                    go.transform.SetParent(transform, false);
                    child = go.transform;
                }
                system = child.GetComponent<ParticleSystem>();
                if (system == null) system = child.gameObject.AddComponent<ParticleSystem>();
            }
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed = false;
            system.randomSeed = NextRandom();
            var main = system.main;
            main.loop = true;
            main.duration = 60f;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = budget;
            main.startSpeed = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            main.startSize3D = !jet;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, jet ? 0.12f : 0.20f),
                        new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.35f, 1f), new Keyframe(1f, jet ? 1.8f : 1.35f)));
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var collision = system.collision;
            collision.enabled = volume.avoidObstacles;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.collidesWith = volume.obstacleMask;
            collision.enableDynamicColliders = false;
            collision.radiusScale = jet ? 0.25f : 0.45f;
            collision.dampen = 0.85f;
            collision.bounce = 0.03f;
            collision.lifetimeLoss = 0.12f;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Tangent, ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent,
                ParticleSystemVertexStream.StableRandomX
            });
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.gameObject.layer = gameObject.layer;
            system.Play();
            return system;
        }

        public void Clear()
        {
            if (nozzle != null) nozzle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (wisps != null) wisps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            wispCredit = 0f;
            flowClock = propertiesClock = 0f;
        }

        private void LateUpdate()
        {
            if (volume == null || nozzle == null || wisps == null || wispBuffer == null) return;
            bool voxelMode = SmokeGameplay.SelectedSmokeStyle == 2;
            if (WispRenderer != null) WispRenderer.forceRenderingOff = voxelMode;
            if (NozzleRenderer != null) NozzleRenderer.forceRenderingOff = voxelMode;
            GunnellSmokeLook look = FlowSettings != null ? FlowSettings.ActiveLook : null;
            bool lookChanged = look != previousLook;
            previousLook = look;
            if (lookChanged) ApplyWispMaterial(look);
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            int crowdThreshold = FlowSettings != null ? Mathf.Max(4, FlowSettings.crowdResolutionThreshold) : 8;
            int cloudCount = GunnellSmokeVolume.Volumes.Count;
            bool crowd = FlowSettings != null && FlowSettings.adaptiveCrowdParticles && cloudCount >= crowdThreshold;
            // Bound supplementary wisps across the scene; native motion and
            // collisions continue every frame, including between flow corrections.
            float emissionScale = crowd ? Mathf.Min(1f, (float)crowdThreshold / cloudCount) : 1f;
            propertiesClock+=dt;
            if(lookChanged||propertiesClock>=(crowd ? .1f : .05f)){UpdateRenderProperties();propertiesClock=0;}
            if (!volume.IsFinished && volume.edgeParticles)
            {
                // The volumetric growth supplies the initial release. A second
                // billboard emitter made a visible halo and a moving grenade trail.
                float dissipate = volume.Dissipation;
                // Low-rate filaments follow the release late in its growth;
                // never add a second jet or an instantaneous full shell.
                float release = Mathf.SmoothStep(0f, 1f, (volume.GrowthProgress - .65f) / .35f);
                float edgeRate = (12f + dissipate * 24f) * release;
                edgeRate *= 1f - Mathf.SmoothStep(0f, 1f, (dissipate - 0.75f) / 0.15f);
                wispCredit += dt * edgeRate * emissionScale;
                for (int i = 0; i < 12 && wispCredit >= 1f; i++, wispCredit -= 1f) EmitWisp();
            }
            // The engine still advances particle positions/collisions every
            // frame. Only CPU flow corrections and particle-array copies tick at 30 Hz.
            flowClock+=dt;
            if(lookChanged||flowClock>=1f/(crowd ? 15f : 30f))
            {
                MoveParticles(nozzle, nozzleBuffer, true, lookChanged,flowClock);
                MoveParticles(wisps, wispBuffer, false, lookChanged,flowClock);
                flowClock=0;
            }
        }

        private void ApplyWispMaterial(GunnellSmokeLook look)
        {
            Material material = look != null && look.wispMaterial != null ? look.wispMaterial : defaultWispMaterial;
            // One shared material per look; switching does not allocate or respawn particles.
            if (WispRenderer != null && WispRenderer.sharedMaterial != material)
                WispRenderer.sharedMaterial = material;
        }

        private void UpdateRenderProperties()
        {
            Vector3 origin = volume.RenderOrigin;
            wispProperties.SetVector("_WispOrigin", new Vector4(origin.x, origin.y, origin.z, volume.VerticalScale));
            wispProperties.SetFloat("_WispRadius", Mathf.Max(.1f, volume.ShapeRadius));
            wispProperties.SetFloat("_WispExtinction", FlowSettings != null
                ? FlowSettings.volumeDensity * (FlowSettings.absorption + FlowSettings.scattering) * .8f : 4f);
            wispProperties.SetFloat("_WispFrontOnly", 1f);
            wispProperties.SetFloat("_WispClip", volume.CollisionTexture != null ? 1f : 0f);
            if (volume.CollisionTexture != null) wispProperties.SetTexture("_WispOccupancy", volume.CollisionTexture);
            wispProperties.SetVector("_WispGridMin", volume.GridMinWorld);
            wispProperties.SetVector("_WispGridSize", volume.GridMaxWorld - volume.GridMinWorld);
            Light sun = RenderSettings.sun;
            Vector3 lightDirection = sun != null ? -sun.transform.forward : new Vector3(0.35f, 0.8f, 0.25f).normalized;
            wispProperties.SetVector("_WispLightDirection", lightDirection);
            GunnellSmokeFeature.SceneAmbient(out Vector3 skyAmbient,out Vector3 groundAmbient);
            wispProperties.SetVector("_WispSceneAmbient",skyAmbient*0.6f+groundAmbient*0.4f);
            Color sunColor = sun != null && sun.isActiveAndEnabled ? sun.color.linear*(sun.intensity/1.35f) : Color.black;
            if (sun != null && sun.useColorTemperature) sunColor *= Mathf.CorrelatedColorTemperatureToRGB(sun.colorTemperature).linear;
            wispProperties.SetVector("_WispSceneSun",sunColor);
            wispProperties.SetFloat("_WispReceiveSceneShadows", FlowSettings == null || FlowSettings.receiveSceneShadows ? 1f : 0f);
            wispProperties.SetFloat("_WispStylization", FlowSettings != null && FlowSettings.ActiveLook != null
                ? FlowSettings.ActiveLook.stylization : 0f);
            WispRenderer.SetPropertyBlock(wispProperties);
            wispProperties.SetFloat("_WispFrontOnly", 0f);
            NozzleRenderer.SetPropertyBlock(wispProperties);
        }

        private Color ParticleTint()
        {
            Color tint = new Color(0.72f, 0.69f, 0.64f);
            if (FlowSettings != null)
            {
                var s = FlowSettings;
                var look = s.ActiveLook;
                float scattering = s.scattering / Mathf.Max(0.001f, s.absorption + s.scattering);
                tint = look != null ? look.smokeColor * look.ambient + look.sunColor * (0.035f * scattering)
                    : s.smokeColor * s.ambient + s.sunColor * (0.035f * scattering);
            }
            return tint;
        }

        private Color SmokeTint(float alpha)
        {
            Color tint = ParticleTint();
            tint *= Range(0.92f, 1.03f);
            tint.a = alpha;
            return tint;
        }

        // La aleatoriedad visual no altera Random.state usado por armas o gameplay.
        private uint NextRandom()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return randomState;
        }
        private float Range(float min, float max) => Mathf.Lerp(min, max, (NextRandom() >> 8) / 16777216f);

        private void EmitWisp()
        {
            if (volume.GrowthProgress < .65f) return;
            float height = Range(-0.65f, 1f), angle = Range(0f, Mathf.PI * 2f);
            float width = Mathf.Sqrt(1f - height * height);
            Vector3 direction = new Vector3(width * Mathf.Cos(angle), height, width * Mathf.Sin(angle));
            direction.y /= height > 0f ? 1.35f : 0.85f;
            float distance = volume.ShapeRadius * Mathf.Lerp(1.30f, 0.75f, volume.Dissipation);
            distance *= Range(0.99f, 1.04f);
            if (!volume.TryGetWispPosition(direction, distance, out Vector3 point)) return;
            if (volume.ExplosionRemaining(point) < 0.35f) return;
            point += direction.normalized * Range(0.025f, 0.08f);
            float size = Mathf.Lerp(0.20f, 0.42f, Mathf.Clamp01(volume.ShapeRadius / 5.2f)) * Range(0.75f, 1.20f);
            var look = FlowSettings != null ? FlowSettings.ActiveLook : null;
            if (look != null) size *= look.wispSize;
            var particle = new ParticleSystem.EmitParams
            {
                position = point,
                velocity = FlowVelocity(point) + new Vector3(direction.x, 0f, direction.z).normalized * 0.16f,
                startLifetime = Range(1.2f, 1.85f),
                startSize3D = new Vector3(size, size * Range(1.15f, 1.65f), size),
                startColor = SmokeTint(look != null ? look.wispOpacity * Mathf.Lerp(1f, 1.28f, volume.Dissipation)
                    : Mathf.Lerp(0.11f, 0.18f, volume.Dissipation)),
                rotation = Range(-180f, 180f)
            };
            wisps.Emit(particle, 1);
        }

        private Vector3 FlowVelocity(Vector3 point)
        {
            var settings = FlowSettings;
            if (settings == null) return new Vector3(-0.075f, 0.14f, -0.055f);
            var look = settings.ActiveLook;
            float strength = look != null ? look.animationStrength : settings.animationStrength;
            float frequency = Mathf.Max(0.3f, look != null ? look.turbulenceScale : settings.turbulenceScale);
            float speed = Mathf.Max(0f, look != null ? look.turbulenceSpeed : settings.turbulenceSpeed);
            float turbulence = Mathf.Clamp(look != null ? look.turbulenceStrength : settings.turbulenceStrength, 0f, 0.8f);
            Vector3 direction = look != null ? look.animationDirection : settings.animationDirection;
            float time = Time.timeSinceLevelLoad * strength;
            Vector3 local = point - volume.RenderOrigin;
            Vector3 rolling = new Vector3(local.y * 0.8f + local.z * 0.6f,
                local.x * 0.6f + local.z * 0.8f, local.x * 0.8f + local.y * 0.6f);
            Vector3 phase = rolling * frequency
                + new Vector3(0.73f, -0.61f, 0.83f) * (time * speed);
            Vector3 rate = new Vector3(Mathf.Cos(phase.x) * 0.73f, Mathf.Cos(phase.y) * -0.61f,
                Mathf.Cos(phase.z) * 0.83f) * (speed * strength);
            // Time derivative of GunnellNoiseCoordinates' bounded rolling field.
            float amplitude = Mathf.Min(0.28f * turbulence, 0.18f / frequency);
            Vector3 curlRate = new Vector3(0.8f * rate.y - 0.6f * rate.z,
                0.8f * rate.z - 0.6f * rate.x, 0.8f * rate.x - 0.6f * rate.y) * amplitude;
            return -direction * strength - curlRate + volume.ExplosionVelocity(point);
        }

        public void BurstFromExplosion(Vector3 point, float reach, float strength)
        {
            if (wisps == null || wispBuffer == null) return;
            int count = wisps.GetParticles(wispBuffer);
            for (int i = 0; i < count; i++)
            {
                Vector3 delta = wispBuffer[i].position - point;
                float attenuation = Mathf.Clamp01(1f - delta.magnitude / Mathf.Max(0.1f, reach));
                wispBuffer[i].velocity += delta.normalized * strength * attenuation;
                wispBuffer[i].remainingLifetime *= Mathf.Lerp(1f, 0.55f, attenuation);
            }
            wisps.SetParticles(wispBuffer, count);
        }

        private void MoveParticles(ParticleSystem system, ParticleSystem.Particle[] buffer, bool jet, bool retint,float dt)
        {
            int count = system.GetParticles(buffer);
            if (count == 0) return;
            float follow = 1f - Mathf.Exp(-2.8f * Mathf.Min(dt, .1f));
            Color tint = retint ? ParticleTint() : Color.white;
            for (int i = 0; i < count; i++)
            {
                if (volume.ExplosionRemaining(buffer[i].position) < 0.08f)
                {
                    buffer[i].remainingLifetime = 0f;
                    continue;
                }
                float age = buffer[i].startLifetime - buffer[i].remainingLifetime;
                if (retint)
                {
                    tint.a = buffer[i].startColor.a / 255f;
                    buffer[i].startColor = tint;
                }
                Vector3 outward = buffer[i].position - volume.RenderOrigin;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.001f ? outward.normalized : Vector3.zero;
                Vector3 velocity = FlowVelocity(buffer[i].position);
                if (jet) velocity += (Vector3.up * 2f + outward * 0.45f) * Mathf.Exp(-age * 2.6f);
                else velocity += outward * (0.16f + volume.Dissipation * 0.27f) + Vector3.up * (volume.Dissipation * 0.12f);
                // Preserve collision damping and explosion impulses, then
                // entrain each wisp into the gas over time rather than snapping.
                buffer[i].velocity = Vector3.Lerp(buffer[i].velocity, velocity, follow);
            }
            system.SetParticles(buffer, count);
        }
    }
}
