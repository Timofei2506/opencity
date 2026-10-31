using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>Procedural buildings and street furniture.</summary>
    public static class BuildingFactory
    {
        const float FacadeTile = 12f;   // one texture tile == 12 m == 4 floors / 4 windows

        public static BoxCollider Build(MeshBatcher batcher, Transform colliderRoot, Vector3 center,
                                        Vector2 footprint, float height, int style, System.Random rng)
        {
            var wallMat = MaterialLibrary.Facade(style);
            float w = footprint.x, d = footprint.y;

            // ---------------- tiers ----------------
            if (height > 36f && rng.NextDouble() < 0.65)
            {
                float lower = height * 0.55f;
                BuildTier(batcher, center, w, d, 0f, lower, wallMat);
                float upperW = w * 0.72f, upperD = d * 0.72f;
                Vector3 upperCenter = center + new Vector3(
                    (float)(rng.NextDouble() - 0.5) * (w - upperW) * 0.6f, 0f,
                    (float)(rng.NextDouble() - 0.5) * (d - upperD) * 0.6f);
                BuildTier(batcher, upperCenter, upperW, upperD, lower, height - lower, wallMat);
                BuildRoof(batcher, upperCenter, upperW, upperD, height, rng);
                BuildLedge(batcher, center, w, d, lower);
                AddStorefront(batcher, center, w, d, rng);
            }
            else
            {
                BuildTier(batcher, center, w, d, 0f, height, wallMat);
                BuildRoof(batcher, center, w, d, height, rng);
                AddStorefront(batcher, center, w, d, rng);
            }

            // ---------------- collider ----------------
            var go = new GameObject("BuildingCollider");
            go.transform.SetParent(colliderRoot, false);
            go.transform.position = new Vector3(center.x, height * 0.5f, center.z);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(w, height, d);
            return col;
        }

        /// <summary>Four facade walls from yBase to yBase + height (no roof, no floor).</summary>
        static void BuildTier(MeshBatcher b, Vector3 center, float w, float d, float yBase, float h, Material mat)
        {
            float cy = yBase + h * 0.5f;
            int uvW = Mathf.Max(1, Mathf.RoundToInt(w / FacadeTile));
            int uvD = Mathf.Max(1, Mathf.RoundToInt(d / FacadeTile));
            int uvH = Mathf.Max(1, Mathf.RoundToInt(h / FacadeTile));

            Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;

            // +Z (north) face -> normal +Z  =>  u=-X, v=+Y
            b.Add(MeshKit.QuadOriented(-X, Y, w * 0.5f, h * 0.5f, new Vector2(uvW, uvH)),
                  Matrix4x4.Translate(new Vector3(center.x, cy, center.z + d * 0.5f)), mat);
            // -Z face -> normal -Z => u=+X, v=+Y
            b.Add(MeshKit.QuadOriented(X, Y, w * 0.5f, h * 0.5f, new Vector2(uvW, uvH)),
                  Matrix4x4.Translate(new Vector3(center.x, cy, center.z - d * 0.5f)), mat);
            // +X face -> normal +X => u=+Z, v=+Y
            b.Add(MeshKit.QuadOriented(Z, Y, d * 0.5f, h * 0.5f, new Vector2(uvD, uvH)),
                  Matrix4x4.Translate(new Vector3(center.x + w * 0.5f, cy, center.z)), mat);
            // -X face -> normal -X => u=-Z, v=+Y
            b.Add(MeshKit.QuadOriented(-Z, Y, d * 0.5f, h * 0.5f, new Vector2(uvD, uvH)),
                  Matrix4x4.Translate(new Vector3(center.x - w * 0.5f, cy, center.z)), mat);
        }

        static void BuildLedge(MeshBatcher b, Vector3 center, float w, float d, float y)
        {
            var mat = MaterialLibrary.Concrete;
            b.Add(MeshKit.Box(new Vector3(w + 0.5f, 0.45f, d + 0.5f)), Matrix4x4.Translate(new Vector3(center.x, y, center.z)), mat);
        }

        static void BuildRoof(MeshBatcher b, Vector3 center, float w, float d, float y, System.Random rng)
        {
            // roof slab
            b.Add(MeshKit.GroundQuad(w, d, new Vector2(w / 4f, d / 4f)),
                  Matrix4x4.Translate(new Vector3(center.x, y + 0.02f, center.z)), MaterialLibrary.Roof);

            // parapet
            var concrete = MaterialLibrary.Concrete;
            float ph = 0.75f, pt = 0.32f;
            b.Add(MeshKit.Box(new Vector3(w, ph, pt)), Matrix4x4.Translate(new Vector3(center.x, y + ph * 0.5f, center.z + d * 0.5f - pt * 0.5f)), concrete);
            b.Add(MeshKit.Box(new Vector3(w, ph, pt)), Matrix4x4.Translate(new Vector3(center.x, y + ph * 0.5f, center.z - d * 0.5f + pt * 0.5f)), concrete);
            b.Add(MeshKit.Box(new Vector3(pt, ph, d)), Matrix4x4.Translate(new Vector3(center.x + w * 0.5f - pt * 0.5f, y + ph * 0.5f, center.z)), concrete);
            b.Add(MeshKit.Box(new Vector3(pt, ph, d)), Matrix4x4.Translate(new Vector3(center.x - w * 0.5f + pt * 0.5f, y + ph * 0.5f, center.z)), concrete);

            // rooftop clutter: AC units, vents, water tank, antenna
            int props = 1 + rng.Next(3);
            for (int i = 0; i < props; i++)
            {
                float px = center.x + (float)(rng.NextDouble() - 0.5) * (w - 3f);
                float pz = center.z + (float)(rng.NextDouble() - 0.5) * (d - 3f);
                float s = 1.0f + (float)rng.NextDouble() * 1.4f;
                b.Add(MeshKit.Box(new Vector3(s, 0.8f, s * 1.3f)),
                      Matrix4x4.Translate(new Vector3(px, y + 0.4f, pz)), MaterialLibrary.Metal);
                b.Add(MeshKit.Cylinder(0.16f, 0.5f, 8, true),
                      Matrix4x4.Translate(new Vector3(px + s * 0.5f, y + 1.0f, pz)), MaterialLibrary.Metal);
            }

            if (rng.NextDouble() < 0.35 && w > 8f)
            {
                Vector3 tankPos = new Vector3(center.x + w * 0.22f, y + 1.6f, center.z - d * 0.2f);
                b.Add(MeshKit.Cylinder(1.15f, 2.2f, 12, true), Matrix4x4.Translate(tankPos), MaterialLibrary.Metal);
                b.Add(MeshKit.Cylinder(0.1f, 2.4f, 6, true),
                      Matrix4x4.Translate(tankPos + new Vector3(0f, 1.6f, 0f)), MaterialLibrary.Metal);
            }

            if (rng.NextDouble() < 0.45)
            {
                float masts = 1 + rng.Next(2);
                for (int i = 0; i < masts; i++)
                {
                    Vector3 p = new Vector3(center.x + (float)(rng.NextDouble() - 0.5) * w * 0.7f, y + 2.4f,
                                           center.z + (float)(rng.NextDouble() - 0.5) * d * 0.7f);
                    b.Add(MeshKit.Cylinder(0.05f, 4.8f, 6, false), Matrix4x4.Translate(p), MaterialLibrary.Metal);
                }
            }

            // rooftop billboard facing the street
            if (rng.NextDouble() < 0.14 && w > 10f)
            {
                Vector3 bp = new Vector3(center.x, y + 3.4f, center.z + d * 0.5f - 0.4f);
                b.Add(MeshKit.Box(new Vector3(6.5f, 3.0f, 0.22f)), Matrix4x4.Translate(bp), MaterialLibrary.Metal);
                b.Add(MeshKit.QuadOriented(-Vector3.right, Vector3.up, 3.1f, 1.4f, Vector2.one),
                      Matrix4x4.Translate(bp + new Vector3(0f, 0f, 0.14f)), MaterialLibrary.Billboard);
                for (int i = -1; i <= 1; i += 2)
                    b.Add(MeshKit.Cylinder(0.08f, 2.2f, 6, false),
                          Matrix4x4.Translate(new Vector3(bp.x + i * 2.6f, y + 1.1f, bp.z)), MaterialLibrary.Metal);
            }
        }

        static void AddStorefront(MeshBatcher b, Vector3 center, float w, float d, System.Random rng)
        {
            var mat = MaterialLibrary.Storefront;
            float h = 3.3f;
            b.Add(MeshKit.Box(new Vector3(w + 0.18f, h, d + 0.18f)),
                  Matrix4x4.Translate(new Vector3(center.x, h * 0.5f, center.z)), mat);

            // canopy above the shop windows
            b.Add(MeshKit.Box(new Vector3(w + 1.1f, 0.16f, d + 1.1f)),
                  Matrix4x4.Translate(new Vector3(center.x, h + 0.1f, center.z)), MaterialLibrary.Concrete);

            // awnings on the street side
            if (rng.NextDouble() < 0.5)
            {
                Color[] awningColors =
                {
                    new Color(0.62f, 0.14f, 0.14f), new Color(0.12f, 0.32f, 0.22f),
                    new Color(0.16f, 0.22f, 0.44f), new Color(0.7f, 0.55f, 0.15f),
                };
                var aw = MaterialLibrary.Cloth(awningColors[rng.Next(awningColors.Length)]);
                float sign = rng.NextDouble() < 0.5 ? 1f : -1f;
                b.Add(MeshKit.Box(new Vector3(w * 0.55f, 0.12f, 1.5f)),
                      Matrix4x4.Translate(new Vector3(center.x, 2.9f, center.z + sign * (d * 0.5f + 0.7f))) *
                      Matrix4x4.Rotate(Quaternion.Euler(sign > 0 ? -12f : 12f, 0f, 0f)), aw);
            }
        }

        // ------------------------------------------------------------------ park

        public static void BuildPark(MeshBatcher b, Transform colliderRoot, Vector3 center, System.Random rng)
        {
            float size = GameConst.BlockSize - GameConst.SidewalkWidth * 2f;
            float y = GameConst.SidewalkHeight;

            // grass
            b.Add(MeshKit.Box(new Vector3(size, 0.12f, size)),
                  Matrix4x4.Translate(new Vector3(center.x, y + 0.06f, center.z)), MaterialLibrary.Grass);

            // crossing paths
            b.Add(MeshKit.GroundQuad(size, 4.5f, new Vector2(size / 3f, 1.5f)),
                  Matrix4x4.Translate(new Vector3(center.x, y + 0.13f, center.z)), MaterialLibrary.Sidewalk);
            b.Add(MeshKit.GroundQuad(4.5f, size, new Vector2(1.5f, size / 3f)),
                  Matrix4x4.Translate(new Vector3(center.x, y + 0.13f, center.z)), MaterialLibrary.Sidewalk);

            // fountain
            b.Add(MeshKit.Cylinder(4.2f, 0.85f, 20, true),
                  Matrix4x4.Translate(new Vector3(center.x, y + 0.4f, center.z)), MaterialLibrary.Concrete);
            b.Add(MeshKit.Cylinder(3.6f, 0.35f, 20, true),
                  Matrix4x4.Translate(new Vector3(center.x, y + 0.72f, center.z)),
                  MaterialLibrary.Plastic(new Color(0.22f, 0.38f, 0.45f), "water"));
            b.Add(MeshKit.Cylinder(0.45f, 2.2f, 12, true),
                  Matrix4x4.Translate(new Vector3(center.x, y + 1.6f, center.z)), MaterialLibrary.Concrete);

            var colliderGo = new GameObject("ParkCollider");
            colliderGo.transform.SetParent(colliderRoot, false);
            colliderGo.transform.position = new Vector3(center.x, y + 0.06f, center.z);
            var col = colliderGo.AddComponent<BoxCollider>();
            col.size = new Vector3(size, 0.24f, size);

            var fountainCollider = new GameObject("FountainCollider");
            fountainCollider.transform.SetParent(colliderRoot, false);
            fountainCollider.transform.position = new Vector3(center.x, y + 0.5f, center.z);
            var fcol = fountainCollider.AddComponent<CapsuleCollider>();
            fcol.radius = 4.2f;
            fcol.height = 1.4f;

            // trees around the lawn
            for (int i = 0; i < 10; i++)
            {
                float angle = (float)(i / 10.0 * Mathf.PI * 2f) + (float)rng.NextDouble() * 0.3f;
                float radius = size * (0.30f + 0.12f * (float)rng.NextDouble());
                Vector3 p = center + new Vector3(Mathf.Cos(angle) * radius, y + 0.12f, Mathf.Sin(angle) * radius);
                PropFactory.Tree(b, p, 0.85f + (float)rng.NextDouble() * 0.5f);
            }

            // benches facing the fountain
            for (int i = 0; i < 6; i++)
            {
                float angle = i / 6f * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(angle) * 6.5f, y + 0.12f, Mathf.Sin(angle) * 6.5f);
                PropFactory.Bench(b, p, angle * Mathf.Rad2Deg + 90f);
            }
        }

        public static void BuildParkingLot(MeshBatcher b, Transform colliderRoot, Vector3 center, System.Random rng,
                                           System.Action<Vector3, float> spawnCar)
        {
            float size = GameConst.BlockSize - GameConst.SidewalkWidth * 2f;
            float y = GameConst.SidewalkHeight;
            b.Add(MeshKit.Box(new Vector3(size, 0.1f, size)),
                  Matrix4x4.Translate(new Vector3(center.x, y + 0.05f, center.z)), MaterialLibrary.Road);

            var go = new GameObject("LotCollider");
            go.transform.SetParent(colliderRoot, false);
            go.transform.position = new Vector3(center.x, y + 0.05f, center.z);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size, 0.2f, size);

            // parking bay markings
            int bays = 6;
            for (int i = 0; i < bays; i++)
            {
                float x = center.x - size * 0.4f + i * (size * 0.8f / (bays - 1));
                b.Add(MeshKit.GroundQuad(0.12f, 5f, Vector2.one),
                      Matrix4x4.Translate(new Vector3(x, y + 0.12f, center.z)), MaterialLibrary.WhiteLine);
            }

            for (int i = 0; i < 4; i++)
            {
                float x = center.x - size * 0.4f + i * (size * 0.8f / 3f);
                if (rng.NextDouble() < 0.75 && spawnCar != null) spawnCar(new Vector3(x, y, center.z), 90f + 180f);
            }
        }
    }

    /// <summary>Street furniture built straight into the batcher (only lights need their own objects).</summary>
    public static class PropFactory
    {
        static readonly Color BarkColor = new Color(0.26f, 0.20f, 0.14f);

        public static void StreetLight(MeshBatcher b, Transform parent, Vector3 position, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            var metal = MaterialLibrary.Metal;

            b.Add(MeshKit.Cylinder(0.085f, 7.4f, 10, false),
                  Matrix4x4.TRS(position + Vector3.up * 3.7f, Quaternion.identity, Vector3.one), metal);
            b.Add(MeshKit.Cylinder(0.16f, 0.3f, 10, true),
                  Matrix4x4.TRS(position + Vector3.up * 0.15f, Quaternion.identity, Vector3.one), metal);

            Vector3 armEnd = position + rot * (Vector3.forward * 1.6f) + Vector3.up * 7.35f;
            b.Add(MeshKit.Box(new Vector3(0.09f, 0.09f, 1.7f)),
                  Matrix4x4.TRS(position + Vector3.up * 7.35f + rot * (Vector3.forward * 0.8f), rot, Vector3.one), metal);
            b.Add(MeshKit.Box(new Vector3(0.34f, 0.16f, 0.62f)),
                  Matrix4x4.TRS(armEnd, rot, Vector3.one), metal);
            b.Add(MeshKit.Box(new Vector3(0.28f, 0.06f, 0.52f)),
                  Matrix4x4.TRS(armEnd + Vector3.down * 0.10f, rot, Vector3.one), MaterialLibrary.Lamp(new Color(1f, 0.86f, 0.62f), "street"));

            // the actual light source (enabled by the day/night budget system)
            var go = new GameObject("StreetLampLight");
            go.transform.SetParent(parent, false);
            go.transform.position = armEnd + Vector3.down * 0.25f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 17f;
            l.intensity = 2.6f;
            l.color = new Color(1f, 0.82f, 0.58f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            DayNightCycle.StreetLights.Add(l);
        }

        public static void Tree(MeshBatcher b, Vector3 position, float scale)
        {
            var bark = MaterialLibrary.Painted(BarkColor, "bark");
            float trunkH = 2.6f * scale;
            b.Add(MeshKit.Cylinder(0.16f * scale, trunkH, 8, false),
                  Matrix4x4.Translate(position + Vector3.up * trunkH * 0.5f), bark);

            // canopy: 3 crossed alpha cards, each double sided
            float canopySize = 4.4f * scale;
            Vector3 canopyCenter = position + Vector3.up * (trunkH + canopySize * 0.32f);
            var leaves = MaterialLibrary.Leaves;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 60f;
                Quaternion r = Quaternion.Euler(0f, a, 0f);
                Vector3 u = r * Vector3.right;
                b.Add(MeshKit.QuadOriented(u, Vector3.up, canopySize * 0.5f, canopySize * 0.42f, Vector2.one),
                      Matrix4x4.Translate(canopyCenter), leaves);
                b.Add(MeshKit.QuadOriented(-u, Vector3.up, canopySize * 0.5f, canopySize * 0.42f, Vector2.one),
                      Matrix4x4.Translate(canopyCenter), leaves);
            }
        }

        public static void Bench(MeshBatcher b, Vector3 position, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            var wood = MaterialLibrary.Painted(new Color(0.34f, 0.23f, 0.15f), "benchwood");
            var metal = MaterialLibrary.Metal;

            b.Add(MeshKit.Box(new Vector3(1.8f, 0.07f, 0.46f)), Matrix4x4.TRS(position + Vector3.up * 0.44f, rot, Vector3.one), wood);
            b.Add(MeshKit.Box(new Vector3(1.8f, 0.34f, 0.06f)), Matrix4x4.TRS(position + Vector3.up * 0.66f + rot * (Vector3.forward * -0.2f), rot, Vector3.one), wood);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 legPos = position + rot * (Vector3.right * (s * 0.72f)) + Vector3.up * 0.22f;
                b.Add(MeshKit.Box(new Vector3(0.07f, 0.44f, 0.44f)), Matrix4x4.TRS(legPos, rot, Vector3.one), metal);
            }
        }

        public static void Hydrant(MeshBatcher b, Vector3 position)
        {
            var red = MaterialLibrary.Painted(new Color(0.65f, 0.12f, 0.10f), "hydrant");
            b.Add(MeshKit.Cylinder(0.10f, 0.62f, 8, true), Matrix4x4.Translate(position + Vector3.up * 0.31f), red);
            b.Add(MeshKit.Cylinder(0.13f, 0.10f, 8, true), Matrix4x4.Translate(position + Vector3.up * 0.66f), red);
            b.Add(MeshKit.Box(new Vector3(0.34f, 0.07f, 0.07f)), Matrix4x4.Translate(position + Vector3.up * 0.46f), red);
        }

        public static void TrashBin(MeshBatcher b, Vector3 position)
        {
            var metal = MaterialLibrary.Metal;
            b.Add(MeshKit.Cylinder(0.28f, 0.9f, 12, true), Matrix4x4.Translate(position + Vector3.up * 0.45f), metal);
            b.Add(MeshKit.Cylinder(0.30f, 0.06f, 12, true), Matrix4x4.Translate(position + Vector3.up * 0.93f),
                  MaterialLibrary.Plastic(new Color(0.14f, 0.16f, 0.15f), "binlid"));
        }

        public static void BusStop(MeshBatcher b, Vector3 position, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            var metal = MaterialLibrary.Metal;
            var glass = MaterialLibrary.GlassTint(new Color(0.42f, 0.52f, 0.56f, 0.45f));

            for (int s = -1; s <= 1; s += 2)
                b.Add(MeshKit.Cylinder(0.06f, 2.6f, 6, false),
                      Matrix4x4.TRS(position + rot * (Vector3.right * (s * 1.9f)) + Vector3.up * 1.3f, rot, Vector3.one), metal);

            b.Add(MeshKit.Box(new Vector3(4.1f, 0.10f, 1.7f)),
                  Matrix4x4.TRS(position + Vector3.up * 2.62f + rot * (Vector3.forward * 0.55f), rot, Vector3.one), metal);
            b.Add(MeshKit.Box(new Vector3(3.8f, 1.7f, 0.06f)),
                  Matrix4x4.TRS(position + Vector3.up * 1.6f + rot * (Vector3.forward * -0.3f), rot, Vector3.one), glass);
            Bench(b, position + rot * (Vector3.forward * 0.3f), yaw);
        }

        public static void TrafficLightPole(MeshBatcher b, Transform parent, RoadNetwork.Intersection node,
                                            Vector3 position, float yaw, bool alongX)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            var metal = MaterialLibrary.Metal;

            b.Add(MeshKit.Cylinder(0.075f, 4.4f, 8, false),
                  Matrix4x4.Translate(position + Vector3.up * 2.2f), metal);

            var head = new GameObject("TrafficHead");
            head.transform.SetParent(parent, false);
            head.transform.position = position + Vector3.up * 4.15f;
            head.transform.rotation = rot;

            Go.MeshObject("Housing", head.transform, MeshKit.Box(new Vector3(0.42f, 1.15f, 0.28f)),
                MaterialLibrary.Plastic(new Color(0.10f, 0.11f, 0.12f), "tlhousing"), Vector3.zero);

            Renderer r1 = Go.MeshObject("Red", head.transform, MeshKit.Cylinder(0.115f, 0.06f, 10, true),
                MaterialLibrary.Plastic(Color.black, "lampoff"), new Vector3(0f, 0.36f, 0.15f), Quaternion.Euler(90f, 0f, 0f))
                .GetComponent<Renderer>();
            Renderer r2 = Go.MeshObject("Yellow", head.transform, MeshKit.Cylinder(0.115f, 0.06f, 10, true),
                MaterialLibrary.Plastic(Color.black, "lampoff"), new Vector3(0f, 0.0f, 0.15f), Quaternion.Euler(90f, 0f, 0f))
                .GetComponent<Renderer>();
            Renderer r3 = Go.MeshObject("Green", head.transform, MeshKit.Cylinder(0.115f, 0.06f, 10, true),
                MaterialLibrary.Plastic(Color.black, "lampoff"), new Vector3(0f, -0.36f, 0.15f), Quaternion.Euler(90f, 0f, 0f))
                .GetComponent<Renderer>();

            var tl = head.AddComponent<TrafficLight>();
            tl.Node = node;
            tl.AlongX = alongX;
            tl.redLamp = r1;
            tl.yellowLamp = r2;
            tl.greenLamp = r3;
            if (node != null) node.Lights.Add(tl);
        }
    }
}
