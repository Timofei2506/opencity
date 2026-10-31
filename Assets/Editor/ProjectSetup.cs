using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenCity.EditorTools
{
    /// <summary>
    /// Project configuration that must survive a clean CI checkout: scene creation and
    /// build settings, linear colour space, quality/shadow settings and the "always included
    /// shaders" list (the game builds every material at runtime, so the shaders used by
    /// Shader.Find have to be forced into the player build).
    /// </summary>
    [InitializeOnLoad]
    public class ProjectSetup : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        const string ScenePath = "Assets/Scenes/Main.unity";

        // Shaders that the game looks up at runtime through Shader.Find(...) and therefore
        // must exist in the player build.
        //
        // IMPORTANT: never list Unity's internal shaders here (Hidden/InternalErrorShader,
        // GUI/Text Shader, ...). They live in "Library/unity default resources" with
        // HideFlags.DontSave, and forcing them into the build makes the player packager try to
        // serialise them into Resources/unity_builtin_extra - the write lock is never released
        // ("Assertion failed on expression: 'm_LockCount == 0'") and the build dies with
        // "Building - Failed to write file: .../unity_builtin_extra".
        static readonly string[] AlwaysIncludedShaders =
        {
            "Standard",
            "Skybox/Procedural",
            "OpenCity/SkyboxSky",
        };

        static ProjectSetup()
        {
            EditorApplication.delayCall += () => Apply(false);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            Apply(true);
        }

        [MenuItem("OpenCity/Apply project settings")]
        public static void ApplyFromMenu() => Apply(true);

        /// <summary>
        /// Applies every setting and dumps the resulting ProjectSettings/GraphicsSettings/
        /// QualitySettings assets as base64 into the log, so a CI run can hand them back
        /// and the repository can store the exact project configuration (colour space,
        /// shader list, quality levels) instead of relying on Unity's defaults.
        /// </summary>
        public static void DumpSettings()
        {
            Apply(true);
            AssetDatabase.SaveAssets();

            string[] files =
            {
                "ProjectSettings/ProjectSettings.asset",
                "ProjectSettings/GraphicsSettings.asset",
                "ProjectSettings/QualitySettings.asset",
                "ProjectSettings/TagManager.asset",
                "ProjectSettings/DynamicsManager.asset",
            };

            foreach (var f in files)
            {
                if (!File.Exists(f))
                {
                    Debug.Log("[DumpSettings] MISSING " + f);
                    continue;
                }
                var bytes = File.ReadAllBytes(f);
                Debug.Log("[DumpSettings] BEGIN " + f + " " + bytes.Length);
                Debug.Log("[DumpSettings] DATA " + Convert.ToBase64String(bytes));
                Debug.Log("[DumpSettings] END " + f);
            }

            Debug.Log("[DumpSettings] colorSpace=" + PlayerSettings.colorSpace);
        }

        [MenuItem("OpenCity/Create or repair Main scene")]
        public static void RepairScene()
        {
            EnsureScene();
            AssetDatabase.SaveAssets();
        }

        static void Apply(bool verbose)
        {
            EnsureScene();
            EnsureBuildSettings();

            // ---- player settings ----
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "OpenCity Studio";
            PlayerSettings.productName = "OpenCity";
            PlayerSettings.bundleVersion = GameConst.Version;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);

            // ---- quality ----
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowDistance = 190f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.lodBias = 1.5f;

            EnsureAlwaysIncludedShaders();

            if (verbose) AssetDatabase.SaveAssets();
            Debug.Log("[OpenCity] project settings applied");
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("GameBootstrap");
                go.AddComponent<GameBootstrap>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
                Debug.Log("[OpenCity] created " + ScenePath);
            }
        }

        static void EnsureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool found = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    scenes[i].enabled = true;
                    found = true;
                }
            }
            if (!found) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureAlwaysIncludedShaders()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0) return;

            var so = new SerializedObject(assets[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;

            for (int s = 0; s < AlwaysIncludedShaders.Length; s++)
            {
                var shader = Shader.Find(AlwaysIncludedShaders[s]);
                if (shader == null)
                {
                    Debug.LogWarning("[OpenCity] shader not found: " + AlwaysIncludedShaders[s]);
                    continue;
                }

                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var element = list.GetArrayElementAtIndex(i);
                    if (element.objectReferenceValue == shader) { present = true; break; }
                }
                if (present) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>
    /// Forces the correct import settings for the generated PBR texture set, based on the
    /// file name suffix ( _d albedo, _n normal, _mg metallic-gloss, _e emissive ).
    /// </summary>
    public class TextureImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Textures/")) return;

            var importer = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.npotScale = TextureImporterNPOTScale.None;

            if (file.EndsWith("_n"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
            }
            else if (file.EndsWith("_mg"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = file.Contains("leaves") || file.Contains("soft_dot") ||
                                               file.Contains("circle_mask") || file.Contains("ui_panel");
            }

            // sprites used by the UI are generated at runtime; keep these as plain textures
            importer.mipmapEnabled = !file.Contains("ui_panel");
        }
    }
}
