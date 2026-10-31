using System.Collections.Generic;
using UnityEngine;

namespace OpenCity
{
    /// <summary>
    /// Builds complete, drivable cars from scratch: lofted body shell, glass house,
    /// wheels, lights, colliders, WheelColliders and a driver seat.
    /// </summary>
    public static class CarFactory
    {
        public class CarSpec
        {
            public string name;
            public float wheelRadius = 0.34f;
            public float wheelWidth = 0.24f;
            public float axleFront = 1.42f;
            public float axleRear = -1.42f;
            public float trackHalf = 0.79f;
            public float mass = 1250f;
            public float motorTorque = 2100f;
            public float[] gearSpeeds = { 33f, 58f, 88f, 122f, 175f };
            public MeshKit.Section[] body;
            public MeshKit.Section[] cabin;
            public float roofY;
            public float roofLength;
            public float roofWidth;
            public Vector3 lightFront = new Vector3(0.62f, 0.62f, 2.05f);
            public float lightDepth = 0.16f;
        }

        static readonly Dictionary<int, Mesh> bodyCache = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Mesh> cabinCache = new Dictionary<int, Mesh>();

        public static readonly Color[] Paints =
        {
            new Color(0.78f, 0.79f, 0.80f), new Color(0.09f, 0.09f, 0.10f),
            new Color(0.55f, 0.10f, 0.11f), new Color(0.13f, 0.22f, 0.42f),
            new Color(0.22f, 0.30f, 0.24f), new Color(0.62f, 0.60f, 0.55f),
            new Color(0.30f, 0.31f, 0.33f), new Color(0.85f, 0.55f, 0.10f),
            new Color(0.42f, 0.12f, 0.30f), new Color(0.15f, 0.35f, 0.36f),
        };

        public static int StyleCount => 3;

