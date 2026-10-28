using System;
using UnityEngine;
using Nyc.Core;

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
        public float steerAngleMax = 0.56f;
        public float steerResponse = 12.0f;
        public float turnGain = 2.6f;
        public float turnSpeedReference = 6.0f;

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

        /// <summary>Fired when vehicle suffers catastrophic crash destruction.</summary>
        public event Action<Vector3, float> OnCarWrecked;
        public bool IsWrecked;

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
            if (IsWrecked)
            {
                TickWreck(dt, ctx);
                return;
            }

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

            if (Mathf.Abs(Speed) > 0.15f)
            {
                float dirSign = Speed >= 0f ? 1f : -1f;
                // Ackermann geometric steering: crisp intersection turning radius (7-9m) at low speeds,
                // with gentle high-speed damping for straight-line stability on avenues.
                float highSpeedDamp = 1.0f / (1.0f + Mathf.Max(0f, Mathf.Abs(Speed) - 10f) * 0.035f);
                float effectiveTurn = (Mathf.Abs(Speed) / 2.65f) * (Mathf.Sin(SteerAngle) / Mathf.Max(0.01f, Mathf.Cos(SteerAngle))) * highSpeedDamp;
                effectiveTurn = Mathf.Clamp(effectiveTurn, -2.8f, 2.8f);
                Heading += effectiveTurn * dirSign * dt;
            }

            // Decompose total velocity into longitudinal (along heading) and lateral (sideways slip)
            float sinH = Mathf.Sin(Heading);
            float cosH = Mathf.Cos(Heading);
            float totalVx = sinH * Speed + Vx;
            float totalVz = cosH * Speed + Vz;
            float fwdVel = totalVx * sinH + totalVz * cosH;
            float latVel = totalVx * cosH - totalVz * sinH;

            // Lateral tyre grip: progressive grip gives tactile drift feedback on sharp corners
            float gripRate = Mathf.Lerp(26.0f, 16.0f, Mathf.Min(1f, Mathf.Abs(latVel) / 5.0f));
            latVel *= Mathf.Exp(-dt * gripRate);
            Speed = fwdVel;
            Vx = cosH * latVel;
            Vz = -sinH * latVel;

            // Collision yaw spin damped by tyre grip
            Heading += AngularVelocity * dt;
            AngularVelocity *= Mathf.Exp(-dt * 12.0f);

            float prevX = Position.x;
            float prevZ = Position.y;

            sinH = Mathf.Sin(Heading);
            cosH = Mathf.Cos(Heading);
            Position.x += (sinH * Speed + Vx) * dt;
            Position.y += (cosH * Speed + Vz) * dt;

            ctx.ClampToIsland(ref Position, false, ref Speed);

            // Two-disc capsule collision: front axle and rear axle.
            // Using two discs of radius = halfWidth (0.86m) prevents diagonal AABB inflation
            // (which previously expanded from 1.72m to 4.30m, causing violent building snagging).
            Vector2 forwardDir = new Vector2(sinH, cosH);
            float axleOffset = Mathf.Max(0.5f, halfLength - halfWidth);
            Vector2 frontPos = Position + forwardDir * axleOffset;
            Vector2 rearPos = Position - forwardDir * axleOffset;

            int cFront = ctx.Collision.Resolve(ref frontPos, halfWidth, halfWidth, 1.0f, ctx.Normals);
            for (int i = 0; i < cFront; i++) ApplyContact(ctx.Normals[i], sinH, cosH);

            if (cFront > 0 && Speed > 13.5f)
            {
                float blastSpeed = Speed;
                IsWrecked = true;
                Speed *= 0.52f; // Impact absorbs kinetic energy; remainder slides/derrapes
                AngularVelocity = (AngularVelocity != 0f ? Mathf.Sign(AngularVelocity) : 1f) * Mathf.Clamp(blastSpeed * 0.24f, 2.8f, 4.8f);
                PitchVelocity -= 4.2f;
                HeaveVelocity += 1.6f;
                Vector3 blastWorldPos = CityFrame.ToUnity(frontPos.x, Height + 0.5f, frontPos.y);
                OnCarWrecked?.Invoke(blastWorldPos, blastSpeed);
            }

            int cRear = ctx.Collision.Resolve(ref rearPos, halfWidth, halfWidth, 1.0f, ctx.Normals);
            for (int i = 0; i < cRear; i++) ApplyContact(ctx.Normals[i], sinH, cosH);

            // Reconstruct vehicle center and heading alignment from resolved axles
            if (cFront > 0 || cRear > 0)
            {
                Position = (frontPos + rearPos) * 0.5f;
                Vector2 resolvedFwd = frontPos - rearPos;
                if (resolvedFwd.sqrMagnitude > 0.01f)
                {
                    float wallHeading = Mathf.Atan2(resolvedFwd.x, resolvedFwd.y);
                    float angleDiff = Mathf.Repeat(wallHeading - Heading + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
                    Heading += Mathf.Clamp(angleDiff, -2.4f * dt, 2.4f * dt);
                }
            }

            // Dynamic traffic is a separate instanced renderer, so it is not part of PhysX's
            // static collision world. Resolve the player/vehicle contact in the same fixed tick
            // and feed the impulse into the car's suspension and yaw response.
            if (ctx.Manager != null && ctx.Manager.trafficSystem != null)
            {
                float impact = ctx.Manager.trafficSystem.ResolvePlayerVehicleCollision(
                    ref Position, ref Speed, ref Heading, ref Vx, ref Vz, ref AngularVelocity,
                    halfWidth, halfLength);
                if (impact > 1.2f)
                {
                    PitchVelocity -= Mathf.Min(14f, impact) * 0.18f; // Nose dips on forward collision
                    HeaveVelocity += Mathf.Min(1.6f, impact * 0.14f);
                    RollVelocity += Mathf.Sign(AngularVelocity != 0f ? AngularVelocity : 1f) * Mathf.Min(10f, impact * 0.16f);
                    CollisionSpark?.Invoke(new Vector3(Position.x, Height + 0.45f, Position.y),
                                           new Vector2(Mathf.Sin(Heading), Mathf.Cos(Heading)), impact);

                    if (impact >= 11.5f)
                    {
                        IsWrecked = true;
                        Speed *= 0.42f;
                        AngularVelocity = (AngularVelocity != 0f ? Mathf.Sign(AngularVelocity) : 1f) * Mathf.Clamp(impact * 0.28f, 3.0f, 5.2f);
                        PitchVelocity -= 4.5f;
                        HeaveVelocity += 1.8f;
                        Vector3 crashPos = new Vector3(Position.x, Height + 0.45f, Position.y);
                        OnCarWrecked?.Invoke(crashPos, impact);
                    }
                }
            }

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

        void TickWreck(float dt, ControllerContext ctx)
        {
            Throttle = 0f;
            SteerAngle = 0f;

            // Kinetic slide friction on asphalt: decelerate speed towards 0
            if (Speed > 0f) Speed = Mathf.Max(0f, Speed - 9.2f * dt);
            else if (Speed < 0f) Speed = Mathf.Min(0f, Speed + 9.2f * dt);

            Vx *= Mathf.Max(0f, 1f - dt * 6.5f);
            Vz *= Mathf.Max(0f, 1f - dt * 6.5f);

            // Spin-out yaw damped by tyre scrub friction
            Heading += AngularVelocity * dt;
            AngularVelocity *= Mathf.Max(0f, 1f - dt * 4.2f);

            // Continuous sparks against asphalt while sliding
            float currentSqrSpeed = Speed * Speed + Vx * Vx + Vz * Vz;
            if (currentSqrSpeed > 0.35f)
            {
                Vector2 sparkDir = new Vector2(Mathf.Sin(Heading), Mathf.Cos(Heading));
                CollisionSpark?.Invoke(new Vector3(Position.x, Height + 0.35f, Position.y), sparkDir, Mathf.Sqrt(currentSqrSpeed) * 0.8f);
            }

            float prevX = Position.x;
            float prevZ = Position.y;
            float sinH = Mathf.Sin(Heading);
            float cosH = Mathf.Cos(Heading);
            Position.x += (sinH * Speed + Vx) * dt;
            Position.y += (cosH * Speed + Vz) * dt;

            ctx.ClampToIsland(ref Position, false, ref Speed);

            // Resolve wall collisions while sliding so the wreck doesn't glitch through buildings
            Vector2 forwardDir = new Vector2(sinH, cosH);
            float axleOffset = Mathf.Max(0.5f, halfLength - halfWidth);
            Vector2 frontPos = Position + forwardDir * axleOffset;
            Vector2 rearPos = Position - forwardDir * axleOffset;

            int cFront = ctx.Collision.Resolve(ref frontPos, halfWidth, halfWidth, 1.0f, ctx.Normals);
            for (int i = 0; i < cFront; i++) ApplyContact(ctx.Normals[i], sinH, cosH);

            int cRear = ctx.Collision.Resolve(ref rearPos, halfWidth, halfWidth, 1.0f, ctx.Normals);
            for (int i = 0; i < cRear; i++) ApplyContact(ctx.Normals[i], sinH, cosH);

            if (cFront > 0 || cRear > 0)
            {
                Position = (frontPos + rearPos) * 0.5f;
            }

            Height = ctx.Ground(Position.x, Position.y);

            // 3-DOF spring-damper settling
            PitchVelocity += (-ChassisPitch * 45.0f - PitchVelocity * 8.0f) * dt;
            RollVelocity += (-ChassisRoll * 45.0f - RollVelocity * 8.0f) * dt;
            ChassisPitch = Mathf.Clamp(ChassisPitch + PitchVelocity * dt, -0.25f, 0.25f);
            ChassisRoll = Mathf.Clamp(ChassisRoll + RollVelocity * dt, -0.30f, 0.30f);

            HeaveVelocity += (-HeaveY * 85.0f - HeaveVelocity * 12.0f) * dt;
            HeaveY = Mathf.Clamp(HeaveY + HeaveVelocity * dt, -0.10f, 0.14f);

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
            if (vn <= 0.35f) return;

            float headOn = Mathf.Abs(sinH * n.x + cosH * n.y);
            float sideDot = cosH * n.x - sinH * n.y;
            float along = sinH * n.x + cosH * n.y;

            if (headOn > 0.65f)
            {
                // Head-on crash: bounce back and absorb energy
                Speed = -Speed * 0.22f;
                Vx += n.x * vn * 0.30f;
                Vz += n.y * vn * 0.30f;
            }
            else
            {
                // Glancing contact: scrape along the wall, slight friction, no violent sideways kick
                Speed *= Mathf.Max(0.78f, 1.0f - 0.38f * headOn);
                // Moderate deflection torque away from the wall
                AngularVelocity += sideDot * (along >= 0f ? -1f : 1f) * Mathf.Min(2.2f, vn * 0.16f);
            }

            PitchVelocity += along * Mathf.Min(12.0f, vn) * 0.12f;
            RollVelocity -= sideDot * Mathf.Min(12.0f, vn) * 0.16f;
            HeaveVelocity += Mathf.Min(1.0f, vn * 0.08f);

            if (vn > 1.8f)
            {
                var point = new Vector3(
                    Position.x - n.x * halfWidth * 0.95f,
                    0.56f,
                    Position.y - n.y * halfWidth * 0.95f);
                CollisionSpark?.Invoke(point, n, vn);
            }
        }

        public override void ResetMotion()
        {
            base.ResetMotion();
            IsWrecked = false;
            Vx = Vz = 0f;
            AngularVelocity = 0f;
            SteerAngle = 0f;
            PitchVelocity = RollVelocity = 0f;
            ChassisPitch = ChassisRoll = 0f;
            HeaveY = HeaveVelocity = 0f;
        }
    }
}
