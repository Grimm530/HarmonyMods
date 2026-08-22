using System;
using System.Collections.Generic;
using Oxide.Core.Plugins;
using Oxide.Plugins.DefendableHomesExtensionMethods;
using UnityEngine;

namespace Oxide.Plugins
{
    /// <summary>
    /// Harmony glue for the ported plugin: Oxide-style lifecycle + hook methods (private instance
    /// members of DefendableHomes) for Harmony patches and console commands. Dispatchers early-out
    /// when no event is active, replacing Oxide Subscribe/Unsubscribe.
    /// </summary>
    public partial class DefendableHomes
    {
        private static bool Ready => _ins != null && _ins.Controllers != null && _ins.Controllers.Count > 0;

        public void CallInit()
        {
            LoadDefaultMessages();
            Init();
        }
        public void CallOnServerInitialized()
        {
            BindOptionalPlugins();
            OnServerInitialized();
        }
        public void CallUnload() => Unload();

        private void BindOptionalPlugins()
        {
            if (EconomicsPluginBridge.IsApiLive())
                Economics = new EconomicsPluginBridge();
        }

        public static void CmdGiveFlareConsole(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null || arg.Args.Length < 2) return;
            string flare = arg.GetString(0);
            ulong steamId = 0UL;
            try { steamId = arg.Args[1].ToULong(); }
            catch { ulong.TryParse(arg.GetString(1), out steamId); }
            int amount = 1;
            if (arg.Args.Length > 2)
            {
                try { amount = arg.Args[2].ToInt(); }
                catch { amount = arg.GetInt(2, 1); }
            }
            TryGiveFlare(flare, steamId, amount);
        }

        /// <summary>Shop / RCON: grant a custom flare by skin ID or difficulty name (EASY/MEDIUM/HARD).</summary>
        public static bool TryGiveFlare(string flareArgument, ulong steamId, int amount = 1)
        {
            if (_ins == null || string.IsNullOrEmpty(flareArgument) || steamId == 0) return false;
            if (amount < 1) amount = 1;

            FlareConfig config = null;
            if (ulong.TryParse(flareArgument, out ulong skinId))
                config = _ins._config.Flares.FirstOrDefault(x => x.SkinId == skinId);
            config ??= _ins._config.Flares.FirstOrDefault(x => string.Equals(x.NameDifficultyLevel, flareArgument, StringComparison.OrdinalIgnoreCase));
            if (config == null)
            {
                _ins.Puts($"Custom flare with SkinID or difficulty level name {flareArgument} was not found in the plugin configuration!");
                return false;
            }

            BasePlayer target = BasePlayer.FindByID(steamId);
            if (target == null)
            {
                _ins.Puts($"Player with SteamID {steamId} not found!");
                return false;
            }

            Item item = GetFlare(config);
            item.amount = amount;
            int slots = target.inventory.containerMain.capacity + target.inventory.containerBelt.capacity;
            int taken = target.inventory.containerMain.itemList.Count + target.inventory.containerBelt.itemList.Count;
            if (slots - taken > 0) target.inventory.GiveItem(item);
            else item.Drop(target.transform.position, Vector3.up);
            _ins.Puts($"Player {target.displayName} has successfully received a custom flare (SkinID: {config.SkinId}, Amount: {item.amount})");
            return true;
        }

        public static void CmdGiveFlareChat(BasePlayer player, string[] args)
        {
            _ins?.ChatCommandGiveFlare(player, "giveflare", args ?? Array.Empty<string>());
        }

        public static void CmdDefStop(BasePlayer player)
        {
            _ins?.ChatStopEvent(player);
        }

        // ----- GrimmNPC CallHook bus -----
        public static object Dispatch_GrimmHook(string hook, object[] args)
        {
            if (_ins == null || string.IsNullOrEmpty(hook) || args == null) return null;
            try
            {
                if (hook == "OnCustomNpcTarget" && args.Length >= 2 && args[0] is ScientistNPC attacker && args[1] is BasePlayer player)
                    return _ins.OnCustomNpcTarget(attacker, player);
                if (hook == "OnBomberExplosion" && args.Length >= 1 && args[0] is ScientistNPC bomber)
                {
                    _ins.OnBomberExplosion(bomber, args.Length > 1 ? args[1] as BaseEntity : null);
                    return null;
                }
                if ((hook == "OnCustomNpcParentEnd" || hook == "OnCustomNpcGuardTargetEnd") && args.Length >= 1 && args[0] is ScientistNPC npc)
                {
                    _ins.OnCustomNpcParentEnd(npc);
                    return null;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[DefendableHomes] Grimm hook " + hook + " failed: " + ex.Message);
            }
            return null;
        }

