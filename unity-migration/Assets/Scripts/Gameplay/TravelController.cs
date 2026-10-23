using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;

namespace Nyc.Gameplay
{
    public enum TravelMode { Person, Car, Cyclist, Helicopter }

    /// <summary>
    /// Normalised control state. Values are 0..1 so the touch stick can feed analog input,
    /// exactly like ThirdPersonControllerManager.keys in controllers.js.
    /// </summary>
    public struct ControllerInput
    {
        public float Forward, Backward, Left, Right, Up, Down, Boost;

        public static ControllerInput FromBooleans(bool f, bool b, bool l, bool r, bool u, bool d, bool boost) =>
            new ControllerInput
            {
                Forward = f ? 1f : 0f,
                Backward = b ? 1f : 0f,
                Left = l ? 1f : 0f,
                Right = r ? 1f : 0f,
                Up = u ? 1f : 0f,
                Down = d ? 1f : 0f,
                Boost = boost ? 1f : 0f
            };
    }

    /// <summary>Per-frame services handed to a controller.</summary>
    public sealed class ControllerContext
    {
        public CollisionWorld Collision;
        public ThirdPersonControllerManager Manager;
        public System.Func<float, float, float> Ground;
        public float Elapsed;

        readonly List<Vector2> _normals = new List<Vector2>(8);
        public List<Vector2> Normals => _normals;

        /// <summary>Island / shoreline clamp (resolveHorizontalCollision's first block).</summary>
        public void ClampToIsland(ref Vector2 pos, bool helicopter, ref float speed)
        {
            if (Manager != null) Manager.ClampToIsland(ref pos, helicopter, ref speed);
        }
    }

    /// <summary>
    /// Base for the four travel modes. The simulation runs in the SOURCE frame
    /// (x = grid east, z = grid south, heading 0 = +Z) so the maths is a line-for-line port of
    /// controllers.js; only SyncTransform() crosses into Unity space.
    /// </summary>
    public abstract class TravelController : MonoBehaviour
    {
        [Header("State (source frame)")]
        public Vector2 Position;
        public float Height;
        public float Heading;
        public float Speed;

        Vector2 _previousPosition;
        float _previousHeight;
        float _previousHeading;
        bool _hasPreviousState;

        [Header("Camera framing")]
        public float ChaseDistance = 4.3f;
        public float ChaseHeight = 1.95f;
        public Vector3 CameraTargetOffset = new Vector3(0f, 1.55f, 0f);
        public float LookUpBias = 1.55f;

        public abstract TravelMode Mode { get; }
        public abstract void Tick(float dt, in ControllerInput input, ControllerContext ctx);

        public virtual void ResetMotion() { Speed = 0f; }

        public virtual void Teleport(Vector2 xz, float heading, float height)
        {
            Position = xz;
            Heading = heading;
            Height = height;
            _previousPosition = xz;
            _previousHeading = heading;
            _previousHeight = height;
            _hasPreviousState = true;
            ResetMotion();
            SyncTransform();
        }

        /// <summary>Captures the last rendered state before a fixed simulation step.</summary>
        public void BeginSimulationStep()
        {
            _previousPosition = Position;
            _previousHeight = Height;
            _previousHeading = Heading;
            _hasPreviousState = true;
        }

        /// <summary>Writes the source-frame state into the Unity transform.</summary>
        public void SyncTransform()
        {
            transform.SetPositionAndRotation(
                CityFrame.ToUnity(Position.x, Height, Position.y),
                CityFrame.HeadingToRotation(Heading));
        }

        /// <summary>Interpolates the visual transform between fixed simulation states.</summary>
        public void InterpolateTransform(float alpha)
        {
            if (!_hasPreviousState) { SyncTransform(); return; }
            alpha = Mathf.Clamp01(alpha);
            Vector2 p = Vector2.Lerp(_previousPosition, Position, alpha);
            float h = Mathf.Lerp(_previousHeight, Height, alpha);
            // Headings change slowly at the fixed step; linear interpolation avoids a wrap
            // dependency in the headless Unity stubs while preserving smooth visual motion.
            float heading = Mathf.Lerp(_previousHeading, Heading, alpha);
            transform.SetPositionAndRotation(CityFrame.ToUnity(p.x, h, p.y), CityFrame.HeadingToRotation(heading));
        }
    }
}