        static CarSpec Spec(int style)
        {
            switch (style)
            {
                case 1: // coupe / sports
                    return new CarSpec
                    {
                        name = "Coupe",
                        wheelRadius = 0.33f, wheelWidth = 0.26f, axleFront = 1.35f, axleRear = -1.32f,
                        trackHalf = 0.80f, mass = 1180f, motorTorque = 2500f,
                        gearSpeeds = new[] { 40f, 68f, 100f, 140f, 200f },
                        roofY = 1.24f, roofLength = 1.05f, roofWidth = 1.23f,
                        lightFront = new Vector3(0.60f, 0.60f, 2.10f),
                        body = new[]
                        {
                            new MeshKit.Section(-2.20f, 0.76f, 0.28f, 0.66f, 0.12f),
                            new MeshKit.Section(-1.90f, 0.86f, 0.24f, 0.78f, 0.15f),
                            new MeshKit.Section(-1.05f, 0.90f, 0.22f, 0.85f, 0.18f),
                            new MeshKit.Section(-0.10f, 0.91f, 0.21f, 0.88f, 0.19f),
                            new MeshKit.Section(0.90f, 0.88f, 0.22f, 0.84f, 0.17f),
                            new MeshKit.Section(1.70f, 0.82f, 0.24f, 0.70f, 0.14f),
                            new MeshKit.Section(2.15f, 0.70f, 0.30f, 0.58f, 0.11f),
                        },
                        cabin = new[]
                        {
                            new MeshKit.Section(-1.55f, 0.68f, 0.82f, 0.88f, 0.07f),
                            new MeshKit.Section(-1.15f, 0.76f, 0.84f, 1.08f, 0.09f),
                            new MeshKit.Section(-0.45f, 0.80f, 0.86f, 1.20f, 0.11f),
                            new MeshKit.Section(0.25f, 0.79f, 0.86f, 1.19f, 0.11f),
                            new MeshKit.Section(0.80f, 0.74f, 0.84f, 1.02f, 0.09f),
                            new MeshKit.Section(1.20f, 0.68f, 0.82f, 0.88f, 0.07f),
                        },
                    };

                case 2: // van / small truck
                    return new CarSpec
                    {
                        name = "Van",
                        wheelRadius = 0.40f, wheelWidth = 0.28f, axleFront = 1.55f, axleRear = -1.45f,
                        trackHalf = 0.86f, mass = 2100f, motorTorque = 2650f,
                        gearSpeeds = new[] { 28f, 50f, 74f, 100f, 135f },
                        roofY = 2.05f, roofLength = 2.85f, roofWidth = 1.62f,
                        lightFront = new Vector3(0.66f, 0.78f, 2.22f),
                        body = new[]
                        {
                            new MeshKit.Section(-2.30f, 0.88f, 0.34f, 1.05f, 0.12f),
                            new MeshKit.Section(-2.05f, 0.94f, 0.30f, 1.20f, 0.14f),
                            new MeshKit.Section(-0.60f, 0.96f, 0.28f, 1.24f, 0.16f),
                            new MeshKit.Section(0.90f, 0.96f, 0.28f, 1.22f, 0.16f),
                            new MeshKit.Section(1.80f, 0.92f, 0.30f, 1.10f, 0.14f),
                            new MeshKit.Section(2.20f, 0.84f, 0.34f, 0.94f, 0.12f),
                        },
                        cabin = new[]
                        {
                            new MeshKit.Section(-2.10f, 0.86f, 1.18f, 2.02f, 0.10f),
                            new MeshKit.Section(-1.20f, 0.90f, 1.20f, 2.05f, 0.12f),
                            new MeshKit.Section(0.85f, 0.90f, 1.20f, 2.05f, 0.12f),
                            new MeshKit.Section(1.45f, 0.86f, 1.18f, 1.86f, 0.10f),
                            new MeshKit.Section(1.95f, 0.80f, 1.12f, 1.30f, 0.08f),
                        },
                    };

                default: // sedan
                    return new CarSpec
                    {
                        name = "Sedan",
                        wheelRadius = 0.34f, wheelWidth = 0.24f, axleFront = 1.42f, axleRear = -1.42f,
                        trackHalf = 0.79f, mass = 1320f, motorTorque = 2200f,
                        gearSpeeds = new[] { 33f, 58f, 88f, 122f, 175f },
                        roofY = 1.36f, roofLength = 1.15f, roofWidth = 1.55f,
                        lightFront = new Vector3(0.62f, 0.66f, 2.06f),
                        body = new[]
                        {
                            new MeshKit.Section(-2.15f, 0.78f, 0.30f, 0.70f, 0.13f),
                            new MeshKit.Section(-1.85f, 0.86f, 0.26f, 0.80f, 0.16f),
                            new MeshKit.Section(-1.10f, 0.90f, 0.24f, 0.86f, 0.18f),
                            new MeshKit.Section(-0.20f, 0.92f, 0.23f, 0.90f, 0.20f),
                            new MeshKit.Section(0.85f, 0.90f, 0.24f, 0.88f, 0.18f),
                            new MeshKit.Section(1.65f, 0.86f, 0.26f, 0.78f, 0.16f),
                            new MeshKit.Section(2.10f, 0.76f, 0.30f, 0.66f, 0.13f),
                        },
                        cabin = new[]
                        {
                            new MeshKit.Section(-1.60f, 0.70f, 0.84f, 0.90f, 0.08f),
                            new MeshKit.Section(-1.20f, 0.78f, 0.86f, 1.18f, 0.10f),
                            new MeshKit.Section(-0.55f, 0.82f, 0.88f, 1.34f, 0.12f),
                            new MeshKit.Section(0.30f, 0.82f, 0.88f, 1.36f, 0.12f),
                            new MeshKit.Section(0.85f, 0.78f, 0.86f, 1.20f, 0.10f),
                            new MeshKit.Section(1.30f, 0.70f, 0.84f, 0.94f, 0.08f),
                        },
                    };
            }
        }

