using System;

namespace Harmony.Plugins
{
    /// <summary>
    /// Harmony glue: exposes lifecycle, commands, and compat-style hooks (private on GrimmBoss)
    /// to GrimmBossMod and Harmony patches.
    /// </summary>
    public partial class GrimmBoss
    {
        private static bool Ready => _ins != null;

        public void CallInit() => Init();
        public void CallOnServerInitialized() => OnServerInitialized();
        public void CallUnload() => Unload();

        public static void CmdWorldPos(BasePlayer player) => _ins?.ChatCommandWorldPos(player);
        public static void CmdSavePos(BasePlayer player, string[] args) => _ins?.ChatCommandSavePos(player, "savepos", args ?? Array.Empty<string>());
        public static void CmdCustomPos(BasePlayer player, string[] args) => _ins?.ChatCommandCustomPos(player, "custompos", args ?? Array.Empty<string>());
        public static void CmdSpawnBossChat(BasePlayer player, string[] args) => _ins?.ChatCommandSpawnBoss(player, "spawnboss", args ?? Array.Empty<string>());

        public static void CmdSpawnBossConsole(string[] args)
        {
            if (_ins == null) return;
            if (args == null || args.Length == 0)
            {
                _ins.Puts("You didn't write the name of the NPC");
                return;
            }
            string name = "";
            for (int i = 0; i < args.Length; i++) name += i == 0 ? args[i] : $" {args[i]}";
            NpcConfig config = null;
            foreach (NpcConfig c in _ins.Configs)
            {
                if (c != null && c.Name == name) { config = c; break; }
            }
            if (config == null)
            {
                _ins.Puts($"There is no configuration named boss - {name}");
                return;
            }
            _ins.SpawnBoss(config);
        }

        public static void CmdKillBossConsole(string[] args)
        {
            if (_ins == null) return;
            if (args == null || args.Length == 0)
            {
                _ins.Puts("You didn't write the name of the NPC");
                return;
            }
            string name = "";
            for (int i = 0; i < args.Length; i++) name += i == 0 ? args[i] : $" {args[i]}";

            // Same as ConsoleCommandKillBoss — remove live instances of the named boss.
            while (true)
            {
                ulong? removeId = null;
                ScientistNPC boss = null;
                foreach (var kv in _ins._controllers)
                {
                    if (kv.Value?.Npc != null && kv.Value.Npc.displayName == name)
                    {
                        removeId = kv.Key;
                        boss = kv.Value.Npc;
                        break;
                    }
                }
                if (removeId == null || boss == null) break;
                _ins._controllers.Remove(removeId.Value);
                boss.Kill();
            }
        }

        // ----- damage (BaseCombatEntity.Hurt) -----
        public static object Dispatch_Hurt(BaseCombatEntity entity, HitInfo info)
        {
            if (!Ready || entity == null || info == null) return null;
            if (entity is ScientistNPC scientist)
                return _ins.OnEntityTakeDamage(scientist, info);
            if (entity is BaseAnimalNPC animal)
                return _ins.OnEntityTakeDamage(animal, info);
            if (entity is BasePlayer player && !(entity is NPCPlayer))
            {
                _ins.OnEntityTakeDamage(player, info);
                return null;
            }
            return null;
        }

        /// <summary>Side-effect path: barricade cover + combat engagement even when a prefix blocks damage.</summary>
        public static void Dispatch_HurtSideEffect(BaseCombatEntity entity, HitInfo info)
        {
            if (!Ready || entity == null || info == null) return;
            if (entity is not ScientistNPC scientist) return;
            if (!_ins._controllers.TryGetValue(scientist.net.ID.Value, out var bossCtrl)) return;
            if (bossCtrl.NpcHelpersActive) return;
            bossCtrl.RefreshProximityPlayers();
            bossCtrl.PlaceGrimmCoverBarricade(info.InitiatorPlayer, info);
            BasePlayer attacker = info.InitiatorPlayer;
            if (attacker != null && !attacker.IsNpc && attacker.IsConnected)
                _ins.EnsureBossCombatEngagement(scientist, attacker);
        }

        // ----- player death -----
        public static void Dispatch_OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (!Ready || player == null) return;
            _ins.OnPlayerDeath(player, info);
        }

        // ----- corpse populate -----
        public static void Dispatch_OnCorpsePopulate(ScientistNPC scientist, NPCPlayerCorpse corpse)
        {
            if (!Ready || scientist == null || corpse == null) return;
            _ins.OnCorpsePopulate(scientist, corpse);
        }
    }
}
