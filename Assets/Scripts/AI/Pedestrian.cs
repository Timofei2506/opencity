using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// Sidewalk pedestrian: walks a loop around its block, panics when it hears gunfire or
    /// sees a crime, and drops cash when killed.
    /// </summary>
    public class Pedestrian : MonoBehaviour
    {
        public HumanoidRig Rig;
        public Health Health;

        CharacterController controller;
        readonly List<Vector3> loop = new List<Vector3>();
        int targetIndex;
        float speed;
        float fleeTimer;
        Vector3 threat;
        bool dead;
        Renderer[] cachedRenderers;
        float lodTimer;

        public static int CasualtyCount;

        public void Build(System.Random rng)
        {
            controller = GetComponent<CharacterController>();
            if (controller == null) controller = gameObject.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.30f;
            controller.center = new Vector3(0f, 0.88f, 0f);
            controller.stepOffset = 0.32f;
            controller.slopeLimit = 50f;
            controller.minMoveDistance = 0f;

            Health = GetComponent<Health>();
            if (Health == null) Health = gameObject.AddComponent<Health>();
            Health.SetMax(60f);
            Health.Died += OnDied;
            Health.Hit += (dmg, p, d) => ProceduralAudio.PlayAt(ProceduralAudio.Hit, p, 0.45f, Random.Range(0.9f, 1.1f));

            Rig = HumanoidRig.Create(transform, HumanoidStyle.Civilian(rng));
            cachedRenderers = GetComponentsInChildren<Renderer>();

            BuildLoop(rng);
            speed = 1.15f + 0.55f * (float)rng.NextDouble();
            targetIndex = rng.Next(loop.Count);
            transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            if (GameManager.I != null) GameManager.I.Register(this);
        }

        /// <summary>Rounded rectangle around the block's sidewalk.</summary>
        void BuildLoop(System.Random rng)
        {
            loop.Clear();
            float half = GameConst.BlockSize * 0.5f - 1.6f;
            Vector3 c = transform.position;
            c.y = GameConst.SidewalkHeight;
            float inset = 0.6f + 1.4f * (float)rng.NextDouble();
            float h = half - inset;

            const int perSide = 3;
            for (int i = 0; i < perSide; i++) loop.Add(c + new Vector3(Mathf.Lerp(-h, h, (float)i / perSide), 0f, -h));
            for (int i = 0; i < perSide; i++) loop.Add(c + new Vector3(h, 0f, Mathf.Lerp(-h, h, (float)i / perSide)));
            for (int i = 0; i < perSide; i++) loop.Add(c + new Vector3(Mathf.Lerp(h, -h, (float)i / perSide), 0f, h));
            for (int i = 0; i < perSide; i++) loop.Add(c + new Vector3(-h, 0f, Mathf.Lerp(h, -h, (float)i / perSide)));
        }

        void OnDestroy()
        {
            if (Health != null) Health.Died -= OnDied;
            if (GameManager.I != null) GameManager.I.Unregister(this);
        }

        void OnDied(Health h)
        {
            dead = true;
            CasualtyCount++;
            if (Rig != null) Rig.SetDead(true);
            if (controller != null) controller.enabled = false;
            if (GameManager.I != null)
            {
                GameManager.I.OnCivilianKilled(transform.position);
                GameManager.I.AddMoney(-10);
            }
            CashPickup.Spawn(transform.position + Vector3.up * 0.4f, Random.Range(15, 70));
            Go.DestroySafe(gameObject, 25f);
        }

        public void Flee(Vector3 from)
        {
            if (dead) return;
            threat = from;
            fleeTimer = Random.Range(4.5f, 8f);
        }

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;

            float distToPlayer = 999f;
            var player = GameManager.I != null ? GameManager.I.Player : null;
            if (player != null) distToPlayer = Vector3.Distance(player.transform.position, transform.position);

            // distance LOD: hide far away pedestrians to keep the draw calls low
            lodTimer -= dt;
            if (lodTimer <= 0f)
            {
                lodTimer = 0.5f;
                bool show = distToPlayer < 85f;
                if (Rig != null) Rig.SetVisible(show);
                if (controller != null) controller.enabled = distToPlayer < 160f && !dead;
            }

            if (fleeTimer > 0f)
            {
                fleeTimer -= dt;
                Vector3 away = transform.position - threat;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                MoveTowards(transform.position + away.normalized * 12f, 4.6f, dt);
                return;
            }

            if (loop.Count == 0) return;
            Vector3 target = loop[targetIndex % loop.Count];
            Vector3 flat = new Vector3(target.x, transform.position.y, target.z);
            if (Vector3.Distance(flat, transform.position) < 1.1f)
            {
                targetIndex = (targetIndex + 1) % loop.Count;
                return;
            }
            MoveTowards(flat, speed, dt);
        }

        void MoveTowards(Vector3 target, float moveSpeed, float dt)
        {
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            // avoid walking into walls / cars
            RaycastHit hit;
            if (Physics.SphereCast(transform.position + Vector3.up * 0.9f, 0.34f, dir, out hit, 1.6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<Pedestrian>() == null)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    dir = (dir + side * (Random.value < 0.5f ? 1f : -1f) * 1.2f).normalized;
                }
            }

            Quaternion wanted = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, 320f * dt);

            Vector3 motion = transform.forward * moveSpeed;
            motion.y = -2f;
            if (controller != null && controller.enabled) controller.Move(motion * dt);
            else transform.position += motion * dt;

            if (Rig != null) Rig.Animate(moveSpeed, true, false, 0f, dt);
        }
    }

    /// <summary>Spinning cash bundle. Collected by walking over it.</summary>
    public class CashPickup : MonoBehaviour
    {
        int amount;
        float life = 60f;

        public static void Spawn(Vector3 position, int amount)
        {
            var go = new GameObject("Cash");
            go.transform.position = position;
            var meshGo = Go.MeshObject("bill", go.transform,
                MeshKit.Box(new Vector3(0.22f, 0.02f, 0.12f)),
                MaterialLibrary.Plastic(new Color(0.35f, 0.72f, 0.35f), "cash"),
                Vector3.zero, null, null, false);
            var p = go.AddComponent<CashPickup>();
            p.amount = amount;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 2.2f;
            light.intensity = 0.6f;
            light.color = new Color(0.5f, 1f, 0.6f);
            light.enabled = false;
            p.meshTransform = meshGo.transform;
        }

        Transform meshTransform;

        void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f) { Go.DestroySafe(gameObject); return; }

            if (meshTransform != null)
            {
                meshTransform.localPosition = Vector3.up * (0.15f + Mathf.Sin(Time.time * 3f) * 0.06f);
                meshTransform.Rotate(Vector3.up, 110f * Time.deltaTime, Space.World);
            }

            var gm = GameManager.I;
            if (gm == null || gm.Player == null) return;
            if (Vector3.Distance(gm.Player.transform.position, transform.position) < 1.8f)
            {
                gm.AddMoney(amount, "Picked up");
                ProceduralAudio.PlayAt(ProceduralAudio.Cash, transform.position, 0.5f, Random.Range(0.95f, 1.1f));
                FxFactory.Cash(transform.position);
                Go.DestroySafe(gameObject);
            }
        }
    }

}
