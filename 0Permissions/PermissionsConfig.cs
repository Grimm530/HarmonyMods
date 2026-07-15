using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace PermissionsHarmony
{
    /// <summary>
    /// HarmonyConfig/Permissions.json — controls whether ownerid/moderatorid bypass all permission checks.
    /// Default is false: grants come only from users.json / groups.json (and perm commands).
    /// </summary>
    public class PermissionsConfig
    {
        [JsonProperty("Server Admins Bypass All Permissions")]
        public bool ServerAdminsBypassAllPermissions { get; set; }

        public static PermissionsConfig LoadOrCreate(string serverRoot)
        {
            string dir = Path.Combine(serverRoot, "HarmonyConfig");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "Permissions.json");

            var cfg = new PermissionsConfig
            {
                // Oxide-like: auth level (ownerid) does NOT imply every mod permission.
                ServerAdminsBypassAllPermissions = false
            };

            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<PermissionsConfig>(File.ReadAllText(path));
                    if (loaded != null) cfg = loaded;
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Permissions] Config load/save failed, using defaults: " + ex.Message);
            }

            return cfg;
        }
    }
}
