using UnityEngine;

namespace Nyc.Gameplay
{
    /// <summary>
    /// Sightseeing helicopter. Port of updateHelicopter() (controllers.js:1123-1171):
    /// 32 / 52 m/s horizontal, 16 m/s vertical, 24 m/s^2 horizontal and 22 m/s^2 vertical
    /// authority, 1.45 rad/s yaw, 0.24 rad pitch / 0.28 rad roll, 380 m ceiling.
    /// </summary>
    public sealed class HelicopterController : TravelController
    {
        [Header("Performance")]
        public float maxHorizSpeed = 32.0f;
        public float boostMaxSpeed = 52.0f;
        public float maxVertSpeed = 16.0f;
        public float horizAccel = 24.0f;
        public float vertAccel = 22.0f;
        public float horizDamping = 1.8f;
        public float vertDamping = 3.5f;
        public float yawRate = 1.45f;

        [Header("Limits")]
        public float minHeight = 0.25f;
        public float maxHeight = 380.0f;
        public float radius = 4.5f;

        [Header("Attitude")]
        public float maxPitch = 0.24f;
        public float maxRoll = 0.28f;
        public float attitudeResponse = 5.0f;
        public float bobAmplitude = 0.18f;
        public float bobRate = 2.6f;

        [Header("Runtime")]
        public float VerticalSpeed;
        public float PitchAngle;
        public float RollAngle;
        public float Bob;

        public override TravelMode Mode => TravelMode.Helicopter;

        void Reset()
        {
            ChaseDistance = 11.0f;
            ChaseHeight = 3.3f;
            CameraTargetOffset = new Vector3(0f, 0.5f, 0f);
            LookUpBias = 0.2f;
        }

        public override void Tick(float dt, in ControllerInput input, ControllerContext ctx)
        {
            float maxHoriz = input.Boost > 0.5f ? boostMaxSpeed : maxHorizSpeed;

            float fwdInput = 0f;
            if (input.Forward > 0f) fwdInput = input.Forward;
            else if (input.Backward > 0f) fwdInput = -input.Backward;

            float yawInput = 0f;
            if (input.Left > 0f) yawInput = input.Left;
            if (input.Right > 0f) yawInput = -input.Right;

            float vertInput = 0f;
            if (input.Up > 0f) vertInput = input.Up;
            else if (input.Down > 0f) vertInput = -input.Down;

            if (Mathf.Abs(fwdInput) > 0.0001f)
            {
                Speed += fwdInput * horizAccel * dt;
                Speed = Mathf.Clamp(Speed, -maxHoriz * 0.6f, maxHoriz);
            }
            else
            {
                Speed *= Mathf.Max(0f, 1f - dt * horizDamping);
            }

            if (Mathf.Abs(vertInput) > 0.0001f)
            {
                VerticalSpeed += vertInput * vertAccel * dt;
                VerticalSpeed = Mathf.Clamp(VerticalSpeed, -maxVertSpeed, maxVertSpeed);
            }
            else
            {
                VerticalSpeed *= Mathf.Max(0f, 1f - dt * vertDamping);
            }

            Heading += yawInput * yawRate * dt;

            Position.x += Mathf.Sin(Heading) * Speed * dt;
            Position.y += Mathf.Cos(Heading) * Speed * dt;
            Height += VerticalSpeed * dt;

            Height = Mathf.Clamp(Height, minHeight, maxHeight);

            ctx.ClampToIsland(ref Position, true, ref Speed);
            ctx.Collision.Resolve(ref Position, radius, radius, Height, ctx.Normals);

            float targetPitch = (Speed / Mathf.Max(0.01f, maxHoriz)) * maxPitch;
            float targetRoll = -yawInput * maxRoll;

            PitchAngle += (targetPitch - PitchAngle) * Mathf.Min(1f, dt * attitudeResponse);
            RollAngle += (targetRoll - RollAngle) * Mathf.Min(1f, dt * attitudeResponse);
            Bob = Mathf.Sin(ctx.Elapsed * bobRate) * bobAmplitude;
        }

        public override void ResetMotion()
        {
            base.ResetMotion();
            VerticalSpeed = 0f;
            PitchAngle = RollAngle = 0f;
        }
    }
}
