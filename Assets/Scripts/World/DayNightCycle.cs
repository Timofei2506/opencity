using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenCity
{
    /// <summary>
    /// Drives the sun, moon, skybox shader, ambient trilight, fog, window emission and the
    /// street lamp budget. A full day lasts GameConst.DayLengthSeconds.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        [Range(0f, 24f)] public float Hour = 15.5f;
        public Light Sun;
        public Light Moon;

        public static readonly List<Light> StreetLights = new List<Light>();

        Material skyMaterial;
        float reflectionTimer;
        float lightBudgetTimer;
        static readonly List<Light> sortedLights = new List<Light>();

        public bool IsNight => Hour < 6.2f || Hour > 19.6f;
        public float NightFactor
        {
            get
            {
                // 0 in full day, 1 in full night, smooth around dusk/dawn
                float elev = Mathf.Sin((Hour / 24f) * 360f * Mathf.Deg2Rad - Mathf.PI * 0.5f);
                return Mathf.Clamp01(1f - Mathf.InverseLerp(-0.12f, 0.18f, elev));
            }
        }

        public void SetHour(float hour)
        {
            Hour = Mathf.Repeat(hour, 24f);
            Apply();
        }

        void Awake()
        {
            if (Sun == null)
            {
                var go = new GameObject("Sun");
                go.transform.SetParent(transform, false);
                Sun = go.AddComponent<Light>();
                Sun.type = LightType.Directional;
                Sun.shadows = LightShadows.Soft;
                Sun.shadowStrength = 0.82f;
                Sun.shadowBias = 0.04f;
                Sun.shadowNormalBias = 0.35f;
                Sun.shadowNearPlane = 0.2f;
            }

            if (Moon == null)
            {
                var go = new GameObject("Moon");
                go.transform.SetParent(transform, false);
                Moon = go.AddComponent<Light>();
                Moon.type = LightType.Directional;
                Moon.shadows = LightShadows.None;
                Moon.color = new Color(0.55f, 0.66f, 1f);
            }

            var shader = Shader.Find("OpenCity/SkyboxSky");
            if (shader == null) shader = Shader.Find("Skybox/Procedural");
            if (shader != null)
            {
                skyMaterial = new Material(shader) { name = "Skybox" };
                RenderSettings.skybox = skyMaterial;
            }

            RenderSettings.sun = Sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 90f;
            RenderSettings.fogEndDistance = 520f;

            QualitySettings.shadowDistance = 190f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

            Apply();
        }

        void Update()
        {
            Hour = Mathf.Repeat(Hour + (24f * Time.deltaTime / GameConst.DayLengthSeconds), 24f);
            Apply();
            UpdateLightBudget();
        }

        void Apply()
        {
            float angle = Hour / 24f * 360f - 90f;      // -90 at midnight, +90 at noon
            float elevation = Mathf.Sin(angle * Mathf.Deg2Rad);
            float day = Mathf.Clamp01(elevation * 2.6f + 0.12f);
            float night = NightFactor;

            Quaternion sunRot = Quaternion.Euler(angle, 42f, 0f);
            if (Sun != null)
            {
                Sun.transform.rotation = sunRot;
                Sun.intensity = Mathf.Max(0f, elevation) * 1.45f;
                Sun.color = Color.Lerp(new Color(1f, 0.62f, 0.34f), new Color(1f, 0.97f, 0.92f),
                                       Mathf.Clamp01(elevation * 2.2f));
            }

            if (Moon != null)
            {
                Moon.transform.rotation = Quaternion.Euler(angle + 180f, 42f, 0f);
                Moon.intensity = night * 0.18f;
            }

            // ambient (trilight) - warm bounce during the day, deep blue at night
            Color daySky = new Color(0.45f, 0.56f, 0.72f);
            Color nightSky = new Color(0.045f, 0.055f, 0.095f);
            Color dayGround = new Color(0.26f, 0.24f, 0.21f);
            Color nightGround = new Color(0.035f, 0.035f, 0.045f);
            Color duskTint = new Color(0.62f, 0.42f, 0.32f);

            bool dusk = elevation > -0.25f && elevation < 0.25f;
            RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySky, day) + (dusk ? duskTint * 0.10f : Color.clear);
            RenderSettings.ambientEquatorColor = Color.Lerp(nightSky * 1.2f, daySky * 0.85f, day);
            RenderSettings.ambientGroundColor = Color.Lerp(nightGround, dayGround, day);
            RenderSettings.ambientIntensity = Mathf.Lerp(1.1f, 1f, day);

            // fog matches the horizon, denser and colder at night
            if (skyMaterial != null)
            {
                Color horizonDay = new Color(0.74f, 0.80f, 0.86f);
                Color horizonDusk = new Color(0.88f, 0.66f, 0.50f);
                Color horizonNight = new Color(0.055f, 0.065f, 0.105f);
                Color horizon = dusk ? Color.Lerp(horizonNight, horizonDusk, day) : Color.Lerp(horizonNight, horizonDay, day);

                RenderSettings.fogColor = horizon;
                RenderSettings.fogStartDistance = Mathf.Lerp(50f, 95f, day);
                RenderSettings.fogEndDistance = Mathf.Lerp(300f, 560f, day);

                if (skyMaterial.HasProperty("_SunDir"))
                {
                    skyMaterial.SetVector("_SunDir", -(Sun != null ? Sun.transform.forward : Vector3.down));
                    skyMaterial.SetColor("_ZenithColor",
                        Color.Lerp(new Color(0.02f, 0.03f, 0.075f), new Color(0.19f, 0.39f, 0.72f), day));
                    skyMaterial.SetColor("_HorizonColor", horizon);
                    skyMaterial.SetColor("_GroundColor",
                        Color.Lerp(new Color(0.02f, 0.022f, 0.03f), new Color(0.20f, 0.19f, 0.17f), day));
                    skyMaterial.SetColor("_SunColor", Sun != null ? Sun.color : Color.white);
                    skyMaterial.SetFloat("_Night", night);
                    skyMaterial.SetFloat("_CloudStrength", Mathf.Lerp(0.35f, 0.6f, day));
                    skyMaterial.SetColor("_CloudColor",
                        Color.Lerp(new Color(0.12f, 0.14f, 0.2f), Color.white, day));
                }
            }

            MaterialLibrary.SetNightFactor(night);

            // update skybox reflections every few seconds (expensive)
            reflectionTimer -= Time.deltaTime;
            if (reflectionTimer <= 0f)
            {
                reflectionTimer = 3f;
                DynamicGI.UpdateEnvironment();
            }
        }

        void UpdateLightBudget()
        {
            lightBudgetTimer -= Time.deltaTime;
            if (lightBudgetTimer > 0f) return;
            lightBudgetTimer = 0.4f;

            bool night = NightFactor > 0.35f;
            if (!night)
            {
                for (int i = 0; i < StreetLights.Count; i++)
                    if (StreetLights[i] != null && StreetLights[i].enabled) StreetLights[i].enabled = false;
                return;
            }

            var cam = CameraDirector.I != null ? CameraDirector.I.Cam : Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;

            sortedLights.Clear();
            for (int i = 0; i < StreetLights.Count; i++)
                if (StreetLights[i] != null) sortedLights.Add(StreetLights[i]);

            sortedLights.Sort((a, b) =>
                (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));

            const int budget = 14;
            for (int i = 0; i < sortedLights.Count; i++)
            {
                bool on = i < budget;
                if (sortedLights[i].enabled != on) sortedLights[i].enabled = on;
            }
        }
    }
}
