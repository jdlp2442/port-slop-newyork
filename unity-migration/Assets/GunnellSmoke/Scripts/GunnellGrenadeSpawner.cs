using UnityEngine;

namespace GunnellSmoke
{
    public static class GunnellGrenadeSpawner
    {
        public static event System.Action<GunnellSmokeVolume> SmokeDeployed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => SmokeDeployed = null;

        public static GunnellSmokeVolume SpawnCloud(Vector3 position, Transform emitter = null,
            float releaseDuration = 1.15f, float sustainDuration = 18f, int obstacleMask = ~0)
        {
            var go = new GameObject("CS2 Smoke cloud");
            go.SetActive(false);
            go.transform.position = position;
            if (emitter != null && emitter.gameObject.scene.IsValid())
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, emitter.gameObject.scene);

            var volume = go.AddComponent<GunnellSmokeVolume>();
            volume.voxelSize = 0.48f;
            volume.gridExtent = new Vector3Int(13, 12, 13);
            volume.maxVoxels = 2800;
            volume.radius = 5.2f;
            volume.growthPerSecond = 4200f;
            volume.expansionDuration = GunnellSmokeVolume.MinimumExpansionDuration;
            volume.emissionDuration = releaseDuration;
            volume.emitter = emitter;
            volume.edgeParticles = true;
            volume.avoidObstacles = true;
            volume.obstacleMask = obstacleMask & SmokeGameplay.EnvironmentMask;
            volume.obstacleRadius = volume.voxelSize * 0.4f;
            volume.sustainDuration = sustainDuration;
            volume.fadeDuration = 4f;
            volume.destroyOnFinish = true;
            volume.autoStart = true;
            go.SetActive(true);
            if (volume.isActiveAndEnabled) SmokeDeployed?.Invoke(volume);
            return volume;
        }
    }
}
