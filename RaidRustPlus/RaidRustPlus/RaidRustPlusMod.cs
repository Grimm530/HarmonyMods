using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CompanionServer;
using UnityEngine;

namespace RaidRustPlus;

public class RaidRustPlusMod : IHarmonyModHooks
{
    public static RaidRustPlusMod Instance { get; private set; }

    private RaidRustPlusConfig _config;
    private string _configPath;
    private readonly Dictionary<ulong, DateTime> _nextNotifyAt = new Dictionary<ulong, DateTime>();
    private HashSet<string> _extraShortnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        Instance = this;
        _config = RaidRustPlusConfig.Load(out _configPath);
        _extraShortnames = new HashSet<string>(_config.ExtraShortnames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        Debug.Log("[RaidRustPlus] Loaded. Config: " + _configPath);
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        _nextNotifyAt.Clear();
        _extraShortnames.Clear();
        Instance = null;
        Debug.Log("[RaidRustPlus] Unloaded.");
    }

    public void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
    {
        if (_config == null || !_config.Enabled || entity == null || info == null)
        {
            return;
        }

        BasePlayer attacker = info.InitiatorPlayer;
        if (attacker == null)
        {
            return;
        }

        if (!ShouldNotifyForEntity(entity, out string destroyedName))
        {
            return;
        }

        if (string.IsNullOrEmpty(ConVar.App.serverid) || ConVar.App.port <= 0 || !ConVar.App.notifications)
        {
            return;
        }

        BuildingPrivlidge privilege = entity as BuildingPrivlidge;
        if (privilege == null)
        {
            privilege = entity.GetBuildingPrivilege();
        }

        if (privilege == null || !privilege.AnyAuthed())
        {
            return;
        }

        string quad = ToGrid(entity.transform.position);
        string attackerName = attacker.displayName;
        string attackerId = attacker.UserIDString;
        string connect = ConVar.Server.ip + ":" + ConVar.Server.port;

        foreach (ulong ownerId in privilege.authorizedPlayers.ToList())
        {
            if (!CanSend(ownerId))
            {
                continue;
            }

            string title = ApplyTemplate(_config.TitleTemplate, attackerName, attackerId, destroyedName, quad, connect);
            string body = ApplyTemplate(_config.BodyTemplate, attackerName, attackerId, destroyedName, quad, connect);
            NotificationList.SendNotificationTo(
                ownerId,
                NotificationChannel.SmartAlarm,
                title,
                body,
                Util.TryGetServerPairingData()
            );
            string consoleMessage = string.IsNullOrWhiteSpace(body) ? title : (title + " | " + body);
            Debug.Log("[Raid Alert] " + consoleMessage + " (to " + ownerId.ToString(CultureInfo.InvariantCulture) + ")");
            _nextNotifyAt[ownerId] = DateTime.UtcNow.AddSeconds(Mathf.Max(0f, _config.CooldownSeconds));
        }
    }

    private bool ShouldNotifyForEntity(BaseCombatEntity entity, out string destroyedName)
    {
        destroyedName = string.Empty;

        if (entity is BuildingBlock block)
        {
            int grade = (int)block.grade;
            if (grade < _config.MinimumBuildingGrade)
            {
                return false;
            }
            destroyedName = Humanize(block.ShortPrefabName) + " " + block.grade;
            return true;
        }

        if (!_config.IncludeExtraDeployables)
        {
            return false;
        }

        bool isExtra =
            entity is DecayEntity ||
            entity is IOEntity ||
            entity is AnimatedBuildingBlock ||
            entity is SamSite ||
            entity is AutoTurret ||
            (!string.IsNullOrEmpty(entity.ShortPrefabName) && _extraShortnames.Contains(entity.ShortPrefabName));

        if (!isExtra)
        {
            return false;
        }

        destroyedName = Humanize(entity.ShortPrefabName);
        return true;
    }

    private bool CanSend(ulong playerId)
    {
        if (!_nextNotifyAt.TryGetValue(playerId, out DateTime next))
        {
            return true;
        }
        return DateTime.UtcNow >= next;
    }

    private string ApplyTemplate(string template, string attackerName, string attackerId, string destroyedName, string quad, string connect)
    {
        string text = template ?? string.Empty;
        return text
            .Replace("{name}", attackerName ?? string.Empty)
            .Replace("{steamid}", attackerId ?? string.Empty)
            .Replace("{destroy}", destroyedName ?? string.Empty)
            .Replace("{quad}", quad ?? "Unknown")
            .Replace("{ip}", connect ?? string.Empty)
            .Replace("{servername}", _config.ServerName ?? string.Empty);
    }

    private static string Humanize(string shortName)
    {
        if (string.IsNullOrEmpty(shortName))
        {
            return "entity";
        }

        string text = shortName.Replace(".deployed", string.Empty).Replace('.', ' ').Trim();
        if (text.Length == 0)
        {
            return "entity";
        }
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static string ToGrid(Vector3 pos)
    {
        float worldSize = ConVar.Server.worldsize;
        if (worldSize <= 0f)
        {
            return "Unknown";
        }

        const float cell = 150f;
        int cells = Mathf.Max(1, Mathf.CeilToInt(worldSize / cell));
        float half = worldSize * 0.5f;

        int x = Mathf.Clamp(Mathf.FloorToInt((pos.x + half) / cell), 0, cells - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt((half - pos.z) / cell), 0, cells - 1);

        return ToLetters(x) + z.ToString(CultureInfo.InvariantCulture);
    }

    private static string ToLetters(int index)
    {
        index++;
        string result = string.Empty;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            result = (char)('A' + rem) + result;
            index = (index - 1) / 26;
        }
        return result;
    }
}
