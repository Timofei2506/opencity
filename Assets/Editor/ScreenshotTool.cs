using System.Collections;
using System.IO;
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
    /// The editor must run with a graphics device (do NOT pass -nographics); on a
    /// headless Linux runner wrap the call in xvfb-run.
    /// </summary>
    [InitializeOnLoad]
    public static class ScreenshotTool
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string OutDir = "screenshots";

        const string SessionKey = "OpenCity.Screenshots";

        static ScreenshotTool()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("OpenCity/Capture screenshots")]
        public static void CaptureFromMenu() => CaptureAll();

        public static void CaptureAll()
        {
            Directory.CreateDirectory(OutDir);
            SessionState.SetBool(SessionKey, true);
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[Screenshots] entering play mode");
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            if (!SessionState.GetBool(SessionKey, false)) return;

            SessionState.SetBool(SessionKey, false);
            var go = new GameObject("~ScreenshotRunner");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ScreenshotRunner>();
            Debug.Log("[Screenshots] runner created");
        }
    }

    /// <summary>Waits for the procedural world, then renders a fixed set of views.</summary>
    public class ScreenshotRunner : MonoBehaviour
    {
        const int W = 1600;
        const int H = 900;

        IEnumerator Start()
        {
            Debug.Log("[Screenshots] waiting for world generation");
            yield return new WaitForSeconds(4f);

            var player = FindObjectOfType<PlayerController>();
            Vector3 anchor = player != null ? player.transform.position : Vector3.zero;
            Debug.Log("[Screenshots] anchor " + anchor);

            // 1 - the game's own third person camera, untouched
            var main = Camera.main;
            if (main != null)
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                Save(main, "01_third_person");
            }

            // 2 - street level, looking along the road
            Vector3 fwd = main != null ? main.transform.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            yield return FreeShot("02_street_level", anchor + fwd * 9f + Vector3.up * 1.65f,
                                  anchor + fwd * 55f + Vector3.up * 2.0f, 60f);

            // 3 - building facade close up
            Vector3 wall = FindWall(anchor + Vector3.up * 2.2f, out Vector3 wallNormal);
            if (wall != Vector3.zero)
            {
                yield return FreeShot("03_facade", wall + wallNormal * 9f + Vector3.up * 2f,
                                      wall + Vector3.up * 6f, 55f);
                yield return FreeShot("04_facade_door", wall + wallNormal * 4.5f + Vector3.up * 1.6f,
                                      wall + Vector3.up * 2.2f, 60f);
            }

            // 5 - closest car
            var cars = FindObjectsOfType<VehicleController>();
            if (cars.Length > 0)
            {
                var car = Nearest(cars, anchor);
                Vector3 p = car.transform.position;
                yield return FreeShot("05_car", p + Quaternion.Euler(0f, 145f, 0f) * (car.transform.forward * 5.5f) + Vector3.up * 1.9f,
                                      p + Vector3.up * 0.8f, 40f);
                yield return FreeShot("06_car_close", p + car.transform.right * 3.2f + car.transform.forward * 2.4f + Vector3.up * 1.5f,
                                      p + Vector3.up * 0.7f, 38f);
            }

            // 7 - closest pedestrian
            var peds = FindObjectsOfType<Pedestrian>();
            if (peds.Length > 0)
            {
                var ped = Nearest(peds, anchor);
                Vector3 p = ped.transform.position;
                yield return FreeShot("07_pedestrian", p + ped.transform.forward * 2.6f + ped.transform.right * 0.8f + Vector3.up * 1.4f,
                                      p + Vector3.up * 0.95f, 35f);
            }

            // 8 - player model
            if (player != null)
            {
                Vector3 p = player.transform.position;
                yield return FreeShot("08_player", p - player.transform.forward * 3.2f + Vector3.up * 1.6f,
                                      p + Vector3.up * 1.0f, 40f);
            }

            // 9 - aerial
            yield return FreeShot("09_aerial", anchor + Vector3.up * 95f - fwd * 70f,
                                  anchor + Vector3.up * 5f, 55f);

            // 10 - night street
            var clock = FindObjectOfType<DayNightCycle>();
            if (clock != null)
            {
                clock.Hour = 22.5f;
                yield return new WaitForSeconds(1.2f);
                yield return FreeShot("10_night", anchor + fwd * 9f + Vector3.up * 1.65f,
                                      anchor + fwd * 55f + Vector3.up * 2.0f, 60f);
            }

            Debug.Log("[Screenshots] done");
            yield return new WaitForSeconds(0.5f);
            EditorApplication.Exit(0);
        }

        IEnumerator FreeShot(string name, Vector3 pos, Vector3 look, float fov)
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

            yield return null;
            yield return new WaitForEndOfFrame();
            Save(cam, name);
            Destroy(go);
        }

        void Save(Camera cam, string name)
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

            Directory.CreateDirectory(ScreenshotTool.OutDir);
            string path = Path.Combine(ScreenshotTool.OutDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            Destroy(rt);
            Destroy(tex);
            Debug.Log("[Screenshots] saved " + path);
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
    }
}
