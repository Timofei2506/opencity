using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// The single object placed in the scene. Builds the entire game at runtime:
    /// lighting, world, player, camera, HUD, police and mission systems.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("0 = random city each run")]
        public int Seed = 0;
        public bool fullscreen = true;

        public static GameBootstrap Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            int seed = Seed != 0 ? Seed : System.Environment.TickCount;
            var rng = new System.Random(seed);
            Debug.Log("[OpenCity] building world, seed=" + seed + " version=" + GameConst.Version);

            // ---- 1. game state ----
            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(transform, false);
            var gm = gmGo.AddComponent<GameManager>();

            // ---- 2. lighting / sky ----
            var clock = new GameObject("DayNightCycle").AddComponent<DayNightCycle>();
            clock.transform.SetParent(transform, false);

            // ---- 3. world ----
            var world = new GameObject("World").transform;
            world.SetParent(transform, false);
            FxFactory.Init(world);

            var roads = RoadNetwork.Build();
            Vector3 spawn = CityGenerator.Generate(world, roads, rng);

            // ---- 4. player ----
            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(world, false);
            playerGo.transform.position = spawn;
            playerGo.AddComponent<CharacterController>();
            var player = playerGo.AddComponent<PlayerController>();
            player.Teleport(spawn, 205f);

            // ---- 5. camera ----
            var camGo = new GameObject("MainCamera");
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.06f;
            cam.farClipPlane = 1500f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();
            var director = camGo.AddComponent<CameraDirector>();
            director.Cam = cam;
            director.Player = player;
            director.Yaw = 205f;
            camGo.transform.position = spawn + Vector3.up * 2.2f - camGo.transform.forward * 4f;

            // ---- 6. hud ----
            var hud = new GameObject("HUD").AddComponent<Hud>();
            hud.transform.SetParent(transform, false);
            hud.Init();

            // ---- 7. police & missions ----
            var police = new GameObject("PoliceDispatch").AddComponent<PoliceDispatch>();
            police.transform.SetParent(transform, false);
            var missions = new GameObject("MissionSystem").AddComponent<MissionSystem>();
            missions.transform.SetParent(transform, false);
            missions.Init();

            // ---- 8. wire everything into the game manager ----
            gm.WorldRoot = world;
            gm.Roads = roads;
            gm.Clock = clock;
            gm.Player = player;
            gm.CameraRig = director;
            gm.Hud = hud;
            gm.Police = police;
            gm.Missions = missions;
            gm.SpawnPoint = spawn;
            gm.SpawnYaw = 205f;

            // ---- 9. audio ----
            ProceduralAudio.InitAmbience(transform);
            ProceduralAudio.SetAmbienceVolume(0.12f);

            Debug.Log("[OpenCity] world ready: " + GameManager.I.Vehicles.Count + " vehicles, " +
                      GameManager.I.Peds.Count + " pedestrians");

            gm.ShowMessage("OpenCity " + GameConst.Version + " - F1 for controls", 6f);
        }
    }
}
