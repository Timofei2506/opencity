using UnityEngine;

namespace OpenCity
{
    /// <summary>Third person on-foot character: camera relative movement, sprint, jump, vehicle transfer.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public CharacterController Controller;
        public HumanoidRig Rig;
        public Health Health;
        public PlayerCombat Combat;

        public bool InVehicle;
        public VehicleController Vehicle;
        public bool ControlEnabled = true;
        public bool Grounded;

        public Vector3 PlanarVelocity;
        public float BodyYaw;
        public bool Aiming { get; private set; }
        public bool Sprinting { get; private set; }

        float verticalVelocity;
        float stepTimer;
        Vector3 spawnPos;
        float spawnYaw;

        public float Speed => PlanarVelocity.magnitude;
        public Vector3 Chest => transform.position + Vector3.up * 1.35f;

        void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Controller.height = 1.8f;
            Controller.radius = 0.32f;
            Controller.center = new Vector3(0f, 0.92f, 0f);
            Controller.slopeLimit = 50f;
            Controller.stepOffset = 0.36f;
            Controller.skinWidth = 0.03f;
            Controller.minMoveDistance = 0f;

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.IsPlayer = true;
            Health.SetMax(100f);
            Health.Died += OnDied;

            Combat = GetComponent<PlayerCombat>();
            if (Combat == null) Combat = gameObject.AddComponent<PlayerCombat>();

