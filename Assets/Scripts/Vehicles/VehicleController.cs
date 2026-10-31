using UnityEngine;

namespace OpenCity
{
    public enum VehicleMode { Parked, AI, Player }

    /// <summary>
    /// Arcade-realistic car: 4 WheelColliders, torque curve, 5 speed automatic gearbox,
    /// speed sensitive steering, anti roll bars, downforce, handbrake drifting, engine sound,
    /// damage and explosions. AI cars are driven kinematically by TrafficCar while the same
    /// object stays fully drivable once the player steals it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        [Header("Refs")]
        public Rigidbody Body;
        public Health Health;
        public Transform DriverSeat;
        public WheelCollider WheelFL, WheelFR, WheelRL, WheelRR;
        public Transform VisFL, VisFR, VisRL, VisRR;
        public Light HeadLightL, HeadLightR;

        [Header("Identity")]
        public string DisplayName = "Sedan";
        public Color Paint = Color.gray;
        public VehicleMode Mode = VehicleMode.Parked;

        [Header("Engine")]
        public float MaxMotorTorque = 2100f;
        public float MaxSteerAngle = 34f;
        public float BrakeTorque = 3600f;
        public float HandbrakeTorque = 5200f;
        public readonly float[] GearRatios = { 3.35f, 2.10f, 1.42f, 1.02f, 0.79f };
        public float FinalDrive = 3.55f;
        public float[] GearSpeeds = { 33f, 58f, 88f, 122f, 175f };

        [Header("State")]
        public bool Occupied;
        public bool EngineRunning = true;
        public bool IsPlayerCar;
        public float SpeedKmh;
        public float CurrentGear;
        public float Rpm = 800f;
        public bool HeadlightsOn;

        // AI visual state
        public float AiSteer;
        public float AiSpeed;

        float steerAngle, throttle, rpmSmooth, brakeInput;
        AudioSource engineSource, screechSource, sirenSource;
        bool handbrakeLatch;
        float smokeTimer;

