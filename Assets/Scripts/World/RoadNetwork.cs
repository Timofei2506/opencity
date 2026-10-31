using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    public enum LightState { Green, Yellow, Red }

    /// <summary>
    /// Grid road network: intersections, lanes (right hand traffic), traffic light phases.
    /// Shared by the traffic AI, police dispatch and the spawn system.
    /// </summary>
    public class RoadNetwork
    {
        public class Intersection
        {
            public int ix, iz;
            public Vector3 Pos;
            public Intersection N, S, E, W;      // +Z, -Z, +X, -X
            public float Offset;                 // per junction phase offset
            public readonly List<TrafficLight> Lights = new List<TrafficLight>();

            public Intersection Get(Vector3 dir)
            {
                if (dir.x > 0.5f) return E;
                if (dir.x < -0.5f) return W;
                if (dir.z > 0.5f) return N;
                if (dir.z < -0.5f) return S;
                return null;
            }

            public IEnumerable<Intersection> All
            {
                get
                {
                    if (N != null) yield return N;
                    if (S != null) yield return S;
                    if (E != null) yield return E;
                    if (W != null) yield return W;
                }
            }
        }

        public const float Cycle = 30f;
        const float GreenTime = 12f;
        const float YellowTime = 3f;

        public static RoadNetwork I { get; private set; }

        public Intersection[,] Grid;
        public int CountX { get; private set; }
        public int CountZ { get; private set; }
        public float Clock { get; private set; }

        public static RoadNetwork Build()
        {
            var rn = new RoadNetwork();
            rn.CountX = GameConst.BlocksX + 1;
            rn.CountZ = GameConst.BlocksZ + 1;
            rn.Grid = new Intersection[rn.CountX, rn.CountZ];

            for (int i = 0; i < rn.CountX; i++)
            {
                for (int j = 0; j < rn.CountZ; j++)
                {
                    rn.Grid[i, j] = new Intersection
                    {
                        ix = i,
                        iz = j,
                        Pos = new Vector3(GameConst.RoadLineX(i), 0f, GameConst.RoadLineZ(j)),
                        Offset = (i * 3.1f + j * 5.7f) % Cycle,
                    };
                }
            }

            for (int i = 0; i < rn.CountX; i++)
            {
                for (int j = 0; j < rn.CountZ; j++)
                {
                    var n = rn.Grid[i, j];
                    if (i + 1 < rn.CountX) n.E = rn.Grid[i + 1, j];
                    if (i - 1 >= 0) n.W = rn.Grid[i - 1, j];
                    if (j + 1 < rn.CountZ) n.N = rn.Grid[i, j + 1];
                    if (j - 1 >= 0) n.S = rn.Grid[i, j - 1];
                }
            }

            I = rn;
            return rn;
        }

        public void Tick(float dt) => Clock += dt;

        public Intersection Get(int i, int j)
        {
            if (Grid == null) return null;
            if (i < 0 || j < 0 || i >= CountX || j >= CountZ) return null;
            return Grid[i, j];
        }

        public Intersection Nearest(Vector3 p)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(p.x / GameConst.CityCell + GameConst.BlocksX * 0.5f), 0, CountX - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt(p.z / GameConst.CityCell + GameConst.BlocksZ * 0.5f), 0, CountZ - 1);
            return Get(i, j);
        }

        public LightState GetState(Intersection node, bool alongX)
        {
            if (node == null) return LightState.Green;
            float t = Mathf.Repeat(Clock + node.Offset, Cycle);
            bool nsGreen = t < GreenTime;
            bool nsYellow = t >= GreenTime && t < GreenTime + YellowTime;
            bool ewGreen = t >= GreenTime + YellowTime && t < GreenTime + YellowTime + GreenTime;
            bool ewYellow = t >= GreenTime + YellowTime + GreenTime;

            // "alongX" == the car travels east/west, i.e. it obeys the east-west phase
            if (alongX)
            {
                if (ewGreen) return LightState.Green;
                if (ewYellow) return LightState.Yellow;
                return LightState.Red;
            }
            if (nsGreen) return LightState.Green;
            if (nsYellow) return LightState.Yellow;
            return LightState.Red;
        }

        public static Vector3 RightOf(Vector3 dir) => Vector3.Cross(Vector3.up, dir).normalized;

        /// <summary>Lane centre offset for right hand traffic.</summary>
        public static float LaneOffset => GameConst.RoadWidth * 0.24f;

        public Vector3 LanePoint(Vector3 a, Vector3 b, float t)
        {
            Vector3 dir = (b - a).normalized;
            return Vector3.Lerp(a, b, t) + RightOf(dir) * LaneOffset;
        }

        /// <summary>Random position on a road lane with a heading direction.</summary>
        public Vector3 RandomSpawn(System.Random rng, out Vector3 direction)
        {
            // Degenerate network (called before generation): keep the caller safe.
            if (CountX < 2 || CountZ < 2)
            {
                direction = Vector3.forward;
                return Vector3.zero;
            }

            bool horizontal = rng.NextDouble() < 0.5;
            if (horizontal)
            {
                int j = rng.Next(0, CountZ);
                int i = rng.Next(0, CountX - 1);
                var a = Get(i, j);
                var b = Get(i + 1, j);
                Vector3 dir = (b.Pos - a.Pos).normalized * (rng.NextDouble() < 0.5 ? 1f : -1f);
                Vector3 from = dir.x > 0 ? a.Pos : b.Pos;
                Vector3 to = dir.x > 0 ? b.Pos : a.Pos;
                float t = 0.25f + 0.5f * (float)rng.NextDouble();
                direction = dir;
                return Vector3.Lerp(from, to, t) + RightOf(dir) * LaneOffset;
            }
            else
            {
                int i = rng.Next(0, CountX);
                int j = rng.Next(0, CountZ - 1);
                var a = Get(i, j);
                var b = Get(i, j + 1);
                Vector3 dir = (b.Pos - a.Pos).normalized * (rng.NextDouble() < 0.5 ? 1f : -1f);
                Vector3 from = dir.z > 0 ? a.Pos : b.Pos;
                Vector3 to = dir.z > 0 ? b.Pos : a.Pos;
                float t = 0.25f + 0.5f * (float)rng.NextDouble();
                direction = dir;
                return Vector3.Lerp(from, to, t) + RightOf(dir) * LaneOffset;
            }
        }

        public bool InCity(Vector3 p)
        {
            float limit = GameConst.CityRadius + 10f;
            return Mathf.Abs(p.x) < limit && Mathf.Abs(p.z) < limit;
        }
    }

    /// <summary>Swaps shared red/amber/green materials according to the junction state.</summary>
    public class TrafficLight : MonoBehaviour
    {
        public RoadNetwork.Intersection Node;
        public bool AlongX;       // does this head face cars travelling along X?

        public Renderer redLamp, yellowLamp, greenLamp;

        static Material onRed, onYellow, onGreen, off;

        void Start()
        {
            if (onRed == null)
            {
                onRed = MaterialLibrary.Signal(new Color(1f, 0.10f, 0.08f), "red");
                onYellow = MaterialLibrary.Signal(new Color(1f, 0.72f, 0.08f), "yellow");
                onGreen = MaterialLibrary.Signal(new Color(0.20f, 1f, 0.40f), "green");
                off = MaterialLibrary.Plastic(new Color(0.05f, 0.05f, 0.05f), "lampoff");
            }
        }

        void Update()
        {
            if (Node == null || redLamp == null) return;
            var state = RoadNetwork.I != null ? RoadNetwork.I.GetState(Node, AlongX) : LightState.Green;
            switch (state)
            {
                case LightState.Red:
                    redLamp.sharedMaterial = onRed; yellowLamp.sharedMaterial = off; greenLamp.sharedMaterial = off;
                    break;
                case LightState.Yellow:
                    redLamp.sharedMaterial = off; yellowLamp.sharedMaterial = onYellow; greenLamp.sharedMaterial = off;
                    break;
                default:
                    redLamp.sharedMaterial = off; yellowLamp.sharedMaterial = off; greenLamp.sharedMaterial = onGreen;
                    break;
            }
        }
    }
}