            if (Rig == null) Rig = HumanoidRig.Create(transform, HumanoidStyle.Player(), true);
            spawnPos = transform.position;
        }

        void OnDestroy()
        {
            if (Health != null) Health.Died -= OnDied;
        }

        void OnDied(Health h)
        {
            if (Rig != null) Rig.SetDead(true);
            ControlEnabled = false;
            if (InVehicle) ExitVehicle(true);
        }

        // ------------------------------------------------------------------ loop

        void Update()
        {
            float dt = Time.deltaTime;
            if (GameManager.I != null && GameManager.I.Paused)
            {
                if (Rig != null) Rig.Animate(0f, true, false, 0f, dt);
                return;
            }

            if (InVehicle)
            {
                FollowVehicleSeat();
                if (Rig != null) Rig.Animate(0f, true, false, 0f, dt);
                return;
            }

            if (Health != null && Health.IsDead)
            {
                if (Rig != null) Rig.Animate(0f, true, false, 0f, dt);
                return;
            }

            Aiming = InputHub.AimHeld && ControlEnabled;
            Sprinting = InputHub.Sprint && !Aiming;

            Move(dt);

            if (Rig != null)
            {
                float aimPitch = CameraDirector.I != null ? CameraDirector.I.Pitch : 0f;
                Rig.Animate(PlanarVelocity.magnitude, Grounded, Aiming, -aimPitch * 0.35f, dt);
            }

            Footsteps(dt);
            TryEnterVehicle();
        }

        void Move(float dt)
        {
            Vector2 input = ControlEnabled ? InputHub.Move : Vector2.zero;
            if (input.sqrMagnitude > 1f) input.Normalize();

            Vector3 camFwd = CameraDirector.I != null ? CameraDirector.I.FlatForward : Vector3.forward;
            Vector3 camRight = CameraDirector.I != null ? CameraDirector.I.FlatRight : Vector3.right;
            Vector3 wishDir = camFwd * input.y + camRight * input.x;
            if (wishDir.sqrMagnitude > 1f * 1f) wishDir.Normalize();

            float targetSpeed = 0f;
            if (input.sqrMagnitude > 0.01f)
            {
                targetSpeed = Aiming ? GameConst.PlayerWalkSpeed * 0.9f
                            : Sprinting ? GameConst.PlayerSprintSpeed
                            : GameConst.PlayerRunSpeed;
                if (InputHub.Walk) targetSpeed = GameConst.PlayerWalkSpeed;
            }

            Vector3 targetVel = wishDir * targetSpeed;
            float accel = Grounded ? (input.sqrMagnitude > 0.01f ? 14f : 16f) : 4f;
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, targetVel, accel * dt);
            if (PlanarVelocity.magnitude < 0.05f) PlanarVelocity = Vector3.zero;

            // facing
            if (Aiming && CameraDirector.I != null)
            {
                BodyYaw = Mathf.LerpAngle(BodyYaw, CameraDirector.I.Yaw, dt * 16f);
            }
            else if (PlanarVelocity.sqrMagnitude > 0.05f)
            {
                float wanted = Mathf.Atan2(PlanarVelocity.x, PlanarVelocity.z) * Mathf.Rad2Deg;
                BodyYaw = Mathf.LerpAngle(BodyYaw, wanted, dt * 12f);
            }
            transform.rotation = Quaternion.Euler(0f, BodyYaw, 0f);

            // gravity + jump
            Grounded = Controller.isGrounded;
            if (Grounded && verticalVelocity < 0f) verticalVelocity = -2f;
            if (Grounded && InputHub.JumpDown && ControlEnabled)
            {
                verticalVelocity = GameConst.PlayerJumpSpeed;
                Grounded = false;
            }
            verticalVelocity -= GameConst.Gravity * dt;
            if (verticalVelocity < -55f) verticalVelocity = -55f;

            Vector3 motion = PlanarVelocity;
            motion.y = verticalVelocity;
            Controller.Move(motion * dt);
        }

        void Footsteps(float dt)
        {
            if (!Grounded || PlanarVelocity.magnitude < 0.6f) return;
            stepTimer -= dt * PlanarVelocity.magnitude;
            if (stepTimer <= 0f)
            {
                stepTimer = 2.4f;
                ProceduralAudio.PlayAt(ProceduralAudio.Footstep, transform.position, 0.18f, Random.Range(0.9f, 1.15f));
            }
        }

        void TryEnterVehicle()
        {
            if (!InputHub.InteractDown || !ControlEnabled) return;
            var gm = GameManager.I;
            if (gm == null) return;
            var v = gm.NearestVehicle(transform.position, 3.6f);
            if (v != null) EnterVehicle(v);
        }

        // ------------------------------------------------------------------ vehicles

        public void EnterVehicle(VehicleController v)
        {
            if (v == null || InVehicle) return;
            InVehicle = true;
            Vehicle = v;
            v.Enter(gameObject);
            Controller.enabled = false;
            if (Rig != null) Rig.SetVisible(false);
            FollowVehicleSeat();
            if (CameraDirector.I != null) CameraDirector.I.SetVehicle(v);
            if (GameManager.I != null) GameManager.I.ShowMessage(v.DisplayName + " - [F] exit", 2.2f);
        }

        public void ExitVehicle(bool silent = false)
        {
            if (!InVehicle) return;
            var v = Vehicle;
            InVehicle = false;
            Vehicle = null;
            transform.SetParent(null, true);

            Vector3 side = v != null ? v.transform.right * 1.9f : Vector3.right;
            Vector3 pos = (v != null ? v.transform.position : transform.position) + side + Vector3.up * 0.4f;

            Controller.enabled = false;
            transform.position = pos;
            Controller.enabled = true;
            PlanarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            if (Rig != null) Rig.SetVisible(true);
            if (v != null) v.Exit();
            if (CameraDirector.I != null) CameraDirector.I.SetVehicle(null);
            if (!silent && GameManager.I != null) GameManager.I.ShowMessage("On foot", 1.2f);
        }

        void FollowVehicleSeat()
        {
            if (Vehicle == null) return;
            transform.position = Vehicle.DriverSeat.position;
            transform.rotation = Vehicle.DriverSeat.rotation;
        }

        public void Teleport(Vector3 position, float yaw)
        {
            bool wasEnabled = Controller != null && Controller.enabled;
            if (Controller != null) Controller.enabled = false;
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            BodyYaw = yaw;
            if (Controller != null) Controller.enabled = wasEnabled || !InVehicle;
            PlanarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            if (Rig != null && Health != null && !Health.IsDead)
            {
                Rig.SetDead(false);
                Rig.SetVisible(!InVehicle);
            }
            ControlEnabled = true;
        }

        public void SetControlEnabled(bool value)
        {
            ControlEnabled = value;
            if (!value) PlanarVelocity = Vector3.zero;
        }
    }
}
