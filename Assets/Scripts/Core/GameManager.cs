using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// Central game state: money, wanted level, pause, respawn, registry of all live actors.
    /// Everything else talks to this through Instance.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        [Header("Scene references")]
        public Transform WorldRoot;
        public RoadNetwork Roads;
        public DayNightCycle Clock;
        public PlayerController Player;
        public CameraDirector CameraRig;
        public Hud Hud;
        public MissionSystem Missions;
        public PoliceDispatch Police;

        [Header("Player state")]
        public int Money = 500;
        public float Wanted;
        public int Kills;
        public int MissionsDone;

        [Header("Runtime")]
        public bool Paused;
        public bool ShowHelp;
        public float SessionTime;

        public readonly List<VehicleController> Vehicles = new List<VehicleController>();
        public readonly List<Pedestrian> Peds = new List<Pedestrian>();
        public readonly List<PoliceOfficer> Officers = new List<PoliceOfficer>();

        public Vector3 SpawnPoint;
        public float SpawnYaw;

        float wantedIdle;
        float deathTimer = -1f;

        public int Stars => GameConst.StarCount(Wanted);
        public PlayerController P => Player;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        void Start()
        {
            LockCursor(true);
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            SessionTime += Time.deltaTime;

            if (InputHub.PauseDown)
            {
                SetPaused(!Paused);
            }

            if (Paused)
            {
                if (InputHub.SaveDown) SaveGame();
                if (InputHub.LoadDown) LoadGame();
                return;
            }

            if (InputHub.HelpDown) ShowHelp = !ShowHelp;
            if (InputHub.SaveDown) SaveGame();
            if (InputHub.LoadDown) LoadGame();

            // wanted level decay (slowly, and only when nothing criminal happened recently)
            wantedIdle += Time.deltaTime;
            if (Wanted > 0f && wantedIdle > 10f)
            {
                Wanted = Mathf.Max(0f, Wanted - (0.030f + 0.012f * (5f - Mathf.Min(5f, Wanted))) * Time.deltaTime);
            }

            if (Roads != null) Roads.Tick(Time.deltaTime);

            // player death / respawn
            if (Player != null && Player.Health != null && Player.Health.IsDead)
            {
                if (deathTimer < 0f) deathTimer = 4f;
                deathTimer -= Time.deltaTime;
                if (deathTimer <= 0f)
                {
                    deathTimer = -1f;
                    RespawnPlayer();
                }
            }
            else deathTimer = -1f;

            // safety net: if the player somehow falls through the world, put him back
            if (Player != null && Player.transform.position.y < -25f) RespawnPlayer();
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            LockCursor(!paused);
            InputHub.Blocked = paused;
            if (Hud != null) Hud.ShowPauseMenu(paused);
        }

        // ------------------------------------------------------------------ messaging

        public void ShowMessage(string text, float duration = 3.2f)
        {
            if (Hud != null) Hud.ShowMessage(text, duration);
        }

        public void SetObjective(string text)
        {
            if (Hud != null) Hud.SetObjective(text);
        }

        // ------------------------------------------------------------------ crime & economy

        public void AddMoney(int amount, string reason = null)
        {
            Money = Mathf.Max(0, Money + amount);
            if (!string.IsNullOrEmpty(reason)) ShowMessage(reason + "  " + (amount >= 0 ? "+$" : "-$") + Mathf.Abs(amount));
        }

        public void AddWanted(float amount, string reason = null)
        {
            float before = Wanted;
            Wanted = Mathf.Clamp(Wanted + amount, 0f, 5f);
            wantedIdle = 0f;
            if (Wanted > before + 0.05f && !string.IsNullOrEmpty(reason))
                ShowMessage("WANTED: " + reason);
        }

        public void ClearWanted()
        {
            Wanted = 0f;
            ShowMessage("You are no longer wanted.");
        }

        public void RegisterCrime(Vector3 position, float wantedAmount, bool alertCivilians = true)
        {
            AddWanted(wantedAmount);
            if (!alertCivilians) return;
            for (int i = 0; i < Peds.Count; i++)
            {
                var p = Peds[i];
                if (p != null && Vector3.Distance(p.transform.position, position) < 35f) p.Flee(position);
            }
        }

        public void OnCivilianKilled(Vector3 pos)
        {
            Kills++;
            AddWanted(0.55f);
            AddMoney(-25);
            ShowMessage("Civilian down. The police are after you.");
            AlertNearby(pos, 40f);
        }

        public void AlertNearby(Vector3 pos, float radius)
        {
            for (int i = 0; i < Peds.Count; i++)
            {
                var p = Peds[i];
                if (p != null && Vector3.Distance(p.transform.position, pos) < radius) p.Flee(pos);
            }
            if (Police != null) Police.OnCrime(pos);
        }

        // ------------------------------------------------------------------ registries

        public void Register(VehicleController v)
        {
            if (v != null && !Vehicles.Contains(v)) Vehicles.Add(v);
        }

        public void Unregister(VehicleController v) => Vehicles.Remove(v);

        public void Register(Pedestrian p)
        {
            if (p != null && !Peds.Contains(p)) Peds.Add(p);
        }

        public void Unregister(Pedestrian p) => Peds.Remove(p);

        public void Register(PoliceOfficer o)
        {
            if (o != null && !Officers.Contains(o)) Officers.Add(o);
        }

        public void Unregister(PoliceOfficer o) => Officers.Remove(o);

        public VehicleController NearestVehicle(Vector3 from, float maxDistance)
        {
            VehicleController best = null;
            float bestScore = maxDistance;
            for (int i = 0; i < Vehicles.Count; i++)
            {
                var v = Vehicles[i];
                if (v == null || v.Occupied) continue;
                float d = Vector3.Distance(v.transform.position, from);
                if (d < bestScore)
                {
                    bestScore = d;
                    best = v;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ respawn

        public void RespawnPlayer()
        {
            if (Player == null) return;
            if (Player.Vehicle != null) Player.ExitVehicle(true);

            int fee = Mathf.Min(200, Money);
            Money -= fee;
            Wanted = 0f;
            wantedIdle = 0f;
            Player.Teleport(SpawnPoint, SpawnYaw);
            Player.Health.Revive(100f);
            if (Police != null) Police.ResetAll();
            ShowMessage(fee > 0 ? "You woke up at the hospital.  -$" + fee : "You woke up at the hospital.");
        }

        // ------------------------------------------------------------------ save / load

        public SaveData CaptureState()
        {
            var d = new SaveData();
            d.money = Money;
            d.wanted = Wanted;
            d.hour = Clock != null ? Clock.Hour : 15f;
            d.missionsDone = MissionsDone;
            if (Player != null)
            {
                var t = Player.transform;
                d.posX = t.position.x; d.posY = t.position.y; d.posZ = t.position.z;
                d.yaw = t.eulerAngles.y;
                if (Player.Health != null)
                {
                    d.health = Player.Health.HP;
                    d.armor = Player.Health.Armor;
                }
                var combat = Player.GetComponent<PlayerCombat>();
                if (combat != null)
                {
                    d.ammoPistol = combat.Ammo(WeaponType.Pistol);
                    d.ammoRifle = combat.Ammo(WeaponType.Rifle);
                    d.weapon = (int)combat.Current;
                }
            }
            if (Missions != null)
            {
                d.missionActive = Missions.Active;
                d.missionTimeLeft = Missions.TimeLeft;
                var target = Missions.TargetPoint;
                d.missionTargetX = target.x;
                d.missionTargetZ = target.z;
            }
            return d;
        }

        public void SaveGame()
        {
            var d = CaptureState();
            if (SaveSystem.Save(d)) ShowMessage("Game saved.");
            else ShowMessage("Save failed (see log).");
        }

        public void LoadGame()
        {
            var d = SaveSystem.Load();
            if (d == null)
            {
                ShowMessage("No save file found.");
                return;
            }
            ApplyState(d);
            ShowMessage("Game loaded.");
        }

        public void ApplyState(SaveData d)
        {
            Money = d.money;
            Wanted = d.wanted;
            wantedIdle = 0f;
            MissionsDone = d.missionsDone;
            if (Clock != null) Clock.SetHour(d.hour);
            if (Player != null)
            {
                if (Player.Vehicle != null) Player.ExitVehicle(true);
                Player.Teleport(new Vector3(d.posX, d.posY, d.posZ), d.yaw);
                if (Player.Health != null)
                {
                    Player.Health.Revive(Mathf.Max(20f, d.health));
                    Player.Health.SetArmor(d.armor);
                }
                var combat = Player.GetComponent<PlayerCombat>();
                if (combat != null)
                {
                    combat.SetAmmo(WeaponType.Pistol, d.ammoPistol);
                    combat.SetAmmo(WeaponType.Rifle, d.ammoRifle);
                    combat.SelectWeapon((WeaponType)Mathf.Clamp(d.weapon, 0, 2));
                }
            }
            if (Missions != null)
            {
                if (d.missionActive) Missions.RestoreMission(new Vector3(d.missionTargetX, 0f, d.missionTargetZ), d.missionTimeLeft);
                else Missions.Cancel();
            }
        }
    }
}