        public static VehicleController Create(Transform parent, Vector3 position, float yaw, int style,
                                              Color paint, bool parked = true, bool police = false)
        {
            var spec = Spec(style);
            var root = new GameObject(police ? "PoliceCruiser" : spec.name);
            root.transform.SetParent(parent, false);
            root.transform.position = position + Vector3.up * 0.15f;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = spec.mass;
            rb.drag = 0.045f;
            rb.angularDrag = 0.9f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.centerOfMass = new Vector3(0f, 0.34f, 0f);

            var vc = root.AddComponent<VehicleController>();
            vc.MaxMotorTorque = spec.motorTorque;
            vc.GearSpeeds = spec.gearSpeeds;
            vc.Paint = paint;
            vc.DisplayName = police ? "Police Cruiser" : spec.name;
            vc.Mode = parked ? VehicleMode.Parked : VehicleMode.AI;

            // ---------------- visual shell ----------------
            Mesh bodyMesh, cabinMesh;
            if (!bodyCache.TryGetValue(style, out bodyMesh) || bodyMesh == null)
            {
                bodyMesh = MeshKit.Loft(spec.body, 3);
                cabinMesh = MeshKit.Loft(spec.cabin, 3);
                bodyCache[style] = bodyMesh;
                cabinCache[style] = cabinMesh;
            }
            cabinMesh = cabinCache[style];

            var paintMat = police
                ? MaterialLibrary.CarPaint(new Color(0.88f, 0.89f, 0.92f))
                : MaterialLibrary.CarPaint(paint);

            var shell = new GameObject("Shell").transform;
            shell.SetParent(root.transform, false);

            AddPart(shell, "Body", bodyMesh, paintMat);
            AddPart(shell, "Cabin", cabinMesh, MaterialLibrary.CarGlass);

            // painted roof panel over the glass house
            Go.MeshObject("Roof", shell,
                MeshKit.Box(new Vector3(spec.roofWidth - 0.1f, 0.055f, spec.roofLength), 1f),
                paintMat, new Vector3(0f, spec.roofY, (spec.cabin[0].z + spec.cabin[spec.cabin.Length - 1].z) * 0.5f - 0.1f));

            // pillars (A and C) for structure
            var pillarMat = paintMat;
            for (int s = -1; s <= 1; s += 2)
            {
                var aPillar = Go.MeshObject("APillar", shell, MeshKit.Box(new Vector3(0.055f, 0.5f, 0.055f), 1f),
                    pillarMat, new Vector3(s * 0.72f, spec.roofY - 0.24f, spec.cabin[spec.cabin.Length - 2].z + 0.18f));
                aPillar.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
                var cPillar = Go.MeshObject("CPillar", shell, MeshKit.Box(new Vector3(0.06f, 0.46f, 0.06f), 1f),
                    pillarMat, new Vector3(s * 0.72f, spec.roofY - 0.26f, spec.cabin[1].z - 0.12f));
                cPillar.transform.localRotation = Quaternion.Euler(-24f, 0f, 0f);
            }

            // bumpers / grille / trim
            float frontZ = spec.lightFront.z;
            Go.MeshObject("BumperF", shell, MeshKit.Box(new Vector3(1.66f, 0.22f, 0.16f), 1f),
                MaterialLibrary.Plastic(new Color(0.14f, 0.14f, 0.15f), "bumper"), new Vector3(0f, 0.40f, frontZ + 0.03f));
            Go.MeshObject("BumperR", shell, MeshKit.Box(new Vector3(1.66f, 0.22f, 0.16f), 1f),
                MaterialLibrary.Plastic(new Color(0.14f, 0.14f, 0.15f), "bumper"), new Vector3(0f, 0.40f, -frontZ - 0.02f));
            Go.MeshObject("Grille", shell, MeshKit.Box(new Vector3(1.10f, 0.16f, 0.05f), 1f),
                MaterialLibrary.Plastic(new Color(0.06f, 0.06f, 0.07f), "grille"), new Vector3(0f, 0.60f, frontZ + 0.02f));
            Go.MeshObject("Plate", shell, MeshKit.Box(new Vector3(0.42f, 0.11f, 0.02f), 1f),
                MaterialLibrary.Plastic(new Color(0.90f, 0.90f, 0.86f), "plate"), new Vector3(0f, 0.38f, frontZ + 0.09f));

            // mirrors
            for (int s = -1; s <= 1; s += 2)
            {
                Go.MeshObject("Mirror", shell, MeshKit.Box(new Vector3(0.16f, 0.09f, 0.07f), 1f),
                    paintMat, new Vector3(s * 0.88f, spec.roofY - 0.30f, spec.cabin[spec.cabin.Length - 2].z + 0.05f));
            }

            // lights
            float halfTrack = spec.trackHalf - 0.02f;
            var headMatL = MaterialLibrary.Signal(new Color(1f, 0.97f, 0.88f), "carhead");
            var tailMat = MaterialLibrary.Signal(new Color(1f, 0.10f, 0.06f), "cartail");
            for (int s = -1; s <= 1; s += 2)
            {
                Go.MeshObject("HeadLight", shell, MeshKit.Box(new Vector3(0.36f, 0.15f, 0.06f), 1f),
                    headMatL, new Vector3(s * halfTrack * 0.82f, spec.lightFront.y, frontZ));
                Go.MeshObject("TailLight", shell, MeshKit.Box(new Vector3(0.34f, 0.13f, 0.06f), 1f),
                    tailMat, new Vector3(s * halfTrack * 0.82f, spec.lightFront.y, -frontZ));
            }

            // exhaust
            Go.MeshObject("Exhaust", shell, MeshKit.Cylinder(0.045f, 0.16f, 8, true), MaterialLibrary.Chrome,
                new Vector3(0.5f, 0.30f, -frontZ - 0.04f), Quaternion.Euler(90f, 0f, 0f));

            // ---------------- wheels ----------------
            var tireMesh = MeshKit.Wheel(spec.wheelRadius, spec.wheelWidth, 20);
            var rimMesh = MeshKit.Wheel(spec.wheelRadius * 0.72f, spec.wheelWidth * 0.55f, 20);

            vc.WheelFL = MakeWheel(root.transform, "WheelFL", new Vector3(-spec.trackHalf, spec.wheelRadius + 0.06f, spec.axleFront), spec,
                tireMesh, rimMesh, out Transform visFL);
            vc.WheelFR = MakeWheel(root.transform, "WheelFR", new Vector3(spec.trackHalf, spec.wheelRadius + 0.06f, spec.axleFront), spec,
                tireMesh, rimMesh, out Transform visFR);
            vc.WheelRL = MakeWheel(root.transform, "WheelRL", new Vector3(-spec.trackHalf, spec.wheelRadius + 0.06f, spec.axleRear), spec,
                tireMesh, rimMesh, out Transform visRL);
            vc.WheelRR = MakeWheel(root.transform, "WheelRR", new Vector3(spec.trackHalf, spec.wheelRadius + 0.06f, spec.axleRear), spec,
                tireMesh, rimMesh, out Transform visRR);
            vc.VisFL = visFL; vc.VisFR = visFR; vc.VisRL = visRL; vc.VisRR = visRR;

            // ---------------- collider ----------------
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, spec.wheelRadius + 0.30f, 0f);
            box.size = new Vector3(1.80f, 0.72f, Mathf.Abs(frontZ) * 2f - 0.06f);

