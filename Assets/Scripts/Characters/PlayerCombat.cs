using UnityEngine;

namespace OpenCity
{
    public enum WeaponType { Pistol = 0, Rifle = 1, Shotgun = 2 }

    /// <summary>Hitscan weapons with spread, recoil, reload, headshots and impact FX.</summary>
    public class PlayerCombat : MonoBehaviour
    {
        public struct WeaponDef
        {
            public string name;
            public float damage;
            public float rpm;
            public float spreadHip;
            public float spreadAim;
            public float range;
            public int pellets;
            public bool automatic;
            public int maxAmmo;
            public float recoil;
            public int clipSize;
        }

        public static WeaponDef Def(WeaponType t)
        {
            switch (t)
            {
                case WeaponType.Rifle:
                    return new WeaponDef
                    {
                        name = "Carbine", damage = 26f, rpm = 640f, spreadHip = 2.6f, spreadAim = 0.7f,
                        range = 220f, pellets = 1, automatic = true, maxAmmo = 400, recoil = 1.15f, clipSize = 30
                    };
                case WeaponType.Shotgun:
                    return new WeaponDef
                    {
                        name = "Pump Shotgun", damage = 13f, rpm = 85f, spreadHip = 5.5f, spreadAim = 4f,
                        range = 45f, pellets = 8, automatic = false, maxAmmo = 80, recoil = 2.4f, clipSize = 6
                    };
                default:
                    return new WeaponDef
                    {
                        name = "Pistol", damage = 24f, rpm = 340f, spreadHip = 1.9f, spreadAim = 0.45f,
                        range = 160f, pellets = 1, automatic = false, maxAmmo = 300, recoil = 0.75f, clipSize = 15
                    };
            }
        }

        public WeaponType Current = WeaponType.Pistol;
        readonly int[] ammo = { 68, 0, 0 };
        readonly int[] clip = { 15, 0, 0 };

        float cooldown;
        float reloadTimer;
        public bool Reloading => reloadTimer > 0f;
        public string CurrentName => Def(Current).name;

        PlayerController player;

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        public int Ammo(WeaponType t) => clip[(int)t] + ammo[(int)t];
        public int Clip(WeaponType t) => clip[(int)t];
        public int Reserve(WeaponType t) => ammo[(int)t];

        public void SetAmmo(WeaponType t, int total)
        {
            int i = (int)t;
            var d = Def(t);
            clip[i] = Mathf.Clamp(total, 0, d.clipSize);
            ammo[i] = Mathf.Max(0, total - clip[i]);
        }

        public void AddAmmo(WeaponType t, int amount)
        {
            var d = Def(t);
            int i = (int)t;
            ammo[i] = Mathf.Clamp(ammo[i] + amount, 0, d.maxAmmo);
        }

