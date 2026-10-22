using UnityEngine;

namespace Nyc.Gameplay
{
    /// <summary>
    /// Bike-lane cyclist. Port of updateCyclist() (controllers.js:1077-1121):
    /// 12.8 / 19 m/s boosted, 9 / 13.5 m/s^2, 2.15 rad/s turn, 0.34 rad lean,
    /// cranks geared 3.2:1 to the rear wheel.
    /// </summary>
    public sealed class BikeController : TravelController
    {
        [Header("Performance")]
        public float maxSpeed = 12.8f;
        public float boostMaxSpeed = 19.0f;
        public float accel = 9.0f;
        public float boostAccel = 13.5f;
        public float reverseFactor = 0.6f;
        public float reverseLimit = 4.5f;
        public float coastDamping = 2.4f;

        [Header("Handling")]
        public float turnRate = 2.15f;
        public float leanMax = 0.34f;
        public float leanResponse = 9.0f;
        public float wheelRadius = 0.35f;
        public float gearRatio = 3.2f;
        public float radius = 0.65f;

        [Header("Runtime")]
        public float LeanAngle;
        public float PedalPhase;
        public float WheelSpin;

        public override TravelMode Mode => TravelMode.Cyclist;

        void Reset()
        {
            ChaseDistance = 4.8f;
            ChaseHeight = 2.05f;
            CameraTargetOffset = new Vector3(0f, 1.35f, 0f);
            LookUpBias = 1.55f;
        }

        public override void Tick(float dt, in ControllerInput input, ControllerContext ctx)
        {
            float topSpeed = input.Boost > 0.5f ? boostMaxSpeed : maxSpeed;
            float acceleration = input.Boost > 0.5f ? boostAccel : accel;

            float throttle = 0f;
            if (input.Forward > 0f) throttle = input.Forward;
            else if (input.Backward > 0f) throttle = -reverseFactor * input.Backward;

            if (Mathf.Abs(throttle) > 0.0001f)
            {
                Speed += throttle * acceleration * dt;
                Speed = Mathf.Clamp(Speed, -reverseLimit, topSpeed);
            }
            else
            {
                Speed *= Mathf.Max(0f, 1f - dt * coastDamping);
                if (Mathf.Abs(Speed) < 0.05f) Speed = 0f;
            }

            float turnInput = 0f;
            if (input.Left > 0f) turnInput = input.Left;
            if (input.Right > 0f) turnInput = -input.Right;

            if (Mathf.Abs(Speed) > 0.15f)
            {
                float dirSign = Speed >= 0f ? 1f : -1f;
                Heading += turnInput * turnRate * dirSign * dt;
            }

            Position.x += Mathf.Sin(Heading) * Speed * dt;
            Position.y += Mathf.Cos(Heading) * Speed * dt;

            ctx.ClampToIsland(ref Position, false, ref Speed);
            ctx.Collision.Resolve(ref Position, radius, radius, 1.0f, ctx.Normals);

            Height = ctx.Ground(Position.x, Position.y);

            float targetLean = -turnInput * Mathf.Min(1f, Mathf.Abs(Speed) / 9.0f) * leanMax;
            LeanAngle += (targetLean - LeanAngle) * Mathf.Min(1f, dt * leanResponse);

            float wheelDelta = (Speed / wheelRadius) * dt;
            WheelSpin = Mathf.Repeat(WheelSpin + wheelDelta, Mathf.PI * 2f);

            if (throttle > 0f && wheelDelta > 0f) PedalPhase += wheelDelta / gearRatio;
            PedalPhase = Mathf.Repeat(PedalPhase, Mathf.PI * 2f);
        }

        public override void ResetMotion()
        {
            base.ResetMotion();
            LeanAngle = 0f;
        }
    }
}
