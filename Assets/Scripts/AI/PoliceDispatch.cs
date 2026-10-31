using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>Foot patrol cop: chases the player, keeps distance and opens fire when in line of sight.</summary>
    public class PoliceOfficer : MonoBehaviour
    {
        public HumanoidRig Rig;
        public Health Health;

        CharacterController controller;
        float fireTimer;
        float strafeDir;
        bool dead;
        float calmTimer;

        public void Build()
        {
            controller = GetComponent<CharacterController>();
            if (controller == null) controller = gameObject.AddComponent<CharacterController>();
            controller.height = 1.78f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.32f;

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.SetMax(120f);
            Health.Died += OnDied;

            Rig = HumanoidRig.Create(transform, HumanoidStyle.Police());
            strafeDir = Random.value < 0.5f ? -1f : 1f;

            if (GameManager.I != null) GameManager.I.Register(this);
        }

        void OnDestroy()
        {
            if (Health != null) Health.Died -= OnDied;
            if (GameManager.I != null) GameManager.I.Unregister(this);
        }

        void OnDied(Health h)
        {
            dead = true;
            if (Rig != null) Rig.SetDead(true);
            if (controller != null) controller.enabled = false;
            if (GameManager.I != null)
            {
                GameManager.I.AddWanted(0.8f);
                GameManager.I.ShowMessage("Officer down!");
            }
            Go.DestroySafe(gameObject, 20f);
        }

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;

            var gm = GameManager.I;
            if (gm == null || gm.Player == null) return;

            // give up when the player is no longer wanted
            if (gm.Stars == 0)
            {
                calmTimer += dt;
                if (calmTimer > 14f)
                {
                    FxFactory.Dust(transform.position);
                    Go.DestroySafe(gameObject);
                    return;
                }
            }
            else calmTimer = 0f;

            Vector3 playerPos = gm.Player.transform.position;
            Vector3 flat = new Vector3(playerPos.x - transform.position.x, 0f, playerPos.z - transform.position.z);
            float dist = flat.magnitude;
            Vector3 dir = dist > 0.01f ? flat / dist : transform.forward;

            float moveSpeed = 0f;
            Vector3 motion = Vector3.zero;
            bool aiming = gm.Stars > 0;

            if (gm.Stars > 0)
            {
                if (dist > 15f)
                {
                    // close the distance at a run
                    moveSpeed = 5.4f;
                    motion = dir * moveSpeed;
                }
                else if (dist < 7f)
                {
                    // hold the line and strafe
                    motion = Vector3.Cross(Vector3.up, dir) * strafeDir * 2.2f;
                    moveSpeed = 2.2f;
                }
                else
                {
                    motion = Vector3.Cross(Vector3.up, dir) * strafeDir * 1.2f;
                    moveSpeed = 1.2f;
                }

                AimAt(playerPos, dt);
                TryShoot(gm, playerPos, dist);
            }
            else
            {
                Quaternion wanted = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, 200f * dt);
            }

            // sphere cast ahead so cops don't glue themselves to walls
            Vector3 move = motion;
            if (move.sqrMagnitude > 0.01f)
            {
                RaycastHit hit;
                if (Physics.SphereCast(transform.position + Vector3.up * 0.9f, 0.36f, move.normalized, out hit, 1.4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<Pedestrian>() == null)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, dir) * strafeDir;
                        move = (move + side * moveSpeed * 1.3f);
                        strafeDir = -strafeDir;
                    }
                }
            }

            move.y = -3f;
            if (controller != null && controller.enabled) controller.Move(move * dt);
            if (Rig != null) Rig.Animate(moveSpeed, true, aiming, 0f, dt);
        }

        void AimAt(Vector3 target, float dt)
        {
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            Quaternion wanted = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, 260f * dt);
        }

        void TryShoot(GameManager gm, Vector3 playerPos, float dist)
        {
            fireTimer -= Time.deltaTime;
            if (dist > 34f || fireTimer > 0f) return;

            Vector3 muzzle = Rig != null && Rig.ForearmR != null ? Rig.ForearmR.position : transform.position + Vector3.up * 1.3f;
            Vector3 chest = playerPos + Vector3.up * 1.2f;

            RaycastHit hit;
            bool clear = true;
            if (Physics.Linecast(muzzle, chest, out hit, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerController>() == null) clear = false;
            }
            if (!clear) return;

            fireTimer = Random.Range(0.7f, 1.5f);

            Vector3 dir = (chest - muzzle).normalized;
            float accuracy = Mathf.Clamp01(1f - dist / 40f);
            Vector3 aim = dir + Random.insideUnitSphere * (0.055f + 0.05f * (1f - accuracy));
            float range = 45f;

            RaycastHit[] hits = Physics.RaycastAll(muzzle, aim, range, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Vector3 end = muzzle + aim * range;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                end = h.point;
                var hp = h.collider.GetComponentInParent<Health>();
                if (hp != null && hp.IsPlayer)
                {
                    hp.Damage(Random.Range(5f, 11f), h.point, aim, gameObject);
                    if (CameraDirector.I != null) CameraDirector.I.Shake(0.35f);
                }
                else if (hp == null) FxFactory.Impact(h.point, h.normal);
                break;
            }

            FxFactory.Tracer(muzzle, end);
            FxFactory.MuzzleFlash(muzzle, aim);
            ProceduralAudio.PlayAt(ProceduralAudio.Gunshot, muzzle, 0.7f, Random.Range(0.92f, 1.06f));
        }
    }

    /// <summary>Spawns and despawns police units according to the wanted level.</summary>
    public class PoliceDispatch : MonoBehaviour
    {
        readonly List<PoliceOfficer> officers = new List<PoliceOfficer>();
        readonly List<VehicleController> cruisers = new List<VehicleController>();
        float spawnTimer;
        float cruiserTimer;
        float heat;

        public void OnCrime(Vector3 position)
        {
            heat = Mathf.Min(1f, heat + 0.35f);
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Player == null || gm.Roads == null) return;

            int stars = gm.Stars;
            officers.RemoveAll(o => o == null);
            cruisers.RemoveAll(c => c == null);

            if (stars == 0)
            {
                heat = Mathf.Max(0f, heat - Time.deltaTime * 0.2f);
                return;
            }

            int wantedUnits = Mathf.Min(1 + stars, 7);
            spawnTimer -= Time.deltaTime;
            if (officers.Count < wantedUnits && spawnTimer <= 0f)
            {
                spawnTimer = Mathf.Lerp(3.2f, 0.9f, stars / 5f);
                SpawnOfficer(gm);
            }

            if (stars >= 3)
            {
                cruiserTimer -= Time.deltaTime;
                int wantedCruisers = Mathf.Min(stars - 2, 3);
                if (cruisers.Count < wantedCruisers && cruiserTimer <= 0f)
                {
                    cruiserTimer = 9f;
                    SpawnCruiser(gm);
                }
            }
        }

        Vector3 FindSpawnPoint(GameManager gm, float minDist, float maxDist)
        {
            Vector3 dir;
            var rng = new System.Random(Random.Range(0, int.MaxValue));
            for (int i = 0; i < 20; i++)
            {
                Vector3 candidate = gm.Player.transform.position + new Vector3(
                    Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * Random.Range(minDist, maxDist);

                if (!gm.Roads.InCity(candidate)) continue;
                var node = gm.Roads.Nearest(candidate);
                if (node == null) continue;
                dir = Vector3.forward;
                return new Vector3(candidate.x, GameConst.SidewalkHeight + 0.1f, candidate.z);
            }
            var fallback = gm.Roads.Nearest(gm.Player.transform.position);
            return fallback != null ? fallback.Pos + Vector3.up * 0.3f : gm.Player.transform.position + Vector3.forward * 30f;
        }

        void SpawnOfficer(GameManager gm)
        {
            Vector3 pos = FindSpawnPoint(gm, 26f, 46f);
            var go = new GameObject("PoliceOfficer");
            go.transform.SetParent(gm.WorldRoot != null ? gm.WorldRoot : null, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var officer = go.AddComponent<PoliceOfficer>();
            officer.Build();
            officers.Add(officer);
        }

        void SpawnCruiser(GameManager gm)
        {
            Vector3 dir;
            Vector3 pos = gm.Roads.RandomSpawn(new System.Random(Random.Range(0, int.MaxValue)), out dir);
            if (Vector3.Distance(pos, gm.Player.transform.position) > 120f) return;

            var car = CarFactory.Create(gm.WorldRoot, pos, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg,
                                        0, Color.white, false, true);
            var tc = car.gameObject.AddComponent<TrafficCar>();
            tc.IsPolice = true;
            tc.CruiseSpeed = 17f;
            tc.Pursuit = true;
            car.SetMode(VehicleMode.AI);
            cruisers.Add(car);
        }

        public void ResetAll()
        {
            for (int i = 0; i < officers.Count; i++) if (officers[i] != null) Go.DestroySafe(officers[i].gameObject);
            for (int i = 0; i < cruisers.Count; i++) if (cruisers[i] != null) Go.DestroySafe(cruisers[i].gameObject);
            officers.Clear();
            cruisers.Clear();
            heat = 0f;
        }
    }
}
