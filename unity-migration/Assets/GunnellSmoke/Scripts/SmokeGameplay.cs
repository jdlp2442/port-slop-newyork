using System;
using UnityEngine;

namespace GunnellSmoke
{
    /// <summary>Gameplay entry points independent of the source project's demo.</summary>
    public static class SmokeGameplay
    {
        public static event Action<Vector3, float, float, float> ExplosionTriggered;
        public static int SelectedSmokeStyle { get; set; }
        public static int EnvironmentMask => ~LayerMask.GetMask("Ignore Raycast", "Player", "PlayerKCC", "FirstPersonView", "Pickup", "UI");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ExplosionTriggered = null;
            SelectedSmokeStyle = 0;
            GunnellBulletHoles.Release();
        }

        public static void AddExplosion(Vector3 point, float radius = 8f, float strength = 9f, float carveRadius = 5f)
            => ExplosionTriggered?.Invoke(point, radius, strength, carveRadius);
    }
}
