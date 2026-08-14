using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ScaleHarmony
{
    public sealed class ScalePlugin
    {
        public const string PermissionUse = "scale.use";
        private const string AdminGroup = "admin";

        private readonly string _configPath;
        private Configuration _config;

        public class Configuration
        {
            public float MaxUniformScale = 5f;
            public float MinUniformScale = 0.1f;
            public float MaxVectorComponent = 5f;
            public float MinVectorComponent = 0.1f;
            public float RaycastDistance = 100f;
            public bool LogChanges = true;
        }

        public ScalePlugin(string serverRoot)
        {
            _configPath = Path.Combine(serverRoot, "HarmonyConfig", "Scale.json");
        }

        public void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(_configPath))
                    _config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configPath));

                if (_config == null)
                {
                    Debug.LogWarning("[Scale] Creating new configuration file.");
                    _config = new Configuration();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Scale] FAIL: load config — using defaults. " + ex.Message);
                _config = new Configuration();
            }
            SaveConfig();
        }

        public void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(_config ?? new Configuration(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Scale] FAIL: save config: " + ex.Message);
            }
        }

        public void RegisterPermissions()
        {
            PermissionsBridge.RegisterPermission(PermissionUse);
            if (!PermissionsBridge.IsAvailable) return;
            if (!PermissionsBridge.GroupExists(AdminGroup))
                PermissionsBridge.CreateGroup(AdminGroup, "Administrators", 0);
            PermissionsBridge.GrantGroupPermission(AdminGroup, PermissionUse);
        }

        private bool HasAccess(BasePlayer player)
        {
            if (player == null) return false;
            if (player.IsAdmin) return true;
            return PermissionsBridge.UserHasPermission(player.UserIDString, PermissionUse);
        }

        public void CmdScale(BasePlayer player, string[] args)
        {
            if (!HasAccess(player))
            {
                player.ChatMessage("You don't have permission to use this.");
                return;
            }

            var target = GetLookEntity(player, _config.RaycastDistance);
            if (target == null)
            {
                player.ChatMessage("No entity found. Look at an entity and try again.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                player.ChatMessage($"Target: {PrettyEntity(target)} | Current scale: {target.transform.localScale}");
                return;
            }

            if (TryApplyScale(target, args, out string msg))
            {
                player.ChatMessage($"{msg} → {PrettyEntity(target)}");
                if (_config.LogChanges)
                    Debug.Log($"[Scale] {player.displayName}({player.userID}) set scale of {PrettyEntity(target, true)} to {target.transform.localScale}");
            }
            else
            {
                player.ChatMessage(msg);
            }
        }

        public void CmdScaleId(BasePlayer player, string[] args)
        {
            if (!HasAccess(player))
            {
                player.ChatMessage("You don't have permission to use this.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                player.ChatMessage("Usage: /scaleid <entityId> [size|x y z|default]");
                return;
            }

            if (!ulong.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong rawId))
            {
                player.ChatMessage("Invalid entity ID.");
                return;
            }

            var bn = BaseNetworkable.serverEntities.Find(new NetworkableId(rawId));
            if (bn == null)
            {
                player.ChatMessage("Entity not found.");
                return;
            }

            var ent = bn as BaseEntity;
            if (ent == null)
            {
                player.ChatMessage("Target is not a BaseEntity.");
                return;
            }

            if (args.Length == 1)
            {
                player.ChatMessage($"Target: {PrettyEntity(ent)} | Current scale: {ent.transform.localScale}");
                return;
            }

            var scaleArgs = new string[args.Length - 1];
            Array.Copy(args, 1, scaleArgs, 0, scaleArgs.Length);
            if (TryApplyScale(ent, scaleArgs, out string msg))
            {
                player.ChatMessage($"{msg} → {PrettyEntity(ent)}");
                if (_config.LogChanges)
                    Debug.Log($"[Scale] {player.displayName}({player.userID}) set scale of {PrettyEntity(ent, true)} to {ent.transform.localScale}");
            }
            else
            {
                player.ChatMessage(msg);
            }
        }

        private bool TryApplyScale(BaseEntity ent, string[] args, out string message)
        {
            message = string.Empty;

            if (args.Length == 1 && args[0].Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                ent.networkEntityScale = false;
                ent.transform.localScale = Vector3.one;
                ent.SendNetworkUpdate();
                message = "Reset scale to default (1,1,1)";
                return true;
            }

            Vector3 targetScale;
            if (args.Length == 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float uniform))
            {
                if (!ValidateUniform(uniform, out message))
                    return false;
                targetScale = new Vector3(uniform, uniform, uniform);
            }
            else
            {
                string[] parts = args.Length == 1
                    ? args[0].Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    : args;
                if (parts.Length != 3
                    || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx)
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy)
                    || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sz))
                {
                    message = "Invalid scale. Use a number like '2' or 3 components like '2 1.5 0.8' or '2,1.5,0.8'.";
                    return false;
                }
                if (!ValidateVector(sx, sy, sz, out message))
                    return false;
                targetScale = new Vector3(sx, sy, sz);
            }

            ent.networkEntityScale = true;
            ent.transform.localScale = targetScale;
            ent.SendNetworkUpdate();
            message = $"Set scale to {targetScale}";
            return true;
        }

        private bool ValidateUniform(float value, out string error)
        {
            error = string.Empty;
            if (value < _config.MinUniformScale || value > _config.MaxUniformScale)
            {
                error = $"Uniform scale must be between {_config.MinUniformScale} and {_config.MaxUniformScale}.";
                return false;
            }
            if (Mathf.Approximately(value, 0f))
            {
                error = "Scale cannot be zero.";
                return false;
            }
            return true;
        }

        private bool ValidateVector(float x, float y, float z, out string error)
        {
            error = string.Empty;
            if (Mathf.Approximately(x, 0f) || Mathf.Approximately(y, 0f) || Mathf.Approximately(z, 0f))
            {
                error = "Scale components cannot be zero.";
                return false;
            }
            if (x < _config.MinVectorComponent || y < _config.MinVectorComponent || z < _config.MinVectorComponent
                || x > _config.MaxVectorComponent || y > _config.MaxVectorComponent || z > _config.MaxVectorComponent)
            {
                error = $"Each component must be between {_config.MinVectorComponent} and {_config.MaxVectorComponent}.";
                return false;
            }
            return true;
        }

        private static string PrettyEntity(BaseEntity ent, bool includeId = false)
        {
            return includeId ? $"{ent.ShortPrefabName}({ent.net?.ID.Value})" : $"{ent.ShortPrefabName}";
        }

        private static BaseEntity GetLookEntity(BasePlayer player, float maxDistance)
        {
            if (player == null || player.eyes == null)
                return null;
            var ray = player.eyes.HeadRay();
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, int.MaxValue, QueryTriggerInteraction.Ignore))
                return null;
            return hit.GetEntity() ?? hit.collider?.GetComponentInParent<BaseEntity>();
        }
    }
}
