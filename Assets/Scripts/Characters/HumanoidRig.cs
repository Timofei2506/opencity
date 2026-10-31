using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>Appearance parameters for a procedurally generated human.</summary>
    public struct HumanoidStyle
    {
        public Color shirt;
        public Color pants;
        public Color shoes;
        public Color hair;
        public int skinTone;
        public bool cap;
        public bool coat;
        public bool police;
        public float scale;

        public static readonly Color[] ShirtPalette =
        {
            new Color(0.72f, 0.20f, 0.22f), new Color(0.20f, 0.32f, 0.55f),
            new Color(0.86f, 0.84f, 0.80f), new Color(0.24f, 0.24f, 0.26f),
            new Color(0.35f, 0.48f, 0.33f), new Color(0.55f, 0.42f, 0.20f),
            new Color(0.15f, 0.16f, 0.18f), new Color(0.62f, 0.35f, 0.55f),
            new Color(0.90f, 0.62f, 0.25f), new Color(0.30f, 0.55f, 0.62f),
        };

        public static readonly Color[] PantsPalette =
        {
            new Color(0.16f, 0.18f, 0.24f), new Color(0.22f, 0.22f, 0.23f),
            new Color(0.35f, 0.32f, 0.27f), new Color(0.12f, 0.13f, 0.16f),
            new Color(0.28f, 0.33f, 0.40f),
        };

        public static HumanoidStyle Civilian(System.Random rng)
        {
            return new HumanoidStyle
            {
                shirt = ShirtPalette[rng.Next(ShirtPalette.Length)],
                pants = PantsPalette[rng.Next(PantsPalette.Length)],
                shoes = new Color(0.10f, 0.10f, 0.11f),
                hair = new Color(0.10f + 0.35f * (float)rng.NextDouble(),
                                 0.07f + 0.25f * (float)rng.NextDouble(),
                                 0.05f + 0.18f * (float)rng.NextDouble()),
                skinTone = rng.Next(0, 6),
                cap = rng.NextDouble() < 0.18,
                coat = rng.NextDouble() < 0.25,
                police = false,
                scale = 0.93f + 0.12f * (float)rng.NextDouble(),
            };
        }

        public static HumanoidStyle Player()
        {
            return new HumanoidStyle
            {
                shirt = new Color(0.13f, 0.14f, 0.17f),
                pants = new Color(0.17f, 0.19f, 0.24f),
                shoes = new Color(0.07f, 0.07f, 0.08f),
                hair = new Color(0.09f, 0.07f, 0.06f),
                skinTone = 1,
                cap = false,
                coat = true,
                police = false,
                scale = 1.0f,
            };
        }

        public static HumanoidStyle Police()
        {
            return new HumanoidStyle
            {
                shirt = new Color(0.11f, 0.15f, 0.30f),
                pants = new Color(0.09f, 0.11f, 0.20f),
                shoes = new Color(0.06f, 0.06f, 0.07f),
                hair = new Color(0.08f, 0.07f, 0.06f),
                skinTone = 1,
                cap = true,
                coat = false,
                police = true,
                scale = 1.0f,
            };
        }
    }

    /// <summary>
    /// Procedurally built, procedurally animated humanoid. No imported rig, no animations:
    /// limbs are lathed meshes driven by a small sine based locomotion solver.
    /// Used for the player, pedestrians and police officers.
    /// </summary>
    public class HumanoidRig : MonoBehaviour
    {
        // shared meshes - built once, reused by every character
        static Mesh mTorso, mHead, mHair, mCap, mBrim, mUpperArm, mForearm, mHand, mThigh, mShin, mFoot, mBadge;
        static readonly List<Mesh> owned = new List<Mesh>();

        public Transform Pelvis, Head, Neck;
        public Transform ArmL, ArmR, ForearmL, ForearmR;
        public Transform ThighL, ThighR, ShinL, ShinR, FootL, FootR;

        Material skinMat, shirtMat, pantsMat, shoesMat, hairMat, detailMat;
        readonly List<Renderer> renderers = new List<Renderer>();

        float phase;
        bool dead;
        float deathTime;
        bool visible = true;
        Vector3 baseLocalPos;
        Quaternion baseLocalRot = Quaternion.identity;

        // ------------------------------------------------------------------ build

        static void EnsureMeshes()
        {
            if (mTorso != null) return;

            var torso = new List<MeshKit.Section>
            {
                new MeshKit.Section(0.00f, 0.118f, -0.085f, 0.085f, 0.060f),
                new MeshKit.Section(0.10f, 0.126f, -0.090f, 0.092f, 0.065f),
                new MeshKit.Section(0.26f, 0.138f, -0.095f, 0.100f, 0.070f),
                new MeshKit.Section(0.42f, 0.176f, -0.100f, 0.105f, 0.075f),
                new MeshKit.Section(0.52f, 0.198f, -0.092f, 0.090f, 0.070f),
                new MeshKit.Section(0.58f, 0.150f, -0.072f, 0.070f, 0.060f),
            };
            mTorso = MeshKit.TransformMesh(MeshKit.Loft(torso, 3), Matrix4x4.Rotate(Quaternion.Euler(-90f, 0f, 0f)));

            // head: slightly egg shaped sphere
            var headPts = new List<Vector2>();
            const int hr = 8;
            for (int i = 0; i <= hr; i++)
            {
                float f = (float)i / hr;
                float ang = Mathf.PI * f;
                headPts.Add(new Vector2(Mathf.Sin(ang) * 0.098f, -Mathf.Cos(ang) * 0.115f));
            }
            mHead = MeshKit.Lathe(headPts, 14, true);

            var hairPts = new List<Vector2>();
            for (int i = 0; i <= 6; i++)
            {
                float f = (float)i / 6;
                float ang = Mathf.PI * 0.5f + Mathf.PI * 0.45f * f;
                hairPts.Add(new Vector2(Mathf.Sin(ang) * 0.104f, -Mathf.Cos(ang) * 0.120f + 0.012f));
            }
            mHair = MeshKit.Lathe(hairPts, 14, true);

            mCap = MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(0.001f, 0.045f), new Vector2(0.082f, 0.040f),
                new Vector2(0.098f, 0.012f), new Vector2(0.100f, -0.005f),
            }, 14, true);
            mBrim = MeshKit.Box(new Vector3(0.20f, 0.012f, 0.10f));

            mUpperArm = MeshKit.Limb(0.046f, 0.036f, 0.30f, 10, 2);
            mForearm = MeshKit.Limb(0.036f, 0.030f, 0.28f, 10, 2);
            mHand = MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(0.001f, -0.055f), new Vector2(0.030f, -0.050f),
                new Vector2(0.034f, 0.000f), new Vector2(0.030f, 0.030f), new Vector2(0.001f, 0.034f),
            }, 10, true);
            mThigh = MeshKit.Limb(0.062f, 0.078f, 0.45f, 10, 2);
            mShin = MeshKit.Limb(0.050f, 0.040f, 0.43f, 10, 2);
            mFoot = MeshKit.Box(new Vector3(0.095f, 0.075f, 0.245f));
            mBadge = MeshKit.Box(new Vector3(0.055f, 0.030f, 0.012f));

            owned.Add(mTorso); owned.Add(mHead); owned.Add(mHair); owned.Add(mCap); owned.Add(mBrim);
            owned.Add(mUpperArm); owned.Add(mForearm); owned.Add(mHand); owned.Add(mThigh);
            owned.Add(mShin); owned.Add(mFoot); owned.Add(mBadge);
        }

        Transform Node(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3? scale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            if (scale.HasValue) go.transform.localScale = scale.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderers.Add(mr);
            return go;
        }

        public static HumanoidRig Create(Transform parent, HumanoidStyle style, bool isPlayer = false)
        {
            EnsureMeshes();
            var root = new GameObject(isPlayer ? "PlayerRig" : "HumanoidRig");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<HumanoidRig>();
            rig.Build(style, isPlayer);
            return rig;
        }

        void Build(HumanoidStyle s, bool isPlayer)
        {
            transform.localScale = Vector3.one * s.scale;
            skinMat = MaterialLibrary.Skin(s.skinTone);
            shirtMat = MaterialLibrary.Cloth(s.shirt);
            pantsMat = MaterialLibrary.Cloth(s.pants);
            shoesMat = MaterialLibrary.Leather(s.shoes);
            hairMat = MaterialLibrary.Hair(s.hair);
            detailMat = MaterialLibrary.Plastic(new Color(0.85f, 0.72f, 0.25f));

            Pelvis = Node("Pelvis", transform, new Vector3(0f, 0.95f, 0f));
            baseLocalPos = Pelvis.localPosition;

            // torso
            var torso = Part("Torso", Pelvis, mTorso, shirtMat, new Vector3(0f, 0.02f, 0f));
            if (s.coat) torso.transform.localScale = new Vector3(1.06f, 1.02f, 1.10f);

            Neck = Node("Neck", Pelvis, new Vector3(0f, 0.58f, 0f));
            Head = Node("Head", Neck, new Vector3(0f, 0.055f, 0f));
            Part("HeadMesh", Head, mHead, skinMat, new Vector3(0f, 0.075f, 0f), new Vector3(1f, 1.05f, 0.94f));
            Part("Hair", Head, mHair, hairMat, new Vector3(0f, 0.075f, 0f), new Vector3(1.02f, 1.05f, 0.96f));
            if (s.cap)
            {
                Part("Cap", Head, mCap, s.police ? MaterialLibrary.Cloth(new Color(0.08f, 0.10f, 0.20f)) : hairMat,
                    new Vector3(0f, 0.155f, 0f));
                Part("Brim", Head, mBrim, s.police ? MaterialLibrary.Cloth(new Color(0.06f, 0.08f, 0.16f)) : hairMat,
                    new Vector3(0f, 0.135f, 0.09f));
            }
            if (s.police) Part("Badge", Pelvis, mBadge, detailMat, new Vector3(0.09f, 0.42f, 0.10f));

            // arms (pivot at the shoulder, limb hangs down)
            ArmL = Node("ArmL", Pelvis, new Vector3(-0.20f, 0.50f, 0f));
            ArmR = Node("ArmR", Pelvis, new Vector3(0.20f, 0.50f, 0f));
            BuildArm(ArmL, true);
            BuildArm(ArmR, false);

            // legs
            ThighL = Node("ThighL", Pelvis, new Vector3(-0.095f, 0f, 0f));
            ThighR = Node("ThighR", Pelvis, new Vector3(0.095f, 0f, 0f));
            BuildLeg(ThighL, true);
            BuildLeg(ThighR, false);
        }

        void BuildArm(Transform root, bool left)
        {
            Part(left ? "UpperArmL" : "UpperArmR", root, mUpperArm, shirtMat, new Vector3(0f, -0.15f, 0f));
            var fore = Node(left ? "ForearmL" : "ForearmR", root, new Vector3(0f, -0.30f, 0f));
            Part("ForearmMesh", fore, mForearm, skinMat, new Vector3(0f, -0.14f, 0f));
            Part("Hand", fore, mHand, skinMat, new Vector3(0f, -0.30f, 0f));
            if (left) ForearmL = fore; else ForearmR = fore;
        }

        void BuildLeg(Transform root, bool left)
        {
            Part(left ? "ThighMeshL" : "ThighMeshR", root, mThigh, pantsMat, new Vector3(0f, -0.225f, 0f));
            var shin = Node(left ? "ShinL" : "ShinR", root, new Vector3(0f, -0.45f, 0f));
            Part("ShinMesh", shin, mShin, pantsMat, new Vector3(0f, -0.215f, 0f));
            var foot = Node(left ? "FootL" : "FootR", shin, new Vector3(0f, -0.43f, 0f));
            Part("Shoe", foot, mFoot, shoesMat, new Vector3(0f, 0.005f, 0.045f));
            if (left) { ShinL = shin; FootL = foot; }
            else { ShinR = shin; FootR = foot; }
        }

        // ------------------------------------------------------------------ animation

        /// <summary>
        /// Drives the walk / run / aim / idle cycle. <paramref name="speed"/> is horizontal m/s.
        /// </summary>
        public void Animate(float speed, bool grounded, bool aiming, float aimPitch, float dt)
        {
            if (dead)
            {
                deathTime += dt;
                float f = Mathf.Clamp01(deathTime * 3.2f);
                transform.localRotation = Quaternion.Slerp(Quaternion.identity,
                    Quaternion.Euler(82f, transform.localRotation.eulerAngles.y, 6f), f);
                Pelvis.localPosition = Vector3.Lerp(baseLocalPos, new Vector3(0f, 0.30f, 0f), f);
                return;
            }
            if (Pelvis == null) return;

            float move = Mathf.Clamp01(speed / 6.5f);
            float freq = Mathf.Lerp(4.2f, 8.4f, move);
            if (speed > 0.12f) phase += dt * freq;
            else phase = Mathf.Lerp(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, dt * 6f);

            float amp = Mathf.Lerp(6f, 46f, move);
            float swing = Mathf.Sin(phase);
            float bob = Mathf.Cos(phase * 2f) * (0.008f + 0.022f * move);

            // torso counter rotation & breathing
            float breathe = Mathf.Sin(Time.time * 1.7f) * 1.2f;
            Pelvis.localPosition = baseLocalPos + new Vector3(0f, bob + breathe * 0.001f, 0f);
            Pelvis.localRotation = Quaternion.Euler(0f, swing * 3f * move, 0f);

            // legs
            if (grounded)
            {
                ThighL.localRotation = Quaternion.Euler(swing * amp, 0f, 1.5f);
                ThighR.localRotation = Quaternion.Euler(-swing * amp, 0f, -1.5f);
                float bendL = Mathf.Clamp(-swing, 0f, 1f) * amp * 1.25f;
                float bendR = Mathf.Clamp(swing, 0f, 1f) * amp * 1.25f;
                ShinL.localRotation = Quaternion.Euler(bendL + 4f, 0f, 0f);
                ShinR.localRotation = Quaternion.Euler(bendR + 4f, 0f, 0f);
                FootL.localRotation = Quaternion.Euler(-bendL * 0.45f, 0f, 0f);
                FootR.localRotation = Quaternion.Euler(-bendR * 0.45f, 0f, 0f);
            }
            else
            {
                ThighL.localRotation = Quaternion.Slerp(ThighL.localRotation, Quaternion.Euler(-38f, 0f, 3f), dt * 8f);
                ThighR.localRotation = Quaternion.Slerp(ThighR.localRotation, Quaternion.Euler(-12f, 0f, -3f), dt * 8f);
                ShinL.localRotation = Quaternion.Slerp(ShinL.localRotation, Quaternion.Euler(62f, 0f, 0f), dt * 8f);
                ShinR.localRotation = Quaternion.Slerp(ShinR.localRotation, Quaternion.Euler(28f, 0f, 0f), dt * 8f);
            }

            // arms
            if (aiming)
            {
                ArmR.localRotation = Quaternion.Slerp(ArmR.localRotation,
                    Quaternion.Euler(-88f + aimPitch, -8f, 6f), dt * 12f);
                ForearmR.localRotation = Quaternion.Slerp(ForearmR.localRotation,
                    Quaternion.Euler(-6f, 0f, 0f), dt * 12f);
                ArmL.localRotation = Quaternion.Slerp(ArmL.localRotation,
                    Quaternion.Euler(-70f + aimPitch, 22f, -10f), dt * 10f);
                ForearmL.localRotation = Quaternion.Slerp(ForearmL.localRotation,
                    Quaternion.Euler(-22f, 0f, 0f), dt * 10f);
            }
            else
            {
                float armAmp = amp * 0.75f;
                ArmL.localRotation = Quaternion.Slerp(ArmL.localRotation,
                    Quaternion.Euler(-swing * armAmp, 0f, 4f), dt * 10f);
                ArmR.localRotation = Quaternion.Slerp(ArmR.localRotation,
                    Quaternion.Euler(swing * armAmp, 0f, -4f), dt * 10f);
                ForearmL.localRotation = Quaternion.Slerp(ForearmL.localRotation,
                    Quaternion.Euler(-Mathf.Max(0f, swing) * armAmp * 0.7f, 0f, 0f), dt * 10f);
                ForearmR.localRotation = Quaternion.Slerp(ForearmR.localRotation,
                    Quaternion.Euler(-Mathf.Max(0f, -swing) * armAmp * 0.7f, 0f, 0f), dt * 10f);
            }

            Head.localRotation = Quaternion.Slerp(Head.localRotation,
                Quaternion.Euler(aiming ? aimPitch * 0.4f : Mathf.Sin(Time.time * 0.7f) * 2f, 0f, 0f), dt * 6f);
        }

        public void SetDead(bool value)
        {
            dead = value;
            deathTime = 0f;
            if (!value)
            {
                transform.localRotation = Quaternion.identity;
                Pelvis.localPosition = baseLocalPos;
            }
        }

        public void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null) renderers[i].enabled = value;
        }

        public Vector3 ChestPosition => Pelvis != null ? Pelvis.position + Vector3.up * 0.35f : transform.position + Vector3.up;
        public Vector3 HeadPosition => Head != null ? Head.position : transform.position + Vector3.up * 1.65f;
    }
}
