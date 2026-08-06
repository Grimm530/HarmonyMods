using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace PveModeHarmony
{
    public enum PveEntityType
    {
        Default,
        Npc,
        Animal,
        Bradley,
        Turret,
        Helicopter
    }

    /// <summary>
    /// Central engine for the PveMode Harmony port. Holds event/scientist/cooldown state and
    /// implements the damage/target/loot rules ported from the Oxide PveMode 1.2.9 plugin.
    /// Harmony patch files call into this class; PveModeMod owns lifecycle (load/unload).
    /// </summary>
    public static class PveModeManager
    {
        public const ulong SteamIdBase = 76561197960265728UL;

        public static PveModeConfig Config { get; private set; } = PveModeConfig.Default();

        public static readonly HashSet<ControllerEvent> Events = new HashSet<ControllerEvent>();
        public static readonly Dictionary<ulong, ControllerScientist> Scientists = new Dictionary<ulong, ControllerScientist>();
        public static readonly Dictionary<ulong, ulong> CanLootScientist = new Dictionary<ulong, ulong>();
        public static readonly Dictionary<ulong, ulong> CanLootCrateScientist = new Dictionary<ulong, ulong>();

        private static HashSet<PlayerData> _playersData = new HashSet<PlayerData>();
        private static string _dataPath;

        private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0);
        public static double CurrentTime => DateTime.Now.Subtract(Epoch).TotalSeconds;

        // ---- Lifecycle ----------------------------------------------------

        public static void Init(PveModeConfig config, string dataPath)
        {
            Config = config ?? PveModeConfig.Default();
            _dataPath = dataPath;
            LoadData();
        }

        public static void Shutdown()
        {
            foreach (KeyValuePair<ulong, ControllerScientist> kv in Scientists)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            Scientists.Clear();

            foreach (ControllerEvent ev in Events)
                if (ev != null) UnityEngine.Object.Destroy(ev.gameObject);
            Events.Clear();

            CanLootScientist.Clear();
            CanLootCrateScientist.Clear();
            SaveData();
        }

        // ---- Cooldown data --------------------------------------------------

        private static void LoadData()
        {
            try
            {
                if (!string.IsNullOrEmpty(_dataPath) && File.Exists(_dataPath))
                {
                    string json = File.ReadAllText(_dataPath);
                    HashSet<PlayerData> loaded = JsonConvert.DeserializeObject<HashSet<PlayerData>>(json);
                    _playersData = loaded ?? new HashSet<PlayerData>();
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Failed to load player data: " + ex.Message);
            }
            _playersData = new HashSet<PlayerData>();
        }

        public static void SaveData()
        {
            try
            {
                if (string.IsNullOrEmpty(_dataPath)) return;
                string dir = Path.GetDirectoryName(_dataPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(_playersData, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Failed to save player data: " + ex.Message);
            }
        }

        public static PlayerData FindPlayerData(ulong steamId) => _playersData.FirstOrDefault(x => x.SteamId == steamId);

        public static bool CanTimeOwner(string nameEvent, ulong steamId, double cooldown)
        {
            PlayerData data = FindPlayerData(steamId);
            if (data == null) return true;
            foreach (KeyValuePair<string, double> kv in data.LastTime)
                if (!string.IsNullOrEmpty(nameEvent) && nameEvent.Contains(kv.Key))
                    return kv.Value + cooldown < CurrentTime;
            return true;
        }

        public static double GetOwnerCooldownRemaining(ulong steamId, string shortname, double cooldown)
        {
            PlayerData data = FindPlayerData(steamId);
            if (data == null || !data.LastTime.TryGetValue(shortname, out double last)) return 0d;
            return last + cooldown - CurrentTime;
        }

        public static void RecordCooldown(string shortname, ulong steamId, double when)
        {
            PlayerData data = FindPlayerData(steamId);
            if (data == null)
            {
                _playersData.Add(new PlayerData { SteamId = steamId, LastTime = new Dictionary<string, double> { [shortname] = when } });
            }
            else if (data.LastTime.ContainsKey(shortname)) data.LastTime[shortname] = when;
            else data.LastTime.Add(shortname, when);
        }

        public static void ConsoleClearTime(ulong steamId, string nameEvent)
        {
            PlayerData data = FindPlayerData(steamId);
            if (data == null)
            {
                Debug.Log("[PveMode] Player " + steamId + " not found in the plugin database");
                return;
            }
            if (string.IsNullOrEmpty(nameEvent))
            {
                data.LastTime.Clear();
                Debug.Log("[PveMode] Cleared all cooldown data for player " + steamId);
                return;
            }
            if (data.LastTime.ContainsKey(nameEvent))
            {
                data.LastTime.Remove(nameEvent);
                Debug.Log("[PveMode] Cleared cooldown '" + nameEvent + "' for player " + steamId);
            }
            else Debug.Log("[PveMode] Event " + nameEvent + " not found for player " + steamId);
        }

        public static void ConsoleClearOwner(ulong steamId, string nameEvent)
        {
            BasePlayer player = BasePlayer.FindByID(steamId);
            foreach (ControllerEvent controller in Events)
            {
                if (controller.Owner != steamId) continue;
                if (string.IsNullOrEmpty(nameEvent) || controller.ShortName == nameEvent)
                {
                    controller.ClearOwner(player);
                    Debug.Log("[PveMode] Cleared owner from event " + controller.ShortName);
                }
            }
        }

        // ---- Formatting -----------------------------------------------------

        private const string StrSec = "sec.";
        private const string StrMin = "min.";
        private const string StrH = "h.";

        public static string GetTimeFormat(double time)
        {
            int integer = (int)time;
            if (time <= 60) return integer + " " + StrSec;
            if (time <= 3600)
            {
                int sec = integer % 60;
                int min = (integer - sec) / 60;
                return sec == 0 ? min + " " + StrMin : min + " " + StrMin + " " + sec + " " + StrSec;
            }
            int hour = (int)(time / 3600);
            time -= hour * 3600;
            integer = (int)time;
            int s2 = integer % 60;
            int m2 = (integer - s2) / 60;
            if (m2 == 0 && s2 == 0) return hour + " " + StrH;
            if (s2 == 0) return hour + " " + StrH + " " + m2 + " " + StrMin;
            return hour + " " + StrH + " " + m2 + " " + StrMin + " " + s2 + " " + StrSec;
        }

        // ---- Player / steam id helpers --------------------------------------

        public static bool IsRealPlayer(BasePlayer player) => player != null && ((ulong)player.userID) >= SteamIdBase;

        public static void SendChat(BasePlayer player, string message)
        {
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message)) return;
            try { player.ChatMessage(message); } catch { }
        }

        // ---- Team (Rust teams + soft Friends/Clans) -------------------------

        public static bool IsTeam(BasePlayer player, ulong targetId)
        {
            if (player == null || targetId == 0) return false;
            return IsTeam(player.userID, targetId);
        }

        public static bool IsTeam(ulong playerId, ulong targetId)
        {
            if (playerId == 0 || targetId == 0) return false;
            if (playerId == targetId) return true;
            try
            {
                RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance?.FindPlayersTeam(playerId);
                if (team != null && team.members.Contains(targetId)) return true;
            }
            catch { }
            if (PveModeSocial.Friends.AreFriends(playerId, targetId)) return true;
            if (PveModeSocial.Clans.IsClanMember(playerId, targetId)) return true;
            return false;
        }

        // ---- Event management (API-facing) ----------------------------------

        public static void EventAdd(string shortname, Dictionary<string, object> config, Vector3 position, float radius,
            HashSet<ulong> crates, HashSet<ulong> npc, HashSet<ulong> tanks, HashSet<ulong> helicopters,
            HashSet<ulong> turrets, HashSet<ulong> owners, BasePlayer owner)
        {
            if (string.IsNullOrEmpty(shortname)) return;
            ControllerEvent existing = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (existing != null) EventRemove(shortname, false);

            ControllerEvent controllerEvent = new GameObject("PveModeEvent_" + shortname).AddComponent<ControllerEvent>();
            controllerEvent.transform.position = position;
            controllerEvent.ShortName = shortname;
            controllerEvent.Config = EventConfig.FromDictionary(config);
            controllerEvent.Radius = radius;
            controllerEvent.Crates = crates ?? new HashSet<ulong>();
            controllerEvent.Npc = npc ?? new HashSet<ulong>();
            controllerEvent.Tanks = tanks ?? new HashSet<ulong>();
            controllerEvent.Helicopters = helicopters ?? new HashSet<ulong>();
            controllerEvent.Turrets = turrets ?? new HashSet<ulong>();
            controllerEvent.Owners = owners ?? new HashSet<ulong>();
            if (owner != null) controllerEvent.SetOwner(owner);
            controllerEvent.InitSphere();
            Events.Add(controllerEvent);
            Debug.Log("[PveMode] Zone " + shortname + " created at " + position);
        }

        public static void EventRemove(string shortname, bool addCooldownOwners = true)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;

            if (addCooldownOwners)
            {
                double now = CurrentTime;
                foreach (ulong id in controllerEvent.Owners)
                {
                    RecordCooldown(shortname, id, now);
                    BasePlayer player = BasePlayer.FindByID(id);
                    if (player != null) SendChat(player, PveModeLang.Get("OwnerEndEvent", shortname, GetTimeFormat(controllerEvent.Config.CooldownOwner)));
                }
                SaveData();
            }

            Events.Remove(controllerEvent);
            UnityEngine.Object.Destroy(controllerEvent.gameObject);
            Debug.Log("[PveMode] Zone " + shortname + " destroyed");
        }

        public static void EventAddCooldown(string shortname, HashSet<ulong> owners, double cooldown)
        {
            if (owners == null) return;
            double now = CurrentTime;
            foreach (ulong id in owners)
            {
                RecordCooldown(shortname, id, now);
                BasePlayer player = BasePlayer.FindByID(id);
                if (player != null) SendChat(player, PveModeLang.Get("OwnerEndEvent", shortname, GetTimeFormat(cooldown)));
            }
            SaveData();
        }

        public static void EventAddCrates(string shortname, HashSet<ulong> crates) => AddToEventSet(shortname, crates, e => e.Crates);
        public static void EventAddScientists(string shortname, HashSet<ulong> npc) => AddToEventSet(shortname, npc, e => e.Npc);
        public static void EventAddTanks(string shortname, HashSet<ulong> tanks) => AddToEventSet(shortname, tanks, e => e.Tanks);
        public static void EventAddHelicopters(string shortname, HashSet<ulong> helicopters) => AddToEventSet(shortname, helicopters, e => e.Helicopters);
        public static void EventAddTurrets(string shortname, HashSet<ulong> turrets) => AddToEventSet(shortname, turrets, e => e.Turrets);

        private static void AddToEventSet(string shortname, HashSet<ulong> values, Func<ControllerEvent, HashSet<ulong>> selector)
        {
            if (values == null) return;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            if (controllerEvent == null) return;
            HashSet<ulong> target = selector(controllerEvent);
            foreach (ulong id in values) target.Add(id);
        }

        public static void SetEventOwner(string shortname, BasePlayer player)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            controllerEvent?.SetOwner(player);
        }

        public static ulong GetEventOwner(string shortname)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            return controllerEvent?.Owner ?? 0;
        }

        public static HashSet<ulong> GetEventOwners(string shortname)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.ShortName == shortname);
            return controllerEvent?.Owners;
        }

        public static ControllerEvent GetControllerEventAtPosition(Vector3 pos) =>
            Events.FirstOrDefault(x => Vector3.Distance(pos, x.transform.position) <= x.Radius);

        public static bool IsEventTurret(BaseEntity entity)
        {
            if (entity == null || entity.net == null) return false;
            if (!(entity is AutoTurret) && !(entity is FlameTurret) && !(entity is GunTrap) && !(entity is SamSite)) return false;
            ulong id = (ulong)entity.net.ID.Value;
            return Events.Any(x => x.Turrets.Contains(id));
        }

        /// <summary>
        /// True when the entity is a combat target registered with an active PveMode event
        /// (NPC / animal / Bradley / heli / turret). Used so TruePVE gets an explicit allow
        /// instead of falling through to rules like "players cannot hurt traps".
        /// </summary>
        public static bool IsEventCombatEntity(BaseEntity entity)
        {
            if (entity == null || entity.net == null || Events.Count == 0) return false;
            ulong id = (ulong)entity.net.ID.Value;
            foreach (ControllerEvent ev in Events)
            {
                if (ev.Npc.Contains(id) || ev.Tanks.Contains(id) ||
                    ev.Helicopters.Contains(id) || ev.Turrets.Contains(id))
                    return true;
            }
            return false;
        }

        // ---- Owner callbacks (for other mods, e.g. Convoy) -------------------

        public const string AppDomainOwnerCallbacksKey = "PveMode_OwnerCallbacks";

        public static void NotifyOwnerCallbacks(string shortname, string action, BasePlayer player)
        {
            try
            {
                if (AppDomain.CurrentDomain.GetData(AppDomainOwnerCallbacksKey) is List<Action<string, string, BasePlayer>> list)
                {
                    Action<string, string, BasePlayer>[] snapshot;
                    lock (list) snapshot = list.ToArray();
                    foreach (Action<string, string, BasePlayer> cb in snapshot)
                    {
                        try { cb(shortname, action, player); }
                        catch (Exception ex) { Debug.LogWarning("[PveMode] Owner callback failed: " + ex.Message); }
                    }
                }
            }
            catch { }

            NotifyConvoyOwnerChange(shortname, action, player);
        }

        private static void NotifyConvoyOwnerChange(string shortname, string action, BasePlayer player)
        {
            try
            {
                Type convoyManager = Type.GetType("Convoy.PveModeManager, Convoy");
                if (convoyManager == null)
                {
                    foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        convoyManager = asm.GetType("Convoy.PveModeManager");
                        if (convoyManager != null) break;
                    }
                }
                if (convoyManager == null) return;

                string methodName = action == "set" ? "OnNewOwnerSet" : "OnOwnerDeleted";
                MethodInfo mi = convoyManager.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                mi?.Invoke(null, new object[] { shortname, player });
            }
            catch { }
        }

        // ---- Scientists (standalone NPC damage tracking) ---------------------

        public static void ScientistAdd(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return;
            ulong id = (ulong)npc.net.ID.Value;
            if (Scientists.ContainsKey(id)) return;
            Scientists.Add(id, npc.gameObject.AddComponent<ControllerScientist>());
        }

        public static void ScientistRemove(ScientistNPC npc)
        {
            if (npc == null || npc.net == null) return;
            ScientistRemove((ulong)npc.net.ID.Value);
        }

        public static void ScientistRemove(ulong netId)
        {
            if (Scientists.TryGetValue(netId, out ControllerScientist controller))
            {
                Scientists.Remove(netId);
                if (controller != null) UnityEngine.Object.Destroy(controller);
            }
        }

        public static void CrateAddScientist(ulong crateId, ulong scientistId)
        {
            if (crateId == 0 || scientistId == 0) return;
            if (Scientists.TryGetValue(scientistId, out ControllerScientist controller)) controller.CrateId = crateId;
        }

        public static Dictionary<ulong, float> GetScientistPlayerDamageMap(ulong netId) =>
            Scientists.TryGetValue(netId, out ControllerScientist controller) ? controller.Players : null;

        // ---- Damage ------------------------------------------------------------

        /// <summary>Entry point from Patch_BaseCombatEntity_Hurt. True = block damage.</summary>
        public static object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || entity.net == null || info == null) return null;

            if (Scientists.Count > 0 && entity is ScientistNPC scientistNpc && Scientists.TryGetValue((ulong)entity.net.ID.Value, out ControllerScientist controller))
            {
                BasePlayer attacker = info.InitiatorPlayer;
                if (IsRealPlayer(attacker)) controller.AddDamage(attacker, info.damageTypes?.Total() ?? 0f);
                return null;
            }

            if (Events.Count == 0) return null;

            bool blocked =
                OnEventEntityTakeDamage(entity, info) is bool ||
                OnEventEntityTarget(info.Initiator as BaseEntity, entity) is bool ||
                OnEventInitiatorNullTakeDamage(entity, info) is bool;

            return blocked ? (object)true : null;
        }

        public static object OnEventEntityTakeDamage(BaseCombatEntity entity, HitInfo info, bool addDamage = true, bool sendMessage = true)
        {
            if (entity == null || entity.net == null || info == null) return null;

            PveEntityType type = PveEntityType.Default;
            ControllerEvent controllerEvent = null;
            ulong entityId = (ulong)entity.net.ID.Value;

            if (entity is ScientistNPC)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Npc.Contains(entityId));
                type = PveEntityType.Npc;
            }
            else if (entity is BaseAnimalNPC)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Npc.Contains(entityId));
                type = PveEntityType.Animal;
            }
            else if (entity is BradleyAPC)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Tanks.Contains(entityId));
                type = PveEntityType.Bradley;
            }
            else if (entity is PatrolHelicopter)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Helicopters.Contains(entityId));
                type = PveEntityType.Helicopter;
            }
            else if (entity is AutoTurret || entity is FlameTurret || entity is GunTrap || entity is SamSite)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Turrets.Contains(entityId));
                type = PveEntityType.Turret;
            }

            if (controllerEvent == null) return null;

            BasePlayer attackerPlayer = info.InitiatorPlayer;
            bool isPlayer = IsRealPlayer(attackerPlayer);

            if (controllerEvent.Owner == 0)
            {
                if (isPlayer && addDamage)
                {
                    controllerEvent.Config.ScaleDamage.TryGetValue(type.ToString(), out float scale);
                    controllerEvent.AddDamage(attackerPlayer, (info.damageTypes?.Total() ?? 0f) * scale);
                }
                return null;
            }

            switch (type)
            {
                case PveEntityType.Npc:
                case PveEntityType.Animal:
                    if (controllerEvent.Config.DamageNpc) return null;
                    break;
                case PveEntityType.Bradley:
                    if (controllerEvent.Config.DamageTank) return null;
                    break;
                case PveEntityType.Helicopter:
                    if (controllerEvent.Config.DamageHelicopter) return null;
                    break;
                case PveEntityType.Turret:
                    if (controllerEvent.Config.DamageTurret) return null;
                    break;
            }

            if (isPlayer)
            {
                if (IsTeam(attackerPlayer, controllerEvent.Owner)) return null;
                if (!sendMessage) return true;
                switch (type)
                {
                    case PveEntityType.Npc:
                    case PveEntityType.Animal:
                        SendChat(attackerPlayer, PveModeLang.Get("NoDamageNpcEvent"));
                        break;
                    case PveEntityType.Bradley:
                        SendChat(attackerPlayer, PveModeLang.Get("NoDamageTankEvent"));
                        break;
                    case PveEntityType.Helicopter:
                        SendChat(attackerPlayer, PveModeLang.Get("NoDamageHelicopterEvent"));
                        break;
                    case PveEntityType.Turret:
                        SendChat(attackerPlayer, PveModeLang.Get("NoDamageTurretEvent"));
                        break;
                }
                return true;
            }

            BaseEntity attacker = info.Initiator;
            if (attacker != null && IsTeam(attacker.OwnerID, controllerEvent.Owner)) return null;
            if (type == PveEntityType.Helicopter && attacker == null && info.damageTypes != null && info.damageTypes.GetMajorityDamageType() == Rust.DamageType.Generic) return null;
            return true;
        }

        public static object OnEventInitiatorNullTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.Initiator != null) return null;

            BaseEntity weaponPrefab = info.WeaponPrefab;
            if (weaponPrefab == null) return null;

            PveEntityType type = PveEntityType.Default;
            switch (weaponPrefab.ShortPrefabName)
            {
                case "maincannonshell":
                    type = PveEntityType.Bradley;
                    break;
                case "rocket_heli":
                case "rocket_heli_napalm":
                    type = PveEntityType.Helicopter;
                    break;
            }
            if (type == PveEntityType.Default) return null;

            ControllerEvent controllerEvent = GetControllerEventAtPosition(entity.transform.position);
            if (controllerEvent == null || controllerEvent.Owner == 0) return null;

            if (type == PveEntityType.Bradley && controllerEvent.Tanks.Count == 0) return null;
            if (type == PveEntityType.Helicopter && controllerEvent.Helicopters.Count == 0) return null;

            BasePlayer targetPlayer = entity as BasePlayer;
            if (IsRealPlayer(targetPlayer))
            {
                if (type == PveEntityType.Bradley && controllerEvent.Config.TargetTank) return null;
                if (type == PveEntityType.Helicopter && controllerEvent.Config.TargetHelicopter) return null;
                return IsTeam(targetPlayer, controllerEvent.Owner) ? null : (object)true;
            }
            return IsTeam(entity.OwnerID, controllerEvent.Owner) ? null : (object)true;
        }

        // ---- Targeting -----------------------------------------------------

        public static object OnEventEntityTarget(BaseEntity attacker, BaseEntity target)
        {
            if (attacker == null || attacker.net == null || target == null) return null;

            PveEntityType type = PveEntityType.Default;
            ControllerEvent controllerEvent = null;
            ulong attackerId = (ulong)attacker.net.ID.Value;

            if (attacker is ScientistNPC)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Npc.Contains(attackerId));
                type = PveEntityType.Npc;
            }
            else if (attacker is BaseAnimalNPC)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Npc.Contains(attackerId));
                type = PveEntityType.Animal;
            }
            else if (attacker is BradleyAPC)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Tanks.Contains(attackerId));
                type = PveEntityType.Bradley;
            }
            else if (attacker is AutoTurret || attacker is FlameTurret || attacker is GunTrap || attacker is SamSite)
            {
                controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Turrets.Contains(attackerId));
                type = PveEntityType.Turret;
            }

            if (controllerEvent == null) return null;

            switch (type)
            {
                case PveEntityType.Npc:
                case PveEntityType.Animal:
                    if (controllerEvent.Config.TargetNpc) return null;
                    break;
                case PveEntityType.Bradley:
                    if (controllerEvent.Config.TargetTank) return null;
                    break;
                case PveEntityType.Turret:
                    if (controllerEvent.Config.TargetTurret) return null;
                    break;
            }

            if (type == PveEntityType.Npc)
            {
                if (target is ScientistNPC && target.net != null && controllerEvent.Npc.Contains((ulong)target.net.ID.Value)) return null;
                if (target is BasePlayer tp && (tp.skinID == 19395142091920 || tp.skinID == 8151920175)) return null;
            }

            if (target is BasePlayer targetPlayer && IsRealPlayer(targetPlayer))
                return IsTeam(targetPlayer, controllerEvent.Owner) ? null : (object)true;

            return IsTeam(target.OwnerID, controllerEvent.Owner) ? null : (object)true;
        }

        // ---- AppDomain bridge functions (published for TruePVE) ------------

        /// <summary>Func&lt;BaseEntity, HitInfo, object&gt; published as PveMode_CanEntityTakeDamage.
        /// false = block, true = explicitly allow (override TruePVE rules for event entities), null = not handled.</summary>
        public static object CanEntityTakeDamageApi(BaseEntity entityObj, HitInfo info)
        {
            if (Events.Count == 0 || !(entityObj is BaseCombatEntity entity) || info == null) return null;

            object result = OnEventEntityTakeDamage(entity, info, false, false);
            if (result is bool blocked && blocked)
                return false;

            // Event-tagged entity and damage not blocked → explicitly allow so TruePVE does not
            // apply "players cannot hurt traps" / defaultAllowDamage:false to ArmoredTrain turrets, etc.
            if (IsEventCombatEntity(entity))
                return true;

            return null;
        }

        /// <summary>Func&lt;BaseEntity, BaseEntity, object&gt; published as PveMode_CanEntityBeTargeted.
        /// Args are (target, attacker), matching TruePVE's CanEntityBeTargeted convention.
        /// false = block targeting, true = explicitly allow (event turret targeting non-owner), null = not handled.</summary>
        public static object CanEntityBeTargetedApi(BaseEntity target, BaseEntity attacker)
        {
            if (attacker == null || target == null) return null;

            object result = OnEventEntityTarget(attacker, target);
            if (result is bool blocked) return blocked ? (object)false : null;

            if (IsEventTurret(attacker)) return true;
            return null;
        }

        // ---- Loot ------------------------------------------------------------

        public static object CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!IsRealPlayer(player) || entity == null || entity.net == null) return null;
            ulong id = (ulong)entity.net.ID.Value;

            // Order matters: DroppedItemContainer/HackableLockedCrate both derive from
            // StorageContainer, so check the more specific types first.
            if (entity is NPCPlayerCorpse)
                return CanLootScientistTarget(player, id);

            if (entity is DroppedItemContainer dic)
                return dic.ShortPrefabName == "item_drop_backpack" ? CanLootScientistTarget(player, id) : null;

            // HackableLockedCrate derives from StorageContainer: same crate-loot rule applies
            // once opened (starting the hack itself is separately gated by CanHackCrate).
            if (entity is StorageContainer)
                return CanLootCrateOrContainer(player, id);

            return null;
        }

        private static object CanLootCrateOrContainer(BasePlayer player, ulong id)
        {
            if (Events.Count > 0)
            {
                ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Crates.Contains(id));
                if (controllerEvent != null)
                {
                    if (controllerEvent.Config.LootCrate || IsTeam(player, controllerEvent.Owner)) return null;
                    SendChat(player, PveModeLang.Get("NoLootCrateEvent"));
                    return true;
                }
            }

            if (CanLootCrateScientist.TryGetValue(id, out ulong ownerId))
            {
                if (IsTeam(player, ownerId)) return null;
                SendChat(player, PveModeLang.Get("NoLootScientist"));
                return true;
            }

            return null;
        }

        private static object CanLootScientistTarget(BasePlayer player, ulong id)
        {
            if (Events.Count > 0)
            {
                ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Backpacks.Contains(id));
                if (controllerEvent != null)
                {
                    if (controllerEvent.Config.LootNpc || IsTeam(player, controllerEvent.Owner)) return null;
                    SendChat(player, PveModeLang.Get("NoLootScientistEvent"));
                    return true;
                }
            }

            if (CanLootScientist.TryGetValue(id, out ulong ownerId))
            {
                if (IsTeam(player, ownerId)) return null;
                SendChat(player, PveModeLang.Get("NoLootScientist"));
                return true;
            }

            return null;
        }

        public static object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (crate == null || crate.net == null || !IsRealPlayer(player)) return null;
            ulong id = (ulong)crate.net.ID.Value;
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.Crates.Contains(id));
            if (controllerEvent == null) return null;
            if (controllerEvent.Config.HackCrate || IsTeam(player, controllerEvent.Owner)) return null;
            SendChat(player, PveModeLang.Get("NoHackCrateEvent"));
            return true;
        }

        // ---- Public query API (for PveModeApi.cs) ----------------------------

        public static bool IsPlayerInEventZone(ulong id) => Events.Any(x => x.InsidePlayers.Any(y => y.userID == id));

        public static HashSet<string> GetEventsPlayer(ulong id)
        {
            HashSet<string> result = new HashSet<string>();
            foreach (ControllerEvent controller in Events)
                if (controller.InsidePlayers.Any(x => x.userID == id)) result.Add(controller.ShortName);
            return result;
        }

        public static Dictionary<string, double> GetTimesPlayer(ulong id)
        {
            PlayerData data = FindPlayerData(id);
            if (data == null) return null;
            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> kv in data.LastTime) result.Add(kv.Key, CurrentTime - kv.Value);
            return result;
        }

        public static object CanActionEvent(string shortname, BasePlayer player)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.ShortName == shortname);
            if (controllerEvent == null) return null;
            if (IsTeam(player, controllerEvent.Owner)) return null;
            SendChat(player, PveModeLang.Get("NoCanActionEvent"));
            return false;
        }

        public static object CanActionEventNoMessage(string shortname, BasePlayer player)
        {
            ControllerEvent controllerEvent = Events.FirstOrDefault(x => x.Owner != 0 && x.ShortName == shortname);
            if (controllerEvent == null) return null;
            return IsTeam(player, controllerEvent.Owner) ? null : (object)false;
        }
    }
}
