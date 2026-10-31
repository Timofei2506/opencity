using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenCity.EditorTools
{
    /// <summary>
    /// Isolated render probe: builds a couple of primitives in an empty scene and renders
    /// them, logging every renderer's state. Used to find out why a specific primitive
    /// (e.g. the ground plane) does not show up in game.
    /// </summary>
    public static class DiagnosticsTool
    {
        const int W = 1200;
        const int H = 700;

        [MenuItem("OpenCity/Diagnostics/render probe")]
        public static void Run()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Directory.CreateDirectory(ScreenshotTool.OutDir);

                // light + camera
                var lightGo = new GameObject("Sun");
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.45f, 0.55f, 0.75f);

                // A: exactly like the game's ground plane
                var a = Go.MeshObject("A_ground_200", null,
                    MeshKit.GroundQuad(200f, 200f, new Vector2(200f, 200f)),
                    MaterialLibrary.Road, new Vector3(0f, 0f, 0f));

                // B: same shape, much bigger and lifted, different material
                var b = Go.MeshObject("B_ground_1000", null,
                    MeshKit.GroundQuad(1000f, 1000f, new Vector2(1000f, 1000f)),
                    MaterialLibrary.Concrete, new Vector3(0f, 6f, 0f));

                // C: small box, the thing that demonstrably renders in game
                var c = Go.MeshObject("C_box", null,
                    MeshKit.Box(new Vector3(6f, 2f, 6f)),
                    MaterialLibrary.Road, new Vector3(14f, 1f, 8f));

                Report("A", a);
                Report("B", b);
                Report("C", c);

                // top-down over A
                Shoot("diag_top", new Vector3(0f, 60f, -55f), new Vector3(0f, 1f, 0f), 55f, sun);
                // low angle over A
                Shoot("diag_low", new Vector3(0f, 1.7f, -40f), new Vector3(0f, 1.5f, 20f), 60f, sun);

                Debug.Log("[Diagnostics] done");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[Diagnostics] failed: " + e);
                EditorApplication.Exit(2);
            }
        }

        static void Report(string tag, GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            var mesh = mf != null ? mf.sharedMesh : null;
            var mat = mr != null ? mr.sharedMaterial : null;

            Debug.Log($"[Diagnostics] {tag} '{go.name}' layer={go.layer} active={go.activeInHierarchy} " +
                      $"pos={go.transform.position} scale={go.transform.localScale}");

            if (mesh != null)
            {
                Debug.Log($"[Diagnostics] {tag} mesh verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} " +
                          $"boundsCenter={mesh.bounds.center} boundsSize={mesh.bounds.size}");
            }
            else Debug.LogWarning($"[Diagnostics] {tag} NO MESH");

            if (mat != null)
            {
                Debug.Log($"[Diagnostics] {tag} material='{mat.name}' shader='{(mat.shader != null ? mat.shader.name : "NULL")}' " +
                          $"queue={mat.renderQueue} color={mat.color} mainTex={(mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null ? mat.GetTexture("_MainTex").name : "none")}");
            }
            else Debug.LogWarning($"[Diagnostics] {tag} NO MATERIAL");

            if (mr != null)
                Debug.Log($"[Diagnostics] {tag} renderer enabled={mr.enabled} visible={mr.isVisible} " +
                          $"shadowCasting={mr.shadowCastingMode} receiveShadows={mr.receiveShadows}");
        }

        static void Shoot(string name, Vector3 pos, Vector3 look, float fov, Light sun)
        {
            var go = new GameObject("~DiagCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 4000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            go.transform.position = pos;
            go.transform.LookAt(look);

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes(Path.Combine(ScreenshotTool.OutDir, name + ".png"), tex.EncodeToPNG());
            Debug.Log("[Diagnostics] saved " + name);

            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
