using UnityEngine;

namespace Nyc.Gameplay
{
    /// <summary>
    /// Player pedestrian. Port of updatePerson() (controllers.js:973-1001).
    /// Walk 4.4 m/s, sprint 8.2, turn 2.6 rad/s, jump 5.2 m/s, gravity 14.5 m/s^2.
    /// </summary>
    public sealed class PedestrianController : TravelController
    {
        [Header("Movement")]
        public float walkSpeed = 4.4f;
        public float sprintSpeed = 8.2f;
        public float turnRate = 2.6f;
        public float radius = 0.48f;

        [Header("Jump")]
        public float jumpVelocity = 5.2f;
        public float gravity = 14.5f;

        [Header("Runtime")]
        public float VerticalVelocity;
        public bool IsGrounded;

        public override TravelMode Mode => TravelMode.Person;

        void Reset()
        {
            ChaseDistance = 4.3f;
            ChaseHeight = 1.95f;
            CameraTargetOffset = new Vector3(0f, 1.55f, 0f);
            LookUpBias = 1.55f;
        }

        public override void Tick(float dt, in ControllerInput input, ControllerContext ctx)
        {
            float maxWalk = input.Boost > 0.5f ? sprintSpeed : walkSpeed;

            float targetSpeed = 0f;
            if (input.Forward > 0f) targetSpeed = maxWalk * input.Forward;
            else if (input.Backward > 0f) targetSpeed = -maxWalk * 0.65f * input.Backward;

            if (input.Left > 0f) Heading += turnRate * dt * input.Left;
            if (input.Right > 0f) Heading -= turnRate * dt * input.Right;

            Speed += (targetSpeed - Speed) * Mathf.Min(1f, dt * 10f);

            Position.x += Mathf.Sin(Heading) * Speed * dt;
            Position.y += Mathf.Cos(Heading) * Speed * dt;

            ctx.ClampToIsland(ref Position, false, ref Speed);
            ctx.Collision.Resolve(ref Position, radius, radius, Height + 1f, ctx.Normals);

            float groundY = ctx.Ground(Position.x, Position.y);

            if (input.Up > 0.5f && Height <= groundY + 0.02f) VerticalVelocity = jumpVelocity;

            VerticalVelocity -= gravity * dt;
            Height += VerticalVelocity * dt;

            if (Height < groundY)
            {
                Height = groundY;
                VerticalVelocity = 0f;
            }

            IsGrounded = Height <= groundY + 0.02f;
        }

        public override void ResetMotion()
        {
            base.ResetMotion();
            VerticalVelocity = 0f;
        }
    }
}
