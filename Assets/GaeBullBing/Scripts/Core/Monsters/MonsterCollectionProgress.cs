using UnityEngine;

namespace GaeBullBing.Core.Monsters
{
    public static class MonsterCollectionProgress
    {
        private const string Prefix = "GaeBullBing.CapturedMonster.";
        public static bool IsCaptured(string id) => !string.IsNullOrWhiteSpace(id) && PlayerPrefs.GetInt(Prefix + id, 0) != 0;
        public static void Record(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            PlayerPrefs.SetInt(Prefix + id, 1);
            PlayerPrefs.Save();
        }
    }
}