        public float Speed => Body != null ? Body.velocity.magnitude : 0f;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (Health == null) Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.SetMax(180f);
            Health.Died += OnDestroyed;
        }

        void OnDestroy()
        {
            if (Health != null) Health.Died -= OnDestroyed;
            if (engineSource != null) Go.DestroySafe(engineSource.gameObject);
        }

        void Start()
        {
            if (GameManager.I != null) GameManager.I.Register(this);
            SetMode(Mode);
        }

        // ------------------------------------------------------------------ modes

        public void SetMode(VehicleMode mode)
        {
            Mode = mode;
            if (Body == null) return;

            bool physics = mode == VehicleMode.Player;
            Body.isKinematic = !physics;
            Body.detectCollisions = true;

            var wheels = new[] { WheelFL, WheelFR, WheelRL, WheelRR };
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                wheels[i].enabled = physics;
                wheels[i].brakeTorque = 0f;
                wheels[i].motorTorque = 0f;
            }

            if (physics)
            {
                Body.WakeUp();
                Body.drag = 0.045f;
                Body.angularDrag = 0.9f;
                Body.centerOfMass = new Vector3(0f, 0.34f, 0f);
                if (engineSource == null && ProceduralAudio.Engine != null)
                {
                    var src = ProceduralAudio.PlayLooping(ProceduralAudio.Engine, transform, 0.5f, 1f);
                    engineSource = src;
                    src.minDistance = 5f;
                    src.maxDistance = 90f;
                }
                if (engineSource != null) engineSource.Play();
            }
            else if (engineSource != null)
            {
                engineSource.Stop();
            }

            if (mode == VehicleMode.Parked)
            {
                Body.velocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            var tc = GetComponent<TrafficCar>();
            SetSiren(mode == VehicleMode.AI && tc != null && tc.IsPolice);
        }

        public void SetSiren(bool on)
        {
            if (on)
            {
                if (sirenSource == null)
                {
                    sirenSource = ProceduralAudio.PlayLooping(ProceduralAudio.Siren, transform, 0.22f, 1f);
                    sirenSource.minDistance = 12f;
                    sirenSource.maxDistance = 140f;
                }
                else if (!sirenSource.isPlaying) sirenSource.Play();
            }
            else if (sirenSource != null && sirenSource.isPlaying)
            {
                sirenSource.Stop();
            }
        }

        public void Enter(GameObject driver)
        {
            Occupied = true;
            IsPlayerCar = true;
            if (GameManager.I != null)
            {
                var tc = GetComponent<TrafficCar>();
                if (tc != null) tc.enabled = false;
                SetSiren(false);
            }
            SetMode(VehicleMode.Player);
            SetHeadlights(true);
        }

        public void Exit()
        {
            Occupied = false;
            IsPlayerCar = false;
            if (Body != null)
            {
                Body.velocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            SetMode(VehicleMode.Parked);
            SetHeadlights(false);
        }

        public void SetHeadlights(bool on)
        {
            HeadlightsOn = on;
            if (HeadLightL != null) HeadLightL.enabled = on;
            if (HeadLightR != null) HeadLightR.enabled = on;
        }

        // ------------------------------------------------------------------ player control

        void Update()
        {
            UpdateWheelVisuals();

            if (Mode != VehicleMode.Player) return;

            if (InputHub.HeadlightsDown) SetHeadlights(!HeadlightsOn);
            if (InputHub.HornDown) ProceduralAudio.PlayAt(ProceduralAudio.Horn, transform.position, 0.55f, 1f);

            if (engineSource != null)
            {
                float k = Mathf.Clamp01(rpmSmooth / 6800f);
                engineSource.pitch = Mathf.Lerp(0.62f, 2.3f, k);
                engineSource.volume = Mathf.Lerp(0.28f, 0.62f, Mathf.Clamp01(Mathf.Abs(throttle) + k * 0.6f));
            }

            // tyre screech when sliding
            float lateral = 0f;
            WheelHit hit;
            if (WheelRL != null && WheelRL.isGrounded && WheelRL.GetGroundHit(out hit)) lateral += Mathf.Abs(hit.sidewaysSlip);
            if (WheelRR != null && WheelRR.isGrounded && WheelRR.GetGroundHit(out hit)) lateral += Mathf.Abs(hit.sidewaysSlip);
            bool sliding = lateral > 0.32f && SpeedKmh > 22f;
            if (sliding)
            {
                if (screechSource == null) screechSource = ProceduralAudio.PlayLooping(ProceduralAudio.Screech, transform, 0f, 1f);
                if (!screechSource.isPlaying) screechSource.Play();
                screechSource.volume = Mathf.Lerp(screechSource.volume, 0.22f, Time.deltaTime * 6f);
            }
            else if (screechSource != null && screechSource.isPlaying)
            {
                screechSource.volume = Mathf.Lerp(screechSource.volume, 0f, Time.deltaTime * 8f);
                if (screechSource.volume < 0.02f) screechSource.Stop();
            }

            // smoke from heavily damaged cars
            if (Health != null && Health.Fraction < 0.35f)
            {
                smokeTimer -= Time.deltaTime;
                if (smokeTimer <= 0f)
                {
                    smokeTimer = 0.12f;
                    FxFactory.TyreSmoke(transform.position + transform.forward * 1.7f + Vector3.up * 0.6f);
                }
            }
        }

        void FixedUpdate()
        {
            SpeedKmh = Speed * 3.6f;

            if (Mode != VehicleMode.Player || Body == null)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;

            // ---- input ----
            bool blocked = GameManager.I != null && GameManager.I.Paused;
            float steerInput = blocked ? 0f : Input.GetAxisRaw("Horizontal");
            float throttleInput = blocked ? 0f : Input.GetAxisRaw("Vertical");
            bool handbrake = !blocked && InputHub.Handbrake;
            handbrakeLatch = handbrake;

            // ---- speed sensitive steering ----
            float speedFactor = Mathf.Clamp01(SpeedKmh / 120f);
            float maxSteer = Mathf.Lerp(MaxSteerAngle, 12f, speedFactor);
            steerAngle = Mathf.Lerp(steerAngle, steerInput * maxSteer, dt * 9f);
            if (WheelFL != null) WheelFL.steerAngle = steerAngle;
            if (WheelFR != null) WheelFR.steerAngle = steerAngle;

            // ---- gearbox ----
            UpdateGearbox(out throttle);

            // ---- drive forces ----
            float reverseFactor = Vector3.Dot(Body.velocity, transform.forward) < -1f ? 0.55f : 1f;
            float torque = throttle * MaxMotorTorque * CurrentGearFactor() * reverseFactor;

            if (WheelRL != null) WheelRL.motorTorque = torque * 0.55f;
            if (WheelRR != null) WheelRR.motorTorque = torque * 0.55f;
            if (WheelFL != null) WheelFL.motorTorque = torque * 0.24f;
            if (WheelFR != null) WheelFR.motorTorque = torque * 0.24f;

            // ---- brakes ----
            bool braking = (throttleInput < -0.1f && Vector3.Dot(Body.velocity, transform.forward) > 1f) ||
                           (throttleInput > 0.1f && Vector3.Dot(Body.velocity, transform.forward) < -1f);
            brakeInput = Mathf.Lerp(brakeInput, braking || Mathf.Abs(throttleInput) < 0.05f ? 1f : 0f, dt * 6f);

            float brake = handbrake
                ? (WheelRL != null ? HandbrakeTorque : 0f)
                : BrakeTorque * brakeInput * (Mathf.Abs(throttleInput) < 0.05f ? 0.35f : 1f);

            SetBrake(WheelFL, brake);
            SetBrake(WheelFR, brake);
            if (handbrake)
            {
                SetBrake(WheelRL, HandbrakeTorque);
                SetBrake(WheelRR, HandbrakeTorque);
            }
            else
            {
                SetBrake(WheelRL, brake);
                SetBrake(WheelRR, brake);
            }

            // ---- grip / drift ----
            ApplyFriction(WheelFL, 1.55f, 1.35f);
            ApplyFriction(WheelFR, 1.55f, 1.35f);
            ApplyFriction(WheelRL, handbrake ? 0.85f : 1.5f, 1.45f);
            ApplyFriction(WheelRR, handbrake ? 0.85f : 1.5f, 1.45f);

            // ---- stability helpers ----
            AntiRoll(WheelFL, WheelFR, 9000f);
            AntiRoll(WheelRL, WheelRR, 7000f);

            if (SpeedKmh > 40f)
            {
                float downforce = Mathf.Clamp(SpeedKmh * 0.9f, 0f, 260f);
                Body.AddForce(-transform.up * downforce, ForceMode.Force);
            }

            // aerodynamic drag rising with speed (keeps top speed realistic)
            Body.AddForce(-Body.velocity.normalized * (Body.velocity.sqrMagnitude * 0.055f), ForceMode.Force);

            // ---- engine rpm (visual / audio) ----
            float wheelRpm = 0f;
            int grounded = 0;
            if (WheelRL != null && WheelRL.isGrounded) { wheelRpm += Mathf.Abs(WheelRL.rpm); grounded++; }
            if (WheelRR != null && WheelRR.isGrounded) { wheelRpm += Mathf.Abs(WheelRR.rpm); grounded++; }
            if (WheelFL != null && WheelFL.isGrounded) { wheelRpm += Mathf.Abs(WheelFL.rpm); grounded++; }
            if (WheelFR != null && WheelFR.isGrounded) { wheelRpm += Mathf.Abs(WheelFR.rpm); grounded++; }
            if (grounded > 0) wheelRpm /= grounded;

            float targetRpm = Mathf.Clamp(900f + wheelRpm * FinalDrive * CurrentGearRatio() * 2.6f, 850f, 6900f);
            if (Mathf.Abs(throttleInput) > 0.1f && SpeedKmh < 8f) targetRpm = Mathf.Max(targetRpm, 2200f);
            rpmSmooth = Mathf.Lerp(rpmSmooth, targetRpm, dt * 6f);
            Rpm = rpmSmooth;

            if (transform.position.y < -8f) Respawn();

            // push out of the world bounds so cars never leave the map
            Vector3 p = transform.position;
            float lim = GameConst.CityRadius + 30f;
            if (Mathf.Abs(p.x) > lim || Mathf.Abs(p.z) > lim) Respawn();
        }

        void UpdateGearbox(out float throttleOut)
        {
            float throttleInput = GameManager.I != null && GameManager.I.Paused ? 0f : Input.GetAxisRaw("Vertical");
            float forwardSpeed = Vector3.Dot(Body.velocity, transform.forward) * 3.6f;
            float kmh = Mathf.Abs(forwardSpeed);

            if (throttleInput < -0.1f && forwardSpeed < 1.5f)
            {
                // reverse gear
                CurrentGear = -1f;
                throttleOut = throttleInput;
                return;
            }

            if (CurrentGear < 0f) CurrentGear = 0f;
            int gear = Mathf.Clamp((int)CurrentGear, 0, GearSpeeds.Length - 1);

            if (kmh > GearSpeeds[gear] && gear < GearSpeeds.Length - 1)
            {
                CurrentGear = gear + 1;
            }
            else if (gear > 0 && kmh < GearSpeeds[gear - 1] * 0.68f)
            {
                CurrentGear = gear - 1;
            }

            throttleOut = throttleInput;
        }

        float CurrentGearRatio()
        {
            int g = Mathf.Clamp((int)Mathf.Max(0f, CurrentGear), 0, GearRatios.Length - 1);
            return GearRatios[g] * FinalDrive;
        }

        float CurrentGearFactor()
        {
            // torque falls off in higher gears, mimicking a real torque curve
            float g = Mathf.Clamp01(CurrentGear / (GearRatios.Length - 1f));
            return Mathf.Lerp(1.15f, 0.62f, g);
        }

        void SetBrake(WheelCollider w, float torque)
        {
            if (w != null) w.brakeTorque = torque;
        }

        static void ApplyFriction(WheelCollider w, float sideways, float forward)
        {
            if (w == null) return;
            var s = w.sidewaysFriction;
            s.stiffness = sideways;
            w.sidewaysFriction = s;
            var f = w.forwardFriction;
            f.stiffness = forward;
            w.forwardFriction = f;
        }

        void AntiRoll(WheelCollider left, WheelCollider right, float stiffness)
        {
            if (left == null || right == null || Body == null) return;

            WheelHit hit;
            float travelL = 1f, travelR = 1f;
            bool groundedL = left.GetGroundHit(out hit);
            if (groundedL) travelL = (-left.transform.InverseTransformPoint(hit.point).y - left.radius) / left.suspensionDistance;
            bool groundedR = right.GetGroundHit(out hit);
            if (groundedR) travelR = (-right.transform.InverseTransformPoint(hit.point).y - right.radius) / right.suspensionDistance;

            float force = (travelL - travelR) * stiffness;
            if (groundedL) Body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedR) Body.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        void UpdateWheelVisuals()
        {
            if (Mode == VehicleMode.Player && Body != null && !Body.isKinematic && WheelFL != null)
            {
                Pose(WheelFL, VisFL);
                Pose(WheelFR, VisFR);
                Pose(WheelRL, VisRL);
                Pose(WheelRR, VisRR);
            }
            else
            {
                // AI / parked: roll the visuals by hand
                float spin = AiSpeed / (2f * Mathf.PI * 0.34f) * 360f * Time.deltaTime;
                RollVisual(VisFL, AiSteer, spin);
                RollVisual(VisFR, AiSteer, spin);
                RollVisual(VisRL, 0f, spin);
                RollVisual(VisRR, 0f, spin);
            }
        }

        static void Pose(WheelCollider wheel, Transform visual)
        {
            if (wheel == null || visual == null) return;
            Vector3 pos;
            Quaternion rot;
            wheel.GetWorldPose(out pos, out rot);
            visual.position = pos;
            visual.rotation = rot;
        }

        static void RollVisual(Transform visual, float steer, float spinDegrees)
        {
            if (visual == null) return;
            visual.localRotation = Quaternion.Euler(spinDegrees, steer, 0f);
        }

        // ------------------------------------------------------------------ damage

        void OnDestroyed(Health h)
        {
            FxFactory.Explosion(transform.position + Vector3.up * 0.8f);
            if (IsPlayerCar && GameManager.I != null && GameManager.I.Player != null)
                GameManager.I.Player.ExitVehicle(true);
            if (GameManager.I != null) GameManager.I.AddWanted(0.4f);
            Occupied = false;
            SetMode(VehicleMode.Parked);
            if (Body != null) Body.isKinematic = true;

            // scorch the paint and leave a burnt out shell
            var renderers = GetComponentsInChildren<Renderer>();
            var burnt = MaterialLibrary.Plastic(new Color(0.09f, 0.08f, 0.075f), "burnt");
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterial = burnt;

            SetHeadlights(false);
            SetSiren(false);
        }

        public void Respawn()
        {
            if (Body == null) return;
            var gm = GameManager.I;
            Vector3 target;
            float yaw;

            if (Mode == VehicleMode.AI)
            {
                // the traffic brain owns the transform - let it re-route instead of teleporting
                var tc = GetComponent<TrafficCar>();
                if (tc != null) tc.RecalculateRoute();
                transform.position = transform.position + Vector3.up * 0.4f;
                Body.velocity = Vector3.zero;
                return;
            }

            Vector3 dir = Vector3.zero;
            target = gm != null && gm.Roads != null
                ? gm.Roads.RandomSpawn(new System.Random(Random.Range(0, 99999)), out dir)
                : Vector3.zero;
            yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            if (!GameConst.IsFinite(target)) target = Vector3.zero;
            transform.position = target + Vector3.up * 0.6f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Body.velocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }
    }
}
