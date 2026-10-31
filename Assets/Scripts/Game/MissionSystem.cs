using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// Simple GTA style job loop: walk into the yellow marker to pick up a delivery, then
    /// reach the destination before the timer runs out to get paid.
    /// </summary>
    public class MissionSystem : MonoBehaviour
    {
        public bool Active { get; private set; }
        public float TimeLeft { get; private set; }
        public Vector3 TargetPoint { get; private set; }
        public float Duration = 110f;
        public int Reward = 320;

        Transform marker;
        Renderer markerRenderer;
        Material markerMat;
        float cooldown;
        bool awaitingPickup;
        float spin;

        public void Init()
        {
            markerMat = MaterialLibrary.Signal(new Color(1f, 0.85f, 0.15f), "mission");
            var mesh = MeshKit.Cylinder(2.4f, 0.06f, 24, true);
            var go = Go.MeshObject("MissionMarker", transform, mesh, markerMat, Vector3.zero, null, null, false);
            marker = go.transform;
            markerRenderer = go.GetComponent<Renderer>();
            cooldown = 3f;
            SetMarkerVisible(false);

            if (GameManager.I != null) GameManager.I.SetObjective("");
        }

        void SetMarkerVisible(bool v)
        {
            if (markerRenderer != null) markerRenderer.enabled = v;
        }

        public void StartDelivery()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Roads == null || gm.Player == null) return;

            var rng = new System.Random(Random.Range(0, int.MaxValue));
            Vector3 player = gm.Player.transform.position;
            Vector3 dir;
            Vector3 best = player;
            for (int i = 0; i < 40; i++)
            {
                Vector3 candidate = gm.Roads.RandomSpawn(rng, out dir);
                float d = Vector3.Distance(candidate, player);
                if (d > 140f && d < 420f) { best = candidate; break; }
                best = candidate;
            }

            TargetPoint = best;
            TimeLeft = Duration;
            Active = true;
            awaitingPickup = false;
            MarkerTo(TargetPoint);
            SetMarkerVisible(true);
            gm.SetObjective("DELIVERY: reach the marker  ($" + Reward + ")");
            gm.ShowMessage("Job accepted - deliver the package!", 2.6f);
            ProceduralAudio.PlayAt(ProceduralAudio.Cash, player, 0.5f, 1.1f);
        }

        public void RestoreMission(Vector3 target, float timeLeft)
        {
            TargetPoint = target;
            TimeLeft = Mathf.Max(1f, timeLeft);
            Active = true;
            awaitingPickup = false;
            MarkerTo(target);
            SetMarkerVisible(true);
            if (GameManager.I != null) GameManager.I.SetObjective("DELIVERY: reach the marker");
        }

        public void Cancel()
        {
            Active = false;
            awaitingPickup = false;
            SetMarkerVisible(false);
            if (GameManager.I != null) GameManager.I.SetObjective("");
        }

        void MarkerTo(Vector3 pos)
        {
            if (marker == null) return;
            marker.position = new Vector3(pos.x, GameConst.SidewalkHeight + 0.12f, pos.z);
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Player == null) return;
            float dt = Time.deltaTime;

            spin += dt * 40f;
            if (marker != null && markerRenderer != null && markerRenderer.enabled)
                marker.rotation = Quaternion.Euler(0f, spin, 0f);

            if (Active)
            {
                TimeLeft -= dt;
                if (TimeLeft <= 0f)
                {
                    Active = false;
                    SetMarkerVisible(false);
                    gm.SetObjective("");
                    gm.ShowMessage("Delivery failed - out of time.", 3f);
                    cooldown = 6f;
                    return;
                }

                float dist = Vector3.Distance(
                    new Vector3(gm.Player.transform.position.x, 0f, gm.Player.transform.position.z),
                    new Vector3(TargetPoint.x, 0f, TargetPoint.z));
                if (dist < 5f)
                {
                    Active = false;
                    SetMarkerVisible(false);
                    gm.SetObjective("");
                    gm.AddMoney(Reward, "Delivery complete");
                    gm.MissionsDone++;
                    FxFactory.Cash(gm.Player.transform.position);
                    ProceduralAudio.PlayAt(ProceduralAudio.Cash, gm.Player.transform.position, 0.8f, 1f);
                    cooldown = 12f;
                }
                return;
            }

            // offer a new job once in a while
            cooldown -= dt;
            if (cooldown <= 0f)
            {
                cooldown = 14f;
                if (gm.Roads == null) return;
                var rng = new System.Random(Random.Range(0, int.MaxValue));
                Vector3 dir;
                Vector3 spot = gm.Roads.RandomSpawn(rng, out dir);
                if (Vector3.Distance(spot, gm.Player.transform.position) > 220f) return;
                TargetPoint = spot;
                MarkerTo(spot);
                SetMarkerVisible(true);
                awaitingPickup = true;
                gm.SetObjective("Yellow marker: pick up a job");
            }

            if (awaitingPickup && markerRenderer != null && markerRenderer.enabled)
            {
                float dist = Vector3.Distance(
                    new Vector3(gm.Player.transform.position.x, 0f, gm.Player.transform.position.z),
                    new Vector3(marker.position.x, 0f, marker.position.z));
                if (dist < 5f) StartDelivery();
            }
        }
    }
}
