using UnityEngine;
using UnityEngine.UI;

namespace OpenCity
{
    /// <summary>
    /// Complete runtime built HUD: health/armour, money, wanted stars, weapon, minimap
    /// (a second camera rendering into a RenderTexture), clock, objective, crosshair,
    /// damage flash, pause menu and controls overlay. No prefabs, no TMP assets.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        Canvas canvas;
        Font font;

        Image healthFill, armorFill, damageFlash, crosshairDot;
        Image[] crosshairParts;
        Text moneyText, wantedText, weaponText, ammoText, clockText, objectiveText, messageText, starsText;
        Text speedText, subtitleText;
        RawImage minimapImage;
        RectTransform minimapRoot;
        Image[] starIcons = new Image[5];

        float messageTimer;
        float damageFlashAlpha;
        float starPulse;

        Sprite whiteSprite, starSprite, triangleSprite;
        Minimap minimap;

        // ------------------------------------------------------------------ setup

        public void Init()
        {
            font = LoadFont();
            whiteSprite = MakeSolidSprite(Color.white);
            starSprite = MakeStarSprite();
            triangleSprite = MakeTriangleSprite();

            var canvasGo = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // ---- minimap (top left) ----
            minimapRoot = MakeRect(canvas.transform, "Minimap", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                   new Vector2(28f, -28f), new Vector2(272f, 272f));
            var mmBg = minimapRoot.gameObject.AddComponent<Image>();
            mmBg.sprite = whiteSprite;
            mmBg.color = new Color(0.03f, 0.04f, 0.05f, 0.85f);

            var mmInner = MakeRect(minimapRoot, "MinimapImage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   Vector2.zero, new Vector2(258f, 258f));
            minimapImage = mmInner.gameObject.AddComponent<RawImage>();
            minimapImage.color = Color.white;

            var arrow = MakeRect(minimapRoot, "PlayerArrow", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 Vector2.zero, new Vector2(26f, 26f));
            var arrowImg = arrow.gameObject.AddComponent<Image>();
            arrowImg.sprite = triangleSprite;
            arrowImg.color = new Color(0.35f, 0.85f, 1f);

            clockText = MakeText(minimapRoot, "Clock", "15:30", 26, TextAnchor.UpperLeft,
                                 new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(4f, -6f), new Vector2(200f, 32f),
                                 new Color(0.92f, 0.94f, 0.98f));

            // ---- objective (under the minimap) ----
            objectiveText = MakeText(canvas.transform, "Objective", "", 24, TextAnchor.UpperLeft,
                                     new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -312f),
                                     new Vector2(560f, 70f), new Color(1f, 0.86f, 0.35f));

            // ---- money & wanted (top right) ----
            moneyText = MakeText(canvas.transform, "Money", "$0", 46, TextAnchor.UpperRight,
                                 new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -34f),
                                 new Vector2(420f, 60f), new Color(0.55f, 0.92f, 0.55f));

            starsText = null;
            var starRow = MakeRect(canvas.transform, "Stars", new Vector2(1f, 1f), new Vector2(1f, 1f),
                                   new Vector2(-40f, -100f), new Vector2(260f, 44f));
            for (int i = 0; i < 5; i++)
            {
                var s = MakeRect(starRow, "Star" + i, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                                 new Vector2(-i * 48f, 0f), new Vector2(40f, 40f));
                var img = s.gameObject.AddComponent<Image>();
                img.sprite = starSprite;
                img.color = new Color(0.25f, 0.25f, 0.28f, 0.75f);
                starIcons[i] = img;
            }

