using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// Lane following traffic brain. Drives a kinematic VehicleController along right hand
    /// lanes, turns smoothly through junctions, obeys traffic lights, brakes for obstacles
    /// and recovers when stuck. Stealing the car simply disables this component.
    /// </summary>
    public class TrafficCar : MonoBehaviour
    {
        public bool IsPolice;
        public bool Pursuit;
        public float CruiseSpeed = 13f;

        VehicleController car;
        Rigidbody body;

        RoadNetwork.Intersection prev, next;
        readonly List<Vector3> path = new List<Vector3>(24);
        float speed;
        float stuckTimer;
        float steerVisual;

        public static Vector3 LastDir = Vector3.forward;

        void Awake()
        {
            car = GetComponent<VehicleController>();
            body = GetComponent<Rigidbody>();
        }

        void Start()
        {
            CruiseSpeed = Random.Range(10f, 16f);
            RecalculateRoute();
        }

        public void RecalculateRoute()
        {
            var roads = GameManager.I != null ? GameManager.I.Roads : RoadNetwork.I;
            if (roads == null) return;

            var node = roads.Nearest(transform.position);
            if (node == null) return;
            prev = node;
            next = PickNext(node, null);
            if (next == null) return;
            path.Clear();
            AppendSegment(node, next);
            speed = Mathf.Max(speed, 3f);
        }

        RoadNetwork.Intersection PickNext(RoadNetwork.Intersection from, RoadNetwork.Intersection cameFrom)
        {
            var options = new List<RoadNetwork.Intersection>(5);
            foreach (var n in from.All) if (n != null && n != cameFrom) options.Add(n);
            if (options.Count == 0)
            {
                foreach (var n in from.All) if (n != null) options.Add(n);
            }
            if (options.Count == 0) return null;

            // prefer going straight 55% of the time
            if (cameFrom != null)
            {
                Vector3 back = (from.Pos - cameFrom.Pos).normalized;
                if (Random.value < 0.55f)
                {
                    foreach (var n in options)
                        if (Vector3.Dot((n.Pos - from.Pos).normalized, back) > 0.9f) return n;
                }
            }
            return options[Random.Range(0, options.Count)];
        }

        void AppendSegment(RoadNetwork.Intersection a, RoadNetwork.Intersection b)
        {
            Vector3 dir = (b.Pos - a.Pos).normalized;
            Vector3 off = RoadNetwork.RightOf(dir) * RoadNetwork.LaneOffset;

            Vector3 start = a.Pos + off + dir * (GameConst.RoadWidth * 0.5f);
            Vector3 end = b.Pos + off - dir * (GameConst.RoadWidth * 0.5f);

            int steps = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(start, end) / 8f));
            for (int i = 0; i <= steps; i++)
                path.Add(Vector3.Lerp(start, end, (float)i / steps));
            LastDir = dir;
        }

        void AppendTurn(RoadNetwork.Intersection node, Vector3 dirIn, Vector3 dirOut)
        {
            Vector3 s = node.Pos + RoadNetwork.RightOf(dirIn) * RoadNetwork.LaneOffset;
            Vector3 e = node.Pos + RoadNetwork.RightOf(dirOut) * RoadNetwork.LaneOffset;
            if (Vector3.Distance(s, e) < 0.35f) return;   // straight through: nothing to blend

            Vector2 s2 = new Vector2(s.x, s.z);
            Vector2 e2 = new Vector2(e.x, e.z);
            Vector2 d1 = new Vector2(dirIn.x, dirIn.z);
            Vector2 d2 = new Vector2(dirOut.x, dirOut.z);

            Vector2 ctrl;
            float denom = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(denom) < 1e-4f)
            {
                ctrl = (s2 + e2) * 0.5f;
            }
            else
            {
                float t = ((e2.x - s2.x) * d2.y - (e2.y - s2.y) * d2.x) / denom;
                ctrl = s2 + d1 * Mathf.Clamp(t, -30f, 30f);
            }

            const int samples = 7;
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples;
                Vector2 p = (1 - t) * (1 - t) * s2 + 2 * (1 - t) * t * ctrl + t * t * e2;
                path.Add(new Vector3(p.x, 0f, p.y));
            }
        }

        void FixedUpdate()
        {
            if (car == null || car.Occupied || car.Health == null || car.Health.IsDead)
            {
                if (car != null) car.AiSpeed = 0f;
                return;
            }

            float dt = Time.fixedDeltaTime;

            if (Pursuit && Pursue(dt)) return;

            // ---------- path bookkeeping ----------
            while (path.Count > 0 && Vector3.Distance(Flat(path[0]), Flat(transform.position)) < 3.4f)
                path.RemoveAt(0);

            if (path.Count < 3)
            {
                if (next != null)
                {
                    var after = PickNext(next, prev);
                    if (after != null)
                    {
                        Vector3 dirIn = (next.Pos - prev.Pos).normalized;
                        Vector3 dirOut = (after.Pos - next.Pos).normalized;
                        AppendTurn(next, dirIn, dirOut);
                        AppendSegment(next, after);
                        prev = next;
                        next = after;
                    }
                }
                if (path.Count == 0) { RecalculateRoute(); return; }
            }

            // ---------- steering ----------
            Vector3 target = Flat(path[Mathf.Min(path.Count - 1, 1)]);
            Vector3 toTarget = target - Flat(transform.position);
            toTarget.y = 0f;
            float steerAngle = 0f;
            if (toTarget.sqrMagnitude > 0.04f)
            {
                Vector3 desired = toTarget.normalized;
                Quaternion wanted = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, 145f * dt);
                steerAngle = Vector3.SignedAngle(transform.forward, desired, Vector3.up);
            }

            // ---------- speed target ----------
            float targetSpeed = CruiseSpeed;

            // slow down for turns
            if (path.Count > 1)
            {
                Vector3 a = Flat(transform.position);
                Vector3 b = Flat(path[0]);
                Vector3 c = Flat(path[Mathf.Min(path.Count - 1, 2)]);
                float turn = Vector3.Angle(b - a, c - b);
                if (turn > 25f) targetSpeed *= Mathf.Clamp(1f - turn / 110f, 0.30f, 1f);
            }

            // traffic lights
            if (next != null)
            {
                Vector3 toNode = Flat(next.Pos) - Flat(transform.position);
                float d = toNode.magnitude;
                bool alongX = Mathf.Abs((next.Pos - prev.Pos).normalized.x) > 0.5f;
                var state = RoadNetwork.I != null ? RoadNetwork.I.GetState(next, alongX) : LightState.Green;
                if (d < 16f && d > 3.5f && state != LightState.Green) targetSpeed = 0f;
                if (d < 26f && state == LightState.Yellow && d > 12f) targetSpeed = Mathf.Min(targetSpeed, 6f);
            }

            // obstacles ahead (other cars, the player, pedestrians)
            Vector3 front = transform.position + Vector3.up * 0.75f + transform.forward * 2.1f;
            float lookAhead = 6f + speed * 1.5f;
            RaycastHit hit;
            if (Physics.Raycast(front, transform.forward, out hit, lookAhead, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(transform))
                {
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(0f, (hit.distance - 2.5f) * 1.6f));
                }
            }
            if (speed < 1f && targetSpeed < 1f) stuckTimer += dt;
            else stuckTimer = Mathf.Max(0f, stuckTimer - dt * 2f);

            if (stuckTimer > 7f)
            {
                stuckTimer = 0f;
                RecalculateRoute();
            }

            float accel = targetSpeed > speed ? 7f : 16f;
            speed = Mathf.MoveTowards(speed, targetSpeed, accel * dt);

            // ---------- move (kinematic) ----------
            Vector3 step = transform.forward * speed * dt;
            body.MovePosition(transform.position + step);
            body.MoveRotation(transform.rotation);

            car.AiSpeed = speed;
            steerVisual = Mathf.Lerp(steerVisual, Mathf.Clamp(steerAngle, -28f, 28f), dt * 6f);
            car.AiSteer = steerVisual;

            car.SpeedKmh = speed * 3.6f;
        }

        /// <summary>Direct pursuit of the player (police cruisers). Returns true when handled.</summary>
        bool Pursue(float dt)
        {
            var gm = GameManager.I;
            if (gm == null || gm.Player == null || gm.Stars == 0)
            {
                Pursuit = false;
                RecalculateRoute();
                return false;
            }

            Vector3 pp = gm.Player.transform.position;
            Vector3 to = Flat(pp) - Flat(transform.position);
            float d = to.magnitude;
            if (d < 0.01f) return true;
            Vector3 desired = to / d;

            Quaternion wanted = Quaternion.LookRotation(desired, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, 200f * dt);

            float targetSpeed = d > 20f ? 23f : (d > 9f ? 13f : 0f);

            RaycastHit hit;
            Vector3 front = transform.position + Vector3.up * 0.8f + transform.forward * 2.2f;
            if (Physics.Raycast(front, transform.forward, out hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(transform))
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(0f, hit.distance * 1.5f - 2.5f));
            }

            speed = Mathf.MoveTowards(speed, targetSpeed, 9f * dt);
            body.MovePosition(transform.position + transform.forward * speed * dt);
            body.MoveRotation(transform.rotation);

            car.AiSpeed = speed;
            car.SpeedKmh = speed * 3.6f;
            car.AiSteer = Mathf.Clamp(Vector3.SignedAngle(transform.forward, desired, Vector3.up), -30f, 30f);
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < path.Count - 1; i++) Gizmos.DrawLine(path[i], path[i + 1]);
        }
    }
}
