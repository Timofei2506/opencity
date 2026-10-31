using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// Builds the whole city: ground, road surface and markings, sidewalks, buildings,
    /// park, parking lots, street furniture, and populates it with traffic and pedestrians.
    /// </summary>
    public static class CityGenerator
    {
        public static Vector3 Generate(Transform root, RoadNetwork roads, System.Random rng)
        {
            var collidersRoot = Go.New("Colliders", root).transform;
            var staticRoot = Go.New("StaticGeometry", root).transform;
            var propRoot = Go.New("Props", root).transform;
            var batcher = new MeshBatcher();

            float citySize = GameConst.CityRadius * 2f + 240f;

            // ---------------- ground ----------------
            var ground = Go.MeshObject("Ground", staticRoot,
                MeshKit.GroundQuad(citySize, citySize, new Vector2(citySize, citySize)),
                MaterialLibrary.Road, new Vector3(0f, 0f, 0f), null, null, false);
            ground.isStatic = true;

            var groundCollider = Go.New("GroundCollider", collidersRoot);
            groundCollider.transform.position = new Vector3(0f, -2f, 0f);
            var gc = groundCollider.AddComponent<BoxCollider>();
            gc.size = new Vector3(citySize * 2f, 4f, citySize * 2f);

            // ---------------- markings ----------------
            BuildMarkings(batcher, roads);

            // ---------------- blocks ----------------
            int centerI = GameConst.BlocksX / 2;
            int centerJ = GameConst.BlocksZ / 2;

            for (int i = 0; i < GameConst.BlocksX; i++)
            {
                for (int j = 0; j < GameConst.BlocksZ; j++)
                {
                    Vector3 c = new Vector3(GameConst.BlockCenterX(i), 0f, GameConst.BlockCenterZ(j));
                    float distFromCenter = Mathf.Sqrt(
                        Mathf.Pow(i - (GameConst.BlocksX - 1) * 0.5f, 2f) +
                        Mathf.Pow(j - (GameConst.BlocksZ - 1) * 0.5f, 2f));

                    // sidewalk slab for every block
                    batcher.Add(MeshKit.Box(new Vector3(GameConst.BlockSize, GameConst.SidewalkHeight, GameConst.BlockSize)),
                        Matrix4x4.Translate(c + Vector3.up * (GameConst.SidewalkHeight * 0.5f)),
                        MaterialLibrary.Sidewalk);

                    var walkCollider = Go.New("SidewalkCollider", collidersRoot);
                    walkCollider.transform.position = c + Vector3.up * (GameConst.SidewalkHeight * 0.5f);
                    var wc = walkCollider.AddComponent<BoxCollider>();
                    wc.size = new Vector3(GameConst.BlockSize, GameConst.SidewalkHeight, GameConst.BlockSize);

                    if (i == centerI && j == centerJ)
                    {
                        BuildingFactory.BuildPark(batcher, collidersRoot, c, rng);
                        AddBlockFurniture(batcher, propRoot, collidersRoot, c, rng, true);
                        continue;
                    }

                    if (rng.NextDouble() < 0.10 && distFromCenter > 1.2)
                    {
                        BuildingFactory.BuildParkingLot(batcher, collidersRoot, c, rng, (pos, yaw) =>
                        {
                            SpawnParkedCar(root, pos, yaw, rng);
                        });
                        AddBlockFurniture(batcher, propRoot, collidersRoot, c, rng, false);
                        continue;
                    }

                    BuildLots(batcher, collidersRoot, c, distFromCenter, rng);
                    AddBlockFurniture(batcher, propRoot, collidersRoot, c, rng, false);
                }
            }

            // ---------------- junctions ----------------
            for (int i = 0; i < roads.CountX; i++)
            {
                for (int j = 0; j < roads.CountZ; j++)
                {
                    var node = roads.Get(i, j);
                    if (node == null) continue;
                    BuildJunction(batcher, propRoot, collidersRoot, node);
                }
            }

            // ---------------- flush static geometry ----------------
            var batches = batcher.Build("CityBatches", staticRoot);
            batches.isStatic = true;

            // ---------------- population ----------------
            SpawnTraffic(root, roads, 14, rng);
            SpawnParkedCars(root, roads, rng, centerI, centerJ);
            SpawnPedestrians(root, rng);

            // player start: corner of the central park block
            float half = GameConst.BlockSize * 0.5f;
            return new Vector3(half - 3.5f, GameConst.SidewalkHeight + 0.4f, half - 3.5f);
        }

        // ------------------------------------------------------------------ lots

        static void BuildLots(MeshBatcher batcher, Transform colliders, Vector3 center, float distFromCenter, System.Random rng)
        {
            float lots = GameConst.BlockSize - GameConst.SidewalkWidth * 2f;   // 42 m of buildable frontage
            bool downtown = distFromCenter < 1.35f;
            bool midtown = distFromCenter < 2.4f;

            int style;
            float minH, maxH;

            if (downtown)
            {
                style = rng.NextDouble() < 0.55 ? 3 : 2;
                minH = 34f; maxH = 74f;
            }
            else if (midtown)
            {
                style = rng.NextDouble() < 0.45 ? 0 : 2;
                minH = 16f; maxH = 36f;
            }
            else
            {
                int r = rng.Next(3);
                style = r == 0 ? 1 : (r == 1 ? 0 : 2);
                minH = 9f; maxH = 21f;
            }

            // choose a block layout
            double layout = rng.NextDouble();

            if (downtown && layout < 0.55)
            {
                // single tower with a plaza in front
                float w = lots * (0.62f + 0.16f * (float)rng.NextDouble());
                float d = lots * (0.62f + 0.16f * (float)rng.NextDouble());
                float h = Mathf.Lerp(minH, maxH, (float)rng.NextDouble());
                BuildingFactory.Build(batcher, colliders, center, new Vector2(w, d), h, style, rng);
                return;
            }

            if (layout < 0.42)
            {
                // one big building
                float w = lots * (0.82f + 0.1f * (float)rng.NextDouble());
                float d = lots * (0.82f + 0.1f * (float)rng.NextDouble());
                float h = Mathf.Lerp(minH, maxH, (float)rng.NextDouble());
                BuildingFactory.Build(batcher, colliders, center, new Vector2(w, d), h, style, rng);
            }
            else if (layout < 0.72)
            {
                // two buildings split along a random axis
                bool alongX = rng.NextDouble() < 0.5;
                float w = alongX ? lots * 0.46f : lots * 0.88f;
                float d = alongX ? lots * 0.88f : lots * 0.46f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 c = center + (alongX ? Vector3.right : Vector3.forward) * (s * lots * 0.25f);
                    float h = Mathf.Lerp(minH, maxH, (float)rng.NextDouble());
                    BuildingFactory.Build(batcher, colliders, c,
                        new Vector2(w * (0.9f + 0.1f * (float)rng.NextDouble()), d * (0.9f + 0.1f * (float)rng.NextDouble())),
                        h, style, rng);
                }
            }
            else
            {
                // four buildings on the corners
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 c = center + new Vector3(sx * lots * 0.25f, 0f, sz * lots * 0.25f);
                        float w = lots * (0.40f + 0.08f * (float)rng.NextDouble());
                        float d = lots * (0.40f + 0.08f * (float)rng.NextDouble());
                        float h = Mathf.Lerp(minH, maxH, (float)rng.NextDouble());
                        BuildingFactory.Build(batcher, colliders, c, new Vector2(w, d), h, style, rng);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ road surface details

        static void BuildMarkings(MeshBatcher b, RoadNetwork roads)
        {
            var white = MaterialLibrary.WhiteLine;
            var yellow = MaterialLibrary.YellowLine;
            float y = 0.012f;

            for (int i = 0; i < roads.CountX; i++)
            {
                for (int j = 0; j < roads.CountZ; j++)
                {
                    var node = roads.Get(i, j);
                    if (node == null) continue;

                    if (i + 1 < roads.CountX) MarkSegment(b, node, roads.Get(i + 1, j), white, yellow, y, true);
                    if (j + 1 < roads.CountZ) MarkSegment(b, node, roads.Get(i, j + 1), white, yellow, y, false);
                }
            }
        }

        static void MarkSegment(MeshBatcher b, RoadNetwork.Intersection a, RoadNetwork.Intersection c,
                                Material white, Material yellow, float y, bool alongX)
        {
            Vector3 dir = (c.Pos - a.Pos).normalized;
            Vector3 start = a.Pos + dir * (GameConst.RoadWidth * 0.5f + 1.5f);
            Vector3 end = c.Pos - dir * (GameConst.RoadWidth * 0.5f + 1.5f);
            float length = Vector3.Distance(start, end);
            if (length < 6f) return;

            Quaternion rot = Quaternion.Euler(0f, alongX ? 90f : 0f, 0f);

            // centre double line (yellow, dashed)
            float dash = 3.2f, gap = 5.5f;
            float t = 0f;
            while (t < length)
            {
                float seg = Mathf.Min(dash, length - t);
                Vector3 pos = start + dir * (t + seg * 0.5f);
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Add(MeshKit.GroundQuad(0.14f, seg, Vector2.one),
                        Matrix4x4.TRS(pos + (alongX ? Vector3.forward : Vector3.right) * (s * 0.16f) + Vector3.up * y, rot, Vector3.one), yellow);
                }
                t += dash + gap;
            }

            // lane dividers
            t = 2f;
            while (t < length)
            {
                float seg = Mathf.Min(dash, length - t);
                Vector3 pos = start + dir * (t + seg * 0.5f);
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Add(MeshKit.GroundQuad(0.12f, seg, Vector2.one),
                        Matrix4x4.TRS(pos + (alongX ? Vector3.forward : Vector3.right) * (s * GameConst.RoadWidth * 0.26f) + Vector3.up * y,
                        rot, Vector3.one), white);
                }
                t += dash + gap * 1.6f;
            }

            // parking bay ticks near the curb
            t = 4f;
            while (t < length - 4f)
            {
                Vector3 pos = start + dir * t;
                for (int s = -1; s <= 1; s += 2)
                {
                    b.Add(MeshKit.GroundQuad(0.1f, 2.4f, Vector2.one),
                        Matrix4x4.TRS(pos + (alongX ? Vector3.forward : Vector3.right) * (s * (GameConst.RoadWidth * 0.5f - 1.3f)) + Vector3.up * y,
                        rot, Vector3.one), white);
                }
                t += 6.5f;
            }
        }

        static void BuildJunction(MeshBatcher b, Transform propRoot, Transform colliders, RoadNetwork.Intersection node)
        {
            var white = MaterialLibrary.WhiteLine;
            float y = 0.013f;
            float half = GameConst.RoadWidth * 0.5f;

            // crosswalks on all four sides
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;              // north/south crossings -> stripes run along X
                float sign = (side % 2 == 0) ? 1f : -1f;
                Vector3 basePos = node.Pos + (alongX ? new Vector3(0f, 0f, sign * (half + 1.6f)) : new Vector3(sign * (half + 1.6f), 0f, 0f));
                for (int s = -3; s <= 3; s++)
                {
                    Vector3 offset = alongX ? new Vector3(s * 1.05f, 0f, 0f) : new Vector3(0f, 0f, s * 1.05f);
                    float w = alongX ? 0.55f : 2.6f;
                    float d = alongX ? 2.6f : 0.55f;
                    b.Add(MeshKit.GroundQuad(w, d, Vector2.one),
                        Matrix4x4.Translate(basePos + offset + Vector3.up * y), white);
                }

                // stop line
                Vector3 stopPos = node.Pos + (alongX ? new Vector3(-sign * half * 0.5f, 0f, sign * (half + 3.4f)) : new Vector3(sign * (half + 3.4f), 0f, sign * half * 0.5f));
                b.Add(MeshKit.GroundQuad(alongX ? half : 0.5f, alongX ? 0.5f : half, Vector2.one),
                    Matrix4x4.Translate(stopPos + Vector3.up * y), white);
            }

            // traffic light poles at the four corners
            for (int corner = 0; corner < 4; corner++)
            {
                float sx = (corner == 0 || corner == 3) ? 1f : -1f;
                float sz = (corner < 2) ? 1f : -1f;
                Vector3 p = node.Pos + new Vector3(sx * (half + 1.1f), 0f, sz * (half + 1.1f));
                if (Mathf.Abs(p.x) > GameConst.CityRadius + 1f || Mathf.Abs(p.z) > GameConst.CityRadius + 1f) continue;

                float yaw = Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg;
                bool alongX = corner < 2;
                PropFactory.TrafficLightPole(b, propRoot, node, p, yaw, alongX);
            }
        }

        // ------------------------------------------------------------------ furniture

        static void AddBlockFurniture(MeshBatcher b, Transform propRoot, Transform colliders, Vector3 center,
                                      System.Random rng, bool isPark)
        {
            float half = GameConst.BlockSize * 0.5f - 1.1f;
            float y = GameConst.SidewalkHeight;

            // street lamps around the block
            int lampsPerSide = 2;
            for (int side = 0; side < 4; side++)
            {
                for (int k = 0; k < lampsPerSide; k++)
                {
                    float t = Mathf.Lerp(-half * 0.62f, half * 0.62f, lampsPerSide == 1 ? 0.5f : k / (float)(lampsPerSide - 1));
                    Vector3 p;
                    float yaw;
                    switch (side)
                    {
                        case 0: p = center + new Vector3(t, y, half); yaw = 180f; break;
                        case 1: p = center + new Vector3(t, y, -half); yaw = 0f; break;
                        case 2: p = center + new Vector3(half, y, t); yaw = -90f; break;
                        default: p = center + new Vector3(-half, y, t); yaw = 90f; break;
                    }
                    PropFactory.StreetLight(b, propRoot, p, yaw);
                }
            }

            // trees on the sidewalk
            int trees = 2 + rng.Next(4);
            for (int i = 0; i < trees; i++)
            {
                float t = Mathf.Lerp(-half * 0.8f, half * 0.8f, (float)rng.NextDouble());
                int side = rng.Next(4);
                Vector3 p;
                switch (side)
                {
                    case 0: p = center + new Vector3(t, y, half * 0.74f); break;
                    case 1: p = center + new Vector3(t, y, -half * 0.74f); break;
                    case 2: p = center + new Vector3(half * 0.74f, y, t); break;
                    default: p = center + new Vector3(-half * 0.74f, y, t); break;
                }
                PropFactory.Tree(b, p, 0.8f + (float)rng.NextDouble() * 0.45f);
            }

            // small street furniture
            if (rng.NextDouble() < 0.6)
            {
                Vector3 p = center + new Vector3(half * 0.9f, y, half * 0.3f);
                PropFactory.Hydrant(b, p);
            }
            if (rng.NextDouble() < 0.7)
            {
                Vector3 p = center + new Vector3(-half * 0.9f, y, -half * 0.35f);
                PropFactory.TrashBin(b, p);
            }
            if (rng.NextDouble() < 0.35)
            {
                Vector3 p = center + new Vector3(-half * 0.55f, y, half * 0.86f);
                PropFactory.BusStop(b, p, 0f);
            }
            if (rng.NextDouble() < 0.5 && !isPark)
            {
                Vector3 p = center + new Vector3(half * 0.5f, y, -half * 0.86f);
                PropFactory.Bench(b, p, 0f);
            }

            // manhole covers on the road corners
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = center + new Vector3((float)(rng.NextDouble() - 0.5) * half * 3f, 0.014f,
                                                 (float)(rng.NextDouble() - 0.5) * half * 3f);
                if (Mathf.Abs(p.x) > half + 2f || Mathf.Abs(p.z) > half + 2f) continue;
                b.Add(MeshKit.Cylinder(0.42f, 0.03f, 14, true), Matrix4x4.Translate(p), MaterialLibrary.Manhole);
            }
        }

        // ------------------------------------------------------------------ population

        static void SpawnTraffic(Transform root, RoadNetwork roads, int count, System.Random rng)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 dir;
                Vector3 pos = roads.RandomSpawn(rng, out dir);
                int style = rng.Next(CarFactory.StyleCount);
                Color paint = CarFactory.Paints[rng.Next(CarFactory.Paints.Length)];
                var car = CarFactory.Create(root, pos, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg,
                                            style, paint, false);
                var tc = car.gameObject.AddComponent<TrafficCar>();
                tc.CruiseSpeed = 9f + (float)rng.NextDouble() * 7f;
                car.SetMode(VehicleMode.AI);
            }
        }

        static void SpawnParkedCars(Transform root, RoadNetwork roads, System.Random rng, int centerI, int centerJ)
        {
            int spawned = 0;
            int attempts = 0;
            while (spawned < 16 && attempts < 200)
            {
                attempts++;
                Vector3 dir;
                Vector3 pos = roads.RandomSpawn(rng, out dir);
                Vector3 lane = pos + RoadNetwork.RightOf(dir) * (GameConst.RoadWidth * 0.36f);
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                SpawnParkedCar(root, lane, yaw, rng);
                spawned++;
            }
        }

        static void SpawnParkedCar(Transform root, Vector3 pos, float yaw, System.Random rng)
        {
            int style = rng.Next(CarFactory.StyleCount);
            Color paint = CarFactory.Paints[rng.Next(CarFactory.Paints.Length)];
            CarFactory.Create(root, pos, yaw, style, paint, true);
        }

        static void SpawnPedestrians(Transform root, System.Random rng)
        {
            int count = 34;
            for (int i = 0; i < count; i++)
            {
                int bi = rng.Next(GameConst.BlocksX);
                int bj = rng.Next(GameConst.BlocksZ);
                Vector3 c = new Vector3(GameConst.BlockCenterX(bi), GameConst.SidewalkHeight, GameConst.BlockCenterZ(bj));
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = GameConst.BlockSize * 0.5f - 2.2f;
                Vector3 pos = c + new Vector3(Mathf.Cos(a) * r, 0.15f, Mathf.Sin(a) * r);

                var go = new GameObject("Pedestrian");
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                var ped = go.AddComponent<Pedestrian>();
                ped.Build(rng);
            }
        }
    }
}
