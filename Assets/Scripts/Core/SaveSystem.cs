using System;
using System.IO;
using UnityEngine;

namespace OpenCity
{
    [Serializable]
    public class SaveData
    {
        public string version = GameConst.Version;
        public string savedAt = "";
        public int money = 500;
        public float wanted = 0f;
        public float health = 100f;
        public float armor = 0f;
        public int ammoPistol = 60;
        public int ammoRifle = 0;
        public int weapon = 0;
        public float posX, posY, posZ, yaw;
        public float hour = 15f;
        public bool missionActive = false;
        public float missionTimeLeft = 0f;
        public float missionTargetX, missionTargetZ;
        public int missionsDone = 0;
    }

    /// <summary>JSON save/load into Application.persistentDataPath.</summary>
    public static class SaveSystem
    {
        public static string PathFile =>
            Path.Combine(Application.persistentDataPath, GameConst.SaveFileName);

        public static bool Exists => File.Exists(PathFile);

        public static bool Save(SaveData data)
        {
            try
            {
                data.version = GameConst.Version;
                data.savedAt = DateTime.UtcNow.ToString("u");
                File.WriteAllText(PathFile, JsonUtility.ToJson(data, true));
                Debug.Log("[OpenCity] Saved to " + PathFile);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpenCity] Save failed: " + e.Message);
                return false;
            }
        }

        public static SaveData Load()
        {
            try
            {
                if (!Exists) return null;
                var json = File.ReadAllText(PathFile);
                var data = JsonUtility.FromJson<SaveData>(json);
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpenCity] Load failed: " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            try { if (Exists) File.Delete(PathFile); } catch { }
        }
    }
}