        public void SelectWeapon(WeaponType t)
        {
            if (Current == t) return;
            Current = t;
            reloadTimer = 0f;
            cooldown = 0.25f;
            if (GameManager.I != null) GameManager.I.ShowMessage(Def(t).name, 1.1f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (GameManager.I != null && GameManager.I.Paused) return;
            if (player == null) return;
            if (player.InVehicle) return;
            if (player.Health == null || player.Health.IsDead) return;

            if (InputHub.Weapon1Down) SelectWeapon(WeaponType.Pistol);
            if (InputHub.Weapon2Down) SelectWeapon(WeaponType.Rifle);
            if (InputHub.Weapon3Down) SelectWeapon(WeaponType.Shotgun);

            var def = Def(Current);
            int idx = (int)Current;

            if (cooldown > 0f) cooldown -= dt;
            if (reloadTimer > 0f)
            {
                reloadTimer -= dt;
                if (reloadTimer <= 0f) FinishReload();
            }

            if (InputHub.ReloadDown && !Reloading && clip[idx] < def.clipSize && ammo[idx] > 0)
                StartReload();

            bool wantFire = def.automatic ? InputHub.FireHeld : InputHub.FireDown;
            if (wantFire && cooldown <= 0f && !Reloading)
            {
                if (clip[idx] > 0) Fire(def);
                else if (ammo[idx] > 0) StartReload();
                else if (InputHub.FireDown) GameManager.I?.ShowMessage("Out of ammo");
            }
        }

        void StartReload()
        {
            reloadTimer = Current == WeaponType.Shotgun ? 2.1f : (Current == WeaponType.Rifle ? 1.9f : 1.35f);
            ProceduralAudio.PlayAt(ProceduralAudio.Reload, transform.position, 0.5f, 1f);
        }

        void FinishReload()
        {
            var def = Def(Current);
            int idx = (int)Current;
            int need = def.clipSize - clip[idx];
            int give = Mathf.Min(need, ammo[idx]);
            clip[idx] += give;
            ammo[idx] -= give;
        }

        void Fire(WeaponDef def)
        {
            int idx = (int)Current;
            clip[idx]--;
            cooldown = 60f / def.rpm;

            var cam = CameraDirector.I;
            if (cam == null) return;

            bool aiming = cam.Aiming;
            float spread = aiming ? def.spreadAim : def.spreadHip;
            Vector3 origin = cam.AimOrigin;
            Vector3 muzzle = MuzzlePosition();

            for (int p = 0; p < def.pellets; p++)
            {
                Vector3 dir = SpreadDirection(cam.AimDirection, spread);
                RaycastHit[] hits = Physics.RaycastAll(origin, dir, def.range, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                Vector3 endPoint = origin + dir * def.range;
                bool resolved = false;
                for (int i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    if (h.collider == null) continue;
                    if (h.collider.transform.IsChildOf(transform)) continue;      // ignore self
                    if (player != null && player.Vehicle != null &&
                        h.collider.transform.IsChildOf(player.Vehicle.transform)) continue;

                    endPoint = h.point;
                    resolved = true;

                    bool head = h.collider.gameObject.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    float damage = def.damage * (head ? 2.1f : 1f);

                    var hp = h.collider.GetComponentInParent<Health>();
                    if (hp != null)
                    {
                        hp.Damage(damage, h.point, dir, gameObject, head);
                        if (hp.IsPlayer == false && GameManager.I != null)
                        {
                            bool police = h.collider.GetComponentInParent<PoliceOfficer>() != null;
                            GameManager.I.RegisterCrime(h.point, police ? 0.9f : 0.35f);
                            if (hp.IsDead && police) GameManager.I.AddWanted(1.1f);
                        }
                    }
                    else
                    {
                        FxFactory.Impact(h.point, h.normal);
                    }

                    var rb = h.collider.attachedRigidbody;
                    if (rb != null && !rb.isKinematic)
                    {
                        rb.AddForceAtPosition(dir * def.damage * 12f, h.point, ForceMode.Impulse);
                    }
                    break;
                }

                if (!resolved) endPoint = origin + dir * Mathf.Min(def.range, 120f);

                FxFactory.Tracer(muzzle, endPoint);
            }

            FxFactory.MuzzleFlash(muzzle, cam.AimDirection);
            ProceduralAudio.PlayAt(ProceduralAudio.Gunshot, muzzle, 0.85f, Random.Range(0.94f, 1.06f));
            cam.AddRecoil(def.recoil * (aiming ? 0.6f : 1f), Random.Range(-0.35f, 0.35f) * def.recoil);

            if (GameManager.I != null)
                GameManager.I.AlertNearby(transform.position, 30f);
        }

        static Vector3 SpreadDirection(Vector3 forward, float degrees)
        {
            if (degrees <= 0.001f) return forward;
            float r = Mathf.Sqrt(Random.value) * degrees * Mathf.Deg2Rad;
            float a = Random.value * Mathf.PI * 2f;
            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, forward).normalized;
            return (forward + right * Mathf.Cos(a) * Mathf.Tan(r) + up * Mathf.Sin(a) * Mathf.Tan(r)).normalized;
        }

        Vector3 MuzzlePosition()
        {
            if (player != null && player.Rig != null && player.Rig.ForearmR != null)
                return player.Rig.ForearmR.position + player.Rig.ForearmR.forward * 0.22f + player.Rig.ForearmR.right * 0.06f;
            return transform.position + Vector3.up * 1.3f + transform.forward * 0.5f;
        }
    }
}
