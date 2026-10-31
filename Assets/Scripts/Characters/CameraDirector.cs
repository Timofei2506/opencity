using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// One camera, two behaviours: an over-the-shoulder orbit while on foot and a
    /// spring-damped chase camera while driving. Also owns mouse look, camera collision,
    /// FOV blending, recoil and the aim ray used by the weapons.
    /// </summary>
    public class CameraDirector : MonoBehaviour
    {
        public static CameraDirector I { get; private set; }

        public Camera Cam;
        public PlayerController Player;
        public VehicleController Vehicle;

        public float Yaw;
        public float Pitch = 8f;

        [Header("Tuning")]
        public float MouseSensitivity = 2.6f;
        public float FootDistance = 4.1f;
        public float AimDistance = 2.0f;
        public float CarDistance = 7.4f;
        public float FootHeight = 1.45f;
        public float MinPitch = -55f;
        public float MaxPitch = 62f;

        float currentDistance;
        float recoilPitch, recoilYaw;
        float fov = 62f;
        Vector3 smoothPivot;
        Vector3 camPosSmooth;
        float shake;

        public bool Aiming { get; private set; }
        public Vector3 AimDirection => Cam != null ? Cam.transform.forward : Vector3.forward;
        public Vector3 AimOrigin => Cam != null ? Cam.transform.position : Vector3.zero;
        public Vector3 LookPoint { get; private set; }

        public Vector3 FlatForward
        {
            get
            {
                Vector3 f = Cam.transform.forward;
                f.y = 0f;
                return f.sqrMagnitude < 0.001f ? Vector3.forward : f.normalized;
            }
        }

        public Vector3 FlatRight
        {
            get
            {
                Vector3 r = Cam.transform.right;
                r.y = 0f;
                return r.sqrMagnitude < 0.001f ? Vector3.right : r.normalized;
            }
        }

        void Awake()
        {
            I = this;
            if (Cam == null) Cam = GetComponent<Camera>();
            if (Cam == null) Cam = GetComponentInChildren<Camera>();
            currentDistance = FootDistance;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        public void AddRecoil(float pitch, float yaw, float amount = 1f)
        {
            recoilPitch += pitch * amount;
            recoilYaw += yaw * amount;
            shake = Mathf.Min(1f, shake + amount * 0.5f);
        }

        public void Shake(float amount)
        {
            shake = Mathf.Min(1.4f, shake + amount);
        }

        public void SetVehicle(VehicleController v)
        {
            Vehicle = v;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (Player == null) return;

            bool driving = Vehicle != null;
            Vector3 pivotTarget;
            float wantedDistance;
            float wantedFov;

            if (driving)
            {
                pivotTarget = Vehicle.transform.position + Vector3.up * 1.55f - Vehicle.transform.forward * 0.4f;
                float speedK = Mathf.Clamp01(Vehicle.SpeedKmh / 150f);
                wantedDistance = CarDistance + speedK * 1.6f;
                wantedFov = Mathf.Lerp(64f, 78f, speedK);
                Aiming = false;
            }
            else
            {
                Aiming = Player.Aiming && !Player.Health.IsDead;
                Vector3 shoulder = Quaternion.Euler(0f, Yaw, 0f) * (Vector3.right * (Aiming ? 0.62f : 0.30f));
                pivotTarget = Player.transform.position + Vector3.up * FootHeight + shoulder;
                wantedDistance = Aiming ? AimDistance : FootDistance;
                wantedFov = Aiming ? 46f : 62f;
            }

            // mouse look
            if (!GameManager.I || !GameManager.I.Paused)
            {
                Vector2 look = InputHub.Look;
                Yaw += look.x * MouseSensitivity;
                Pitch -= look.y * MouseSensitivity;
                Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
            }

            // recoil decay
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, dt * 7f);
            recoilYaw = Mathf.Lerp(recoilYaw, 0f, dt * 7f);
            shake = Mathf.Lerp(shake, 0f, dt * 6f);

            float usePitch = Mathf.Clamp(Pitch - recoilPitch * 0.35f, MinPitch - 10f, MaxPitch + 10f);
            Quaternion rot = Quaternion.Euler(usePitch, Yaw + recoilYaw * 0.3f, 0f);

            smoothPivot = Vector3.Lerp(smoothPivot, pivotTarget, dt * (driving ? 12f : 22f));
            currentDistance = Mathf.Lerp(currentDistance, wantedDistance, dt * 8f);

            Vector3 back = rot * Vector3.back;
            Vector3 wanted = smoothPivot + back * currentDistance;

            // camera collision: keep the camera out of walls and vehicles
            RaycastHit hit;
            Vector3 dir = wanted - smoothPivot;
            float dist = dir.magnitude;
            if (dist > 0.01f && Physics.SphereCast(smoothPivot, 0.32f, dir.normalized, out hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerController>() == null &&
                    !(Vehicle != null && hit.collider.transform.IsChildOf(Vehicle.transform)))
                {
                    wanted = smoothPivot + dir.normalized * Mathf.Max(0.55f, hit.distance - 0.12f);
                }
            }

            float lag = driving ? 16f : 26f;
            camPosSmooth = Vector3.Lerp(camPosSmooth, wanted, dt * lag);
            if (camPosSmooth == Vector3.zero) camPosSmooth = wanted;

            Vector3 shakeOffset = new Vector3(
                (Mathf.PerlinNoise(Time.time * 31f, 0.3f) - 0.5f),
                (Mathf.PerlinNoise(0.7f, Time.time * 27f) - 0.5f), 0f) * (shake * 0.12f);

            Cam.transform.position = camPosSmooth + Cam.transform.rotation * shakeOffset;
            Cam.transform.rotation = rot;

            // aim point
            Vector3 aimTarget;
            if (Physics.Raycast(AimOrigin, AimDirection, out hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                aimTarget = hit.point;
            else
                aimTarget = AimOrigin + AimDirection * 400f;
            LookPoint = aimTarget;

            fov = Mathf.Lerp(fov, wantedFov, dt * 6f);
            Cam.fieldOfView = fov;

            // keep the sun's shadow distance reasonable relative to the camera
            if (Vehicle != null && Vehicle.Health != null && Vehicle.Health.IsDead)
            {
                // vehicle destroyed - eject the player
                Player.ExitVehicle(true);
            }
        }
    }
}
