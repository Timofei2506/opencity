using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// Lightweight, allocation-pooled billboard particle FX (sparks, blood, smoke, muzzle
    /// flashes, tracers, explosions). Built from code so the build needs no particle assets.
    /// </summary>
    public static class FxFactory
    {
        static Transform root;
        static Mesh quadMesh;
        static readonly Dictionary<Color, Material> additiveMats = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Material> alphaMats = new Dictionary<Color, Material>();
        static readonly List<FxParticle> pool = new List<FxParticle>();
        static readonly List<FxParticle> live = new List<FxParticle>();
        static readonly List<LineRenderer> tracers = new List<LineRenderer>();

        public static bool Ready => root != null;

        public static void Init(Transform parent)
        {
            root = new GameObject("FX").transform;
            root.SetParent(parent, false);
            quadMesh = MeshKit.Quad(Vector2.one, Vector2.one);
        }

        static Material MatFor(Color c, bool additive)
        {
            var dict = additive ? additiveMats : alphaMats;
            Material m;
            if (dict.TryGetValue(c, out m) && m != null) return m;

            var shader = Shader.Find("Standard");
            m = new Material(shader) { name = "FX_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetTexture("_MainTex", Resources.Load<Texture2D>("Textures/soft_dot"));
            m.SetColor("_Color", c);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 2.4f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3100;
            dict[c] = m;
            return m;
        }

        static FxParticle Spawn(Vector3 pos, Vector3 vel, Color color, float startScale, float endScale,
                                float life, bool additive, float drag = 0.9f, float gravity = 0f)
        {
            if (root == null) return null;
            FxParticle p = null;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (!pool[i].gameObject.activeSelf) { p = pool[i]; pool.RemoveAt(i); break; }
            }
            if (p == null)
            {
                var go = new GameObject("fx");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                p = go.AddComponent<FxParticle>();
                p.Rend = mr;
            }
            else
            {
                p.gameObject.SetActive(true);
            }

            p.transform.position = pos;
            p.transform.localScale = Vector3.one * startScale;
            p.Setup(vel, MatFor(color, additive), startScale, endScale, life, drag, gravity);
            live.Add(p);
            return p;
        }

        internal static void Recycle(FxParticle p)
        {
            if (p == null) return;
            live.Remove(p);
            p.gameObject.SetActive(false);
            pool.Add(p);
        }

        // ------------------------------------------------------------------ effects

        public static void Impact(Vector3 point, Vector3 normal)
        {
            for (int i = 0; i < 5; i++)
            {
                Vector3 v = (normal + Random.insideUnitSphere * 0.7f).normalized * Random.Range(2f, 6.5f);
                Spawn(point, v, new Color(1f, 0.75f, 0.28f, 1f), 0.09f, 0.01f, Random.Range(0.15f, 0.32f), true, 0.86f, 14f);
            }
            Spawn(point + normal * 0.05f, Random.insideUnitSphere * 0.4f, new Color(0.6f, 0.58f, 0.55f, 0.55f),
                  0.22f, 0.8f, 0.55f, false, 0.9f, -0.4f);
        }

        public static void Blood(Vector3 point, Vector3 dir, float scale = 1f)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector3 v = (dir.normalized * 0.6f + Random.insideUnitSphere).normalized * Random.Range(1.2f, 3.6f);
                Spawn(point, v, new Color(0.55f, 0.03f, 0.04f, 0.9f),
                      0.11f * scale, 0.03f, Random.Range(0.25f, 0.5f), false, 0.88f, 16f);
            }
        }

        public static void MuzzleFlash(Vector3 pos, Vector3 dir)
        {
            Spawn(pos, dir * 1.5f, new Color(1f, 0.85f, 0.45f, 1f), 0.30f, 0.06f, 0.06f, true, 0.8f, 0f);
            Spawn(pos, dir * 0.6f + Random.insideUnitSphere * 0.2f, new Color(0.55f, 0.5f, 0.45f, 0.35f),
                  0.12f, 0.5f, 0.35f, false, 0.9f, -0.5f);

            var go = new GameObject("flashLight");
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.82f, 0.5f);
            l.range = 9f;
            l.intensity = 3.4f;
            l.shadows = LightShadows.None;
            Object.Destroy(go, 0.055f);
        }

        public static void Tracer(Vector3 from, Vector3 to)
        {
            LineRenderer lr = null;
            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                if (tracers[i] == null) { tracers.RemoveAt(i); continue; }
                if (!tracers[i].gameObject.activeSelf) { lr = tracers[i]; break; }
            }
            if (lr == null)
            {
                var go = new GameObject("tracer");
                go.transform.SetParent(root, false);
                lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.widthMultiplier = 0.035f;
                lr.material = MatFor(new Color(1f, 0.92f, 0.6f, 0.9f), true);
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.useWorldSpace = true;
                tracers.Add(lr);
            }
            lr.gameObject.SetActive(true);
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startColor = new Color(1f, 0.95f, 0.7f, 0.85f);
            lr.endColor = new Color(1f, 0.85f, 0.5f, 0.05f);
            var fade = lr.gameObject.GetComponent<TracerFade>();
            if (fade == null) fade = lr.gameObject.AddComponent<TracerFade>();
            fade.ResetTimer(0.055f);
        }

        public static void Explosion(Vector3 pos)
        {
            for (int i = 0; i < 22; i++)
            {
                Vector3 v = Random.insideUnitSphere * Random.Range(3f, 14f);
                Spawn(pos, v, new Color(1f, 0.62f, 0.14f, 1f), Random.Range(0.5f, 1.1f), 0.05f,
                      Random.Range(0.3f, 0.7f), true, 0.88f, 6f);
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 v = Random.insideUnitSphere * Random.Range(1f, 5f) + Vector3.up * 1.5f;
                Spawn(pos, v, new Color(0.25f, 0.24f, 0.23f, 0.6f), Random.Range(0.8f, 1.8f),
                      Random.Range(2.2f, 3.4f), Random.Range(1.1f, 2.2f), false, 0.92f, -1.2f);
            }
            var go = new GameObject("boomLight");
            go.transform.SetParent(root, false);
            go.transform.position = pos + Vector3.up;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.65f, 0.3f);
            l.range = 26f;
            l.intensity = 9f;
            l.shadows = LightShadows.None;
            Object.Destroy(go, 0.35f);
            ProceduralAudio.PlayAt(ProceduralAudio.Explosion, pos, 1f, Random.Range(0.9f, 1.05f));
            if (CameraDirector.I != null)
            {
                float d = Vector3.Distance(CameraDirector.I.AimOrigin, pos);
                CameraDirector.I.Shake(Mathf.Clamp01(1.4f - d / 40f));
            }
        }

        public static void Dust(Vector3 pos)
        {
            Spawn(pos, Vector3.up * 0.4f, new Color(0.62f, 0.6f, 0.56f, 0.4f), 0.2f, 1.1f, 0.6f, false, 0.9f, -0.9f);
        }

        public static void TyreSmoke(Vector3 pos)
        {
            Spawn(pos, Vector3.up * Random.Range(0.3f, 1.2f), new Color(0.72f, 0.72f, 0.72f, 0.35f),
                  0.3f, 1.6f, Random.Range(0.6f, 1.1f), false, 0.93f, -0.8f);
        }

        public static void Cash(Vector3 pos)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = Vector3.up * Random.Range(2f, 4f) + Random.insideUnitSphere * 1.5f;
                Spawn(pos, v, new Color(0.35f, 0.85f, 0.35f, 0.95f), 0.14f, 0.1f, 0.9f, false, 0.94f, 8f);
            }
        }
    }

    public class FxParticle : MonoBehaviour
    {
        public MeshRenderer Rend;
        Vector3 velocity;
        float startScale, endScale, life, t, drag, gravity;

        public void Setup(Vector3 vel, Material mat, float s0, float s1, float lifetime, float dragCoef, float grav)
        {
            velocity = vel;
            startScale = s0;
            endScale = s1;
            life = Mathf.Max(0.02f, lifetime);
            t = 0f;
            drag = dragCoef;
            gravity = grav;
            if (Rend != null) Rend.sharedMaterial = mat;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            if (t >= life)
            {
                FxFactory.Recycle(this);
                return;
            }
            velocity += Vector3.down * gravity * dt;
            velocity *= Mathf.Pow(drag, dt * 60f);
            transform.position += velocity * dt;

            float f = t / life;
            transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, f);

            var cam = CameraDirector.I;
            if (cam != null && cam.Cam != null) transform.rotation = cam.Cam.transform.rotation;
            else transform.rotation = Quaternion.identity;
        }
    }

    public class TracerFade : MonoBehaviour
    {
        LineRenderer lr;
        float timer;

        void Awake()
        {
            lr = GetComponent<LineRenderer>();
        }

        public void ResetTimer(float seconds)
        {
            timer = seconds;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f && gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
