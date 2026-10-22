using System;
using UnityEngine;

namespace Nyc.Gameplay
{
    /// <summary>
    /// NYC yellow cab. Port of updateCar() + the car branch of resolveHorizontalCollision()
    /// (controllers.js:766-900, 1003-1075):
    /// top speed 23.5 / 36 m/s boosted, 14 / 21 m/s^2, steering 0.52 rad with 2.4 gain,
    /// lateral tyre-slip velocity with exp(-6.8 dt) grip, collision yaw spin and a 3-DOF
    /// spring-damper chassis (pitch, roll, heave).
    /// </summary>
    public sealed class CarController : TravelController
    {
        [Header("Performance")]
        public float maxSpeed = 23.5f;
        public float boostMaxSpeed = 36.0f;
        public float accel = 14.0f;
        public float boostAccel = 21.0f;
        public float reverseFactor = 0.45f;
        public float coastDamping = 2.8f;

        [Header("Steering")]
        public float steerAngleMax = 0.52f;
        public float steerResponse = 10.0f;
        public float turnGain = 2.4f;
        public float turnSpeedReference = 8.0f;

        [Header("Body (half extents, m)")]
        public float halfWidth = 0.86f;
        public float halfLength = 2.18f;
        public float wheelRadius = 0.35f;

        [Header("Suspension")]
        public float springK = 68.0f;
        public float dampC = 11.2f;

        [Header("Runtime")]
        public float Vx, Vz;
        public float AngularVelocity;
        public float SteerAngle;
        public float PitchVelocity, RollVelocity;
        public float ChassisPitch, ChassisRoll;
        public float HeaveY, HeaveVelocity;
        public float WheelSpin;
        public float Throttle;

        /// <summary>Source-space contact point, source-space normal, impact speed.</summary>
        public event Action<Vector3, Vector2, float> CollisionSpark;

        public override TravelMode Mode => TravelMode.Car;

        void Reset()
        {
            ChaseDistance = 6.9f;
            ChaseHeight = 2.12f;
            CameraTargetOffset = new Vector3(0f, 0.96f, 0f);
            LookUpBias = 0.52f;
        }

