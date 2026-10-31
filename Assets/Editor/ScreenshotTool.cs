using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenCity.EditorTools
{
    /// <summary>
    /// Headless screenshot pass used for visual QA from CI:
    ///
    ///   Unity -batchmode -projectPath . \
    ///         -executeMethod OpenCity.EditorTools.ScreenshotTool.CaptureAll
    ///
    /// The world is built in *edit mode* by invoking the same construction code the
    /// game runs at startup (GameBootstrap.Awake + component Start methods), so no
    /// play mode is required - play mode inside "-batchmode" never starts and makes
    /// the editor wait forever.
    ///
    /// A graphics device is required (do not pass -nographics); on a headless Linux
    /// runner start Xvfb first and export DISPLAY.
    /// </summary>
    public static class ScreenshotTool
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string OutDir = "screenshots";

        const int W = 1600;
        const int H = 900;

        [MenuItem("OpenCity/Capture screenshots")]
        public static void CaptureAll()
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                EditorSceneManager.OpenScene(ScenePath);

                // --- build the world exactly like the game does -------------------
                var root = new GameObject("~ScreenshotWorld");
                var boot = root.AddComponent<GameBootstrap>();

                Invoke(boot, "Awake");
                Debug.Log("[Screenshots] GameBootstrap.Awake done");

                // Components create their visuals in Start(); no player loop runs in a
                // batchmode edit session, so call every Start() once by hand.
                var behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    Invoke(behaviours[i], "Start");
                }
                Debug.Log("[Screenshots] Start() invoked on " + behaviours.Length + " behaviours");

                // Let the render pipeline settle (camera setup, skybox).
                InvokeAll(behaviours, "Update");

                Capture();
            }
            catch (Exception e)
            {
                Debug.LogError("[Screenshots] failed: " + e);
                EditorApplication.Exit(2);
                return;
            }

            Debug.Log("[Screenshots] done");
            EditorApplication.Exit(0);
        }

        static void Capture()
        {
            var player = UnityEngine.Object.FindObjectOfType<PlayerController>();
            Vector3 anchor = player != null ? player.transform.position : Vector3.zero;
            Debug.Log("[Screenshots] anchor " + anchor);

            var main = Camera.main;
            Vector3 fwd = main != null ? main.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();

            // 1 - the game's own third person camera
            if (main != null) Save(main, "01_third_person");

            // 2 - street level
            FreeShot("02_street_level", anchor + fwd * 9f + Vector3.up * 1.65f,
                     anchor + fwd * 55f + Vector3.up * 2.0f, 60f);

            // 3/4 - facade
            Vector3 wall = FindWall(anchor + Vector3.up * 2.2f, out Vector3 n);
            if (wall != Vector3.zero)
            {
                FreeShot("03_facade", wall + n * 9f + Vector3.up * 2f, wall + Vector3.up * 6f, 55f);
                FreeShot("04_facade_close", wall + n * 3.5f + Vector3.up * 1.7f, wall + Vector3.up * 2.6f, 60f);
            }

            // 5/6 - car
            var cars = UnityEngine.Object.FindObjectsOfType<VehicleController>();
            if (cars.Length > 0)
            {
                var car = Nearest(cars, anchor);
                Vector3 p = car.transform.position;
                FreeShot("05_car", p + Quaternion.Euler(0f, 145f, 0f) * (car.transform.forward * 5.5f) + Vector3.up * 1.9f,
                         p + Vector3.up * 0.8f, 40f);
                FreeShot("06_car_close", p + car.transform.right * 3.0f + car.transform.forward * 2.6f + Vector3.up * 1.4f,
                         p + Vector3.up * 0.75f, 38f);
            }

            // 7 - pedestrian
            var peds = UnityEngine.Object.FindObjectsOfType<Pedestrian>();
            if (peds.Length > 0)
            {
                var ped = Nearest(peds, anchor);
                Vector3 p = ped.transform.position;
                FreeShot("07_pedestrian", p + ped.transform.forward * 2.4f + ped.transform.right * 0.9f + Vector3.up * 1.35f,
                         p + Vector3.up * 0.95f, 35f);
            }

            // 8 - player model
            if (player != null)
            {
                Vector3 p = player.transform.position;
                FreeShot("08_player", p - player.transform.forward * 3.0f + Vector3.up * 1.55f,
                         p + Vector3.up * 1.0f, 40f);
            }

            // 9 - aerial
            FreeShot("09_aerial", anchor + Vector3.up * 95f - fwd * 70f, anchor + Vector3.up * 5f, 55f);

            // 10 - night
            var clock = UnityEngine.Object.FindObjectOfType<DayNightCycle>();
            if (clock != null)
            {
                clock.Hour = 22.5f;
                Invoke(clock, "Update");
                FreeShot("10_night", anchor + fwd * 9f + Vector3.up * 1.65f,
                         anchor + fwd * 55f + Vector3.up * 2.0f, 60f);
            }
        }

        static void FreeShot(string name, Vector3 pos, Vector3 look, float fov)
        {
            var go = new GameObject("~ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 3000f;

            var main = Camera.main;
            if (main != null)
            {
                cam.clearFlags = main.clearFlags;
                cam.backgroundColor = main.backgroundColor;
                cam.cullingMask = main.cullingMask;
            }

            go.transform.position = pos;
            go.transform.LookAt(look);
            Save(cam, name);
            UnityEngine.Object.DestroyImmediate(go);
        }

        static void Save(Camera cam, string name)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("[Screenshots] saved " + name);
        }

        static Vector3 FindWall(Vector3 origin, out Vector3 normal)
        {
            normal = Vector3.forward;
            Vector3[] dirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int i = 0; i < dirs.Length; i++)
            {
                if (Physics.Raycast(origin, dirs[i], out RaycastHit hit, 60f))
                {
                    normal = hit.normal;
                    normal.y = 0f;
                    normal.Normalize();
                    return hit.point;
                }
            }
            return Vector3.zero;
        }

        static T Nearest<T>(T[] items, Vector3 from) where T : Component
        {
            T best = items[0];
            float bestD = float.MaxValue;
            for (int i = 0; i < items.Length; i++)
            {
                float d = (items[i].transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = items[i]; }
            }
            return best;
        }

        static void Invoke(object target, string method)
        {
            if (target == null) return;
            var m = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (m == null) return;
            try { m.Invoke(target, null); }
            catch (Exception e) { Debug.LogWarning("[Screenshots] " + method + " on " + target.GetType().Name + ": " + e.Message); }
        }

        static void InvokeAll(MonoBehaviour[] list, string method)
        {
            for (int i = 0; i < list.Length; i++) Invoke(list[i], method);
        }
    }
}
