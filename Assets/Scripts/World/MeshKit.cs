using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenCity
{
    /// <summary>
    /// Procedural mesh toolkit. Everything in the game (city, cars, characters, props)
    /// is generated from these primitives at runtime - the project ships without any
    /// imported FBX/DCC assets, yet every surface is real geometry with proper UVs,
    /// normals and tangents.
    ///
    /// UV convention: 1 UV unit == 1 metre. Materials set mainTextureScale = 1 / tileMeters
    /// so a texture repeats every N metres regardless of object size.
    /// </summary>
    public static class MeshKit
    {
        // ------------------------------------------------------------------ primitives

        /// <summary>
        /// Finalises a procedurally built mesh. RecalculateBounds is mandatory: a mesh
        /// created with "new Mesh()" starts with a zero-sized bounding box, and Unity
        /// frustum-culls a renderer by that box - a single quad (the ground plane, decals,
        /// big props) disappears as soon as the world origin is off screen.
        /// </summary>
        static Mesh Finish(Mesh m)
        {
            m.RecalculateBounds();
            return m;
        }

        static void AddQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t,
                            Vector3 center, Vector3 u, Vector3 w, float hu, float hw,
                            Vector2 uvScale, Vector2 uvOffset)
        {
            int i = v.Count;
            Vector3 p0 = center - u * hu - w * hw;
            Vector3 p1 = center + u * hu - w * hw;
            Vector3 p2 = center + u * hu + w * hw;
            Vector3 p3 = center - u * hu + w * hw;

            // Unity renders clockwise triangles as front faces. p0..p3 are ordered
            // (-u-w) (+u-w) (+u+w) (-u+w), so 0-1-2 is counter-clockwise seen from the
            // +normal side and the indices must be emitted as 0-2-1 / 0-3-2. The previous
            // order made every quad a back face: the ground plane and all decals vanished
            // and boxes were shaded from their inside (flat, dark geometry everywhere).
            Vector3 nrm = -Vector3.Cross(u, w).normalized;

            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            for (int k = 0; k < 4; k++) n.Add(nrm);
            uv.Add(uvOffset + new Vector2(0f, 0f) * uvScale);
            uv.Add(uvOffset + new Vector2(uvScale.x, 0f));
            uv.Add(uvOffset + uvScale);
            uv.Add(uvOffset + new Vector2(0f, uvScale.y));

            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        /// <summary>Axis aligned box with per face UVs measured in metres.</summary>
        public static Mesh Box(Vector3 size, float uvPerMeter = 1f)
        {
            var v = new List<Vector3>(24);
            var n = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var t = new List<int>(36);

            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            float s = uvPerMeter;
            Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;

            AddQuad(v, n, uv, t, new Vector3(hx, 0, 0), Z, Y, hz, hy, new Vector2(size.z, size.y) * s, Vector2.zero);
            AddQuad(v, n, uv, t, new Vector3(-hx, 0, 0), -Z, Y, hz, hy, new Vector2(size.z, size.y) * s, Vector2.zero);
            AddQuad(v, n, uv, t, new Vector3(0, hy, 0), X, Z, hx, hz, new Vector2(size.x, size.z) * s, Vector2.zero);
            AddQuad(v, n, uv, t, new Vector3(0, -hy, 0), X, -Z, hx, hz, new Vector2(size.x, size.z) * s, Vector2.zero);
            AddQuad(v, n, uv, t, new Vector3(0, 0, hz), -X, Y, hx, hy, new Vector2(size.x, size.y) * s, Vector2.zero);
            AddQuad(v, n, uv, t, new Vector3(0, 0, -hz), X, Y, hx, hy, new Vector2(size.x, size.y) * s, Vector2.zero);

            var m = new Mesh { name = "Box" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateTangents();
            return Finish(m);
        }

        /// <summary>Flat quad in the XY plane, facing -Z (like Unity's built-in Quad).</summary>
        public static Mesh Quad(Vector2 size, Vector2 uvScale)
        {
            var v = new List<Vector3>(4);
            var n = new List<Vector3>(4);
            var uv = new List<Vector2>(4);
            var t = new List<int>(6);
            AddQuad(v, n, uv, t, Vector3.zero, Vector3.right, Vector3.up,
                    size.x * 0.5f, size.y * 0.5f, uvScale, Vector2.zero);
            var m = new Mesh { name = "Quad" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            return Finish(m);
        }

        /// <summary>
        /// Free-oriented quad in the plane spanned by <paramref name="u"/> and <paramref name="w"/>.
        /// The face points along -cross(u, w).
        /// </summary>
        public static Mesh QuadOriented(Vector3 u, Vector3 w, float hu, float hw, Vector2 uvScale)
        {
            var v = new List<Vector3>(4);
            var n = new List<Vector3>(4);
            var uv = new List<Vector2>(4);
            var t = new List<int>(6);
            AddQuad(v, n, uv, t, Vector3.zero, u.normalized, w.normalized, hu, hw, uvScale, Vector2.zero);
            var m = new Mesh { name = "QuadOriented" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            return Finish(m);
        }

        /// <summary>Flat quad in the XZ plane facing +Y (roads, markings, decals).</summary>
        public static Mesh GroundQuad(float sizeX, float sizeZ, Vector2 uvScale)
        {
            var v = new List<Vector3>(4);
            var n = new List<Vector3>(4);
            var uv = new List<Vector2>(4);
            var t = new List<int>(6);
            AddQuad(v, n, uv, t, Vector3.zero, Vector3.right, Vector3.forward,
                    sizeX * 0.5f, sizeZ * 0.5f, uvScale, Vector2.zero);
            var m = new Mesh { name = "GroundQuad" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            return Finish(m);
        }

        /// <summary>Cylinder along +Y with caps.</summary>
        public static Mesh Cylinder(float radius, float height, int segments, bool caps = true)
        {
            segments = Mathf.Max(3, segments);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            float hh = height * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 b0 = new Vector3(Mathf.Cos(a0) * radius, -hh, Mathf.Sin(a0) * radius);
                Vector3 b1 = new Vector3(Mathf.Cos(a1) * radius, -hh, Mathf.Sin(a1) * radius);
                Vector3 t1 = new Vector3(b1.x, hh, b1.z);
                Vector3 t0 = new Vector3(b0.x, hh, b0.z);

                int i0 = v.Count;
                v.Add(b0); v.Add(b1); v.Add(t1); v.Add(t0);
                Vector3 n0 = new Vector3(b0.x, 0, b0.z).normalized;
                Vector3 n1 = new Vector3(b1.x, 0, b1.z).normalized;
                n.Add(n0); n.Add(n1); n.Add(n1); n.Add(n0);
                uv.Add(new Vector2((float)i / segments, 0f));
                uv.Add(new Vector2((float)(i + 1) / segments, 0f));
                uv.Add(new Vector2((float)(i + 1) / segments, height));
                uv.Add(new Vector2((float)i / segments, height));
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            }

            if (caps)
            {
                int topCenter = v.Count;
                v.Add(new Vector3(0, hh, 0)); n.Add(Vector3.up); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments;
                    float a1 = (i + 1) * Mathf.PI * 2f / segments;
                    int a = v.Count;
                    v.Add(new Vector3(Mathf.Cos(a0) * radius, hh, Mathf.Sin(a0) * radius));
                    v.Add(new Vector3(Mathf.Cos(a1) * radius, hh, Mathf.Sin(a1) * radius));
                    n.Add(Vector3.up); n.Add(Vector3.up);
                    uv.Add(new Vector2(Mathf.Cos(a0) * 0.5f + 0.5f, Mathf.Sin(a0) * 0.5f + 0.5f));
                    uv.Add(new Vector2(Mathf.Cos(a1) * 0.5f + 0.5f, Mathf.Sin(a1) * 0.5f + 0.5f));
                    t.Add(topCenter); t.Add(a + 1); t.Add(a);
                }

                int botCenter = v.Count;
                v.Add(new Vector3(0, -hh, 0)); n.Add(Vector3.down); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments;
                    float a1 = (i + 1) * Mathf.PI * 2f / segments;
                    int a = v.Count;
                    v.Add(new Vector3(Mathf.Cos(a0) * radius, -hh, Mathf.Sin(a0) * radius));
                    v.Add(new Vector3(Mathf.Cos(a1) * radius, -hh, Mathf.Sin(a1) * radius));
                    n.Add(Vector3.down); n.Add(Vector3.down);
                    uv.Add(new Vector2(Mathf.Cos(a0) * 0.5f + 0.5f, Mathf.Sin(a0) * 0.5f + 0.5f));
                    uv.Add(new Vector2(Mathf.Cos(a1) * 0.5f + 0.5f, Mathf.Sin(a1) * 0.5f + 0.5f));
                    t.Add(botCenter); t.Add(a); t.Add(a + 1);
                }
            }

            var m = new Mesh { name = "Cylinder" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            return Finish(m);
        }

        /// <summary>Surface of revolution around +Y. Profile entries are (radius, y).</summary>
        public static Mesh Lathe(IList<Vector2> profile, int segments = 16, bool smooth = true)
        {
            segments = Mathf.Max(3, segments);
            if (profile == null || profile.Count < 2) return Box(Vector3.one * 0.1f);

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();

            int rows = profile.Count;
            for (int r = 0; r < rows; r++)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    float rad = Mathf.Max(profile[r].x, 0.0005f);
                    v.Add(new Vector3(Mathf.Cos(a) * rad, profile[r].y, Mathf.Sin(a) * rad));
                    n.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
                    uv.Add(new Vector2((float)i / segments, (float)r / (rows - 1)));
                }
            }

            for (int r = 0; r < rows - 1; r++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int i1 = (i + 1) % segments;
                    int b0 = r * segments + i;
                    int b1 = r * segments + i1;
                    int t0 = (r + 1) * segments + i;
                    int t1 = (r + 1) * segments + i1;
                    t.Add(b0); t.Add(b1); t.Add(t1);
                    t.Add(b0); t.Add(t1); t.Add(t0);
                }
            }

            var m = new Mesh { name = "Lathe" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            if (smooth) m.RecalculateNormals();
            m.RecalculateTangents();
            return Finish(m);
        }

        /// <summary>Helper: capsule-ish lathe profile from bottom to top.</summary>
        public static Mesh Capsule(float radius, float length, int segments = 12, int rings = 6)
        {
            var p = new List<Vector2>();
            float half = length * 0.5f;
            // bottom hemisphere
            for (int i = 0; i <= rings; i++)
            {
                float f = (float)i / rings;
                float a = Mathf.PI * 0.5f * f;
                p.Add(new Vector2(Mathf.Sin(a) * radius, -half - Mathf.Cos(a) * radius));
            }
            // top hemisphere
            for (int i = 0; i <= rings; i++)
            {
                float f = (float)i / rings;
                float a = Mathf.PI * 0.5f * f;
                p.Add(new Vector2(Mathf.Cos(a) * radius, half + Mathf.Sin(a) * radius));
            }
            return Lathe(p, segments, true);
        }

        /// <summary>Helper: tapered capsule (limb) from bottom to top.</summary>
        public static Mesh Limb(float rBottom, float rTop, float length, int segments = 10, int rings = 3)
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= rings; i++)
            {
                float f = (float)i / rings;
                p.Add(new Vector2(Mathf.Lerp(rBottom, rTop, f), -length * 0.5f + length * f));
            }
            // rounded ends
            p.Insert(0, new Vector2(0.001f, -length * 0.5f));
            p.Add(new Vector2(0.001f, length * 0.5f));
            return Lathe(p, segments, true);
        }

        // ------------------------------------------------------------------ lofting (cars)

        public struct Section
        {
            public float z;
            public float halfWidth;
            public float bottom;
            public float top;
            public float radius;

            public Section(float z, float halfWidth, float bottom, float top, float radius)
            {
                this.z = z; this.halfWidth = halfWidth; this.bottom = bottom;
                this.top = top; this.radius = radius;
            }
        }

        static readonly List<Vector2> ringCache = new List<Vector2>(64);

        /// <summary>Rounded rectangle cross section in XY at a given Z.</summary>
        static void BuildRing(Section s, int cornerSegs, List<Vector2> outPts)
        {
            outPts.Clear();
            float cy = (s.bottom + s.top) * 0.5f;
            float hh = (s.top - s.bottom) * 0.5f;
            float hw = s.halfWidth;
            float rr = Mathf.Clamp(s.radius, 0.001f, Mathf.Min(hw, hh) * 0.999f);

            for (int q = 0; q < 4; q++)
            {
                for (int k = 0; k < cornerSegs; k++)
                {
                    float a = (q * 90f + 90f * k / cornerSegs) * Mathf.Deg2Rad;
                    float cx = (q == 0 || q == 3) ? (hw - rr) : -(hw - rr);
                    float cyc = (q == 0 || q == 1) ? (hh - rr) : -(hh - rr);
                    outPts.Add(new Vector2(cx + Mathf.Cos(a) * rr, cy + cyc + Mathf.Sin(a) * rr));
                }
            }
            // store absolute Y (add section centre) - done by caller
            for (int i = 0; i < outPts.Count; i++)
                outPts[i] = new Vector2(outPts[i].x, outPts[i].y + cy);
        }

        /// <summary>
        /// Loft a car-like body from cross sections along +Z. Smooth shaded, capped.
        /// </summary>
        public static Mesh Loft(IList<Section> sections, int cornerSegs = 3, bool capFront = true, bool capBack = true)
        {
            if (sections == null || sections.Count < 2) return Box(Vector3.one * 0.5f);
            cornerSegs = Mathf.Clamp(cornerSegs, 1, 8);

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();

            int ringCount = sections.Count;
            var rings = new List<Vector2>[ringCount];
            for (int r = 0; r < ringCount; r++)
            {
                var pts = new List<Vector2>(cornerSegs * 4);
                BuildRing(sections[r], cornerSegs, pts);
                rings[r] = pts;
            }

            int perRing = rings[0].Count;
            float zMin = sections[0].z, zMax = sections[ringCount - 1].z;
            float len = Mathf.Max(0.001f, zMax - zMin);

            for (int r = 0; r < ringCount; r++)
            {
                float vCoord = (sections[r].z - zMin) / len;
                for (int i = 0; i < perRing; i++)
                {
                    v.Add(new Vector3(rings[r][i].x, rings[r][i].y, sections[r].z));
                    n.Add(Vector3.up);
                    uv.Add(new Vector2((float)i / perRing * 2f, vCoord * 2f));
                }
            }

            for (int r = 0; r < ringCount - 1; r++)
            {
                for (int i = 0; i < perRing; i++)
                {
                    int i1 = (i + 1) % perRing;
                    int a0 = r * perRing + i;
                    int a1 = r * perRing + i1;
                    int b0 = (r + 1) * perRing + i;
                    int b1 = (r + 1) * perRing + i1;
                    t.Add(a0); t.Add(b0); t.Add(b1);
                    t.Add(a0); t.Add(b1); t.Add(a1);
                }
            }

            if (capFront)
            {
                var last = sections[ringCount - 1];
                int c = v.Count;
                v.Add(new Vector3(0, (last.bottom + last.top) * 0.5f, last.z)); n.Add(Vector3.forward);
                uv.Add(new Vector2(0.5f, 0.5f));
                int baseIdx = (ringCount - 1) * perRing;
                for (int i = 0; i < perRing; i++)
                {
                    int i1 = (i + 1) % perRing;
                    t.Add(c); t.Add(baseIdx + i1); t.Add(baseIdx + i);
                }
            }

            if (capBack)
            {
                var first = sections[0];
                int c = v.Count;
                v.Add(new Vector3(0, (first.bottom + first.top) * 0.5f, first.z)); n.Add(Vector3.back);
                uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < perRing; i++)
                {
                    int i1 = (i + 1) % perRing;
                    t.Add(c); t.Add(i); t.Add(i1);
                }
            }

            var m = new Mesh { name = "Loft" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            return Finish(m);
        }

        // ------------------------------------------------------------------ ops

        public static Mesh TransformMesh(Mesh src, Matrix4x4 tr)
        {
            var m = Object.Instantiate(src);
            var verts = m.vertices;
            var normals = m.normals;
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = tr.MultiplyPoint3x4(verts[i]);
                normals[i] = tr.MultiplyVector(normals[i]).normalized;
            }
            m.vertices = verts;
            m.normals = normals;
            m.RecalculateTangents();
            return Finish(m);
        }

        public static Mesh Wheel(float radius, float width, int segments = 18)
        {
            var m = Cylinder(radius, width, segments, true);
            return TransformMesh(m, Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f)));
        }

        public static Mesh Disc(float radius, int segments = 20)
        {
            var p = new List<Vector2>
            {
                new Vector2(0.001f, 0f),
                new Vector2(radius, 0f),
                new Vector2(radius, 0.001f),
            };
            return Lathe(p, segments, false);
        }
    }

    /// <summary>
    /// Merges thousands of small procedural meshes into a handful of draw calls,
    /// grouped by material. Keeps vertex counts under the 16 bit limit per chunk.
    /// </summary>
    public class MeshBatcher
    {
        class Group
        {
            public Material material;
            public readonly List<CombineInstance> items = new List<CombineInstance>();
            public int vertexCount;
            public bool castShadows = true;
        }

        readonly Dictionary<Material, Group> groups = new Dictionary<Material, Group>();
        readonly List<Group> order = new List<Group>();
        public const int MaxVertsPerChunk = 60000;

        public void Add(Mesh mesh, Matrix4x4 tr, Material material, bool castShadows = true)
        {
            if (mesh == null || material == null) return;
            Group g;
            if (!groups.TryGetValue(material, out g))
            {
                g = new Group { material = material, castShadows = castShadows };
                groups[material] = g;
                order.Add(g);
            }
            g.items.Add(new CombineInstance { mesh = mesh, transform = tr, subMeshIndex = 0 });
            g.vertexCount += mesh.vertexCount;
        }

        public void Add(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material material, bool castShadows = true)
        {
            Add(mesh, Matrix4x4.TRS(pos, rot, scale), material, castShadows);
        }

        /// <summary>Creates one GameObject (+MeshFilter/MeshRenderer) per chunk.</summary>
        public GameObject Build(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            int chunk = 0;
            foreach (var g in order)
            {
                int start = 0;
                while (start < g.items.Count)
                {
                    int verts = 0;
                    int end = start;
                    while (end < g.items.Count && (verts + g.items[end].mesh.vertexCount) <= MaxVertsPerChunk)
                    {
                        verts += g.items[end].mesh.vertexCount;
                        end++;
                    }
                    if (end == start) end = start + 1; // single huge mesh

                    var slice = g.items.GetRange(start, end - start);
                    var combined = new Mesh();
                    if (verts + 1024 > 65535) combined.indexFormat = IndexFormat.UInt32;
                    combined.name = name + "_" + chunk;
                    combined.CombineMeshes(slice.ToArray(), true, true);
                    combined.RecalculateBounds();

                    var go = new GameObject("chunk" + chunk);
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = combined;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = g.material;
                    mr.shadowCastingMode = g.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    mr.receiveShadows = true;
                    chunk++;
                    start = end;
                }
            }
            return root;
        }
    }
}