        public override void Tick(float dt, in ControllerInput input, ControllerContext ctx)
        {
            float topSpeed = input.Boost > 0.5f ? boostMaxSpeed : maxSpeed;
            float acceleration = input.Boost > 0.5f ? boostAccel : accel;

            Throttle = 0f;
            if (input.Forward > 0f) Throttle = input.Forward;
            else if (input.Backward > 0f) Throttle = -input.Backward;

            if (Mathf.Abs(Throttle) > 0.0001f)
            {
                Speed += Throttle * acceleration * dt;
                Speed = Mathf.Clamp(Speed, -topSpeed * reverseFactor, topSpeed);
            }
            else
            {
                Speed *= Mathf.Max(0f, 1f - dt * coastDamping);
                if (Mathf.Abs(Speed) < 0.08f) Speed = 0f;
            }

            float steerInput = 0f;
            if (input.Left > 0f) steerInput = input.Left;
            if (input.Right > 0f) steerInput = -input.Right;

            SteerAngle += (steerInput * steerAngleMax - SteerAngle) * Mathf.Min(1f, dt * steerResponse);

            if (Mathf.Abs(Speed) > 0.2f)
            {
                float dirSign = Speed >= 0f ? 1f : -1f;
                float speedFactor = Mathf.Min(1f, Mathf.Abs(Speed) / turnSpeedReference);
                Heading += SteerAngle * turnGain * speedFactor * dirSign * dt;
            }

            // Collision yaw spin + lateral tyre slip, damped by asphalt grip.
            float gripDamp = Mathf.Exp(-dt * 6.8f);
            Heading += AngularVelocity * dt;
            AngularVelocity *= Mathf.Exp(-dt * 7.8f);

            float prevX = Position.x;
            float prevZ = Position.y;

            Position.x += (Mathf.Sin(Heading) * Speed + Vx) * dt;
            Position.y += (Mathf.Cos(Heading) * Speed + Vz) * dt;
            Vx *= gripDamp;
            Vz *= gripDamp;

            float sinH = Mathf.Sin(Heading);
            float cosH = Mathf.Cos(Heading);
            float extX = Mathf.Abs(cosH) * halfWidth + Mathf.Abs(sinH) * halfLength;
            float extZ = Mathf.Abs(sinH) * halfWidth + Mathf.Abs(cosH) * halfLength;

            ctx.ClampToIsland(ref Position, false, ref Speed);

            int contacts = ctx.Collision.Resolve(ref Position, extX, extZ, 1.0f, ctx.Normals);
            for (int i = 0; i < contacts; i++) ApplyContact(ctx.Normals[i], sinH, cosH);

            Height = ctx.Ground(Position.x, Position.y);

            // 3-DOF spring-damper chassis.
            float targetPitch = -Throttle * 0.026f;
            float targetRoll = -SteerAngle * (Speed / Mathf.Max(0.01f, topSpeed)) * 0.092f;

            PitchVelocity += ((targetPitch - ChassisPitch) * springK - PitchVelocity * dampC) * dt;
            RollVelocity += ((targetRoll - ChassisRoll) * springK - RollVelocity * dampC) * dt;

            ChassisPitch = Mathf.Clamp(ChassisPitch + PitchVelocity * dt, -0.22f, 0.22f);
            ChassisRoll = Mathf.Clamp(ChassisRoll + RollVelocity * dt, -0.26f, 0.26f);

            HeaveVelocity += (-HeaveY * 85.0f - HeaveVelocity * 12.0f) * dt;
            HeaveY = Mathf.Clamp(HeaveY + HeaveVelocity * dt, -0.08f, 0.14f);

            // Wheel spin from the distance actually travelled along the heading.
            float forwardDist = (Position.x - prevX) * sinH + (Position.y - prevZ) * cosH;
            float rawStep = forwardDist / wheelRadius;
            float step = Mathf.Sign(rawStep) * Mathf.Min(Mathf.Abs(rawStep), 0.48f);
            WheelSpin = Mathf.Repeat(WheelSpin + step, Mathf.PI * 2f);
        }

        void ApplyContact(Vector2 n, float sinH, float cosH)
        {
            float totalVx = sinH * Speed + Vx;
            float totalVz = cosH * Speed + Vz;
            float vn = -(totalVx * n.x + totalVz * n.y);
            if (vn <= 0.5f) return;

            float headOn = Mathf.Abs(sinH * n.x + cosH * n.y);
            float sideDot = cosH * n.x - sinH * n.y;
            float along = sinH * n.x + cosH * n.y;

            Vx += n.x * vn * 0.42f;
            Vz += n.y * vn * 0.42f;

            if (headOn > 0.72f)
            {
                Speed = -Speed * 0.25f;
            }
            else
            {
                Speed *= 1.0f - 0.62f * headOn;
                AngularVelocity += sideDot * (along >= 0f ? -1f : 1f) * Mathf.Min(3.2f, vn * 0.22f);
            }

            PitchVelocity += along * Mathf.Min(14.0f, vn) * 0.16f;
            RollVelocity -= sideDot * Mathf.Min(14.0f, vn) * 0.22f;
            HeaveVelocity += Mathf.Min(1.4f, vn * 0.11f);

            if (vn > 2.2f)
            {
                var point = new Vector3(
                    Position.x - n.x * (Mathf.Abs(Mathf.Cos(Heading)) * halfWidth + Mathf.Abs(Mathf.Sin(Heading)) * halfLength) * 0.92f,
                    0.56f,
                    Position.y - n.y * (Mathf.Abs(Mathf.Sin(Heading)) * halfWidth + Mathf.Abs(Mathf.Cos(Heading)) * halfLength) * 0.92f);
                CollisionSpark?.Invoke(point, n, vn);
            }
        }

        public override void ResetMotion()
        {
            base.ResetMotion();
            Vx = Vz = 0f;
            AngularVelocity = 0f;
            SteerAngle = 0f;
            PitchVelocity = RollVelocity = 0f;
            ChassisPitch = ChassisRoll = 0f;
            HeaveY = HeaveVelocity = 0f;
        }
    }
}
