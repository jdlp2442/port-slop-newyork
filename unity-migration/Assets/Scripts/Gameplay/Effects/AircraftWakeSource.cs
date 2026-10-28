using System.Collections.Generic;
using UnityEngine;

namespace Nyc.Gameplay.Effects
{
    /// <summary>
    /// Source for rotor downwash, prop wash, and aerodynamic wake displacement.
    /// Attached to helicopters, drones, quadcopters, or any aircraft to dynamically
    /// part and clear volumetric smoke plumes, burning vehicle fires, and rooftop steam.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AircraftWakeSource : MonoBehaviour
    {
        public static readonly List<AircraftWakeSource> ActiveAircraft = new List<AircraftWakeSource>();

        [Header("Aircraft Profile")]
        [Tooltip("Identifier for logging and tuning (e.g. Helicopter, Drone, Quadcopter).")]
        public string aircraftType = "Helicopter";

        [Tooltip("Effective rotor or propeller diameter in meters.")]
        public float rotorRadius = 4.8f;

        [Tooltip("Downward air thrust velocity in m/s (rotor downwash).")]
        public float downwashStrength = 18.0f;

        [Tooltip("Carving cavity radius carved into the volumetric smoke.")]
        public float carveRadius = 7.5f;

        [Tooltip("Clearing fraction (0 = none, 1 = complete hole through smoke).")]
        [Range(0.2f, 1f)] public float clearingFraction = 0.75f;

        public Vector3 Position => transform.position;
        public Vector3 Velocity { get; private set; }

        private Vector3 _lastPos;

        void OnEnable()
        {
            if (!ActiveAircraft.Contains(this))
            {
                ActiveAircraft.Add(this);
            }
            _lastPos = transform.position;
        }

        void OnDisable()
        {
            ActiveAircraft.Remove(this);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0.0001f)
            {
                Velocity = (transform.position - _lastPos) / dt;
                _lastPos = transform.position;
            }
        }
    }
}
