using System;
using System.IO;
using UnityEngine;

namespace RaidableBasesBuyableUI
{
    internal static class Paths
    {
        public static string ServerRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string ConfigFile =>
            Path.Combine(ServerRoot, "HarmonyConfig", "RaidableBasesBuyableUI.json");

        public static string RaidableBasesConfigFile =>
            Path.Combine(ServerRoot, "HarmonyConfig", "RaidableBases.json");

        public static string DataDir =>
            Path.Combine(ServerRoot, "HarmonyData", "RaidableBasesBuyableUI");

        public static string ImagesDir =>
            Path.Combine(DataDir, "Images");

        public static string RaidsDir =>
            Path.Combine(DataDir, "raids");

        public static string PlayerPreferencesFile =>
            Path.Combine(DataDir, "PlayerPreferences.json");

        public static string ProfilesDir =>
            Path.Combine(ServerRoot, "HarmonyData", "RaidableBases", "Profiles");

        public static string CopyPasteDir =>
            Path.Combine(ServerRoot, "HarmonyData", "copypaste");

        public static void EnsureDataDirs()
        {
            try
            {
                if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                if (!Directory.Exists(ImagesDir)) Directory.CreateDirectory(ImagesDir);
                if (!Directory.Exists(RaidsDir)) Directory.CreateDirectory(RaidsDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidableBasesBuyableUI] EnsureDataDirs: " + ex.Message);
            }
        }
    }
}