            // ---------------- headlights ----------------
            for (int s = -1; s <= 1; s += 2)
            {
                var lightGo = new GameObject("Headlamp");
                lightGo.transform.SetParent(shell, false);
                lightGo.transform.localPosition = new Vector3(s * halfTrack * 0.82f, spec.lightFront.y, frontZ + 0.05f);
                lightGo.transform.localRotation = Quaternion.Euler(3f, s * 4f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = 52f;
                l.spotAngle = 62f;
                l.intensity = 3.2f;
                l.color = new Color(1f, 0.96f, 0.86f);
                l.shadows = LightShadows.None;
                l.enabled = false;
                if (s < 0) vc.HeadLightL = l; else vc.HeadLightR = l;
            }

            // ---------------- seat ----------------
            var seat = new GameObject("DriverSeat").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = new Vector3(-0.34f, 0.55f, -0.15f);
            vc.DriverSeat = seat;

            var health = root.GetComponent<Health>();
            if (health == null) health = root.AddComponent<Health>();
            health.SetMax(police ? 240f : 180f);
            health.BloodFx = false;
            vc.Health = health;

            return vc;
        }

        static GameObject AddPart(Transform parent, string name, Mesh mesh, Material mat)
        {
            return Go.MeshObject(name, parent, mesh, mat);
        }

        static WheelCollider MakeWheel(Transform root, string name, Vector3 localPos, CarSpec spec,
                                       Mesh tireMesh, Mesh rimMesh, out Transform visual)
        {
            var wheelGo = new GameObject(name);
            wheelGo.transform.SetParent(root, false);
            wheelGo.transform.localPosition = localPos;

            var wc = wheelGo.AddComponent<WheelCollider>();
            wc.radius = spec.wheelRadius;
            wc.mass = 26f;
            wc.suspensionDistance = 0.20f;
            wc.forceAppPointDistance = 0.18f;
            wc.wheelDampingRate = 0.28f;

            var spring = wc.suspensionSpring;
            spring.spring = 42000f;
            spring.damper = 4400f;
            spring.targetPosition = 0.45f;
            wc.suspensionSpring = spring;

            var fwd = wc.forwardFriction;
            fwd.extremumSlip = 0.36f;
            fwd.extremumValue = 1f;
            fwd.asymptoteSlip = 0.85f;
            fwd.asymptoteValue = 0.72f;
            fwd.stiffness = 1.5f;
            wc.forwardFriction = fwd;

            var side = wc.sidewaysFriction;
            side.extremumSlip = 0.24f;
            side.extremumValue = 1f;
            side.asymptoteSlip = 1.0f;
            side.asymptoteValue = 0.78f;
            side.stiffness = 1.5f;
            wc.sidewaysFriction = side;

            var vis = new GameObject(name + "_vis").transform;
            vis.SetParent(root, false);
            vis.localPosition = localPos;
            Go.MeshObject("Tire", vis, tireMesh, MaterialLibrary.Tire);
            Go.MeshObject("Rim", vis, rimMesh, MaterialLibrary.Rim);
            visual = vis;
            return wc;
        }
    }
}
