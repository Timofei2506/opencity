using UnityEngine;

namespace OpenCity
{
    /// <summary>Thin wrapper over the legacy Input Manager (see ProjectSettings/InputManager.asset).</summary>
    public static class InputHub
    {
        public static bool Blocked { get; set; }

        public static Vector2 Move
        {
            get
            {
                if (Blocked) return Vector2.zero;
                float x = Input.GetAxisRaw("Horizontal");
                float y = Input.GetAxisRaw("Vertical");
                return new Vector2(x, y);
            }
        }

        public static Vector2 Look
        {
            get
            {
                if (Blocked) return Vector2.zero;
                return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            }
        }

        public static float Scroll => Blocked ? 0f : Input.GetAxisRaw("Mouse ScrollWheel");

        public static bool Sprint => !Blocked && Input.GetKey(KeyCode.LeftShift);
        public static bool Walk => !Blocked && Input.GetKey(KeyCode.LeftAlt);
        public static bool JumpDown => !Blocked && Input.GetKeyDown(KeyCode.Space);
        public static bool JumpHeld => !Blocked && Input.GetKey(KeyCode.Space);

        public static bool FireDown => !Blocked && Input.GetMouseButtonDown(0);
        public static bool FireHeld => !Blocked && Input.GetMouseButton(0);
        public static bool AimHeld => !Blocked && Input.GetMouseButton(1);

        public static bool ReloadDown => !Blocked && Input.GetKeyDown(KeyCode.R);
        public static bool InteractDown => !Blocked && Input.GetKeyDown(KeyCode.F);
        public static bool EnterDown => !Blocked && Input.GetKeyDown(KeyCode.F);
        public static bool HornDown => !Blocked && Input.GetKeyDown(KeyCode.H);
        public static bool Handbrake => !Blocked && Input.GetKey(KeyCode.Space);
        public static bool HeadlightsDown => !Blocked && Input.GetKeyDown(KeyCode.L);

        public static bool Weapon1Down => !Blocked && Input.GetKeyDown(KeyCode.Alpha1);
        public static bool Weapon2Down => !Blocked && Input.GetKeyDown(KeyCode.Alpha2);
        public static bool Weapon3Down => !Blocked && Input.GetKeyDown(KeyCode.Alpha3);

        public static bool PauseDown => !Blocked && Input.GetKeyDown(KeyCode.Escape);
        public static bool SaveDown => !Blocked && Input.GetKeyDown(KeyCode.F5);
        public static bool LoadDown => !Blocked && Input.GetKeyDown(KeyCode.F9);
        public static bool HelpDown => !Blocked && Input.GetKeyDown(KeyCode.F1);
        public static bool CameraDown => !Blocked && Input.GetKeyDown(KeyCode.C);
    }
}