            // ---- health / armour (bottom left) ----
            MakeText(canvas.transform, "HealthLabel", "HEALTH", 20, TextAnchor.LowerLeft,
                     new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 106f), new Vector2(200f, 24f),
                     new Color(0.85f, 0.86f, 0.9f, 0.85f));

            var healthBg = MakeRect(canvas.transform, "HealthBg", new Vector2(0f, 0f), new Vector2(0f, 0f),
                                    new Vector2(34f, 62f), new Vector2(340f, 26f));
            var hbImg = healthBg.gameObject.AddComponent<Image>();
            hbImg.sprite = whiteSprite;
            hbImg.color = new Color(0.06f, 0.07f, 0.08f, 0.8f);

            var healthFillRt = MakeRect(healthBg, "HealthFill", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                        new Vector2(3f, 0f), new Vector2(334f, 20f));
            healthFill = healthFillRt.gameObject.AddComponent<Image>();
            healthFill.sprite = whiteSprite;
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;
            healthFill.color = new Color(0.82f, 0.25f, 0.25f);
            healthFill.fillAmount = 1f;

            var armorBg = MakeRect(canvas.transform, "ArmorBg", new Vector2(0f, 0f), new Vector2(0f, 0f),
                                   new Vector2(34f, 30f), new Vector2(340f, 20f));
            var abImg = armorBg.gameObject.AddComponent<Image>();
            abImg.sprite = whiteSprite;
            abImg.color = new Color(0.06f, 0.07f, 0.08f, 0.8f);

            var armorFillRt = MakeRect(armorBg, "ArmorFill", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                       new Vector2(3f, 0f), new Vector2(334f, 14f));
            armorFill = armorFillRt.gameObject.AddComponent<Image>();
            armorFill.sprite = whiteSprite;
            armorFill.type = Image.Type.Filled;
            armorFill.fillMethod = Image.FillMethod.Horizontal;
            armorFill.color = new Color(0.35f, 0.62f, 0.92f);
            armorFill.fillAmount = 0f;

            // ---- weapon / ammo (bottom right) ----
            weaponText = MakeText(canvas.transform, "Weapon", "Pistol", 30, TextAnchor.LowerRight,
                                  new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 74f),
                                  new Vector2(420f, 36f), new Color(0.92f, 0.93f, 0.96f));
            ammoText = MakeText(canvas.transform, "Ammo", "0", 44, TextAnchor.LowerRight,
                                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 30f),
                                new Vector2(420f, 50f), new Color(0.95f, 0.85f, 0.45f));
            speedText = MakeText(canvas.transform, "Speed", "", 40, TextAnchor.LowerRight,
                                 new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 130f),
                                 new Vector2(420f, 50f), new Color(0.8f, 0.9f, 1f));

            // ---- messages ----
            messageText = MakeText(canvas.transform, "Message", "", 30, TextAnchor.LowerCenter,
                                   new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f),
                                   new Vector2(1000f, 44f), new Color(1f, 0.95f, 0.85f));
            subtitleText = MakeText(canvas.transform, "Subtitle", "", 22, TextAnchor.LowerCenter,
                                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 148f),
                                    new Vector2(1000f, 30f), new Color(0.75f, 0.8f, 0.86f));

            // ---- crosshair ----
            crosshairParts = new Image[5];
            for (int i = 0; i < 4; i++)
            {
                var c = MakeRect(canvas.transform, "Cross" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 Vector2.zero, new Vector2(2f, 10f));
                var img = c.gameObject.AddComponent<Image>();
                img.sprite = whiteSprite;
                img.color = new Color(1f, 1f, 1f, 0.85f);
                c.pivot = new Vector2(0.5f, 0.5f);
                crosshairParts[i] = img;
                img.enabled = false;
            }
            var dotRt = MakeRect(canvas.transform, "CrossDot", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 Vector2.zero, new Vector2(3f, 3f));
            crosshairDot = dotRt.gameObject.AddComponent<Image>();
            crosshairDot.sprite = whiteSprite;
            crosshairDot.color = new Color(1f, 1f, 1f, 0.9f);
            crosshairDot.enabled = false;

            // ---- damage flash ----
            var flashRt = MakeRect(canvas.transform, "DamageFlash", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   Vector2.zero, new Vector2(4000f, 4000f));
            damageFlash = flashRt.gameObject.AddComponent<Image>();
            damageFlash.sprite = whiteSprite;
            damageFlash.color = new Color(0.75f, 0.05f, 0.05f, 0f);
            damageFlash.raycastTarget = false;

            // ---- minimap camera ----
            var camGo = new GameObject("MinimapCamera");
            camGo.transform.SetParent(transform, false);
            minimap = camGo.AddComponent<Minimap>();
            minimap.Init();
            minimapImage.texture = minimap.Texture;
        }

        void Start()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Player == null) return;
            gm.Player.Health.Hit += OnPlayerHit;
        }

        void OnDestroy()
        {
            var gm = GameManager.I;
            if (gm != null && gm.Player != null && gm.Player.Health != null)
                gm.Player.Health.Hit -= OnPlayerHit;
            if (minimap != null) minimap.Release();
        }

        void OnPlayerHit(float damage, Vector3 point, Vector3 dir)
        {
            damageFlashAlpha = Mathf.Min(0.55f, damageFlashAlpha + 0.18f + damage * 0.012f);
        }

        // ------------------------------------------------------------------ api

        public void ShowMessage(string text, float duration = 3.2f)
        {
            if (messageText == null) return;
            messageText.text = text;
            messageTimer = duration;
        }

        public void SetObjective(string text)
        {
            if (objectiveText != null) objectiveText.text = text;
        }

        public void ShowPauseMenu(bool visible)
        {
            paused = visible;
            if (visible && messageText != null) messageText.text = "";
        }

        bool paused;

        // ------------------------------------------------------------------ update

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            float dt = Time.unscaledDeltaTime;

            var player = gm.Player;
            if (player != null && player.Health != null)
            {
                healthFill.fillAmount = Mathf.Lerp(healthFill.fillAmount, player.Health.Fraction, dt * 10f);
                healthFill.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f), new Color(0.55f, 0.78f, 0.30f),
                                              player.Health.Fraction);
                armorFill.fillAmount = Mathf.Lerp(armorFill.fillAmount, player.Health.Armor / Mathf.Max(1f, player.Health.MaxArmor), dt * 10f);
            }

            moneyText.text = "$" + gm.Money.ToString("N0");

            int stars = gm.Stars;
            starPulse += dt * 4f;
            for (int i = 0; i < starIcons.Length; i++)
            {
                bool on = i < stars;
                Color target = on ? new Color(1f, 0.79f, 0.22f) : new Color(0.22f, 0.22f, 0.25f, 0.7f);
                if (on) target *= 0.85f + 0.15f * Mathf.Sin(starPulse + i);
                starIcons[i].color = Color.Lerp(starIcons[i].color, target, dt * 8f);
            }

            if (player != null && player.Combat != null)
            {
                var def = PlayerCombat.Def(player.Combat.Current);
                weaponText.text = def.name;
                ammoText.text = (player.InVehicle ? "-" : (player.Combat.Clip(player.Combat.Current) + " / " +
                                 player.Combat.Reserve(player.Combat.Current)));
            }

            if (player != null && player.InVehicle && player.Vehicle != null)
            {
                speedText.text = Mathf.RoundToInt(player.Vehicle.SpeedKmh) + " km/h";
            }
            else speedText.text = "";

            // clock
            if (gm.Clock != null)
            {
                int h = Mathf.FloorToInt(gm.Clock.Hour);
                int m = Mathf.FloorToInt((gm.Clock.Hour - h) * 60f);
                clockText.text = string.Format("{0:00}:{1:00}", h, m);
            }

            // messages
            if (messageTimer > 0f)
            {
                messageTimer -= Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(messageTimer * 2f);
                SetAlpha(messageText, a);
            }
            else SetAlpha(messageText, 0f);

            // objective fade when empty
            SetAlpha(objectiveText, string.IsNullOrEmpty(objectiveText.text) ? 0f : 1f);

            // crosshair
            bool aiming = player != null && player.Aiming && !player.InVehicle;
            SetCrosshair(aiming || (player != null && player.Combat != null && !player.InVehicle));

            // damage flash
            if (damageFlashAlpha > 0f) damageFlashAlpha = Mathf.Max(0f, damageFlashAlpha - dt * 1.1f);
            damageFlash.color = new Color(0.7f, 0.04f, 0.04f, damageFlashAlpha);

            // minimap follows the player
            if (minimap != null && player != null)
                minimap.Follow(player.InVehicle && player.Vehicle != null ? player.Vehicle.transform : player.transform);
        }

        void SetCrosshair(bool visible)
        {
            for (int i = 0; i < crosshairParts.Length; i++)
                if (crosshairParts[i] != null && crosshairParts[i].enabled != visible) crosshairParts[i].enabled = visible;
            if (crosshairDot != null && crosshairDot.enabled != visible) crosshairDot.enabled = visible;
            if (!visible) return;

            var player = GameManager.I != null ? GameManager.I.Player : null;
            float spread = player != null && player.Aiming ? 7f : 16f;
            float[] angles = { 90f, 270f, 0f, 180f };
            for (int i = 0; i < 4; i++)
            {
                var rt = crosshairParts[i].rectTransform;
                float rad = angles[i] * Mathf.Deg2Rad;
                rt.anchoredPosition = new Vector2(Mathf.Cos(rad) * spread, Mathf.Sin(rad) * spread);
                rt.localRotation = Quaternion.Euler(0f, 0f, angles[i] - 90f);
            }
        }

        static void SetAlpha(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            c.a = a;
            g.color = c;
        }

        // ------------------------------------------------------------------ pause / help

        void OnGUI()
        {
            if (!paused && (GameManager.I == null || !GameManager.I.ShowHelp)) return;

            var gm = GameManager.I;
            float w = 520f, h = paused ? 420f : 470f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            var oldColor = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.94f);
            GUI.Box(new Rect(rect.x - 8f, rect.y - 8f, rect.width + 16f, rect.height + 16f), "");
            GUI.color = oldColor;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(1f, 0.86f, 0.35f);
            GUI.Label(new Rect(rect.x, rect.y + 8f, rect.width, 44f), paused ? "PAUSED" : "CONTROLS", title);

            if (paused)
            {
                var btn = new GUIStyle(GUI.skin.button) { fontSize = 22 };
                float bw = 380f, bh = 56f, bx = rect.x + (rect.width - bw) * 0.5f, by = rect.y + 74f;
                if (GUI.Button(new Rect(bx, by, bw, bh), "Resume", btn)) gm.SetPaused(false);
                if (GUI.Button(new Rect(bx, by + 68f, bw, bh), "Save game (F5)", btn)) gm.SaveGame();
                if (GUI.Button(new Rect(bx, by + 136f, bw, bh), "Load game (F9)", btn)) gm.LoadGame();
                if (GUI.Button(new Rect(bx, by + 204f, bw, bh), "Delete save", btn)) SaveSystem.Delete();
                if (GUI.Button(new Rect(bx, by + 272f, bw, bh), "Quit", btn))
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }
            else
            {
                var body = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
                body.normal.textColor = new Color(0.88f, 0.9f, 0.94f);
                string text =
                    "WASD - move      Shift - sprint      Space - jump\n" +
                    "Mouse - look      LMB - fire         RMB - aim\n" +
                    "F - enter / exit a car      Space - handbrake (in car)\n" +
                    "L - headlights      H - horn(hold)      1/2/3 - weapons\n" +
                    "R - reload      F5 quick save      F9 quick load\n" +
                    "Esc - pause      C - camera distance      F1 - hide this\n\n" +
                    "Jobs: step into the yellow marker, then reach the destination\n" +
                    "before the timer runs out. Crimes raise your wanted level.";
                GUI.Label(new Rect(rect.x + 26f, rect.y + 60f, rect.width - 52f, rect.height - 80f), text, body);
            }
        }

        // ------------------------------------------------------------------ ui helpers

        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 20);
            return f;
        }

        static RectTransform MakeRect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Text MakeText(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
                      Vector2 anchorPoint, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var rt = MakeRect(parent, name, anchorPoint, pivot, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.text = content;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        static Sprite MakeSolidSprite(Color c)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeStarSprite()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - c;
                    float r = p.magnitude / (S * 0.5f);
                    float a = Mathf.Atan2(p.y, p.x) + Mathf.PI * 0.5f;
                    // 5 point star: radius modulated by 5 fold symmetry
                    float k = Mathf.Cos(a * 5f);
                    float radius = Mathf.Lerp(0.42f, 1.0f, Mathf.Pow(Mathf.Max(0f, k), 0.6f));
                    float edge = Mathf.Clamp01((radius - r) * 12f);
                    px[y * S + x] = new Color(1f, 1f, 1f, edge);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeTriangleSprite()
        {
            const int S = 48;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float ny = (y + 0.5f) / S;
                    float halfWidth = Mathf.Lerp(0.5f, 0.06f, ny);
                    float nx = Mathf.Abs((x + 0.5f) / S - 0.5f);
                    px[y * S + x] = new Color(1f, 1f, 1f, nx < halfWidth ? 1f : 0f);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f));
        }
    }

    /// <summary>Top down orthographic camera rendering the world into a RenderTexture.</summary>
    public class Minimap : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public RenderTexture Texture { get; private set; }
        Transform target;

        public void Init()
        {
            Texture = new RenderTexture(384, 384, 16, RenderTextureFormat.Default);
            Texture.name = "MinimapRT";
            Texture.filterMode = FilterMode.Bilinear;
            Texture.Create();

            Cam = gameObject.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.orthographicSize = 78f;
            Cam.nearClipPlane = 1f;
            Cam.farClipPlane = 420f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.05f, 0.06f, 0.075f);
            Cam.targetTexture = Texture;
            Cam.depth = -10;
            Cam.allowHDR = false;
            Cam.allowMSAA = false;
            Cam.cullingMask = ~0;
            Cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        public void Follow(Transform t)
        {
            target = t;
        }

        void LateUpdate()
        {
            if (target == null || Cam == null) return;
            float yaw = target.eulerAngles.y;
            Vector3 pos = target.position + Vector3.up * 160f;
            Cam.transform.position = pos;
            Cam.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
        }

        public void Release()
        {
            if (Cam != null) Cam.targetTexture = null;
            if (Texture != null)
            {
                Texture.Release();
                Go.DestroySafe(Texture);
            }
            if (Cam != null) Go.DestroySafe(Cam.gameObject);
        }
    }
}
