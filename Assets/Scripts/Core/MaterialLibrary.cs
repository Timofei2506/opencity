using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>Shared constants and layer-free helpers.</summary>
    public static class GameConst
    {
        // --- city layout (metres) ---
        public const float RoadWidth = 16f;
        public const float BlockSize = 52f;
        public const int BlocksX = 7;
        public const int BlocksZ = 7;
        public const float SidewalkWidth = 5f;
        public const float SidewalkHeight = 0.16f;
        public const float CityCell = BlockSize + RoadWidth;
        public const float CityRadius = CityCell * BlocksX * 0.5f;

        // --- gameplay ---
        public const float DayLengthSeconds = 600f;   // one in-game day = 10 real minutes
        public const float PlayerWalkSpeed = 3.4f;
        public const float PlayerRunSpeed = 6.8f;
        public const float PlayerSprintSpeed = 9.2f;
        public const float PlayerJumpSpeed = 5.2f;
        public const float Gravity = 22f;

        public const string SaveFileName = "opencity_save.json";
        public const string Version = "0.9.0";

        public static int StarCount(float wanted) => Mathf.Clamp(Mathf.CeilToInt(wanted - 0.001f), 0, 5);

        public static float RoadLineX(int k) => (k - BlocksX * 0.5f) * CityCell;
        public static float RoadLineZ(int k) => (k - BlocksZ * 0.5f) * CityCell;
        public static float BlockCenterX(int i) => RoadLineX(i) + CityCell * 0.5f;
        public static float BlockCenterZ(int j) => RoadLineZ(j) + CityCell * 0.5f;

        public static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }

    /// <summary>Small scene-graph helpers used all over the procedural content.</summary>
    public static class Go
    {
        public static GameObject New(string name, Transform parent, Vector3 localPos = default(Vector3), Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            return go;
        }

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat,
                                            Vector3 localPos = default(Vector3), Quaternion? rot = null,
                                            Vector3? scale = null, bool shadows = true)
        {
            var go = New(name, parent, localPos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (scale.HasValue) go.transform.localScale = scale.Value;
            return go;
        }

        public static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        public static void DestroySafe(Object o, float delay)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o, delay);
            else Object.DestroyImmediate(o);
        }
    }

    /// <summary>
    /// Loads the procedurally generated PBR texture set from Resources and builds the
    /// shared material palette. Every mesh in the game reuses these materials, which is
    /// what makes vertex batching effective.
    /// </summary>
    public static class MaterialLibrary
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<Material, Color> emissionBase = new Dictionary<Material, Color>();
        static readonly List<Material> nightMaterials = new List<Material>();

        public static Texture2D Tex(string name)
        {
            Texture2D t;
            if (textures.TryGetValue(name, out t) && t != null) return t;
            t = Resources.Load<Texture2D>("Textures/" + name);
            if (t == null)
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                var c = new Color32(178, 178, 180, 255);
                t.SetPixels32(new[] { c, c, c, c });
                t.Apply(true);
                Debug.LogWarning("[OpenCity] Missing texture: " + name + " - using flat fallback");
            }
            textures[name] = t;
            return t;
        }

        static Texture2D Optional(string name) => Resources.Load<Texture2D>("Textures/" + name);

        static Material Make(string key, string texBase, Vector2 uvScale, Color tint,
                             bool emissive = false, Color? emissionColor = null,
                             float smoothness = -1f, float metallic = -1f, float bumpScale = 1f,
                             bool transparent = false, bool cutout = false, bool nightAnimated = true)
        {
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;

            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            m = new Material(shader) { name = "M_" + key };

            if (!string.IsNullOrEmpty(texBase))
            {
                m.SetTexture("_MainTex", Tex(texBase + "_d"));
                var normal = Optional(texBase + "_n");
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.SetFloat("_BumpScale", bumpScale);
                    m.EnableKeyword("_NORMALMAP");
                }
                var mg = Optional(texBase + "_mg");
                if (mg != null)
                {
                    m.SetTexture("_MetallicGlossMap", mg);
                    m.EnableKeyword("_METALLICGLOSSMAP");
                }
            }

            m.SetColor("_Color", tint);
            m.mainTextureScale = uvScale;
            if (smoothness >= 0f) m.SetFloat("_Glossiness", smoothness);
            if (metallic >= 0f) m.SetFloat("_Metallic", metallic);

            if (emissive)
            {
                var em = string.IsNullOrEmpty(texBase) ? null : Optional(texBase + "_e");
                if (em != null) m.SetTexture("_EmissionMap", em);
                m.EnableKeyword("_EMISSION");
                var ecol = emissionColor ?? Color.white;
                m.SetColor("_EmissionColor", ecol);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                emissionBase[m] = ecol;
                if (nightAnimated && !nightMaterials.Contains(m)) nightMaterials.Add(m);
            }

            if (transparent)
            {
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            else if (cutout)
            {
                m.SetFloat("_Mode", 1f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                m.SetInt("_ZWrite", 1);
                m.EnableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 2450;
            }

            cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ world

        public static Material Road => Make("road", "asphalt", new Vector2(1f / 7f, 1f / 7f), Color.white);
        public static Material Sidewalk => Make("sidewalk", "sidewalk", new Vector2(1f / 3f, 1f / 3f), Color.white);
        public static Material Concrete => Make("concrete", "concrete", new Vector2(1f / 4f, 1f / 4f), new Color(0.92f, 0.92f, 0.90f));
        public static Material Brick => Make("brick", "brick", new Vector2(1f / 2.2f, 1f / 2.2f), Color.white);
        public static Material Grass => Make("grass", "grass", new Vector2(1f / 4f, 1f / 4f), Color.white);
        public static Material Roof => Make("roof", "roof", new Vector2(1f / 4f, 1f / 4f), Color.white);
        public static Material Metal => Make("metal", "metal", new Vector2(1f / 2f, 1f / 2f), Color.white);
        public static Material Tire => Make("tire", "tire", Vector2.one, Color.white);
        public static Material Rim => Make("rim", "rim", Vector2.one, Color.white);
        public static Material WhiteLine => Make("whiteline", null, Vector2.one, new Color(0.93f, 0.93f, 0.88f), smoothness: 0.35f, metallic: 0f);
        public static Material YellowLine => Make("yellowline", null, Vector2.one, new Color(0.86f, 0.72f, 0.18f), smoothness: 0.35f, metallic: 0f);
        public static Material Manhole => Make("manhole", "metal", new Vector2(2f, 2f), new Color(0.34f, 0.33f, 0.32f));
        public static Material Leaves => Make("leaves", "leaves", Vector2.one, Color.white, cutout: true);
        public static Material Billboard => Make("billboard", "billboard", Vector2.one, Color.white,
            emissive: true, emissionColor: new Color(0.9f, 0.9f, 0.9f));

        public static Material Facade(int style)
        {
            switch (style)
            {
                case 1: return Make("facade_brick", "facade_brick", Vector2.one, Color.white, true, new Color(2.1f, 1.55f, 0.85f));
                case 2: return Make("facade_panel", "facade_panel", Vector2.one, Color.white, true, new Color(2.1f, 1.70f, 1.00f));
                case 3: return Make("facade_glass", "facade_glass", Vector2.one, Color.white, true, new Color(1.70f, 1.75f, 1.50f));
                default: return Make("facade_concrete", "facade_concrete", Vector2.one, Color.white, true, new Color(2.1f, 1.60f, 0.90f));
            }
        }

        public static Material Storefront => Make("storefront", "facade_glass", new Vector2(2f, 1f),
            new Color(0.55f, 0.60f, 0.62f), emissive: true, emissionColor: new Color(0.75f, 0.70f, 0.58f));

        public static Material Painted(Color c, string key = null)
            => Make("paint_" + (key ?? ColorUtility.ToHtmlStringRGB(c)), null, Vector2.one, c, smoothness: 0.45f, metallic: 0.25f);

        public static Material Plastic(Color c, string key = null)
            => Make("plastic_" + (key ?? ColorUtility.ToHtmlStringRGB(c)), null, Vector2.one, c, smoothness: 0.55f, metallic: 0f);

        public static Material Rubber => Make("rubber", null, Vector2.one, new Color(0.055f, 0.055f, 0.065f), smoothness: 0.10f, metallic: 0f);

        public static Material GlassTint(Color c)
            => Make("glass_" + ColorUtility.ToHtmlStringRGB(c), null, Vector2.one, c, smoothness: 0.95f, metallic: 0.35f, transparent: true);

        /// <summary>Always-on emissive (traffic lights, car lights) - not dimmed by the day cycle.</summary>
        public static Material Signal(Color c, string key)
            => Make("signal_" + key, null, Vector2.one, Color.black, emissive: true,
                    emissionColor: c * 2.6f, smoothness: 0.80f, metallic: 0f, nightAnimated: false);

        public static Material Lamp(Color c, string key)
            => Make("lamp_" + key, null, Vector2.one, Color.black, emissive: true, emissionColor: c * 2.2f, smoothness: 0.75f, metallic: 0f);

        // ------------------------------------------------------------------ vehicles

        public static Material CarPaint(Color c)
            => Make("carpaint_" + ColorUtility.ToHtmlStringRGB(c), null, Vector2.one, c, smoothness: 0.88f, metallic: 0.75f);

        public static Material CarGlass => Make("carglass", null, Vector2.one, new Color(0.09f, 0.11f, 0.13f, 0.72f),
            smoothness: 0.97f, metallic: 0.45f, transparent: true);

        public static Material Chrome => Make("chrome", "metal", new Vector2(3f, 3f), new Color(0.86f, 0.87f, 0.90f),
            smoothness: 0.92f, metallic: 1f);

        public static Material HeadLight => Lamp(new Color(1f, 0.96f, 0.85f), "head");
        public static Material TailLight => Lamp(new Color(1f, 0.12f, 0.08f), "tail");
        public static Material Indicator => Lamp(new Color(1f, 0.55f, 0.05f), "indicator");

        // ------------------------------------------------------------------ characters

        public static Material Skin(int tone)
        {
            Color[] tones =
            {
                new Color(0.94f, 0.78f, 0.65f), new Color(0.86f, 0.68f, 0.53f),
                new Color(0.72f, 0.53f, 0.39f), new Color(0.55f, 0.38f, 0.27f),
                new Color(0.38f, 0.25f, 0.18f), new Color(0.24f, 0.16f, 0.12f),
            };
            int i = Mathf.Clamp(tone, 0, tones.Length - 1);
            return Make("skin_" + i, null, Vector2.one, tones[i], smoothness: 0.32f, metallic: 0f);
        }

        public static Material Cloth(Color c)
            => Make("cloth_" + ColorUtility.ToHtmlStringRGB(c), null, Vector2.one, c, smoothness: 0.14f, metallic: 0f);

        public static Material Leather(Color c)
            => Make("leather_" + ColorUtility.ToHtmlStringRGB(c), null, Vector2.one, c, smoothness: 0.38f, metallic: 0f);

        public static Material Hair(Color c)
            => Make("hair_" + ColorUtility.ToHtmlStringRGB(c), null, Vector2.one, c, smoothness: 0.25f, metallic: 0f);

        // ------------------------------------------------------------------ fx

        public static Material FxAdditive(Color c, string key)
            => Make("fx_" + key, null, Vector2.one, c, emissive: true, emissionColor: c * 3f, transparent: true);

        // ------------------------------------------------------------------ day / night

        /// <summary>0 = day, 1 = night. Drives window, billboard and street lamp emission.</summary>
        public static void SetNightFactor(float t)
        {
            t = Mathf.Clamp01(t);
            float k = Mathf.Lerp(0.06f, 1f, t);
            for (int i = 0; i < nightMaterials.Count; i++)
            {
                var m = nightMaterials[i];
                if (m == null) continue;
                Color baseColor;
                if (!emissionBase.TryGetValue(m, out baseColor)) continue;
                m.SetColor("_EmissionColor", baseColor * k);
            }
        }
    }
}