        // ----- damage (BaseCombatEntity.Hurt) -----
        public static object Dispatch_Hurt(BaseCombatEntity entity, HitInfo info)
        {
            if (!Ready || entity == null || info == null) return null;
            if (entity is ScientistNPC sn) return _ins.OnEntityTakeDamage(sn, info);
            return null;
        }

        public static object Dispatch_CanEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (_ins == null || entity == null || info == null) return null;
            if (_ins.Controllers == null || _ins.Controllers.Count == 0) return null;

            ScientistNPC eventNpc = FindEventScientistAttacker(info);
            ScientistNPC eventVictim = entity as ScientistNPC;
            bool victimIsEvent = eventVictim != null && IsEventScientist(eventVictim);

            if (victimIsEvent)
            {
                if (eventNpc != null) return false;
                return true;
            }

            if (eventNpc != null) return true;
            return null;
        }

        public static object Dispatch_CanEntityBeTargeted(BaseEntity target, BaseEntity attacker)
        {
            if (_ins == null || target == null || attacker == null) return null;
            if (_ins.Controllers == null || _ins.Controllers.Count == 0) return null;

            if (target is ScientistNPC npc && attacker is AutoTurret turret)
                return _ins.CanEntityBeTargeted(npc, turret);

            if (attacker is ScientistNPC atk && IsEventScientist(atk))
                return true;

            return null;
        }

        private static bool IsEventScientist(ScientistNPC npc)
        {
            if (npc == null || _ins?.Controllers == null) return false;
            foreach (ControllerHomeRaid controller in _ins.Controllers)
                if (controller?.Scientists != null && controller.Scientists.Contains(npc))
                    return true;
            return false;
        }

        private static ScientistNPC FindEventScientistAttacker(HitInfo info)
        {
            if (info.InitiatorPlayer is ScientistNPC fromPlayer && IsEventScientist(fromPlayer))
                return fromPlayer;
            if (info.Initiator is ScientistNPC fromInitiator && IsEventScientist(fromInitiator))
                return fromInitiator;

            BaseEntity source = info.Initiator ?? info.WeaponPrefab;
            for (int i = 0; i < 4 && source != null; i++)
            {
                if (source is ScientistNPC npc && IsEventScientist(npc))
                    return npc;
                if (source.creatorEntity is ScientistNPC createdBy && IsEventScientist(createdBy))
                    return createdBy;
                source = source.GetParentEntity();
            }
            return null;
        }

        // ----- death -----
        public static void Dispatch_Die(BaseCombatEntity entity, HitInfo info)
        {
            if (!Ready || entity == null) return;
            if (entity is ScientistNPC sn) _ins.OnEntityDeath(sn, info);
        }

        public static object Dispatch_OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!Ready || player == null) return null;
            return _ins.OnPlayerDeath(player, info);
        }

        // ----- spawn -----
        public static void Dispatch_Spawned(BaseNetworkable entity)
        {
            if (!Ready || entity == null) return;
            if (entity is HackableLockedCrate crate) _ins.OnEntitySpawned(crate);
        }

        public static void Dispatch_OnLootSpawn(LootContainer container)
        {
            if (_ins == null || container == null) return;
            _ins.OnLootSpawn(container);
        }

        // ----- build -----
        public static object Dispatch_CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (!Ready || planner == null || prefab == null) return null;
            return _ins.CanBuild(planner, prefab, target);
        }

        // ----- kill -----
        public static void Dispatch_OnEntityKill(BaseNetworkable entity)
        {
            if (!Ready || entity == null) return;
            if (entity is BuildingBlock block) _ins.OnEntityKill(block);
        }

        // ----- loot -----
        public static object Dispatch_CanLoot(BasePlayer player, BaseEntity target)
        {
            if (!Ready || player == null || target == null) return null;
            if (target is HackableLockedCrate crate) return _ins.CanLootEntity(player, crate);
            return null;
        }

        // ----- corpse -----
        public static void Dispatch_OnCorpsePopulate(ScientistNPC npc, NPCPlayerCorpse corpse)
        {
            if (!Ready || npc == null || corpse == null) return;
            _ins.OnCorpsePopulate(npc, corpse);
        }

        // ----- items -----
        public static object Dispatch_CanStackItem(Item item, Item targetItem)
        {
            if (_ins == null) return null;
            return _ins.CanStackItem(item, targetItem);
        }

        public static object Dispatch_CanCombineDroppedItem(DroppedItem a, DroppedItem b)
        {
            if (_ins == null) return null;
            return _ins.CanCombineDroppedItem(a, b);
        }

        public static Item Dispatch_OnItemSplit(Item item, int amount)
        {
            if (_ins == null) return null;
            return _ins.OnItemSplit(item, amount);
        }

        // ----- thrown flare -----
        public static void Dispatch_OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon weapon)
        {
            if (_ins == null || player == null || entity == null || weapon == null) return;
            _ins.OnExplosiveThrown(player, entity, weapon);
        }
    }
}
