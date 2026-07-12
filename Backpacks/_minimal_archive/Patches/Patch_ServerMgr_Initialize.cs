using System.IO;
using ConVar;
using HarmonyLib;

namespace Backpacks.Patches
{
    /// <summary>
    /// Ensures server identity folder (e.g. server/my_server_identity) exists before the game
    /// opens FileStorage (sv.files.*.db). Prevents SqliteException error 14 (CANTOPEN) when
    /// loading/saving NPCVendingMachine and other entities that use FileStorage.
    /// </summary>
    [HarmonyPatch(typeof(ServerMgr), nameof(ServerMgr.Initialize))]
    public static class Patch_ServerMgr_Initialize
    {
        static void Prefix()
        {
            try
            {
                string folder = Server.filesStorageFolder;
                if (string.IsNullOrEmpty(folder)) return;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    UnityEngine.Debug.Log("[Backpacks] Created server identity folder for FileStorage: " + folder);
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Backpacks] Could not ensure FileStorage folder: " + ex.Message);
            }
        }
    }
}
