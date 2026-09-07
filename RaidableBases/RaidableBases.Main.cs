//#define RBDEBUG
/*
*  < ----- End-User License Agreement ----->
*  
*  You may not merge, publish, distribute, sublicense, or sell copies of This Software without the Developer’s consent. Copy or modify is allowed for personal use only.
*
*  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, 
*  THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS 
*  BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE 
*  GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT 
*  LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*
*  Developer: nivex (mswenson82@yahoo.com)
*
*  Copyright © nivex. All rights reserved.

███▄    ██ ██▓ ██▒   ██▒▓█████ ██▓    ██▓
 ██ ▀█  ░█ ▓██▒▓██░   ██▒▓█  ▀   ▓██▒██▓
▓██  ▀█ ██▒▒██▒ ▓██  █▒░▒████     ▒██▒
▓██▒  ▐▌██▒░██░  ▒██ █░░▒▓█  ▄   ░██░██░
▒██░   ▓██░░██░   ▒▀█░  ░▒████▒░██░   ██▓
░ ▒░   ▒ ▒ ░▓     ░ ▐░  ░░ ▒░ ░░▓     ▓ ░
░ ░░   ░ ▒░ ▒ ░   ░ ░░   ░ ░  ░ ▒ ░   ▒ ░ 
   ░   ░ ░  ▒ ░     ░░     ░    ▒ ░   ▒
         ░  ░        ░     ░  ░ ░     ░
*/

using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Rust;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Color = UnityEngine.Color;
using Graphics = System.Drawing.Graphics;
using WrapMode = System.Drawing.Drawing2D.WrapMode;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases : RaidableBasesBase
    {
        public const string Version = "3.2.634";
        private HarmonyEngine harmonyEngine;
        private class HarmonyEngine : IDisposable
        {
            private static RaidableBases Instance;
            private List<PatchDefinition> _definitions = new();
            private Harmony _harmony;
            private bool _validated;

            internal enum PatchGroup { RaidWindow, ServerPveBypass }

            private class PatchDefinition
            {
                internal readonly PatchGroup Group;
                internal readonly HarmonyPatchType PatchType;
                internal readonly Type TargetType;
                internal readonly string TargetMethodName;
                internal readonly Type[] TargetParameterTypes;
                internal readonly Type PatchTypeClass;
                internal readonly string PatchMethodName;
                internal MethodInfo TargetMethod;
                internal MethodInfo PatchMethod;
                internal bool Applied;
                internal bool Valid;

                internal PatchDefinition(PatchGroup group, HarmonyPatchType patchType, Type targetType, string targetMethodName, Type[] targetParameterTypes, Type patchTypeClass, string patchMethodName)
                {
                    Group = group;
                    PatchType = patchType;
                    TargetType = targetType;
                    TargetMethodName = targetMethodName;
                    TargetParameterTypes = targetParameterTypes;
                    PatchTypeClass = patchTypeClass;
                    PatchMethodName = patchMethodName;
                }
            }

            internal HarmonyEngine(RaidableBases instance)
            {
                Instance = instance;
                _definitions.Add(new PatchDefinition(PatchGroup.RaidWindow, HarmonyPatchType.Transpiler, typeof(RaidWindow), nameof(RaidWindow.BlocksDamage), new[] { typeof(BaseCombatEntity), typeof(HitInfo) }, typeof(RaidWindowRedirect), nameof(RaidWindowRedirect.Transpiler)));
                if (Instance.AegisPVE.CanCall() || Instance.RealPVE.CanCall()) return; // ChatGPT: these already patch server.pve, would be better if we could see if any harmony patch was applied to it though without reflection on AegisPVE or RealPVE, but rather looking at all harmony patches and name matching against server.pve to see if it's contained in any of them?
                _definitions.Add(new PatchDefinition(PatchGroup.ServerPveBypass, HarmonyPatchType.Transpiler, typeof(BasePlayer), nameof(BasePlayer.Hurt), new[] { typeof(HitInfo) }, typeof(PveFieldRedirect), nameof(PveFieldRedirect.Transpiler)));
                _definitions.Add(new PatchDefinition(PatchGroup.ServerPveBypass, HarmonyPatchType.Transpiler, typeof(BuildingBlock), nameof(BuildingBlock.Hurt), new[] { typeof(HitInfo) }, typeof(PveFieldRedirect), nameof(PveFieldRedirect.Transpiler)));
            }

            internal bool ServerPveBypassApplied => IsPatched(PatchGroup.ServerPveBypass);

            internal bool RaidWindowBypassApplied => IsPatched(PatchGroup.RaidWindow);

            internal void SetEnabled(PatchGroup group, bool enabled)
            {
                if (enabled)
                {
                    PatchDefinitions(group);
                }
                else
                {
                    UnpatchDefinitions(group);
                }
            }

            private bool IsPatched(PatchGroup group)
            {
                bool found = false;

                for (int i = 0; i < _definitions.Count; i++)
                {
                    PatchDefinition definition = _definitions[i];

                    if (definition.Group != group)
                    {
                        continue;
                    }

                    found = true;

                    if (!definition.Applied)
                    {
                        return false;
                    }
                }

                return found;
            }

            private void ValidateDefinitions()
            {
                if (_validated)
                {
                    return;
                }

                _validated = true;

                for (int i = 0; i < _definitions.Count; i++)
                {
                    PatchDefinition definition = _definitions[i];
                    definition.TargetMethod = AccessTools.Method(definition.TargetType, definition.TargetMethodName, definition.TargetParameterTypes);
                    definition.PatchMethod = AccessTools.Method(definition.PatchTypeClass, definition.PatchMethodName);

                    if (definition.TargetMethod == null)
                    {
                        Puts("Harmony: failed to find target method {0}.{1} with the expected parameters.", definition.TargetType.FullName, definition.TargetMethodName);
                        continue;
                    }

                    if (definition.PatchMethod == null)
                    {
                        Puts("Harmony: failed to find patch method {0}.{1}.", definition.PatchTypeClass.FullName, definition.PatchMethodName);
                        continue;
                    }

                    definition.Valid = true;
                }
            }

            private void PatchDefinitions(PatchGroup group)
            {
                ValidateDefinitions();
                _harmony ??= new Harmony($"{nameof(RaidableBases)}Patch");

                for (int i = 0; i < _definitions.Count; i++)
                {
                    PatchDefinition definition = _definitions[i];

                    if (definition.Group != group || !definition.Valid || definition.Applied)
                    {
                        continue;
                    }

                    try
                    {
                        HarmonyMethod patch = new(definition.PatchMethod);

                        switch (definition.PatchType)
                        {
                            case HarmonyPatchType.Prefix:
                                _harmony.Patch(definition.TargetMethod, prefix: patch);
                                break;
                            case HarmonyPatchType.Postfix:
                                _harmony.Patch(definition.TargetMethod, postfix: patch);
                                break;
                            case HarmonyPatchType.Transpiler:
                                _harmony.Patch(definition.TargetMethod, transpiler: patch);
                                break;
                            case HarmonyPatchType.Finalizer:
                                _harmony.Patch(definition.TargetMethod, finalizer: patch);
                                break;
                            default:
                                continue;
                        }

                        if (definition.PatchTypeClass == typeof(RaidWindowRedirect) && !RaidWindowRedirect.Matched)
                        {
                            _harmony.Unpatch(definition.TargetMethod, definition.PatchType, _harmony.Id);
                            continue;
                        }

                        if (definition.PatchTypeClass == typeof(PveFieldRedirect) && !PveFieldRedirect.Matched)
                        {
                            _harmony.Unpatch(definition.TargetMethod, definition.PatchType, _harmony.Id);
                            continue;
                        }

                        definition.Applied = true;
                    }
                    catch (Exception ex)
                    {
                        Puts("Failed to patch {0}.{1}: {2}", definition.TargetType.FullName, definition.TargetMethodName, ex);
                    }
                }
            }

            private void UnpatchDefinitions(PatchGroup? group = null)
            {
                if (_harmony == null)
                {
                    return;
                }

                for (int i = 0; i < _definitions.Count; i++)
                {
                    PatchDefinition definition = _definitions[i];

                    if (group.HasValue && definition.Group != group.Value || !definition.Applied || definition.TargetMethod == null)
                    {
                        continue;
                    }

                    try
                    {
                        _harmony.Unpatch(definition.TargetMethod, definition.PatchType, _harmony.Id);
                    }
                    catch (Exception ex)
                    {
                        Puts("Failed to unpatch {0}.{1}: {2}", definition.TargetType.FullName, definition.TargetMethodName, ex);
                    }

                    definition.Applied = false;
                }
            }

            public void Dispose()
            {
                UnpatchDefinitions();
                _definitions.Clear();
                _harmony = null;
                Instance = null;
            }

            private static class RaidWindowRedirect
            {
                internal static bool Matched { get; private set; }

                private static bool OnRaidWindowBlockDamage(BaseCombatEntity victim, HitInfo info)
                {
                    RaidableBases instance = Instance;
                    return instance == null || !instance.Has(victim);
                }

                internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
                {
                    Matched = false;
                    
                    List<CodeInstruction> codes = new(instructions);
                    MethodInfo blockDamageMethod = AccessTools.Method(typeof(RaidWindowRedirect), nameof(OnRaidWindowBlockDamage));

                    if (blockDamageMethod == null)
                    {
                        Puts("Could not find 'OnRaidWindowBlockDamage'");
                        return codes;
                    }

                    for (int i = codes.Count - 3; i >= 0; i--)
                    {
                        CodeInstruction current = codes[i];

                        if (current.opcode != OpCodes.Ldc_I4_1 || !IsStoreLocal(codes[i + 1].opcode) || !IsLeave(codes[i + 2].opcode))
                        {
                            continue;
                        }

                        current.opcode = OpCodes.Ldarg_0;
                        current.operand = null;
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
                        codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, blockDamageMethod));
                        Matched = true;
                        break;
                    }

                    if (!Matched)
                    {
                        Puts("Could not patch OnRaidWindowBlockDamage in {0}.{1}.", original.DeclaringType?.FullName, original.Name);
                    }

                    return codes;
                }

                private static bool IsStoreLocal(OpCode opcode)
                {
                    return opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S || opcode == OpCodes.Stloc_0 || opcode == OpCodes.Stloc_1 || opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3;
                }

                private static bool IsLeave(OpCode opcode)
                {
                    return opcode == OpCodes.Leave || opcode == OpCodes.Leave_S;
                }
            }

            private static class PveFieldRedirect // Dynamic implementation reserved for a later date.
            {
                internal static bool Matched { get; private set; }

                internal static bool GetDynamicPve(BaseCombatEntity entity, HitInfo info) => false;

                internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
                {
                    Matched = false;
                    
                    List<CodeInstruction> codes = new(instructions);
                    FieldInfo serverPveField = AccessTools.Field(typeof(ConVar.Server), nameof(ConVar.Server.pve));
                    MethodInfo dynamicPveMethod = AccessTools.Method(typeof(PveFieldRedirect), nameof(GetDynamicPve));

                    if (serverPveField == null)
                    {
                        Puts("Could not find 'serverPveField'");
                        return codes;
                    }

                    if (dynamicPveMethod == null)
                    {
                        Puts("Could not find 'dynamicPveMethod'");
                        return codes;
                    }

                    for (int i = 0; i + 1 < codes.Count; i++)
                    {
                        CodeInstruction current = codes[i];

                        if (current.opcode != OpCodes.Ldsfld || current.operand is not FieldInfo field || !field.Equals(serverPveField))
                        {
                            continue;
                        }

                        OpCode branch = codes[i + 1].opcode;

                        if (branch != OpCodes.Brfalse && branch != OpCodes.Brfalse_S)
                        {
                            continue;
                        }

                        current.opcode = OpCodes.Ldarg_0;
                        current.operand = null;
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
                        codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, dynamicPveMethod));
                        Matched = true;
                        i += 2;
                    }

                    if (!Matched)
                    {
                        Puts("Could not patch PveFieldRedirect in {0}.{1}.", original.DeclaringType?.FullName, original.Name);
                    }

                    return codes;
                }
            }
        }

        #pragma warning disable CS0649, CS0169
        private object AbandonedBases, DangerousTreasures, ZoneManager, BankSystem, IQEconomic, Economics, ServerRewards, GUIAnnouncements, AdvancedAlerts, Archery, Space, PocketDimensions, FauxAdmin, PreventLooting;
        private object IQDronePatrol, Friends, Clans, Kits, Mercatura, AegisPVE, TruePVE, RealPVE, SimplePVE, NightLantern, Wizardry, NextGenPVE, Imperium, Backpacks, BaseRepair, Notify, SkillTree, ShoppyStock, BuyableBases, XPerience, XLevels;
#pragma warning restore CS0649, CS0169
        private const int targetMask = 8454145;
        private const int visibleMask = 10551553;
        private const int targetMask2 = 10551313;
        private const int manualMask = 1084293393;
        private const int blockLayers = 2228480;
        private const int queueLayers = 2294528;
        private const int gridLayers = 327936;
        private const float M_RADIUS = 25f;
        private const float CELL_SIZE = 12.5f;
        private float OceanLevel;
        private bool wiped;
        private bool buyableEnabled;
        private bool IsUnloading;
        private bool IsShuttingDown;
        private bool bypassRestarting;
        private bool DebugMode;
        private const ulong RB_SKIN_ID = 3710562502;
        private const ulong GRIMM_PAPER_SKIN = 2961180853UL;
        private static ulong BotIdCounter = 514922525;
        private int MaxConsideredWorkbenchLevel = 3;

        private SkinSettingsImportedWorkshop ImportedWorkshopSkins = new();
        private ConfigPresetController _configPresetController;
        private PasteEngine _pasteEngine;
        private TargetInfoController _targetInfo;
        private ProtectionProperties _elevatorProtection;
        private ProtectionProperties _turretProtection;
        private AutomatedController Automated;
        private StoredData data = new();
        public BuildingTables Buildings = new();
        public QueueController Queues;
        private SkinsPlugin skinsPlugin = new();
        private Coroutine checkPlayersNearEventsCo;
        private Coroutine markerUpdateCo;
        private Coroutine npcSchedulerCo;
        private Coroutine raidMaintenanceCo;
        private Coroutine setupCopyPasteObstructionRadius;
        private readonly Dictionary<RandomBase, Coroutine> loadCoroutines = new();
        public List<string> DestroyedPrefabs = new();
        public List<string> RaidableModes = new();
        public List<RaidableBase> Raids = new();
        public Dictionary<ulong, DelaySettings> PvpDelay = new();
        public Dictionary<string, SkinInfo> Skins = new();
        private Dictionary<string, PasteData> _pasteData = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<ulong, HumanoidBrain> HumanoidBrains = new();
        private Dictionary<NetworkableId, RaidableBase> EntityToRaid = new();
        private Dictionary<string, ItemDefinition> PaidDeployableItems = new();
        private Dictionary<string, ItemDefinition> DeployableItems = new();
        public Dictionary<ItemDefinition, int> DefinitionToWorkbenchLevel = new();
        public Dictionary<Projectile, int> ProjectileToWorkbenchLevel = new();
        public Dictionary<string, int> AttackEntityToWorkbenchLevel = new();
        public Dictionary<Projectile, HashSet<string>> ProjectileToShortnames = new();
        public Dictionary<string, int> ProjectileEntityToWorkbenchLevel = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> ProjectileEntityToShortnames = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> WeaponEntityToShortnames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> CatapultProjectilePrefabs = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> CatapultWeaponShortnames = new(StringComparer.OrdinalIgnoreCase);
        private int CatapultWorkbenchLevel;
        private Dictionary<ItemDefinition, string> ItemDefinitions = new();
        private readonly Dictionary<string, string> TypeNameLookup = new();
        private Dictionary<ItemDefinition, ItemModConsume> _itemModConsume = new();
        private Dictionary<ItemDefinition, ItemModProjectile> _itemModProjectile = new();
        private readonly string[] BaseRouteIdleGestures = { "pickup_item", "wave", "thumbsup", "shrug" };
        private readonly string[] ManualEventHelperCommands = { "despawn", "draw", "debug", "kill_cleanup", "despawnall", "despawn_inactive", "generateloot", "active", "expire", "resetcooldown", "expireall", "resetall", "setowner", "lockraid", "clearowner" };
        private readonly Dictionary<SphereColor, string[]> ColorPrefabMap = new() { [SphereColor.Blue] = new[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab" }, [SphereColor.Cyan] = new[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" }, [SphereColor.Green] = new[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" }, [SphereColor.Magenta] = new[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab" }, [SphereColor.Purple] = new[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab" }, [SphereColor.Red] = new[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab" }, [SphereColor.Yellow] = new[] { "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab", "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab" } };
        private readonly List<string> ExcludedMounts = new() { "beachchair", "boogieboard", "cardtable", "chair", "chippyarcademachine", "computerstation", "drumkit", "microphonestand", "piano", "secretlabchair", "slotmachine", "sofa", "xylophone" };
        private readonly List<string> Blocks = new() { "wall.frame.cell", "wall.doorway", "wall", "wall.frame", "wall.half", "wall.low", "wall.window", "foundation.triangle", "foundation", "wall.external.high.wood", "wall.external.high.stone", "wall.external.high.ice", "floor.triangle.frame", "floor.triangle", "floor.frame" };
        private readonly List<string> TrueDamage = new() { "spikes.floor", "barricade.metal", "barricade.woodwire", "barricade.wood", "wall.external.high.wood", "wall.external.high.stone", "wall.external.high.ice" };
        private readonly List<string> arguments = new() { "add", "remove", "list", "clean", "enable_dome_marker", "toggle", "stability", "inventories", "maintained", "scheduled", "noexplosivecosts", "accepted-items", "retrieve-items", "export" };
        private readonly List<uint> CupboardPrefabIDs = new() { 2476970476, 785685130, 3932172323 };
        private readonly IPlayer _consolePlayer = new RustConsolePlayer();
        private readonly List<BaseEntity.Slot> _checkSlots = new() { BaseEntity.Slot.Lock, BaseEntity.Slot.UpperModifier, BaseEntity.Slot.MiddleModifier, BaseEntity.Slot.LowerModifier };

        public class RaidElevator
        {
            public BMGELEVATOR BMG;
            public Elevator Elevator;
            public RaidableBase raid;
            public Elevator Entity => BMG?._elevator ?? Elevator;
            public bool IsBMG() => BMG != null && raid != null;
            public bool IsVanilla() => raid != null && !Elevator.IsKilled();
            public bool CanUseElevator(BasePlayer player)
            {
                var elevator = Entity;
                if (elevator.IsKilled()) return false;
                if (raid.Options.Elevators.RequiresPower && !elevator.IsPowered()) return false;
                return raid.HasCardPermission(player) && raid.HasBuildingPermission(player);
            }
        }

        public class PasteData
        {
            public bool FloorsAreFoundations;
            public bool UsesWaterBasesFoundations;
            public bool valid;
            public float radius;
            public float foundationClearance;
            public float minFoundationY;
            public float maxFoundationY;
            public float minEntityY;
            public float maxEntityY;
            public int groundedFoundationCornerCount;
            public Vector3 centerOffset;
            public List<Vector3> floors = new();
            public List<Vector3> foundations = new();
            public List<Vector3> compound = new();
            public List<Vector3> foundationCorners = new();
            public List<Vector3> importantEntities = new();
            public List<Vector3> stairs = new();
            public List<Vector3> ramps = new();
            public List<string> invalid;
            public bool HasFloatingFoundationFallback => groundedFoundationCornerCount > 0 && groundedFoundationCornerCount < foundationCorners.Count;
            public void ClearPlacementGeometry()
            {
                floors.Clear();
                compound.Clear();
                foundations.Clear();
                foundationCorners.Clear();
                importantEntities.Clear();
                stairs.Clear();
                ramps.Clear();
                UsesWaterBasesFoundations = false;
                groundedFoundationCornerCount = 0;
            }
            public PasteData() { }
        }

        public static class RaidableMode
        {
            public const string Easy = en ? "Easy" : "Легкий";
            public const string Medium = en ? "Medium" : "Средний";
            public const string Hard = en ? "Hard" : "Тяжело";
            public const string Expert = en ? "Expert" : "Эксперт";
            public const string Nightmare = en ? "Nightmare" : "Кошмарный";
            public const string Legacy = "Legacy", Random = "Random", Points = "Points", Disabled = "Disabled";
        }

        public float MaxTerrainY = 150f;

        public struct DamageMultiplier { public DamageType index; public float amount; }

        public enum DamageResult { None, Allowed, Blocked }

        public enum SphereColor { None, Blue, Cyan, Green, Magenta, Purple, Red, Yellow }

        public enum RaidableType { None, Manual, Scheduled, Purchased, Maintained, Grid }

        public enum CacheType { Close, Delete, Generic, Generic2, Temporary, Privilege, Seabed, Seabed2, Submerged }

        public enum ConstructionType { Barricade, Ladder, Any }

        public enum SkinType { Box, Deployable, Loot, Npc }

        public class StoredData
        {
            public RotationCycle Cycle = new();
            public Dictionary<string, Lockout> Lockouts = new();
            public Dictionary<string, PlayerInfo> Players = new();
            public Dictionary<ulong, BuyableInfo> BuyableCooldowns = new();
            public DateTime RaidTime = DateTime.MinValue;
            public int TotalEvents;
            public int protocol = -1;
            public StoredData() { }
            public PlayerInfo GetPlayerInfo(string userid)
            {
                if (!Players.TryGetValue(userid, out var info))
                {
                    Players[userid] = info = new();
                }
                return info;
            }
        }

        public class RandomBase
        {
            public float typeDistance, protectionRadius, safeRadius, ignoreRadius, buildRadius, baseHeight = 1f;
            public bool stability, checkTerrain, Sorted, Save, IsPasting, inventories = true, precisePlacement, IsWaterSpawn;
            public string BaseName, username = "", id = "";
            public int attempts, errors;
            public ulong userid;
            public Vector3 Position;
            public IPlayer user;
            public IClan clan;
            public RaidableType type;
            public BasePlayer owner;
            public BasePlayer admin;
            public PasteData pasteData;
            public BaseProfile Profile;
            public RaidableSpawns spawns;
            public RaidableBases Instance;
            public RaidableBase raid;
            public Payments payments = new();
            public HashSet<ulong> alliance = new();
            internal bool invalid => !pasteData.valid || pasteData.foundations.IsNullOrEmpty() || pasteData.foundationCorners.IsNullOrEmpty();
            public BuildingOptions options => Profile?.Options;
            public CacheType FromCacheType => IsWaterSpawn ? CacheType.Seabed : CacheType.Generic;
            public CacheType ToCacheType => IsWaterSpawn ? CacheType.Seabed2 : CacheType.Generic2;
            public bool isCustomSpawn => spawns != null && spawns.IsCustomSpawn;
            public bool isBuyableEvent => payments.position != Vector3.zero;
            public bool HasSpawns() => options.Water.Seabed >= 100f ? (spawns.IsCustomSpawn ? spawns.Seabed.Count > 0 || spawns.Spawns.Count > 0 : spawns.Seabed.Count > 0) : spawns.Spawns.Count > 0 || spawns.Seabed.Count > 0;
            public void TrySortByDistance()
            {
                if (!Sorted && isBuyableEvent && Instance.config.Settings.Buyable.Closest)
                {
                    Instance.SendNotification(owner, "BuyBaseLocate");
                    var set = spawns.GetLocations(FromCacheType);
                    if (set.Count > 0)
                    {
                        using var tmp = set.ToPooledList();
                        tmp.Sort((x, y) => (x.Location - payments.position).sqrMagnitude.CompareTo((y.Location - payments.position).sqrMagnitude));
                        set.Clear();
                        set.UnionWith(tmp);
                        Sorted = true;
                    }
                }
                else Sorted = false;
            }
            public bool IsTeleportPending(BasePlayer player, Vector3 v)
            {
                return type == RaidableType.Purchased && options.CustomSpawns.BuyableTeleportPositions.Count > 0 && player != null && player.HasPermission("raidablebases.buyraid.prefabteleport") && options.CustomSpawns.HasTeleportPositionAt(v);
            }
        }

        public class BackpackData : Pool.IPooled
        {
            public BackpackData() { }
            public void EnterPool() { Pool.FreeUnmanaged(ref containers); _player = null; userid = 0uL; }
            public void LeavePool() => containers = Pool.Get<List<DroppedItemContainer>>();
            public List<DroppedItemContainer> containers;
            public BasePlayer _player;
            public ulong userid;
            public bool IsEmpty => containers.Count == 0 || containers.All(x => x.IsKilled());
            public BasePlayer player { get { if (_player == null) { _player = RustCore.FindPlayerById(userid); } return _player; } }
        }

        public class BuyableInfo
        {
            public Dictionary<string, DateTime> Modes = new(StringComparer.OrdinalIgnoreCase);

            public static double GetTimeRemaining(RaidableBases m, ulong userid, string mode)
            {
                if (m.config.Settings.Buyable.Cooldowns.Get(mode).Cooldown <= 0 || !m.data.BuyableCooldowns.TryGetValue(userid, out var info) || !info.Modes.TryGetValue(mode, out DateTime date))
                {
                    return 0;
                }
                return Math.Max(0, date.Subtract(DateTime.Now).TotalSeconds);
            }
            public static double GetTimeRemaining(RaidableBases m, BasePlayer buyer, string mode, bool message)
            {
                double time = GetTimeRemaining(m, buyer.userID, mode);
                if (time > 0 && message)
                {
                    m.SendNotification(buyer, "BuyCooldown", m.FormatTime(time, buyer.UserIDString));
                }
                return time;
            }
            public static bool HasTimeRemaining(RaidableBases m, ulong userid)
            {
                return m.GetRaidableModes().Exists(mode => GetTimeRemaining(m, userid, mode) > 0);
            }
        }

        public class TimeSettings
        {
            public string mode;
            public Timer Timer;
            public double time;
            public void Destroy()
            {
                if (Timer != null && !Timer.Destroyed)
                {
                    Timer.Destroy();
                }

                Timer = null;
            }
        }

        public class DelaySettings : TimeSettings
        {
            public RaidableBase raid;
            public object[] hookObjectsFallback;
        }

        public class SkinInfo
        {
            public List<ulong> skins = new(), workshopSkins = new(), importedWorkshopSkins = new(), allSkins = new();
        }

        public class Lockout
        {
            public Dictionary<string, DateTime> Levels = new(StringComparer.OrdinalIgnoreCase);

            public bool Any()
            {
                foreach (var level in Levels)
                {
                    if (level.Value > DateTime.Now)
                    {
                        return true;
                    }
                }
                return false;
            }

            public double Get(string mode)
            {
                if (Levels.TryGetValue(mode, out var level) && level > DateTime.Now)
                {
                    return level.Subtract(DateTime.Now).TotalSeconds;
                }
                Levels.Remove(mode);
                return 0;
            }

            public void Set(string mode, double time)
            {
                if (!Levels.TryGetValue(mode, out DateTime expiry) || expiry <= DateTime.Now)
                {
                    Levels[mode] = DateTime.Now.AddSeconds(time);
                }
            }
        }

        public class RankedRecord
        {
            public string Permission = string.Empty;
            public string Group = string.Empty;
            public string Mode = string.Empty;
            internal bool IsValid => !string.IsNullOrWhiteSpace(Permission) && !string.IsNullOrWhiteSpace(Group) && !string.IsNullOrWhiteSpace(Mode);
            public RankedRecord(string permission, string group, string mode)
            {
                (Permission, Group, Mode) = (permission, group, mode);
            }
            public RankedRecord() { }
        }

        public class RaidableSpawnLocation : IEquatable<RaidableSpawnLocation>
        {
            public List<Vector3> Surroundings = new();
            public Vector3 Location;
            public float WaterHeight;
            public float TerrainHeight;
            public float SpawnHeight;
            public float Radius;
            public int? biome;
            public RaidableSpawnLocation(Vector3 location)
            {
                Location = location;
            }
            public bool Equals(RaidableSpawnLocation other) => other != null && Location.Equals(other.Location);
            public override bool Equals(object obj) => obj is RaidableSpawnLocation other && Equals(other);
            public override int GetHashCode() => Location.GetHashCode();
        }

        public class ZoneInfo
        {
            internal string ZoneId;
            internal Quaternion Rotation;
            internal Vector3 Position;
            internal Vector3 Size;
            internal Vector3 extents;
            internal float Distance;
            internal bool IsBlocked;

            public ZoneInfo(string id, Vector3 pos, Quaternion rot, float radius, Vector3 size, bool isBlocked, float dist)
            {
                (IsBlocked, ZoneId, Position, Rotation) = (isBlocked, id, pos, rot);

                dist = Mathf.Max(dist, 100f);
                Distance = radius + M_RADIUS + dist;

                if (size != Vector3.zero)
                {
                    Size = size + new Vector3(dist, Position.y + 100f, dist);
                    extents = Size * 0.5f;
                }
            }

            public bool IsPositionInZone(Vector3 a)
            {
                if (Size != Vector3.zero)
                {
                    Vector3 v = Quaternion.Inverse(Rotation) * (a - Position);

                    return v.x <= extents.x && v.x > -extents.x && v.y <= extents.y && v.y > -extents.y && v.z <= extents.z && v.z > -extents.z;
                }
                return InRange2D(Position, a, Distance);
            }
        }

        public class BaseProfile
        {
            public List<LootItem> BaseLootList = new();
            public BuildingOptions Options = new();
            public Dictionary<RaidableType, RaidableSpawns> Spawns;
            public string ProfileName;
            public RaidableBases Instance;
            public BaseProfile(RaidableBases instance)
            {
                Instance = instance;
                Spawns = new();
                Options.AdditionalBases = new(StringComparer.OrdinalIgnoreCase);
                Options.NPC.SetAccuracy(Options.Mode);
            }

            public BaseProfile(RaidableBases instance, BuildingOptions options, string name)
            {
                Spawns = new();
                Instance = instance;
                Options = options;
                ProfileName = name;
            }

        }

        public class BuildingTables
        {
            public Dictionary<string, DateTime> LootID = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<LootItem>> DifficultyLootLists = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<DayOfWeek, List<LootItem>> WeekdayLootLists = new();
            public Dictionary<string, BaseProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
            public List<string> Removed = new();

            public bool IsConfigured(string baseName)
            {
                foreach (var m in Profiles)
                {
                    if (m.Key == baseName || m.Value.Options.AdditionalBases.ContainsKey(baseName))
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool TryGetValue(string baseName, out BaseProfile profile)
            {
                foreach (var m in Profiles)
                {
                    if (m.Key == baseName || m.Value.Options.AdditionalBases.ContainsKey(baseName))
                    {
                        profile = m.Value;
                        return true;
                    }
                }
                profile = null;
                return false;
            }

            public void Remove(string baseName)
            {
                if (Profiles.Remove(baseName) || Profiles.Values.Exists(m => m.Options.AdditionalBases.Remove(baseName)))
                {
                    Removed.Add(baseName);
                }
            }
        }

        public GridControllerManager GridController = new();

        public class GridControllerManager
        {
            internal RaidableBases Instance;
            internal Dictionary<RaidableType, RaidableSpawns> Spawns = new();
            internal IEnumerator gridCoroutine;
            internal Coroutine fileCoroutine;
            internal double gridTime;
            internal int step = int.MaxValue;
            internal int progress;
            internal double progressTotal;
            private SpawnsControllerManager.AreaSafetyQuery areaSafetyQuery = new();

            public SpawnsControllerManager SpawnsController => Instance.SpawnsController;
            public StoredData data => Instance.data;
            public Configuration config => Instance.config;
            public double GetRaidTime() => data.RaidTime.Subtract(DateTime.Now).TotalSeconds;

            public void StartAutomation()
            {
                if (Instance.Automated.IsScheduledEnabled)
                {
                    if (data.RaidTime != DateTime.MinValue && GetRaidTime() > config.Settings.Schedule.IntervalMax)
                    {
                        data.RaidTime = DateTime.MinValue;
                    }

                    Instance.Automated.StartCoroutine(RaidableType.Scheduled);
                }

                if (Instance.Automated.IsMaintainedEnabled)
                {
                    Instance.Automated.StartCoroutine(RaidableType.Maintained);
                }
            }

            private IEnumerator LoadFiles(bool loadProfiles)
            {
                Instance.Buildings = new();
                step = 1; using var sb = DisposableBuilder.Get();
                step = 2; if (loadProfiles) yield return Instance.LoadProfiles(sb);
                step = 3; yield return Instance.LoadTables(sb);
                if (Instance.Buildings.Profiles.Count == 0)
                {
                    step = int.MinValue;
                    CriticalError();
                    yield break;
                }
                step = 4;
                FrameDeadline deadline = new(1, 5);
                bool customSpawnsEnabled = Instance.Buildings.Profiles.Exists(x => x.Value.Options.CustomSpawns.Any);
                if (customSpawnsEnabled)
                {
                    using var custom = DisposableList<(string, int)>();
                    foreach (var prefab in World.Serialization.world.prefabs)
                    {
                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }
                        if (StringPool.toString.TryGetValue(prefab.id, out var fullname))
                        {
                            TryAddCustomSpawn(prefab, fullname, new(prefab.position.x, prefab.position.y, prefab.position.z), custom);
                        }
                    }
                    foreach (var (type, amount) in custom) Puts($"Loaded {amount} custom spawns from {type}");
                }
                step = 5; Instance.ProcessExtensions(ExtOp.Init);
                step = 6; Instance.ProcessExtensions(ExtOp.Validate);
                yield return CoroutineEx.waitForSeconds(5f);
                Instance.IsSpawnerBusy = false;
                step = 0; StartAutomation();
                if (!Instance.IsPasteEngineReady(out var error)) Puts(error);
            }

            public void SetupGrid()
            {
                if (Spawns.Count >= 5)
                {
                    fileCoroutine = ServerMgr.Instance.StartCoroutine(LoadFiles(true));
                    return;
                }

                StopCoroutine();
                gridCoroutine = GenerateGrid();
                ServerMgr.Instance.StartCoroutine(gridCoroutine);
            }

            public void StopCoroutine()
            {
                if (gridCoroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(gridCoroutine);
                    gridCoroutine = null;
                }
                if (fileCoroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(fileCoroutine);
                    fileCoroutine = null;
                }

                areaSafetyQuery.Clear();
            }

            private void CriticalError(string text = "No valid profiles exist!")
            {
                if (Instance.profileErrors.Count > 0)
                {
                    Puts("Json errors found in:");
                    Instance.profileErrors.ForEach(str => Puts(str));
                }
                Puts("ERROR: Grid has failed initialization. {0}", text);
                HarmonyModInterface.Mods.NextTick(() => gridCoroutine = null);
            }

            private IEnumerator GenerateGrid()
            {
                try
                {
                    yield return GenerateGridInternal();
                }
                finally
                {
                    areaSafetyQuery.Clear();
                    Instance.IsSpawnerBusy = false;
                    Instance.Queues.Messages.Clear();
                    if (step == 0) Instance.GridController.StartAutomation();
                    else if (step != 999) Puts("Initialization has failed at step {0}", step);
                    if (!Instance.IsPasteEngineReady(out var error)) Puts(error);
                    gridCoroutine = null;
                }
            }

            private IEnumerator GenerateGridInternal()
            {
                step = 1;
                yield return CoroutineEx.waitForSeconds(0.1f);

                FrameDeadline deadline = new(1, 5);
                Stopwatch gridStopwatch = Stopwatch.StartNew();

                step = 2;
                RaidableSpawns spawns = Spawns[RaidableType.Grid] = new(Instance);

                gridTime = Time.realtimeSinceStartupAsDouble;
                Instance.Buildings = new();

                using var sb = DisposableBuilder.Get();
                step = 3; yield return Instance.LoadProfiles(sb);
                deadline.Reset();

                if (Instance.Buildings.Profiles.Values.All(x => x.Options.CustomSpawns.All))
                {
                    step = 999;
                    gridStopwatch.Stop();
                    fileCoroutine = ServerMgr.Instance.StartCoroutine(LoadFiles(false));
                    yield break;
                }

                step = 4; yield return Instance.LoadTables(sb);
                deadline.Reset();

                step = 5; yield return SpawnsController.SetupMonuments();
                deadline.Reset();

                step = 6; Instance.ProcessExtensions(ExtOp.Init);

                if (deadline.Expired)
                {
                    yield return null;
                    deadline.Reset();
                }

                step = 7; Instance.ProcessExtensions(ExtOp.Validate);
                if (deadline.Expired)
                {
                    yield return null;
                    deadline.Reset();
                }

                if (Instance.Buildings.Profiles.Count == 0)
                {
                    step = int.MinValue;
                    gridStopwatch.Stop();
                    CriticalError();
                    yield break;
                }

                var spawnOnSeabed = false;
                var minPos = (int)(World.Size / -2f) + 100;
                var maxPos = (int)(World.Size / 2f) - 100;
                var maxProtectionRadius = -10000f;
                var minProtectionRadius = 10000f;
                var maxWaterDepthSeabed = float.MaxValue;
                var minWaterDepthSeabed = float.MinValue;
                var maxWaterDepth = 0f;
                step = 8;

                foreach (var profile in Instance.Buildings.Profiles.Values)
                {
                    if (profile.Options.Water.Seabed > 0f)
                    {
                        spawnOnSeabed = true;
                        maxWaterDepthSeabed = Mathf.Min(maxWaterDepthSeabed, profile.Options.Water.MaximumSeabedWaterDepth);
                        minWaterDepthSeabed = Mathf.Max(minWaterDepthSeabed, profile.Options.Water.MinimumSeabedWaterDepth);
                    }

                    maxProtectionRadius = Mathf.Max(profile.Options.ProtectionRadii.Max(), maxProtectionRadius);

                    minProtectionRadius = Mathf.Min(profile.Options.ProtectionRadii.Min(), minProtectionRadius);

                    maxWaterDepth = Mathf.Max(maxWaterDepth, profile.Options.Water.WaterDepth);
                }

                if (!config.Settings.Management.AllowOnBeach && !config.Settings.Management.AllowInland && !spawnOnSeabed)
                {
                    step = int.MinValue;
                    gridStopwatch.Stop();
                    CriticalError("Spawn options for beach, inland and seabed are disabled!");
                    yield break;
                }

                using var blockedPositions = config.Settings.Management.BlockedPositions.ToPooledList();
                using var blockedMapPrefabs = DisposableList<(Vector3, float)>();
                using var custom = DisposableList<(string, int)>();
                var zero = blockedPositions.Find(x => x.position == Vector3.zero);

                if (zero == null)
                {
                    blockedPositions.Add(zero = new(Vector3.zero, 200f));
                }

                if (zero.radius < 200f)
                {
                    zero.radius = 200f;
                }

                step = 9;
                var prefabs = config.Settings.Management.BlockedPrefabs.ToDictionary(pair => pair.Key, pair => pair.Value);

                prefabs.Remove("test_prefab");
                prefabs.Remove("test_prefab_2");
                step = 10;

                bool customSpawnsOrBlocksEnabled = prefabs.Count > 0 || Instance.Buildings.Profiles.Exists(x => x.Value.Options.CustomSpawns.Any);

                foreach (var prefab in World.Serialization.world.prefabs)
                {
                    if (deadline.Expired)
                    {
                        yield return null;
                        deadline.Reset();
                    }
                    if (prefab.id == 1724395471 && !string.IsNullOrEmpty(prefab.category))
                    {
                        bool flag = false;
                        foreach (var m in config.Settings.Management.BlockedMonumentMarkers)
                        {
                            if (m == "*" || m.Equals(prefab.category, StringComparison.OrdinalIgnoreCase))
                            {
                                flag = true;
                                break;
                            }
                        }
                        if (!flag)
                        {
                            yield return SpawnsController.CalculateMonumentSize(new(prefab.position.x, prefab.position.y, prefab.position.z), prefab.category);
                            deadline.Reset();
                        }
                    }
                    if (!customSpawnsOrBlocksEnabled || !StringPool.toString.TryGetValue(prefab.id, out var fullname))
                    {
                        continue;
                    }
                    Vector3 v = new(prefab.position.x, prefab.position.y, prefab.position.z);
                    if (prefabs.Count > 0 && prefabs.TryGetValue(GetFileNameWithoutExtension(fullname), out var dist))
                    {
                        blockedMapPrefabs.Add((v, dist));
                    }
                    TryAddCustomSpawn(prefab, fullname, v, custom);
                }

                step = 11;
                float railRadius = Mathf.Max(M_RADIUS * 2f, minProtectionRadius);
                bool hasBlockedMapPrefabs = blockedMapPrefabs.Count > 0;
                bool hasBlockedPositions = blockedPositions.Count > 0;
                progressTotal = Math.Pow((maxPos - minPos) / CELL_SIZE, 2);
                double stepCount = progressTotal / 4.0;
                int stepCounter = 0;

                step = 12;
                progress = 0;

                var instruction = HarmonyModInterface.Mods.CallHook("GetGridWaitTime") is float obj1 ? CoroutineEx.waitForSeconds(obj1) : null;
                var threshold = HarmonyModInterface.Mods.CallHook("GetGridWaitThreshold") is int obj2 ? obj2 : 0;
                var checks = 0;

                if (threshold > 0)
                {
                    deadline.Value = long.MaxValue; // speed run
                }

                for (float x = minPos; x < maxPos; x += CELL_SIZE) // Credits to Jake_Rich for helping me with this!
                {
                    for (float z = minPos; z < maxPos; z += CELL_SIZE)
                    {
                        if (threshold > 0 && ++checks >= threshold)
                        {
                            checks = 0;
                            yield return instruction;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 13;
                        progress++;
                        if (++stepCounter >= stepCount)
                        {
                            Puts($"{Math.Round((progress / progressTotal) * 100.0)}% loaded ({spawns.Spawns.Count + spawns.Seabed.Count} potential points)");
                            stepCounter = 0;
                        }

                        var v = new Vector3(x, 0f, z);

                        step = 14;
                        if (hasBlockedPositions)
                        {
                            bool flag = false;
                            foreach (var a in blockedPositions)
                            {
                                if (InRange2D(v, a.position, a.radius))
                                {
                                    flag = true;
                                    break;
                                }
                            }
                            if (flag)
                            {
                                continue;
                            }
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        v.y = SpawnsController.GetSpawnHeight(v);
                        step = 15;
                        if (hasBlockedMapPrefabs && SpawnsController.IsBlockedByMapPrefab(blockedMapPrefabs, v))
                        {
                            continue;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 16;
                        bool canSpawnOnSeabed = spawnOnSeabed && SpawnsController.InDeepWater(v, true, minWaterDepthSeabed, maxWaterDepthSeabed);

                        if (canSpawnOnSeabed)
                        {
                            v.y = SpawnsController.GetSpawnHeight(v, false);
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 17;

                        if (config.Settings.Management.BlockAtSpawnsDatabase)
                        {
                            bool flag = false;
                            foreach (var (type, rs) in Spawns)
                            {
                                if (rs.IsCustomSpawn && rs.Spawns.Count > 0)
                                {
                                    foreach (var r in rs.Spawns)
                                    {
                                        if (deadline.Expired)
                                        {
                                            yield return null;
                                            deadline.Reset();
                                        }
                                        if (r.Location.Distance(v) <= r.Radius)
                                        {
                                            flag = true;
                                            break;
                                        }
                                    }
                                }
                                if (flag)
                                {
                                    break;
                                }
                            }
                            if (flag)
                            {
                                continue;
                            }
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 18;
                        List<Vector3> close = TerrainMeta.Path?.OceanPatrolClose;
                        if (close != null && close.Count > 0)
                        {
                            bool flag = false;
                            foreach (var b in close)
                            {
                                if (deadline.Expired)
                                {
                                    yield return null;
                                    deadline.Reset();
                                }
                                if (InRange2D(v, b, 100f))
                                {
                                    flag = true;
                                    break;
                                }
                            }
                            if (flag)
                            {
                                continue;
                            }
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 19;
                        List<Vector3> far = TerrainMeta.Path?.OceanPatrolFar;
                        if (far != null && far.Count > 0)
                        {
                            bool flag = false;
                            foreach (var b in far)
                            {
                                if (deadline.Expired)
                                {
                                    yield return null;
                                    deadline.Reset();
                                }
                                if (InRange(v, b, 100f))
                                {
                                    flag = true;
                                    break;
                                }
                            }
                            if (flag)
                            {
                                continue;
                            }
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 20;
                        if (config.Settings.Management.BlockedGrids.Count > 0)
                        {
                            string grid = MapHelper.PositionToString(v);
                            bool isGridBlocked = false;
                            foreach (var blockedGrid in config.Settings.Management.BlockedGrids)
                            {
                                if (deadline.Expired)
                                {
                                    yield return null;
                                    deadline.Reset();
                                }
                                if (grid.Equals(blockedGrid, StringComparison.OrdinalIgnoreCase))
                                {
                                    isGridBlocked = true;
                                    break;
                                }
                            }
                            if (isGridBlocked)
                            {
                                continue;
                            }
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 21;
                        if (SpawnsController.IsZoneBlocked(v))
                        {
                            continue;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 22;
                        areaSafetyQuery.Reset();

                        bool areaSafe;
                        while (!SpawnsController.StepAreaSafe(v, 0f, CELL_SIZE, CELL_SIZE, CELL_SIZE, gridLayers, false, areaSafetyQuery, deadline.Value, out areaSafe))
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        areaSafetyQuery.Reset();

                        if (!areaSafe)
                        {
                            continue;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 23;
                        if (!spawnOnSeabed && SpawnsController.InDeepWater(v, false, 5f, 5f))
                        {
                            continue;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 24;
                        if (SpawnsController.IsMonumentPosition(v, config.Settings.Management.MonumentDistance > 0 ? config.Settings.Management.MonumentDistance : minProtectionRadius))
                        {
                            continue;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 25;
                        int? t = TerrainMeta.BiomeMap?.GetBiomeMaxType(v);
                        if (!SpawnsController.TopologyChecks(null, t, v, minProtectionRadius, railRadius, spawnOnSeabed, false, out _))
                        {
                            continue;
                        }

                        if (!config.Settings.Management.AllowOnRailroads)
                        {
                            int railPathIndex = 0;
                            int railPointIndex = 0;
                            bool blockedByRailPath;

                            while (!SpawnsController.StepRailPathCheck(v, railRadius, deadline.Value, ref railPathIndex, ref railPointIndex, out blockedByRailPath))
                            {
                                yield return null;
                                deadline.Reset();
                            }

                            if (blockedByRailPath)
                            {
                                continue;
                            }
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 26;
                        if (!canSpawnOnSeabed)
                        {
                            float minY = float.MaxValue;
                            float maxY = float.MinValue;
                            const float radius = 15f;
                            const float sampleSpacing = 5f;

                            for (float dx = -radius; dx <= radius; dx += sampleSpacing)
                            {
                                for (float dz = -radius; dz <= radius; dz += sampleSpacing)
                                {
                                    if (dx * dx + dz * dz > radius * radius)
                                    {
                                        continue;
                                    }

                                    Vector3 a = new(v.x + dx, 0f, v.z + dz);
                                    a.y = SpawnsController.GetSpawnHeight(a, true, true);

                                    if (a.y < minY) minY = a.y;
                                    if (a.y > maxY) maxY = a.y;

                                    if (deadline.Expired)
                                    {
                                        yield return null;
                                        deadline.Reset();
                                    }
                                }
                            }

                            if (maxY - minY > M_RADIUS)
                            {
                                continue;
                            }
                        }

                        step = 28;
                        if (EnvironmentManager.Check(v, EnvironmentType.TrainTunnels, 25f))
                        {
#if RBDEBUG
                            Puts("Blocked by train tunnels: {0}", position);
#endif
                            continue;
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        step = 29;
                        RaidableSpawnLocation rsl = new(v)
                        {
                            WaterHeight = Mathf.Max(WaterSystem.OceanLevel, TerrainMeta.WaterMap.GetHeight(v)), //GetWaterOrTerrainSurface
                            TerrainHeight = TerrainMeta.HeightMap.GetHeight(v),
                            SpawnHeight = canSpawnOnSeabed ? v.y : SpawnsController.GetSpawnHeight(v, false),
                            Radius = maxProtectionRadius,
                            biome = t
                        };

                        step = 30;
                        if (canSpawnOnSeabed)
                        {
                            spawns.Seabed.Add(rsl);
                        }
                        else if (rsl.WaterHeight - rsl.SpawnHeight <= maxWaterDepth)
                        {
                            spawns.Spawns.Add(rsl);
                        }
                    }
                }

                step = 0;
                gridStopwatch.Stop();
                Puts(Instance.mx("Initialized Grid", null, Math.Floor(gridStopwatch.Elapsed.TotalSeconds), gridStopwatch.Elapsed.Milliseconds, World.Size, spawns.Spawns.Count));
                if (spawns.Seabed.Count > 0) Puts(Instance.mx("Initialized Grid Sea", null, spawns.Seabed.Count));
                foreach (var (type, amount) in custom) Puts($"Loaded {amount} custom spawns from {type}");
            }

            public void TryAddCustomSpawn(ProtoBuf.PrefabData prefab, string fullname, Vector3 v, List<(string type, int amount)> custom)
            {
                foreach (var (type, profile) in Instance.Buildings.Profiles)
                {
                    if (profile.Options.CustomSpawns.ShouldAdd(profile, prefab, fullname, v))
                    {
                        var index = custom.FindIndex(x => x.type == type);
                        if (index != -1)
                        {
                            custom[index] = (custom[index].type, custom[index].amount + 1);
                        }
                        else
                        {
                            custom.Add((type, 1));
                        }
                    }
                }
            }

            public readonly Dictionary<string, HashSet<RaidableSpawnLocation>> SpawnCache = new();

            public void LoadSpawns()
            {
                Spawns = new();
                Spawns.Add(RaidableType.Grid, new(Instance));

                LoadSpawnsForType(RaidableType.Manual, config.Settings.Manual.SpawnsFile, "LoadedManual");
                LoadSpawnsForType(RaidableType.Scheduled, config.Settings.Schedule.SpawnsFile, "LoadedScheduled");
                LoadSpawnsForType(RaidableType.Maintained, config.Settings.Maintained.SpawnsFile, "LoadedMaintained");
                LoadSpawnsForType(RaidableType.Purchased, config.Settings.Buyable.SpawnsFile, "LoadedBuyable");
            }

            private void LoadSpawnsForType(RaidableType type, string spawnsFile, string key)
            {
                if (!SpawnsFileValid(spawnsFile))
                {
                    return;
                }

                if (!SpawnCache.TryGetValue(spawnsFile, out var spawns))
                {
                    spawns = GetSpawnsLocations(spawnsFile);

                    if (spawns.Count > 0)
                    {
                        SpawnCache[spawnsFile] = spawns;
                    }
                }

                if (spawns.Count > 0)
                {
                    Puts(Instance.mx(key, null, spawns.Count));
                    Spawns[type] = new(Instance, spawns);
                }
            }

            public bool SpawnsFileValid(string spawnsFile)
            {
                if (string.IsNullOrWhiteSpace(spawnsFile) || spawnsFile.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return Instance.DataFileExists(Path.Combine("SpawnsDatabase", spawnsFile));
            }

            public HashSet<RaidableSpawnLocation> GetSpawnsLocations(string spawnsFile)
            {
                try
                {
                    Spawnfile spawnfile = HarmonyDataLayer.ReadObject<Spawnfile>(Path.Combine("SpawnsDatabase", spawnsFile));

                    HashSet<RaidableSpawnLocation> locations = new();

                    foreach (var value in spawnfile.spawnPoints.Values)
                    {
                        locations.Add(new(value.ToString().ToVector3()));
                    }

                    Puts("Loaded {0} spawn locations from file: {1}", locations.Count, spawnsFile);

                    return locations;
                }
                catch
                {
                    Puts("Invalid spawns file: {0}", spawnsFile);

                    return new();
                }
            }
        }

        private class Spawnfile
        {
            public Dictionary<string, object> spawnPoints = new();
        }

        public class QueueController
        {
            internal YieldInstruction instruction0, instruction1;
            internal Queue<RandomBase> queue = new();
            internal DebugMessageBuffer Messages;
            internal Coroutine _coroutine;
            internal int spawnChecks;
            internal bool Paused;
            internal RaidableBases Instance;
            internal const float REMOVE_RADIUS = 15f;
            private Dictionary<RaidableSpawnLocation, SubmergedCache> _submergedCache = new();
            private SpawnsControllerManager.AreaSafetyQuery areaSafetyQuery = new();
            private float _submergedCacheOceanLevel = 0f;
            internal Configuration config => Instance.config;
            internal SpawnsControllerManager SpawnsController => Instance.SpawnsController;
            internal bool Any => queue.Count > 0;

            private void Notify(BasePlayer player, string key, params object[] args) => Instance.SendNotification(player, key, args);

            private string mx(string key, string id = null, params object[] args) => Instance.mx(key, id, args);

            private struct SubmergedCache
            {
                internal float Radius;
                internal float WaterDepth;
                internal bool Result;
            }

            private class PastePlacementQuery
            {
                internal Dictionary<Vector2Int, float> TerrainHeights = new();
                internal List<float> CornerHeights = new();
                internal Vector3 Origin;
                internal float MinHeight;
                internal float MaxHeight;
                internal float FinalHeight;
                internal float MinEntranceHeight;
                internal float MaxEntranceHeight;
                internal float MinEntranceHeightRaw;
                internal float FloatingTotal;
                internal float FloatingMax;
                internal int EntranceCount;
                internal int FloatingCorners;
                internal int Index;
                internal int Stage;
                internal bool HeightInitialized;
                internal bool GroundedFoundationCheck;
                internal bool Started;

                internal void Reset()
                {
                    TerrainHeights.Clear();
                    CornerHeights.Clear();
                    Origin = default;
                    MinHeight = float.MinValue;
                    MaxHeight = float.MaxValue;
                    FinalHeight = 0f;
                    MinEntranceHeight = float.MaxValue;
                    MaxEntranceHeight = float.MinValue;
                    MinEntranceHeightRaw = float.MaxValue;
                    FloatingTotal = 0f;
                    FloatingMax = 0f;
                    EntranceCount = 0;
                    FloatingCorners = 0;
                    Index = 0;
                    Stage = 0;
                    HeightInitialized = false;
                    GroundedFoundationCheck = false;
                    Started = false;
                }

                internal void BeginGroundedFoundationCheck()
                {
                    FloatingTotal = 0f;
                    FloatingMax = 0f;
                    FloatingCorners = 0;
                    GroundedFoundationCheck = true;
                    Index = 0;
                    Stage = 5;
                }
            }

            private PastePlacementQuery pastePlacementQuery = new();

            internal class DebugMessageBuffer
            {
                private const int MaxEntries = 20;
                private const int MaxValues = 5;
                private List<Entry> _entries = new(MaxEntries);
                private RaidableBases _instance;

                internal IPlayer User;

                private class Entry : Pool.IPooled
                {
                    internal List<string> Values = new(MaxValues);
                    internal string Message;
                    internal int Amount;

                    public void EnterPool()
                    {
                        Message = null;
                        Values.Clear();
                        Amount = 0;
                    }

                    public void LeavePool() { }

                    internal void AppendTo(DisposableBuilder sb)
                    {
                        sb.Append(Amount);
                        sb.Append("x - ");
                        sb.Append(Message);
                        if (Values.Count == 0) return;
                        sb.Append(": ");
                        for (int i = 0; i < Values.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(Values[i]);
                        }
                    }

                    internal string Format()
                    {
                        using var sb = DisposableBuilder.Get();
                        AppendTo(sb);
                        return sb.ToString();
                    }
                }

                internal DebugMessageBuffer(RaidableBases instance) => _instance = instance;

                private Entry Track(string message)
                {
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        Entry entry = _entries[i];
                        if (entry.Message != message)
                        {
                            continue;
                        }
                        entry.Amount++;
                        return entry;
                    }

                    if (_entries.Count >= MaxEntries)
                    {
                        Entry removed = _entries[0];
                        _entries.RemoveAt(0);
                        Pool.Free(ref removed);
                    }

                    Entry added = Pool.Get<Entry>();
                    added.Message = message;
                    added.Amount = 1;
                    _entries.Add(added);
                    return added;
                }

                public void Add(string message, object obj = null)
                {
                    if (string.IsNullOrWhiteSpace(message)) return;

                    Entry entry = Track(message);

                    if (obj == null) return;

                    string value = (obj.ToString() ?? string.Empty).Replace("(", "").Replace(")", "").Replace(",", "");

                    if (!entry.Values.Contains(value))
                    {
                        if (entry.Values.Count >= MaxValues) entry.Values.RemoveAt(0);
                        entry.Values.Add(value);
                    }
                }

                public void Clear()
                {
                    for (int i = _entries.Count - 1; i >= 0; i--)
                    {
                        Entry entry = _entries[i];
                        Pool.Free(ref entry);
                    }

                    _entries.Clear();
                }
                public bool Any() => _entries.Count > 0;

                public void PrintAll(IPlayer user = null)
                {
                    if (!_instance.DebugMode || _entries.Count == 0) return;
                    using var sb = DisposableBuilder.Get();
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        if (i > 0) sb.AppendLine();
                        _entries[i].AppendTo(sb);
                    }
                    PrintInternal(user, sb.ToString());
                    Clear();
                }

                private bool PrintInternal(IPlayer user, string message)
                {
                    if (!_instance.DebugMode || string.IsNullOrWhiteSpace(message)) return false;
                    if (_instance.config.LogToFile) _instance.LogToFile("debug", message, _instance, true);
                    if (user == null || user.IsServer) Puts("DEBUG: {0}", message);
                    else user.Reply($"DEBUG: {message}");
                    return true;
                }

                public void Log(string baseName, string message)
                {
                    _instance.Buildings?.Remove(baseName);
                    _instance.IsSpawnerBusy = false;
                    Print(message);
                    Puts(message);
                }

                public bool Print(string message)
                {
                    Print(User, message, null);
                    return false;
                }

                public void Print(string message, object obj) => Print(User, message, obj);

                public void Print(IPlayer user, string message, object obj)
                {
                    if (!PrintInternal(user, obj == null ? message : $"{message}: {obj}")) Add(message, obj);
                }

                public void PrintLast(string id = null)
                {
                    if (_instance.DebugMode && _entries.Count > 0) PrintInternal(User, GetLast(id));
                }

                public string GetLast(string id = null)
                {
                    if (_entries.Count == 0)
                    {
                        return _instance.m("CannotFindPosition", id);
                    }

                    int index = _entries.Count - 1;
                    Entry entry = _entries[index];
                    string message = entry.Format();

                    _entries.RemoveAt(index);
                    Pool.Free(ref entry);

                    return message;
                }
            }

            public QueueController(RaidableBases instance)
            {
                Instance = instance;
                Messages = new(instance);
                spawnChecks = Mathf.Clamp(instance.config.Settings.Management.SpawnChecks, 1, 500);
                instruction0 = CoroutineEx.waitForSeconds(0.1f);
                instruction1 = CoroutineEx.waitForSeconds(1f);
            }

            public void RestartCoroutine()
            {
                StopCoroutine();
                _coroutine = ServerMgr.Instance.StartCoroutine(FindEventPosition());
            }

            public void StopCoroutine()
            {
                if (_coroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_coroutine);
                    _coroutine = null;
                }

                foreach (var rb in queue)
                {
                    rb.payments.Refund();
                }

                queue.Clear();
                _submergedCache.Clear();
                areaSafetyQuery.Clear();
                pastePlacementQuery.Reset();
            }

            public void Add(RandomBase rb)
            {
                if (!queue.Contains(rb))
                {
                    queue.Enqueue(rb);
                }
            }

            private void Spawn(RandomBase rb, Vector3 position)
            {
                if (!Instance.IsUnloading)
                {
                    rb.Position = position;

                    if (Instance.PasteBuilding(rb))
                    {
                        Instance.IsSpawnerBusy = true;
                        rb.IsPasting = true;

                        CompletePurchase(rb);
                        Teleport(rb);
                    }
                }
            }

            private void CompletePurchase(RandomBase rb)
            {
                if (rb.type == RaidableType.Purchased)
                {
                    if (!rb.owner.IsKilled())
                    {
                        var grid = Instance.FormatGridReference(rb.owner, rb.Position);

                        if (rb.owner.HasPermission("raidablebases.despawn.buyraid"))
                        {
                            if (config.Settings.Buyable.Refunds.Enabled)
                            {
                                Notify(rb.owner, "BuyRefundableBaseSpawnedAt", rb.Position, grid, config.Settings.EventCommand, config.Settings.Buyable.Refunds.Percentage);
                            }
                            else Notify(rb.owner, "BuyCancellationsBaseSpawnedAt", rb.Position, grid, config.Settings.EventCommand);
                        }
                        else Notify(rb.owner, "BuyBaseSpawnedAt", rb.Position, grid);

                        if (config.EventMessages.AnnounceBuy)
                        {
                            foreach (var target in BasePlayer.activePlayerList)
                            {
                                if (target != rb.owner && target.HasPermission("raidablebases.limitedannouncements")) continue;
                                Notify(target, "BuyBaseAnnouncement", rb.owner.displayName, rb.Position, Instance.FormatGridReference(target, rb.Position));
                            }
                        }
                    }

                    Puts(mx("BuyBaseAnnouncementConsole", null, rb.username, rb.options.Mode, rb.BaseName, rb.Position, Instance.PositionToGrid(rb.Position, false)));

                    config.Settings.Buyable.Cooldowns.Process(Instance, rb.alliance, rb.userid, rb.clan, rb.options.Mode, set: false);
                }
            }

            private void Teleport(RandomBase rb)
            {
                if (rb.user != null && rb.user.IsAdmin && rb.user.IsConnected && !rb.IsTeleportPending(rb.user.Player(), rb.Position))
                {
                    rb.user.Teleport(rb.Position.x, rb.Position.y, rb.Position.z);
                }
            }

            private bool CanBypassPause(RandomBase rb)
            {
                if (rb.type == RaidableType.Purchased)
                {
                    return rb.userid.HasPermission("raidablebases.canbypass");
                }
                if (rb.type == RaidableType.Manual)
                {
                    return rb.user != null;
                }
                return false;
            }

            private IEnumerator FindEventPosition()
            {
                int checks = 0;
                FrameDeadline deadline = new(searchFrameBudgetMilliseconds);

                while (!Instance.IsUnloading)
                {
                    bool delay = ++checks >= spawnChecks;
                    if (delay || deadline.Expired)
                    {
                        checks = 0;
                        yield return delay ? instruction0 : null;
                        deadline.Reset();
                    }

                    if (!queue.TryPeek(out var spq))
                    {
                        yield return instruction1;
                        deadline.Reset();
                        continue;
                    }

                    if (Instance.Buildings.Removed.Contains(spq.BaseName))
                    {
                        if (spq.type == RaidableType.Scheduled)
                        {
                            Instance.data.RaidTime = DateTime.Now.AddSeconds(1f);
                        }
                        if (spq.type == RaidableType.Purchased)
                        {
                            spq.payments.Refund();
                        }
                        queue.Dequeue();
                        continue;
                    }

                    if (spq.Position != Vector3.zero)
                    {
                        queue.Dequeue();
                        Spawn(spq, spq.Position);
                        yield return instruction1;
                        deadline.Reset();
                        CheckSpawner(spq);
                        continue;
                    }

                    if (Instance.ZoneManager != null)
                    {
                        SpawnsController.SetupZones(false);
                    }

                    spq.spawns.Check();
                    spq.IsWaterSpawn = spq.options.Water.Random;
                    spq.TrySortByDistance();
                    Instance.ParseListedOptions(spq);

                    if (Instance.DebugMode && !spq.HasSpawns())
                    {
                        int cached = 0;

                        foreach (var cache in spq.spawns.Cached.Values)
                        {
                            cached += cache.Count;
                        }

                        Messages.Print($"{spq.type}: No available spawn points, amount cached:", cached);
                    }

                    if (Instance.DebugMode && Instance.IsSpawnerBusy)
                    {
                        Messages.Print($"{spq.type}: Waiting for the current base to finish spawning");
                    }

                    while (spq.HasSpawns())
                    {
                        delay = ++checks >= spawnChecks;
                        if (delay || deadline.Expired)
                        {
                            checks = 0;
                            yield return delay ? instruction0 : null;
                            deadline.Reset();
                        }

                        if (Instance.IsSpawnerBusy || Paused && !CanBypassPause(spq))
                        {
                            yield return instruction1;
                            deadline.Reset();
                            continue;
                        }

                        spq.attempts++;

                        var rsl = spq.spawns.GetRandom(spq, spq.isBuyableEvent);

                        if (rsl == null)
                        {
                            Messages.Add("Spawn selection returned no location");
                            break;
                        }

                        spq.precisePlacement = false;
                        var v = rsl.Location;
                        bool rejected = !TopologyChecks(spq, rsl.biome, v, Mathf.Max(M_RADIUS * 2f, spq.protectionRadius));

                        if (rejected)
                        {
                            continue;
                        }

                        v.y = GetAdjustedHeight(spq, v);
                        rejected = IsTooClose(spq, v);

                        if (rejected)
                        {
                            continue;
                        }

                        if (IsAreaManuallyBlocked(spq, v))
                        {
                            continue;
                        }

                        if (CanSpawnCustom(spq, spq.type, v, spq.options.CustomSpawns.Ignore, spq.options.CustomSpawns.SafeRadius))
                        {
                            yield return instruction1;
                            deadline.Reset();
                            break;
                        }

                        if (CanSpawnCustom(spq, RaidableType.Maintained, v, config.Settings.Maintained.Ignore, config.Settings.Maintained.SafeRadius))
                        {
                            yield return instruction1;
                            deadline.Reset();
                            break;
                        }

                        if (CanSpawnCustom(spq, RaidableType.Scheduled, v, config.Settings.Schedule.Ignore, config.Settings.Schedule.SafeRadius))
                        {
                            yield return instruction1;
                            deadline.Reset();
                            break;
                        }

                        if (CanSpawnCustom(spq, RaidableType.Purchased, v, config.Settings.Buyable.Ignore, config.Settings.Buyable.SafeRadius))
                        {
                            yield return instruction1;
                            deadline.Reset();
                            break;
                        }

                        rejected = IsSubmerged(spq, rsl, v);

                        if (rejected)
                        {
                            continue;
                        }

                        if (!spq.pasteData.valid)
                        {
                            yield return SetupCopyPasteRadius(spq);
                            deadline.Reset();
                        }

                        if (spq.invalid)
                        {
                            Instance.Buildings.Remove(spq.BaseName);
                            break;
                        }

                        if (Instance.Buildings.Removed.Contains(spq.BaseName))
                        {
                            break;
                        }

                        bool precisePlacement = UsesPrecisePlacement(spq);

                        areaSafetyQuery.Reset();

                        bool areaSafe;
                        while (!SpawnsController.StepAreaSafe(rsl.Location, spq.ignoreRadius, spq.safeRadius, spq.buildRadius, spq.pasteData.radius, queueLayers, spq.spawns.IsCustomSpawn, areaSafetyQuery, deadline.Value, out areaSafe, spq.type, spq.options.CustomSpawns))
                        {
                            yield return null;
                            deadline.Reset();
                        }

                        CacheType cacheType = areaSafetyQuery.Result;
                        areaSafetyQuery.Reset();

                        if (!areaSafe)
                        {
                            CacheUnsafeArea(spq, rsl, cacheType);
                            continue;
                        }

                        if (!spq.spawns.IsCustomSpawn && SpawnsController.IsZoneBlocked(v))
                        {
                            Messages.Add("Blocked by a ZoneManager zone", v);
                            continue;
                        }

                        if (precisePlacement)
                        {
                            pastePlacementQuery.Reset();

                            bool fitsTerrain;
                            Vector3 pastePosition;
                            while (!StepResolvePastePosition(spq, v, pastePlacementQuery, deadline.Value, out fitsTerrain, out pastePosition))
                            {
                                yield return null;
                                deadline.Reset();
                            }

                            pastePlacementQuery.Reset();

                            if (!fitsTerrain)
                            {
                                Messages.Add("The base footprint does not fit the terrain", v);
                                continue;
                            }

                            spq.precisePlacement = true;
                            v = pastePosition;
                        }

                        Spawn(spq, v);
                        yield return instruction1;
                        deadline.Reset();
                        break;
                    }

                    queue.Dequeue();
                    CheckSpawner(spq);
                }

                _coroutine = null;
            }

            private void CheckSpawner(RandomBase spq)
            {
                if (!spq.IsPasting)
                {
                    if (spq.type == RaidableType.Manual)
                    {
                        if (spq.user == null || spq.user.IsServer)
                        {
                            Puts(mx("CannotFindPosition"));
                        }
                        else
                        {
                            Notify(spq.user.Player(), Instance.Queues.Messages.GetLast(spq.user.Id));
                        }
                    }

                    if (spq.type == RaidableType.Purchased)
                    {
                        spq.payments.Refund();
                        Notify(spq.owner, Instance.mx("CannotFindPosition", spq.id));
                    }

                    spq.spawns.TryAddRange();
                    Messages.PrintAll();
                    Instance.data.Cycle.Add(spq.type, spq.options.Mode, spq.BaseName, spq.owner);
                }
            }

            private IEnumerator SetupCopyPasteRadius(RandomBase spq)
            {
                yield return Instance.SetupCopyPasteObstructionRadius(spq.options.NPC, spq.BaseName, spq.options.ProtectionRadii.Obstruction == -1 ? 0f : GetObstructionRadius(spq.options.ProtectionRadii, RaidableType.None));
            }

            private void CacheUnsafeArea(RandomBase spq, RaidableSpawnLocation rsl, CacheType cacheType)
            {
                if (spq.IsWaterSpawn)
                {
                    cacheType = CacheType.Seabed;
                }

                if (cacheType == CacheType.Delete)
                {
                    spq.spawns.Remove(rsl, cacheType);
                }
                else
                {
                    spq.spawns.RemoveNear(rsl.Location, REMOVE_RADIUS, cacheType, spq.type);
                }
            }

            internal bool IsSubmerged(RandomBase spq, RaidableSpawnLocation rsl, Vector3 v)
            {
                if (spq.spawns.IsCustomSpawn || spq.options.Setup.Sky || spq.options.Setup.ForcedHeight != -1 || spq.options.Water.Seabed > 0f)
                {
                    return false;
                }

                float oceanLevel = WaterSystem.OceanLevel;
                if (_submergedCacheOceanLevel != oceanLevel)
                {
                    _submergedCacheOceanLevel = oceanLevel;
                    _submergedCache.Clear();
                }

                var options = spq.options.Water;
                rsl.WaterHeight = Mathf.Max(oceanLevel, TerrainMeta.WaterMap.GetHeight(rsl.Location));

                if (rsl.WaterHeight - rsl.TerrainHeight > options.WaterDepth)
                {
                    if (!options.AllowSubmerged)
                    {
                        Messages.Add("Water depth exceeds the configured limit", v);
                        return true;
                    }

                    rsl.Location.y = rsl.WaterHeight;
                }

                if (options.AllowSubmerged || !options.SubmergedAreaCheck)
                {
                    return false;
                }

                if (_submergedCache.TryGetValue(rsl, out var cached) && cached.Radius == rsl.Radius && cached.WaterDepth == options.WaterDepth)
                {
                    if (cached.Result)
                    {
                        Messages.Add("Surrounding water depth exceeds the configured limit", v);
                    }
                    return cached.Result;
                }

                bool result = SpawnsController.IsSubmerged(options, rsl, rsl.Radius);
                _submergedCache[rsl] = new()
                {
                    Radius = rsl.Radius,
                    WaterDepth = options.WaterDepth,
                    Result = result
                };

                if (result)
                {
                    Messages.Add("Surrounding water depth exceeds the configured limit", v);
                }

                return result;
            }

            private bool CanSpawnCustom(RandomBase spq, RaidableType type, Vector3 v, bool ignore, float radius)
            {
                if (spq.type == type && spq.spawns.IsCustomSpawn && (ignore || radius > 0f))
                {
                    if (radius <= 0f)
                    {
                        Messages.Add($"{spq.type} custom spawn bypassed area checks", v);
                        Spawn(spq, v);
                        return true;
                    }
                    else spq.ignoreRadius = radius;
                }
                return false;
            }

            private bool IsTooClose(RandomBase spq, Vector3 v)
            {
                if (spq.typeDistance > 0 && Instance.IsTooClose(v, spq.typeDistance))
                {
                    spq.spawns.RemoveNear(v, REMOVE_RADIUS, CacheType.Close, spq.type);
                    Messages.Add("Too close to another raid (Spawn Bases X Distance Apart)", v);
                    return true;
                }
                return false;
            }

            internal bool IsAreaManuallyBlocked(RandomBase spq, Vector3 v)
            {
                if (spq.spawns.IsCustomSpawn)
                {
                    return false;
                }

                var blockedPositions = config.Settings.Management.BlockedPositions;

                for (int i = 0; i < blockedPositions.Count; i++)
                {
                    var blocked = blockedPositions[i];

                    if (!InRange2D(v, blocked.position, blocked.radius))
                    {
                        continue;
                    }

                    spq.spawns.RemoveNear(v, REMOVE_RADIUS, CacheType.Close, spq.type);
                    Messages.Add("Blocked by a configured spawn-block position", v);
                    return true;
                }

                return false;
            }

            //Custom spawn heights, forced heights, Sky and water-surface placement retain their existing behavior.
            //IgnoreFlatTerrain remains a seabed opt-out; normal terrain placement uses the cached footprint.
            public bool UsesPrecisePlacement(RandomBase rb)
            {
                return !rb.isCustomSpawn && !rb.options.Setup.Sky && rb.options.Setup.ForcedHeight == -1f && (!rb.IsWaterSpawn || (!rb.options.Water.Surface && !rb.options.Water.IgnoreFlatTerrain));
            }

            private bool StepResolvePastePosition(RandomBase rb, Vector3 candidate, PastePlacementQuery query, long deadline, out bool accepted, out Vector3 resolved)
            {
                accepted = false;
                resolved = candidate;
                PasteData data = rb.pasteData;

                if (!query.Started)
                {
                    if (deadline > 0L && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    query.Reset();
                    query.Started = true;
                    query.MaxHeight = data.FloorsAreFoundations ? float.MinValue : float.MaxValue;
                    query.Origin = candidate - data.centerOffset;
                    query.Origin.y = 0f;
                }

                while (query.Stage < 4)
                {
                    List<Vector3> points;
                    float contactOffset = 0f;

                    switch (query.Stage)
                    {
                        case 0:
                            points = data.foundationCorners;
                            break;
                        case 1:
                            points = data.importantEntities;
                            break;
                        case 2:
                            points = data.stairs;
                            contactOffset = StairContactOffset;
                            break;
                        default:
                            points = data.ramps;
                            contactOffset = RampContactOffset;
                            break;
                    }

                    while (query.Index < points.Count)
                    {
                        if (deadline > 0L && Stopwatch.GetTimestamp() >= deadline)
                        {
                            return false;
                        }

                        Vector3 local = points[query.Index++];

                        if (query.Stage >= 2)
                        {
                            float maxFoundationY = data.maxFoundationY;
                            var maxY = data.FloorsAreFoundations ? maxFoundationY + 0.5f : maxFoundationY;
                            var minY = data.minFoundationY - (data.FloorsAreFoundations ? contactOffset : contactOffset + 0.5f);

                            if (local.y > maxY || local.y < minY)
                                continue;
                        }

                        Vector3 sample = query.Origin + local;
                        //Preserve the selected surface height so elevated map prefabs remain above the ray origin.
                        sample.y = candidate.y;
                        var cacheKey = new Vector2Int(Mathf.RoundToInt(sample.x * 4f), Mathf.RoundToInt(sample.z * 4f));

                        if (!query.TerrainHeights.TryGetValue(cacheKey, out float groundHeight))
                        {
                            //pasteLayers replaced with targetMask. Default layer map surfaces are valid and GetSpawnHeight still filters invisible colliders which need to fallback on the Default layer for floating islands or other custom map prefabs.
                            groundHeight = Instance.GetSpawnHeight(sample, rb.type == RaidableType.Manual, false, targetMask, rb.admin);
                            query.TerrainHeights[cacheKey] = groundHeight;
                        }

                        switch (query.Stage)
                        {
                            case 0:
                                float cornerHeight = groundHeight - local.y + data.foundationClearance;
                                query.CornerHeights.Add(cornerHeight);
                                if (data.FloorsAreFoundations ? cornerHeight > query.MaxHeight : cornerHeight < query.MaxHeight)
                                {
                                    query.MaxHeight = cornerHeight;
                                }
                                break;
                            case 1:
                                float requiredHeight = groundHeight - local.y;
                                if (requiredHeight > query.MinHeight)
                                {
                                    query.MinHeight = requiredHeight;
                                }
                                break;
                            default:
                                float rawHeight = groundHeight - local.y;
                                float entranceHeight = rawHeight - contactOffset;
                                query.EntranceCount++;
                                if (entranceHeight < query.MinEntranceHeight) query.MinEntranceHeight = entranceHeight;
                                if (rawHeight > query.MaxEntranceHeight) query.MaxEntranceHeight = rawHeight;
                                if (rawHeight < query.MinEntranceHeightRaw) query.MinEntranceHeightRaw = rawHeight;
                                break;
                        }
                    }

                    query.Stage++;
                    query.Index = 0;
                }

                if (query.CornerHeights.Count == 0 || query.MaxHeight == float.MaxValue || query.MaxHeight == float.MinValue)
                {
                    return true;
                }

                if (!query.HeightInitialized)
                {
                    query.MinHeight = query.MinHeight == float.MinValue ? query.MaxHeight - 1f : query.MinHeight;

                    if (query.MinHeight > query.MaxHeight)
                    {
                        query.FinalHeight = query.MinHeight - ImportantEntityBurialAllowance;
                    }
                    else
                    {
                        float suggestedBand = query.MaxHeight - query.MinHeight; // Fixes issue with suggestedHeight when importantPrefabs are high above foundations, the base sank 2/3 of that band
                        if (suggestedBand > data.foundationClearance + SuggestedBandAboveClearance)
                            suggestedBand = 1f;

                        float suggestedHeight = query.MaxHeight - suggestedBand * 2f / 3f;
                        query.FinalHeight = suggestedHeight;

                        if (query.EntranceCount > 0 && query.MaxEntranceHeight >= query.MinEntranceHeight)
                        {
                            if (query.EntranceCount == 1 && query.MaxEntranceHeight <= query.MaxHeight)
                            {
                                query.FinalHeight = query.MaxEntranceHeight >= query.MinHeight ? query.MaxEntranceHeight : query.MinHeight - ImportantEntityBurialAllowance;
                            }
                            else if (query.EntranceCount > 1)
                            {
                                if (query.MinEntranceHeightRaw >= query.MinHeight && query.MinEntranceHeightRaw <= query.MaxHeight)
                                {
                                    query.FinalHeight = query.MinEntranceHeightRaw;
                                }
                                else
                                {
                                    float middleHeight = (query.MaxEntranceHeight - query.MinEntranceHeight) / 2f + query.MinEntranceHeight;
                                    if (middleHeight >= query.MinHeight && middleHeight <= query.MaxHeight)
                                    {
                                        query.FinalHeight = middleHeight;
                                    }
                                }
                            }
                            else if (query.EntranceCount == 1 && query.MaxEntranceHeight > query.MaxHeight)
                            {
                                query.FinalHeight = query.MaxHeight;
                            }
                        }
                    }

                    query.HeightInitialized = true;
                    query.Stage = 5;
                    query.Index = 0;
                }

                while (true)
                {
                    int cornerCount = query.GroundedFoundationCheck ? data.groundedFoundationCornerCount : query.CornerHeights.Count;

                    if (query.Stage == 5)
                    {
                        while (query.Index < cornerCount)
                        {
                            if (deadline > 0L && Stopwatch.GetTimestamp() >= deadline)
                            {
                                return false;
                            }

                            float gap = query.FinalHeight - query.CornerHeights[query.Index++];
                            if (gap <= 0f)
                            {
                                continue;
                            }

                            query.FloatingCorners++;
                            query.FloatingTotal += gap;
                            if (gap > query.FloatingMax)
                            {
                                query.FloatingMax = gap;
                            }
                        }

                        query.Stage = 6;
                    }

                    if (!RejectsFloating(query.FloatingCorners, query.FloatingTotal, query.FloatingMax, cornerCount))
                    {
                        break;
                    }

                    //The normal resolver keeps every foundation involved so the base remains as low as possible.
                    //Only a rejected multi-level footprint gets a second floating check, at the same resolved height,
                    //using the dominant terrain-contact band. This exemption can make the position pass, but cannot
                    //raise the base or create extra crawl space beneath upper foundations.
                    if (query.GroundedFoundationCheck || !data.HasFloatingFoundationFallback)
                    {
                        return true;
                    }

                    query.BeginGroundedFoundationCheck();
                }

                //Keeps rb.baseHeight = 1.0f (PasteOptions "height") as the normalizer like it used to.
                float offset = rb.type == RaidableType.Manual && !rb.pasteData.UsesWaterBasesFoundations ? 2.0f : 1.0f;
                query.Origin.y = query.FinalHeight - offset + rb.baseHeight + rb.options.Setup.PasteHeightAdjustment;
                resolved = query.Origin;
                accepted = true;
                return true;
            }

            private static bool RejectsFloating(int floatingCorners, float floatingTotal, float floatingMax, int cornerCount)
            {
                if (floatingCorners <= FloatingCornerGraceCount || cornerCount <= 0)
                {
                    return false;
                }

                return floatingMax > FloatingCornerMax || floatingTotal / floatingCorners > FloatingCornerAverageMax || floatingCorners * 100f / cornerCount > FloatingCornerPercentMax;
            }

            internal IEnumerator TryResolvePrecisePlacement(RandomBase rb, Vector3 candidate)
            {
                if (!rb.pasteData.valid)
                {
                    yield return SetupCopyPasteRadius(rb);
                }

                if (rb.invalid)
                {
                    yield break;
                }

                PastePlacementQuery query = new();
                FrameDeadline deadline = new(1, 5);
                bool accepted;
                Vector3 resolved;

                while (!StepResolvePastePosition(rb, candidate, query, deadline.Value, out accepted, out resolved))
                {
                    yield return null;
                    deadline.Reset();
                }

                if (accepted)
                {
                    rb.Position = resolved;
                    rb.precisePlacement = true;
                }
            }

            private float GetAdjustedHeight(RandomBase spq, Vector3 v)
            {
                if (spq.options.Setup.ForcedHeight != -1)
                {
                    return spq.options.Setup.PasteHeightAdjustment + spq.options.Setup.ForcedHeight;
                }
                if (spq.IsWaterSpawn && spq.options.Water.Surface)
                {
                    return WaterSystem.OceanLevel + spq.options.Setup.PasteHeightAdjustment;
                }
                return v.y + spq.options.Setup.PasteHeightAdjustment;
            }

            private bool TopologyChecks(RandomBase spq, int? t, Vector3 v, float railRadius)
            {
                if (!spq.spawns.IsCustomSpawn && !SpawnsController.TopologyChecks(spq.options.Biomes, t, v, spq.protectionRadius, railRadius, spq.IsWaterSpawn, true, out var topology))
                {
                    spq.spawns.RemoveNear(v, REMOVE_RADIUS, CacheType.Delete, spq.type);
                    Messages.Add(topology.EndsWith("disabled") ? topology : $"Blocked by {topology} topology", v);
                    return false;
                }
                return true;
            }
        }

        public class AutomatedController
        {
            internal YieldInstruction instruction0, instruction1, instruction5, instruction15;
            internal Coroutine _maintainedCoroutine, _scheduledCoroutine;
            internal bool IsMaintainedEnabled, IsScheduledEnabled;
            internal RaidableBases Instance;
            internal int _maxOnce;
            internal double DelayUntilNextSpawn;

            internal StoredData data => Instance.data;
            internal Configuration config => Instance.config;

            public AutomatedController(RaidableBases instance, bool a, bool b)
            {
                instruction0 = CoroutineEx.waitForSeconds(0.0025f);
                instruction1 = CoroutineEx.waitForSeconds(1f);
                instruction5 = CoroutineEx.waitForSeconds(5f);
                instruction15 = CoroutineEx.waitForSeconds(15f);
                Instance = instance;
                IsMaintainedEnabled = a;
                IsScheduledEnabled = b;
            }

            public void DestroyMe()
            {
                StopCoroutine(RaidableType.Scheduled);
                StopCoroutine(RaidableType.Maintained);
            }

            public void StopCoroutine(RaidableType type, IPlayer user = null)
            {
                if (type == RaidableType.Scheduled && _scheduledCoroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_scheduledCoroutine);
                    Instance.Reply(user, "ReloadScheduleCo");
                    _scheduledCoroutine = null;
                }
                else if (type == RaidableType.Maintained && _maintainedCoroutine != null)
                {
                    ServerMgr.Instance.StopCoroutine(_maintainedCoroutine);
                    Instance.Reply(user, "ReloadMaintainCo");
                    _maintainedCoroutine = null;
                }
            }

            public void StartCoroutine(RaidableType type, IPlayer user = null)
            {
                StopCoroutine(type, user);

                if (type == RaidableType.Scheduled ? !IsScheduledEnabled || config.Settings.Schedule.Max <= 0 : !IsMaintainedEnabled || config.Settings.Maintained.Max <= 0)
                {
                    return;
                }

                if (Instance.IsGridLoading() || !Instance.CanContinueAutomation(type))
                {
                    Instance.timer.Once(1f, () => StartCoroutine(type));
                    return;
                }

                if (type == RaidableType.Scheduled && data.RaidTime == DateTime.MinValue)
                {
                    ScheduleNextAutomatedEvent();
                }

                if (type == RaidableType.Scheduled)
                {
                    Instance.timer.Once(0.2f, () => _scheduledCoroutine = ServerMgr.Instance.StartCoroutine(ScheduleCoroutine()));
                }
                else Instance.timer.Once(0.2f, () => _maintainedCoroutine = ServerMgr.Instance.StartCoroutine(MaintainCoroutine()));
            }

            private IEnumerator MaintainCoroutine()
            {
                float timeBetweenSpawns = Mathf.Max(1f, config.Settings.Maintained.Time);

                while (!Instance.IsUnloading)
                {
                    if (!CanSpawn(RaidableType.Maintained, config.Settings.Maintained.GetPlayerCount(), config.Settings.Maintained.PlayerLimitMin, config.Settings.Maintained.PlayerLimitMax, config.Settings.Maintained.Max, false))
                    {
                        yield return instruction5;
                    }
                    else if (!Instance.Queues.Any)
                    {
                        yield return ProcessEvent(RaidableType.Maintained, timeBetweenSpawns);
                    }
                    else if (Instance.Queues.Messages.Any())
                    {
                        Instance.Queues.Messages.PrintLast();
                    }

                    yield return instruction5;
                }

                _maintainedCoroutine = null;
            }

            private IEnumerator ScheduleCoroutine()
            {
                float timeBetweenSpawns = Mathf.Max(1f, config.Settings.Schedule.Time);

                while (!Instance.IsUnloading)
                {
                    if (CanSpawn(RaidableType.Scheduled, config.Settings.Schedule.GetPlayerCount(), config.Settings.Schedule.PlayerLimitMin, config.Settings.Schedule.PlayerLimitMax, config.Settings.Schedule.Max, true))
                    {
                        while (Instance.Get(RaidableType.Scheduled) < config.Settings.Schedule.Max && MaxOnce())
                        {
                            if (SaveRestore.IsSaving)
                            {
                                Instance.Queues.Messages.Print("Scheduled: Server saving");
                                yield return instruction15;
                            }
                            else if (!Instance.Queues.Any)
                            {
                                yield return ProcessEvent(RaidableType.Scheduled, timeBetweenSpawns);
                            }
                            else if (Instance.Queues.Messages.Any())
                            {
                                Instance.Queues.Messages.PrintLast();
                            }

                            yield return instruction1;
                        }

                        yield return CoroutineEx.waitForSeconds(ScheduleNextAutomatedEvent());
                    }

                    yield return instruction5;
                }

                _scheduledCoroutine = null;
            }

            private IEnumerator ProcessEvent(RaidableType type, float timeBetweenSpawns)
            {
                string mode;
                if (!IsModeValid(mode = Instance.GetRandomDifficulty(type)))
                {
                    Instance.Queues.Messages.PrintLast();
                    yield return instruction1;
                    yield break;
                }

                Instance.SpawnRandomBase(type, mode);
                yield return instruction1;
                //yield return new WaitWhile(() => Instance.Queues.Any);

                if (!Instance.IsSpawnerBusy)
                {
                    yield break;
                }

                if (type == RaidableType.Scheduled)
                {
                    _maxOnce++;
                }

                Instance.Queues.Messages.Print($"{type}: Waiting for base to be setup", Instance.IsBusy(out var pastedLocation) ? pastedLocation : (object)null);
                yield return new WaitWhile(() => Instance.IsSpawnerBusy);

                Instance.Queues.Messages.Print($"{type}: Waiting {timeBetweenSpawns} seconds");
                yield return CoroutineEx.waitForSeconds(timeBetweenSpawns);
            }

            private float ScheduleNextAutomatedEvent()
            {
                var raidInterval = Core.Random.Range(config.Settings.Schedule.IntervalMin, config.Settings.Schedule.IntervalMax + 1);

                _maxOnce = 0;
                data.RaidTime = DateTime.Now.AddSeconds(raidInterval);
                Puts(Instance.mx("Next Automated Raid", null, Instance.FormatTime(raidInterval, null), data.RaidTime));
                Instance.Queues.Messages.Print("Scheduled next automated event");

                return (float)raidInterval;
            }

            private bool MaxOnce()
            {
                return config.Settings.Schedule.MaxOnce <= 0 || _maxOnce < config.Settings.Schedule.MaxOnce;
            }

            private bool CanSpawn(RaidableType type, int onlinePlayers, int playerLimit, int playerLimitMax, int maxEvents, bool checkRaidTime)
            {
                if (DelayUntilNextSpawn > 0 && DelayUntilNextSpawn > Time.timeAsDouble)
                {
                    return false;
                }
                DelayUntilNextSpawn = 0d;
                if (onlinePlayers < playerLimit)
                {
                    return Instance.Queues.Messages.Print($"{type}: Insufficient amount of players online {onlinePlayers}/{playerLimit}");
                }
                else if (onlinePlayers > playerLimitMax)
                {
                    return Instance.Queues.Messages.Print($"{type}: Too many players online {onlinePlayers}/{playerLimitMax}");
                }
                else if (Instance.IsLoaderBusy(out var str))
                {
                    return Instance.Queues.Messages.Print($"{type}: Waiting for a base to finish its task: {str}");
                }
                else if (Instance.IsSpawnerBusy)
                {
                    return Instance.Queues.Messages.Print($"{type}: Waiting for the spawner to finish its task");
                }
                else if (maxEvents > 0 && Instance.Get(type) >= maxEvents)
                {
                    return Instance.Queues.Messages.Print($"{type}: The max amount of events are spawned");
                }
                else if (checkRaidTime && Instance.GridController.GetRaidTime() > 0)
                {
                    return Instance.Queues.Messages.Print($"{type}: Waiting on timer for next event");
                }
                else if (SaveRestore.IsSaving)
                {
                    return Instance.Queues.Messages.Print($"{type}: Server saving");
                }
                else if (!Instance.IsPasteEngineReady(out var error))
                {
                    return Instance.Queues.Messages.Print(error);
                }

                return true;
            }
        }

        public class BMGELEVATOR : FacepunchBehaviour // credits: bmgjet
        {
            internal Elevator _elevator;
            internal RaycastHit hit;
            internal BaseEntity hitEntity;
            internal RaidableBase raid;
            internal BuildingOptionsElevators options;
            internal bool HasButton;
            internal int currentFloor;
            internal int returnDelay = 60;
            internal float Floors;
            internal const float _LiftSpeedPerMetre = 3f;
            internal RaidableBases env;
            private int _downRetryAttempts;
            private const int MaxDownRetries = 5;

            internal int GetMaxFloors() => (int)(Floors / 3f);
            internal int CurrentFloor
            {
                get => currentFloor;
                set => currentFloor = Mathf.Clamp(value, 0, GetMaxFloors());
            }

            private void Awake()
            {
                _elevator = GetComponent<Elevator>();
                _elevator.LiftSpeedPerMetre = _LiftSpeedPerMetre;
            }

            private void OnDestroy()
            {
                _elevator.SafelyKill();
                try { CancelInvoke(); } catch { }
            }

            private Vector3 GetWorldSpaceFloorPosition(int targetFloor)
            {
                int num = _elevator.Floor - targetFloor;
                Vector3 b = Vector3.up * ((float)num * _elevator.FloorHeight);
                b.y -= 1f;
                return base.transform.position - b;
            }

            public void GoToFloor(Elevator.Direction Direction = Elevator.Direction.Down, bool FullTravel = false, int forcedFloor = -1)
            {
                if (!GetElevatorLift(_elevator, out var elevatorLift))
                {
                    return;
                }

                if (_elevator.HasFlag(BaseEntity.Flags.Busy))
                {
                    return;
                }

                var serverPosition = elevatorLift.transform.position;
                int maxFloors = GetMaxFloors();
                if (forcedFloor != -1)
                {
                    int targetFloor = Mathf.RoundToInt((forcedFloor - serverPosition.y) / 3);
                    if (targetFloor == 0 && CurrentFloor == 0) { targetFloor = maxFloors; }
                    else if (targetFloor == 0 && CurrentFloor == maxFloors) { targetFloor = -maxFloors; }
                    CurrentFloor += targetFloor;

                    if (CurrentFloor > maxFloors) { CurrentFloor = maxFloors; }

                    if (CurrentFloor < 0) { CurrentFloor = 0; }
                }
                else
                {
                    if (Direction == Elevator.Direction.Up)
                    {
                        CurrentFloor++;
                        if (FullTravel) CurrentFloor = (int)(Floors / _elevator.FloorHeight);
                        if ((CurrentFloor * 3) > Floors) CurrentFloor = (int)(Floors / _elevator.FloorHeight);
                    }
                    else
                    {
                        if (GamePhysics.CheckSphere(serverPosition - new Vector3(0, 1f, 0), 0.5f, Layers.Mask.Construction | Layers.Server.Deployed, QueryTriggerInteraction.Ignore))
                        {
                            ScheduleDownRetry();
                            return;
                        }

                        CurrentFloor--;
                        if (CurrentFloor < 0 || FullTravel) CurrentFloor = 0;
                    }
                }
                Vector3 worldSpaceFloorPosition = GetWorldSpaceFloorPosition(CurrentFloor);
                if (!GamePhysics.LineOfSight(serverPosition, worldSpaceFloorPosition, 2097152))
                {
                    if (Direction == Elevator.Direction.Up)
                    {
                        if (!Physics.Raycast(serverPosition, Vector3.up, out hit, 21f) || (hitEntity = hit.GetEntity()).IsNull())
                        {
                            return;
                        }
                        CurrentFloor = (int)(hitEntity.transform.position.Distance(_elevator.transform.position) / 3);
                        worldSpaceFloorPosition = GetWorldSpaceFloorPosition(CurrentFloor);
                    }
                    else
                    {
                        if (!Physics.Raycast(serverPosition - new Vector3(0, 2.9f, 0), Vector3.down, out hit, 21f) || (hitEntity = hit.GetEntity()).IsNull() || hitEntity.ShortPrefabName == "foundation" || hitEntity.ShortPrefabName == "elevator.static")
                        {
                            ScheduleDownRetry();
                            return;
                        }
                        CurrentFloor = (int)(hitEntity.transform.position.Distance(_elevator.transform.position) / 3) + 1;
                        worldSpaceFloorPosition = GetWorldSpaceFloorPosition(CurrentFloor);
                    }
                }
                _downRetryAttempts = 0;
                float distance = Mathf.Abs(elevatorLift.transform.position.y - worldSpaceFloorPosition.y);
                float timeToTravel = _elevator.TimeToTravelDistance(distance);
                LeanTween.moveY(elevatorLift.gameObject, worldSpaceFloorPosition.y, timeToTravel);
                _elevator.SetFlagLocal(BaseEntity.Flags.Busy, true);
                elevatorLift.ToggleHurtTrigger(true);
                _elevator.Invoke(_elevator.ClearBusy, timeToTravel);
                _elevator.CancelInvoke(ElevatorToGround);
                _elevator.Invoke(ElevatorToGround, timeToTravel + returnDelay);
                _elevator.SendNetworkUpdate();
            }

            private void ScheduleDownRetry()
            {
                if (_downRetryAttempts >= MaxDownRetries)
                {
                    env?.Puts($"Elevator down-retry limit ({MaxDownRetries}) reached; stopping retries.");
                    return;
                }
                _downRetryAttempts++;
                _elevator.Invoke(Retry, returnDelay);
            }

            private void Retry()
            {
                GoToFloor(Elevator.Direction.Down, true);
            }

            private void ElevatorToGround()
            {
                if (CurrentFloor != 0)
                {
                    if (_elevator.HasFlag(BaseEntity.Flags.Busy))
                    {
                        _elevator.Invoke(ElevatorToGround, 5f);
                        return;
                    }
                    GoToFloor(Elevator.Direction.Down, true);
                }
            }

            public void Init(RaidableBase raid)
            {
                this.raid = raid;
                env = raid.Instance;
                options = raid.Options.Elevators;
                _elevator._maxHealth = options.ElevatorHealth;
                _elevator.InitializeHealth(options.ElevatorHealth, options.ElevatorHealth);

                if (HasButton)
                {
                    env.Subscribe(nameof(OnButtonPress));
                }
            }

            private static void CleanElevatorKill(BaseEntity entity)
            {
                if (!entity.IsKilled())
                {
                    entity.transform.position = new(0, -100f, 0);
                    entity.DelayedSafeKill();
                }
            }

            public static PooledList<PooledList<BaseEntity>> SplitElevators(RaidableBase raid, List<BaseEntity> source)
            {
                var groups = DisposableList<PooledList<BaseEntity>>();
                using var positions = DisposableList<Vector2Int>();

                foreach (var entity in source)
                {
                    raid.Entities.Remove(entity);
                    if (entity.IsKilled()) continue;
                    var position = entity.transform.position;
                    var key = new Vector2Int(Mathf.RoundToInt(position.x * 2f), Mathf.RoundToInt(position.z * 2f));
                    int index = positions.IndexOf(key);
                    if (index >= 0)
                    {
                        groups[index].Add(entity);
                    }
                    else
                    {
                        positions.Add(key);
                        var group = DisposableList<BaseEntity>();
                        group.Add(entity);
                        groups.Add(group);
                    }
                }

                return groups;
            }

            public static PooledList<KeyValuePair<NetworkableId, BMGELEVATOR>> FixElevators(RaidableBase raid)
            {
                using var elevators = DisposableList<BaseEntity>();
                bool hasButton = false;
                var bmgs = DisposableList<KeyValuePair<NetworkableId, BMGELEVATOR>>();

                foreach (BaseEntity entity in raid.Entities)
                {
                    switch (entity)
                    {
                        case Elevator or ElevatorLift:
                            elevators.Add(entity);
                            break;
                        case PressButton _:
                            hasButton = true;
                            break;
                    }
                }

                using var splitElevators = SplitElevators(raid, elevators);

                for (int i = splitElevators.Count - 1; i >= 0; i--)
                {
                    using var split = splitElevators[i];
                    var bmg = FixElevator(raid, split);
                    if (bmg != null)
                    {
                        elevators.Add(bmg._elevator);
                        bmg.HasButton = hasButton;
                        raid.SetupEntity(bmg._elevator);
                        bmgs.Add(new(bmg._elevator.net.ID, bmg));
                    }
                    splitElevators.RemoveAt(i);
                }

                return bmgs;
            }

            public static BMGELEVATOR FixElevator(RaidableBase raid, List<BaseEntity> elevators)
            {
                RaidableBases instance = raid.Instance;
                if (elevators.IsNullOrEmpty())
                {
                    return null;
                }
                if (elevators.Count == 1)
                {
                    CleanElevatorKill(elevators[0]);
                    return null;
                }
                Vector3 bottom = new(999f, 999f, 999f);
                Vector3 top = new(-999f, -999f, -999f);
                Quaternion rot = elevators[0].transform.rotation;
                foreach (BaseEntity entity in elevators)
                {
                    var position = entity.transform.position;
                    if (position.y < bottom.y) bottom = position;
                    if (position.y > top.y) top = position;
                    CleanElevatorKill(entity);
                }
                Elevator elevator = GameManager.server.CreateEntity("assets/prefabs/deployable/elevator/static/elevator.static.prefab", bottom, rot, true) as Elevator;
                if (rot != Quaternion.identity) elevator.transform.rotation = rot;
                elevator.transform.position = bottom;
                elevator.transform.localPosition += new Vector3(0f, 0.25f, 0f);
                var bmgELEVATOR = elevator.gameObject.AddComponent<BMGELEVATOR>();
                bmgELEVATOR.env = instance;
                bmgELEVATOR._elevator = elevator;
                bmgELEVATOR._elevator.LiftSpeedPerMetre = BMGELEVATOR._LiftSpeedPerMetre;
                elevator.enableSaving = false;
                elevator.Spawn();
                bmgELEVATOR.Floors = top.y - bottom.y;
                elevator.Invoke(() =>
                {
                    if (elevator.IsDestroyed) return;
                    elevator.baseProtection = instance.GetElevatorProtection();
                    if (GetElevatorLift(elevator, out var lift)) lift.baseProtection = instance.GetElevatorProtection();
                    RemoveImmortality(elevator.baseProtection, 0.9f, 0f, 0f, 0f, 0f, 0.95f, 0f, 0f, 0f, 0.99f, 0.99f, 0.99f, 0f, 1f, 1f, 0.99f, 0.5f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 0f);
                }, 0.0625f);
                using (var update = elevator.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                {
                    update.Set(BaseEntity.Flags.Reserved1, true);
                    update.Set(Elevator.Flag_HasPower, true);
                }
                raid.Elevators[elevator.net.ID] = new() { BMG = bmgELEVATOR, raid = raid };
                if (raid.Elevators.Count == 1)
                {
                    if (raid.Options.Elevators.RequiresBuildingPermission || raid.Options.Elevators.RequiredAccessLevel > 0) instance.Subscribe(nameof(OnElevatorButtonPress));
                    instance.Subscribe(nameof(OnElevatorMove));
                    instance.Subscribe(nameof(OnElevatorCall));
                }
                return bmgELEVATOR;
            }

            internal static bool GetElevatorLift(Elevator elevator, out ElevatorLift lift)
            {
                if (!elevator.IsKilled() && elevator.liftEntity.IsValid(true))
                {
                    lift = elevator.liftEntity.Get(true);
                    return !lift.IsKilled();
                }
                lift = null;
                return false;
            }

            internal static void RemoveImmortality(ProtectionProperties baseProtection, params float[] obj)
            {
                DamageType[] damageTypes = (DamageType[])Enum.GetValues(typeof(DamageType));

                for (int i = 0; i < damageTypes.Length && i < obj.Length; i++)
                {
                    baseProtection.amounts[(int)damageTypes[i]] = obj[i];
                }
            }
        }

        public class RaidableSpawns
        {
            public HashSet<RaidableSpawnLocation> Seabed = new(), Spawns = new(), Garbage = new();
            public Dictionary<CacheType, HashSet<RaidableSpawnLocation>> Cached = new();
            private double lastTryTime;
            public bool IsCustomSpawn;
            public RaidableBases Instance;
            internal Configuration config => Instance.config;
            public SpawnsControllerManager SpawnsController => Instance.SpawnsController;
            public HashSet<RaidableSpawnLocation> Inactive(CacheType cacheType) => GetCache(cacheType);

            public RaidableSpawns(RaidableBases instance, HashSet<RaidableSpawnLocation> spawns)
            {
                Spawns = spawns;
                Instance = instance;
                IsCustomSpawn = true;
                foreach (var x in spawns)
                {
                    if (x.Location.y > Instance.MaxTerrainY)
                    {
                        Instance.MaxTerrainY = x.Location.y + 1f;
                    }
                }
            }

            public RaidableSpawns(RaidableBases instance)
            {
                Instance = instance;
            }

            public bool CanBuild(Vector3 buildPos, float radius)
            {
                if (IsCustomSpawn && Spawns.Count > 0)
                {
                    foreach (var rsl in Spawns)
                    {
                        if (InRange(rsl.Location, buildPos, radius))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            public bool Add(RaidableSpawnLocation rsl, CacheType cacheType, HashSet<RaidableSpawnLocation> cache, bool forced)
            {
                if (!forced)
                {
                    switch (cacheType)
                    {
                        case CacheType.Close when Instance.IsTooClose(rsl.Location, Instance.GetDistance(RaidableType.None)):
                        case CacheType.Generic when Instance.EventTerritory(rsl.Location):
                        case CacheType.Submerged when !SetOceanLevel(rsl):
                            return false;
                    }
                }

                return GetLocations(cacheType).Add(rsl);
            }

            public bool SetOceanLevel(RaidableSpawnLocation rsl)
            {
                rsl.WaterHeight = WaterSystem.OceanLevel;
                rsl.Surroundings.Clear();
                return true;
            }

            public void Check()
            {
                if (Time.timeAsDouble > lastTryTime)
                {
                    TryAddRange(CacheType.Temporary, true);
                    TryAddRange(CacheType.Privilege, true);
                    TryAddRange(CacheType.Seabed, false);
                    lastTryTime = Time.timeAsDouble + 300d;
                }

                if (Spawns.Count == 0)
                {
                    TryAddRange();
                }

                if (Seabed.Count == 0)
                {
                    TryAddRange(CacheType.Seabed);
                }
            }

            public void TryAddRange(CacheType cacheType = CacheType.Generic, bool forced = false)
            {
                HashSet<RaidableSpawnLocation> cache = GetCache(cacheType);

                foreach (var rsl in cache)
                {
                    if (Add(rsl, cacheType, cache, forced))
                    {
                        Garbage.Add(rsl);
                    }
                }

                cache.RemoveWhere(Garbage.Contains);

                Garbage.Clear();
            }

            private RaidableSpawnLocation GetSeabed(BuildingWaterOptions options, bool isNearest)
            {
                using var locations = Seabed.ToPooledList();
                if (!isNearest)
                {
                    Shuffle(locations);
                }
                foreach (var rsl in locations)
                {
                    if (SpawnsController.InDeepWater(rsl.Location, true, options.MinimumSeabedWaterDepth, options.MaximumSeabedWaterDepth))
                    {
                        return rsl;
                    }
                }
                return null;
            }

            public RaidableSpawnLocation GetRandom(RandomBase rb, bool buyableEvent)
            {
                BuildingWaterOptions options = rb.options.Water;
                RaidableSpawnLocation rsl;

                if (Seabed.Count > 0 && rb.IsWaterSpawn && (rsl = GetSeabed(options, buyableEvent && config.Settings.Buyable.Closest)) != null)
                {
                    rb.IsWaterSpawn = true;
                }
                else
                {
                    rsl = buyableEvent && config.Settings.Buyable.Closest ? Spawns.ElementAt(0) : Spawns.GetRandom();
                    rb.IsWaterSpawn = false;
                }

                Remove(rsl, rb.FromCacheType);

                return rsl;
            }

            public HashSet<RaidableSpawnLocation> GetLocations(CacheType cacheType)
            {
                return cacheType == CacheType.Seabed ? Seabed : Spawns;
            }

            public HashSet<RaidableSpawnLocation> GetCache(CacheType cacheType)
            {
                if (!Cached.TryGetValue(cacheType, out var cache))
                {
                    Cached[cacheType] = cache = new();
                }
                return cache;
            }

            public void AddNear(Vector3 target, float radius, CacheType to, CacheType from, float delayTime)
            {
                if (delayTime > 0)
                {
                    Instance.timer.Once(delayTime, () => AddNear(target, radius, to, from, 0f));
                    return;
                }

                HashSet<RaidableSpawnLocation> cache = GetCache(from);
                HashSet<RaidableSpawnLocation> locations = GetLocations(to);

                foreach (var rsl in cache)
                {
                    if (rsl == null || InRange2D(target, rsl.Location, radius) && locations.Add(rsl))
                    {
                        Garbage.Add(rsl);
                    }
                }

                cache.RemoveWhere(Garbage.Contains);

                Garbage.Clear();
            }

            public void Remove(RaidableSpawnLocation a, CacheType cacheType)
            {
                if (a == null) return;
                GetCache(cacheType).Add(a);
                GetLocations(cacheType).Remove(a);
            }

            public float RemoveNear(Vector3 target, float radius, CacheType cacheType, RaidableType type) =>
                RemoveNear(target, radius, cacheType, cacheType, type);

            public float RemoveNear(Vector3 target, float radius, CacheType from, CacheType to, RaidableType type)
            {
                if (from == CacheType.Generic)
                {
                    radius = Mathf.Max(Instance.GetDistance(type), radius);
                }

                HashSet<RaidableSpawnLocation> cacheFrom = GetCache(from);
                HashSet<RaidableSpawnLocation> cacheTo = GetCache(to);
                HashSet<RaidableSpawnLocation> locations = GetLocations(from);

                foreach (var rsl in locations)
                {
                    if (rsl == null || InRange2D(target, rsl.Location, radius) && (from == CacheType.Delete || cacheTo.Add(rsl)))
                    {
                        Garbage.Add(rsl);
                    }
                }

                locations.RemoveWhere(Garbage.Contains);

                if (cacheFrom != cacheTo)
                {
                    foreach (var rsl in cacheFrom)
                    {
                        if (rsl == null || InRange2D(target, rsl.Location, radius))
                        {
                            cacheTo.Add(rsl);
                        }
                    }

                    cacheFrom.RemoveWhere(cacheTo.Contains);
                }

                Garbage.Clear();

                return radius;
            }
        }

        public class PlayerInfo : ConfigurationExtension<int>
        {
            public string Name;
            public int Raids { get; set; }
            public int Points { get; set; }
            public int TotalRaids { get; set; }
            public int TotalPoints { get; set; }
            public DateTime ExpiredDate { get; set; } = DateTime.MinValue;
            public PlayerInfo() : base("LadderInfo", RaidableMode.Easy, RaidableMode.Medium, RaidableMode.Hard, RaidableMode.Expert, RaidableMode.Nightmare) { }
            internal Dictionary<string, int> Modes => Dictionary;
            public new int Get(string mode) => Modes.TryGetValue(mode, out int value) ? value : 0;
            public override bool ShouldProcessExtension() => false;

            public void AddPoints(string mode, int points)
            {
                Raids++;
                TotalRaids++;
                Points += points;
                TotalPoints += points;

                string modePointsKey = mode + "Points";
                string totalModeKey = "Total" + mode;
                string totalModePointsKey = "Total" + mode + "Points";

                Set(mode, Modes.GetValueOrDefault(mode) + 1);
                Set(modePointsKey, Modes.GetValueOrDefault(modePointsKey) + points);
                Set(totalModeKey, Modes.GetValueOrDefault(totalModeKey) + 1);
                Set(totalModePointsKey, Modes.GetValueOrDefault(totalModePointsKey) + points);
            }

            public bool IsExpired(double value)
            {
                if (ExpiredDate == DateTime.MinValue)
                {
                    ResetExpiredDate(value);
                    return false;
                }

                return ExpiredDate < DateTime.Now;
            }

            public void ResetExpiredDate(double value) => ExpiredDate = value > 0 ? DateTime.Now.AddDays(value) : DateTime.MinValue;

            public void ResetWipe()
            {
                Raids = Points = 0;
                using var keys = Modes.Keys.ToPooledList();

                foreach (string key in keys)
                {
                    if (!key.StartsWith("Total"))
                    {
                        Set(key, 0);
                    }
                }
            }

            public void ResetLifetime()
            {
                TotalRaids = TotalPoints = 0;
                using var keys = Modes.Keys.ToPooledList();

                foreach (string key in keys)
                {
                    if (key.StartsWith("Total"))
                    {
                        Set(key, 0);
                    }
                }
            }

            public bool Any() => Modes.Values.Exists(x => x > 0);
        }

        public class RotationCycle
        {
            [JsonProperty(PropertyName = "Buildings")]
            public Dictionary<string, List<string>> _buildings = new();

            [JsonProperty(PropertyName = "Player Buildings")]
            public Dictionary<ulong, Dictionary<string, List<string>>> _playerBuildings = new();

            internal RaidableBases Instance;

            internal Configuration config => Instance.config;

            public void Add(RaidableType type, string mode, string key, BasePlayer player)
            {
                if (type == RaidableType.Grid || type == RaidableType.Manual)
                {
                    return;
                }

                var buildings = GetBuildingsDictionary(type, player);

                if (buildings == null)
                {
                    return;
                }

                if (!buildings.TryGetValue(mode, out var keyList))
                {
                    buildings[mode] = keyList = new();
                }

                if (!keyList.Contains(key))
                {
                    keyList.Add(key);
                }
            }

            private Dictionary<string, List<string>> GetBuildingsDictionary(RaidableType type, BasePlayer player)
            {
                if (config.Settings.Management.RequireAllSpawnedBuyableEvents && type == RaidableType.Purchased && !player.IsNull())
                {
                    if (!_playerBuildings.TryGetValue(player.userID, out var buildings))
                    {
                        _playerBuildings[player.userID] = buildings = new();
                    }

                    return buildings;
                }

                return config.Settings.Management.RequireAllSpawned ? _buildings : null;
            }

            public bool CanSpawn(RaidableType type, string mode, string file, BasePlayer player, bool canReset = true)
            {
                if (mode == RaidableMode.Disabled)
                {
                    return false;
                }

                if (mode == RaidableMode.Random || type is RaidableType.Grid or RaidableType.Manual)
                {
                    return true;
                }

                var buildings = GetBuildingsDictionary(type, player);

                if (buildings == null)
                {
                    return !config.Settings.Management.RequireAllSpawned;
                }

                if (!buildings.TryGetValue(mode, out var files) || !files.Contains(file))
                {
                    return true;
                }

                bool Required(string item) => !files.Contains(item) && Instance.FileExists(item);

                foreach (var (key, profile) in Instance.Buildings.Profiles)
                {
                    if (!profile.Options.Enabled || profile.Options.Mode != mode || !Instance.CanSpawnDifficultyToday(type, mode) || Instance.MustExclude(type, profile.Options.AllowPVP))
                    {
                        continue;
                    }

                    if (Required(key) || profile.Options.AdditionalBases.Keys.Exists(Required))
                    {
                        return false;
                    }
                }

                if (canReset)
                {
                    files.Clear();
                }

                return true;
            }
        }

        public class PlayerInputEx : FacepunchBehaviour
        {
            private BasePlayer player;
            private Action queuedAction;
            private RaidableBases Instance;
            private RaidableBase raid;
            private Raider raider;
            private Transform t;
            private RaycastHit hit;
            private float deltaTimeTaken;
            private double nextConsumeTime;
            private bool IsWaterSpawn;
            private bool AllowLadders;
            private bool AllowBarricades;
            public Configuration config => Instance.config;
            public bool IsInvalid => t == null || player == null || !player.IsConnected || !player.IsAlive() || player.IsSleeping();

            public void Setup(RaidableBase raid, Raider ri)
            {
                player = ri.player;
                t = player.transform;
                raider = ri;
                raider.Input = this;
                Instance = raid.Instance;
                this.raid = raid;
                IsWaterSpawn = raid.IsWaterSpawn;
                AllowBarricades = raid.Options.AllowBarricades;
                AllowLadders = config.Settings.Management.AllowLadders;
                queuedAction = null;
                deltaTimeTaken = 0f;
                enabled = true;
            }

            public void Restart()
            {
                deltaTimeTaken = 0f;
            }

            private void Update()
            {
                deltaTimeTaken += Time.deltaTime;

                if (deltaTimeTaken >= 0.1f && !IsInvalid)
                {
                    if ((t.position - raider.lastPosition).sqrMagnitude > 0.01f)
                    {
                        raider.IsVanished = player._limitedNetworking;
                        raider.IsFlying = player.IsFlying;
                        raider.lastPosition = t.position;
                        raider.lastActiveTime = Time.timeAsDouble;
                        raider.participantTime += deltaTimeTaken;
                        if (player.clanId != 0)
                        {
                            raider.clan = GetClan(player);
                            raider.clanId = player.clanId;
                        }
                        else raider.clanId = 0;
                    }

                    if (AllowLadders || IsWaterSpawn)
                    {
                        if (queuedAction != null)
                        {
                            queuedAction();
                            queuedAction = null;
                        }

                        TryPlace(ConstructionType.Any);
                    }

                    deltaTimeTaken = 0f;
                }
            }

            private bool IsFireButton => player.serverInput.IsDown(BUTTON.FIRE_PRIMARY) || player.serverInput.WasDown(BUTTON.FIRE_PRIMARY);

            private bool IsUseButton => player.serverInput.IsDown(BUTTON.USE) || player.serverInput.WasDown(BUTTON.USE);

            public Quaternion GetRotation(string shortname)
            {
                var dir = shortname == "ladder.wooden.wall" ? hit.normal : (t.position - hit.point).XZ3D();
                return dir.sqrMagnitude <= 0.0001f ? t.rotation : Quaternion.LookRotation(dir.normalized);
            }

            public bool TryPlace(ConstructionType constructionType)
            {
                if (!player.svActiveItemID.IsValid)
                {
                    return false;
                }

                if (!IsFireButton)
                {
                    if (IsWaterSpawn && IsUseButton)
                    {
                        Item active = player.GetActiveItem();
                        if (active != null && active.info != null)
                        {
                            UseHeal(active, true);
                        }
                    }
                    return false;
                }

                Item item = player.GetActiveItem();
                if (item == null || item.info == null)
                {
                    return false;
                }

                if (!IsConstructionType(item.info.shortname, ref constructionType) || item.info.shortname == "ladder.wooden.wall" && (!AllowLadders || Mathf.Abs(hit.normal.y) > Mathf.Max(Mathf.Abs(hit.normal.x), Mathf.Abs(hit.normal.z))))
                {
                    UseHeal(item, false);
                    return false;
                }

                int amount = item.amount;
                string shortname = item.info.shortname;

                queuedAction = () =>
                {
                    if (raid == null || item == null || item.amount != amount || IsConstructionNear(constructionType, hit.point) || !Instance.ItemDefinitions.TryGetValue(item.info, out var prefab))
                    {
                        return;
                    }

                    if (GameManager.server.CreateEntity(prefab, hit.point, GetRotation(shortname), true) is BaseEntity e && e != null)
                    {
                        e.gameObject.SendMessage("SetDeployedBy", player, SendMessageOptions.DontRequireReceiver);
                        e.OwnerID = 0;
                        e.enableSaving = false;
                        e.Spawn();
                        item.UseItem(1);

                        if (constructionType == ConstructionType.Ladder && hit.GetEntity() is BaseEntity hitEntity && hitEntity != null)
                        {
                            e.SetParent(hitEntity, true, false);
                        }

                        raid.BuiltList.Add(e);
                        raid.AddEntity(e);
                    }
                };

                return true;
            }

            private void UseHeal(Item item, bool consume)
            {
                if (Time.timeAsDouble < nextConsumeTime) return;
                nextConsumeTime = Time.timeAsDouble + 1d;
                if (!player.CanInteract() || !player.IsSwimming()) return;
                if (consume && Instance._itemModConsume.TryGetValue(item.info, out var con))
                {
                    player.metabolism.MarkConsumption();
                    con.DoAction(item, player);
                }
                if (!consume && item.GetHeldEntity() is MedicalTool tool && tool != null && !tool.HasAttackCooldown())
                {
                    player.ClientRPC(RpcTarget.Player("Reset", player));
                    player.metabolism.MarkConsumption();
                    nextConsumeTime = Time.timeAsDouble + 3d;
                    tool.ServerUse();
                }
            }

            private bool IsConstructionType(string shortname, ref ConstructionType constructionType)
            {
                hit = default;

                if ((constructionType == ConstructionType.Any || constructionType == ConstructionType.Ladder) && shortname == "ladder.wooden.wall")
                {
                    constructionType = ConstructionType.Ladder;

                    if (raid.Options.RequiresCupboardAccessLadders && !raid.CanBuild(player))
                    {
                        raid.Notify(player, "Ladders Require Building Privilege!");
                        return false;
                    }

                    if (config.Settings.Management.AllowLadders && Physics.Raycast(player.eyes.HeadRay(), out hit, 4f, Layers.Mask.Construction, QueryTriggerInteraction.Ignore) && hit.GetEntity() is BaseEntity entity && entity.OwnerID == 0)
                    {
                        foreach (var block in Instance.Blocks)
                        {
                            if (block == entity.ShortPrefabName)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }

                if ((constructionType == ConstructionType.Any || constructionType == ConstructionType.Barricade) && shortname.StartsWith("barricade."))
                {
                    constructionType = ConstructionType.Barricade;

                    return AllowBarricades && Physics.Raycast(player.eyes.HeadRay(), out hit, 5f, targetMask, QueryTriggerInteraction.Ignore) && hit.GetEntity().IsNull();
                }

                return false;
            }

            private bool IsConstructionNear(ConstructionType constructionType, Vector3 target)
            {
                float radius = constructionType == ConstructionType.Barricade ? 1f : 0.3f;
                int layerMask = constructionType == ConstructionType.Barricade ? -1 : Layers.Mask.Deployed;
                using var tmp = FindEntitiesOfType<BaseEntity>(target, radius, layerMask);
                if (constructionType == ConstructionType.Barricade) return tmp.Count > 0;
                foreach (var e in tmp) { if (e is BaseLadder) return true; }
                return false;
            }
        }


        public class BuyableUiPanic : FacepunchBehaviour
        {
            private RaidableBases Instance;
            private BasePlayer player;
            private int jumpPresses;
            private int movementPresses;
            private int jumpLimit;
            private int movementLimit;
            private double resetSeconds;
            private double lastInputTime;
            private double nextTick;
            private bool jumpDown;
            private bool forwardDown;
            private bool backwardDown;
            private bool leftDown;
            private bool rightDown;

            public void Setup(RaidableBases instance, BasePlayer target, UIBuyablePanicCloseSettings settings)
            {
                Instance = instance;
                player = target;
                jumpLimit = Math.Max(0, settings.JumpPresses);
                movementLimit = Math.Max(0, settings.MovementPresses);
                resetSeconds = Mathf.Max(0f, settings.ResetSeconds);
                jumpPresses = 0;
                movementPresses = 0;
                lastInputTime = 0d;
                nextTick = 0d;

                if (player?.serverInput == null)
                {
                    return;
                }

                jumpDown = player.serverInput.IsDown(BUTTON.JUMP);
                forwardDown = player.serverInput.IsDown(BUTTON.FORWARD);
                backwardDown = player.serverInput.IsDown(BUTTON.BACKWARD);
                leftDown = player.serverInput.IsDown(BUTTON.LEFT);
                rightDown = player.serverInput.IsDown(BUTTON.RIGHT);
            }

            private void Update()
            {
                double now = Time.timeAsDouble;

                if (now < nextTick)
                {
                    return;
                }

                nextTick = now + 0.05d;

                if (Instance == null || player == null || !player.IsConnected)
                {
                    Destroy(this);
                    return;
                }

                var input = player.serverInput;
                if (input == null)
                {
                    return;
                }

                if (resetSeconds > 0d && lastInputTime > 0d && now - lastInputTime >= resetSeconds)
                {
                    jumpPresses = 0;
                    movementPresses = 0;
                    lastInputTime = 0d;
                }

                bool jump = input.IsDown(BUTTON.JUMP);
                bool forward = input.IsDown(BUTTON.FORWARD);
                bool backward = input.IsDown(BUTTON.BACKWARD);
                bool left = input.IsDown(BUTTON.LEFT);
                bool right = input.IsDown(BUTTON.RIGHT);
                bool pressed = false;

                if (jump && !jumpDown)
                {
                    jumpPresses++;
                    pressed = true;
                }

                if (forward && !forwardDown)
                {
                    movementPresses++;
                    pressed = true;
                }

                if (backward && !backwardDown)
                {
                    movementPresses++;
                    pressed = true;
                }

                if (left && !leftDown)
                {
                    movementPresses++;
                    pressed = true;
                }

                if (right && !rightDown)
                {
                    movementPresses++;
                    pressed = true;
                }

                jumpDown = jump;
                forwardDown = forward;
                backwardDown = backward;
                leftDown = left;
                rightDown = right;

                if (pressed)
                {
                    lastInputTime = now;
                }

                if (jumpLimit > 0 && jumpPresses >= jumpLimit || movementLimit > 0 && movementPresses >= movementLimit)
                {
                    enabled = false;
                    Instance.CloseBuyableUi(player);
                }
            }
        }

        public class HumanoidNPC : ScientistNPC
        {
            public new HumanoidBrain Brain;

            public string DisplayNameOverride;

            public RaidableBase raid => Brain?.raid;

            public RaidableBases Instance;

            public new Translate.Phrase LootPanelTitle => displayName;

            public override string Categorize() => "Humanoid";

            public override bool ShouldDropActiveItem() => false;

            public override string displayName => string.IsNullOrWhiteSpace(DisplayNameOverride) ? base.displayName : DisplayNameOverride;

            public override void AttackerInfo(ProtoBuf.PlayerLifeStory.DeathInfo info)
            {
                info.attackerName = displayName;
                info.attackerSteamID = userID;
                info.inflictorName = inventory?.containerBelt?.GetSlot(0)?.info?.shortname;
                if (Brain != null) info.attackerDistance = Vector3.Distance(Brain.ServerPosition, Brain.AttackPosition);
            }

            public override void OnDied(HitInfo info)
            {
                Brain?.DisableShouldThink();
                base.OnDied(info);
            }

            private void TryRespawnNpc()
            {
                HumanoidBrain brain = Brain;
                if (raid == null || raid.IsDespawning || brain == null)
                {
                    return;
                }
                if (raid.npcs != null)
                {
                    raid.npcs.RemoveAll(npc => npc.IsKilled() || npc.userID == userID);
                }
                if (brain.spawnedInside)
                {
                    raid.npcAmountInside = Math.Max(0, raid.npcAmountInside - 1);
                }
                raid.TryRespawnNpc(brain.isMurderer, brain.respawnsRemaining);
            }

            public override BaseCorpse CreateCorpse(PlayerFlags flagsOnDeath, Vector3 posOnDeath, Quaternion rotOnDeath, List<TriggerBase> triggersOnDeath, bool forceServerSide = false)
            {
                TryRespawnNpc();
                BasePlayer.bots.Remove(this);
                Instance.HumanoidBrains.Remove(userID);
                if (inventory == null || Brain == null || !Brain.HasCorpseLoot())
                {
                    if (HarmonyModInterface.Mods.CallHook("OnRaidableNpcStrip", GetEntity(), 2) == null)
                    {
                        inventory.SafelyStrip();
                    }
                    return null;
                }
                if (Brain.keepInventory)
                {
                    inventory.containerWear.SafelyRemove("gloweyes");
                }
                else if (HarmonyModInterface.Mods.CallHook("OnRaidableNpcStrip", GetEntity(), 1) == null)
                {
                    inventory.SafelyStrip();
                }
                if (raid.Options.DespawnGreyNpcBags)
                {
                    Instance.NpcCorpse.Add(userID);
                }
                List<LootItem> drops = Brain.isMurderer ? Brain.Settings.MurdererDrops : Brain.Settings.ScientistDrops;
                if (!RemoveOwnershipPass() && drops.Count == 0 && LootSpawnSlots.Length == 0)
                {
                    return null;
                }
                PlayerCorpse corpse = DropCorpse("assets/prefabs/player/player_corpse.prefab") as PlayerCorpse;
                if (corpse == null)
                {
                    return null;
                }
                corpse.transform.position = corpse.transform.position + Vector3.down * NavAgent.baseOffset;
                corpse.TakeFrom(this, inventory.containerMain, inventory.containerWear, inventory.containerBelt);
                corpse.playerName = displayName;
                corpse.playerSteamID = userID;
                corpse.skinID = RB_SKIN_ID;
                corpse.Spawn();
                if (corpse.IsKilled())
                {
                    return null;
                }
                corpse.TakeChildren(this);
                bool canPopulateLoot = !Brain.Settings.AlternateScientistLoot.CallHook || HarmonyModInterface.CallHook("OnCorpsePopulate", this, corpse) == null;
                if (canPopulateLoot && LootSpawnSlots.Length != 0)
                {
                    foreach (var lootSpawnSlot in LootSpawnSlots)
                    {
                        for (int k = 0; k < lootSpawnSlot.numberToSpawn; k++)
                        {
                            if (UnityEngine.Random.Range(0f, 1f) <= lootSpawnSlot.probability)
                            {
                                lootSpawnSlot.definition.SpawnIntoContainer(corpse.containers[0]);
                            }
                        }
                    }
                }
                raid.SpawnDrops(corpse.containers, drops);
                CheckCorpse(corpse);
                return corpse;
            }

            private void CheckCorpse(PlayerCorpse corpse)
            {
                raid.npcs.RemoveAll(npc => npc.IsKilled() || npc.userID == userID);

                if (!Brain.keepInventory)
                {
                    corpse.Invoke(corpse.SafelyKill, 30f);
                }

                if (!Instance.AnyNpcs())
                {
                    Instance.Unsubscribe(nameof(OnNpcDestinationSet));
                }

                if (raid.Options.DespawnGreyNpcBags)
                {
                    raid.SetupEntity(corpse);
                }

                corpse.playerName = displayName;
                Brain.DisableShouldThink();
                UnityEngine.Object.Destroy(Brain);
            }

            private bool RemoveOwnershipPass()
            {
                if (!Instance.config.BlockPaidContent) return true;
                using var itemList = DisposableList<Item>();
                inventory.GetAllItems(itemList);
                bool hasItems = false;
                for (int i = itemList.Count - 1; i >= 0; i--)
                {
                    Item item = itemList[i];
                    if (!Instance.RequiresOwnership(item.info, item.skin))
                    {
                        hasItems = true;
                        continue;
                    }
                    item.GetHeldEntity().SafelyKill();
                    item.RemoveFromContainer();
                    item.Remove(0f);
                }
                return hasItems;
            }
        }

        public class HumanoidBrain : ScientistBrain
        {
            public void DisableShouldThink()
            {
                if (isKilled)
                {
                    return;
                }
                isKilled = true;
                movementStarted = false;
                nextBaseRouteResumeTime = 0d;
                unwakeable = true;
                if (!Rust.Application.isQuitting)
                {
                    BaseEntity.Query.Server.RemoveBrain(GetBaseEntity());
                    LeaveGroup();
                }
                if (thinker != null)
                {
                    AIThinkManager._processQueue.Remove(thinker);
                }
                lastWarpTime = float.MaxValue;
                sleeping = true;
                SetEnabled(false);
            }

            internal enum AttackType { BaseProjectile, Explosive, FlameThrower, Melee, Water, None }
            internal string displayName, AttackName = string.Empty;
            internal Transform NpcTransform;
            internal IThinker thinker;
            internal uint LootSpawnSlotsPrefabID = 1536035819u;
            internal ulong userid;
            internal HumanoidNPC npc;
            internal AttackEntity _attackEntity;
            internal FlameThrower flameThrower;
            internal BaseProjectile launcher;
            internal LiquidWeapon liquidWeapon;
            internal BaseMelee baseMelee;
            internal BaseProjectile baseProjectile;
            internal BasePlayer AttackTarget;
            internal Transform AttackTransform;
            internal RaidableBases Instance;
            internal RaidableBase raid;
            internal NpcSettings Settings;
            internal List<Vector3> RandomRoamPositions;
            internal List<Vector3> RandomNearPositions;
            internal List<int> BaseRoute;
            internal Vector3 DestinationOverride, baseRouteProgressPosition;
            internal int baseRouteIndex, baseRouteDirection = 1, baseRoutePauseNodeIndex = -1;
            internal int respawnsRemaining;
            internal bool keepInventory, isKilled, isMurderer, isStationary, isSleeper, movementStarted, spawnedInside, unwakeable, baseRouteDirty;
            internal float attackCooldown, attackRange, ScientistChaseRange;
            internal double lastWarpTime, lastAttackTime, nextAttackTime, equipWeaponTime, equipToolTime;
            internal double lastEquipmentThinkTime, nextAttackThinkTime, nextEquipmentThinkTime, nextBaseRouteNavigationTime, nextDoorCheckTime, lastDoorCheckTime, nextRoamThinkTime, nextBaseRouteProgressTime, nextBaseRouteResumeTime;
            internal AttackType attackType = AttackType.None;
            internal BaseNavigator.NavigationSpeed CurrentSpeed = BaseNavigator.NavigationSpeed.Normal;
            internal Configuration config => Instance.config;
            internal Vector3 AttackPosition => AttackTransform == null ? default : AttackTransform.position;
            internal Vector3 ServerPosition => NpcTransform == null ? default : NpcTransform.position;

            public double SecondsSinceLastAttack => Time.timeAsDouble - lastAttackTime;
            internal bool UsesBaseNavigation => spawnedInside && !isStationary;
            internal bool IsDormant => raid.intruders.Count == 0 && AttackTarget == null;
            internal List<AttackEntity> AttackWeapons = Pool.Get<List<AttackEntity>>();
            internal List<Item> MedicalTools = Pool.Get<List<Item>>();

            internal AttackEntity AttackEntity
            {
                get
                {
                    if (_attackEntity.IsNull())
                    {
                        IdentifyWeapon();
                    }

                    return _attackEntity;
                }
            }

            internal void TickEquipment(double now)
            {
                if (isKilled || now < nextEquipmentThinkTime)
                {
                    return;
                }

                float delta = lastEquipmentThinkTime > 0d ? (float)(now - lastEquipmentThinkTime) : 0.25f;
                lastEquipmentThinkTime = now;
                nextEquipmentThinkTime = now + 0.25d;

                equipToolTime += delta;
                if (equipToolTime >= 5f)
                {
                    equipToolTime = float.MinValue;
                    EquipMedicalTool();
                }

                equipWeaponTime += delta;
                if (equipWeaponTime >= 5f)
                {
                    equipWeaponTime = 0f;
                    EquipWeapon();
                }
            }

            private void EquipWeapon()
            {
                AttackWeapons.RemoveAll(IsKilled);
                if (AttackWeapons.Count <= 1 || npc.IsWounded())
                {
                    return;
                }
                Shuffle(AttackWeapons);
                foreach (var weapon in AttackWeapons)
                {
                    if (AttackTransform != null)
                    {
                        if (weapon is BaseMelee && !IsInAttackRange(5f))
                        {
                            continue;
                        }
                        if (weapon is ThrownWeapon && (AttackTarget == null || !AttackTarget.IsOnGround() || !IsInAttackRange(15f)))
                        {
                            continue;
                        }
                    }
                    UpdateWeapon(weapon, weapon.ownerItemUID);
                    _attackEntity = null;
                    IdentifyWeapon();
                    break;
                }
            }

            public bool HasCorpseLoot()
            {
                if (isMurderer ? Settings.MurdererDrops.Count > 0 : Settings.ScientistDrops.Count > 0) return true;
                return keepInventory || npc != null && npc.LootSpawnSlots.Length != 0;
            }

            public void EnableMedicalTools()
            {
                equipToolTime = MedicalTools.Count == 0 ? float.MinValue : 4f;
            }

            private void EquipMedicalTool()
            {
                if (npc.IsWounded() || npc.health > npc.startHealth * HealBelowHealthFraction)
                {
                    equipToolTime = 0f;
                    return;
                }
                if (AttackTransform != null)
                {
                    if (!isMurderer && Senses.Memory.IsLOS(AttackTarget))
                    {
                        equipToolTime = 0f;
                        return;
                    }
                    if (isMurderer && IsInReachableRange())
                    {
                        equipToolTime = 0f;
                        return;
                    }
                }
                MedicalTools.RemoveAll(IsKilled);
                if (MedicalTools.Count == 0)
                {
                    return;
                }
                Item tool = MedicalTools[0];
                equipWeaponTime = 0f;
                StartCoroutine(Heal(tool));
            }

            private IEnumerator Heal(Item medicalItem)
            {
                npc.UpdateActiveItem(medicalItem.uid);
                MedicalTool medicalTool = medicalItem.GetHeldEntity() as MedicalTool;
                yield return CoroutineEx.waitForSeconds(1f);
                if (medicalTool != null)
                {
                    medicalTool.ServerUse();
                }
                if (!npc.IsKilled())
                {
                    npc.Heal(npc.MaxHealth());
                    equipToolTime = 0f;
                }
            }

            public void UpdateWeapon(AttackEntity attackEntity, ItemId uid)
            {
                npc.UpdateActiveItem(uid);

                if (attackEntity is Chainsaw cs)
                {
                    cs.ServerNPCStart();
                }

                npc.damageScale = 1f;

                attackEntity.TopUpAmmo();
                attackEntity.SetHeld(true);
            }

            internal void IdentifyWeapon()
            {
                _attackEntity = GetEntity().GetAttackEntity();

                attackRange = 0f;
                attackCooldown = 99999f;
                attackType = AttackType.None;
                baseMelee = null;
                flameThrower = null;
                launcher = null;
                liquidWeapon = null;
                AttackName = string.Empty;

                if (_attackEntity.IsNull())
                {
                    return;
                }

                ((AttackName = _attackEntity.ShortPrefabName) switch
                {
                    "double_shotgun.entity" or "shotgun_pump.entity" or "shotgun_waterpipe.entity" or "spas12.entity" or "nailgun.entity" or "t1_smg.entity" or "snowballgun.entity" or "blunderbuss.entity" or "pistol_eoka.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.BaseProjectile, 30f, 0f, 30f);
                    }),
                    "pistol_revolver.entity" or "pistol_semiauto.entity" or "smg.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.BaseProjectile, 50f, 0f, 50f);
                    }),
                    "m4_shotgun.entity" or "glock.entity" or "python.entity" or "thompson.entity" or "m92.entity" or "mp5.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.BaseProjectile, 100f, 0f, 75f);
                    }),
                    "ak47u.entity" or "ak47u_ice.entity" or "ak47u_diver.entity" or "ak47u_med.entity" or "m249.entity" or "minigun.entity" or "sks.entity" or "m39.entity" or "semi_auto_rifle.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.BaseProjectile, 300f, 0f, 190f);
                    }),
                    "hc_revolver.entity" or "lr300.entity" or "hmlmg.entity" or "l96.entity" or "bolt_rifle.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.BaseProjectile, 400f, 0f, 380f);
                    }),
                    "chainsaw.entity" or "jackhammer.entity" => (Action)(() =>
                    {
                        baseMelee = _attackEntity as BaseMelee;
                        SetAttackRestrictions(AttackType.Melee, 2.5f, (_attackEntity.animationDelay + _attackEntity.deployDelay) * 2f);
                    }),
                    "axe_salvaged.entity" or "bone_club.entity" or "butcherknife.entity" or "candy_cane.entity" or "hammer_salvaged.entity" or "hatchet.entity" or "icepick_salvaged.entity" or "knife.combat.entity" or "knife_bone.entity" or "longsword.entity" or "mace.baseballbat" or "mace.entity" or "machete.weapon" or "pickaxe.entity" or "salvaged_cleaver.entity" or "salvaged_sword.entity" or "sickle.entity" or "spear_stone.entity" or "spear_wooden.entity" or "cny_spear.entity" or "stone_pickaxe.entity" or "stonehatchet.entity" or "vampirestake.entity" or "skinningknife.entity" or "pitchfork.entity" => (Action)(() =>
                    {
                        baseMelee = _attackEntity as BaseMelee;
                        SetAttackRestrictions(AttackType.Melee, 2.5f, (_attackEntity.animationDelay + _attackEntity.deployDelay) * 1.5f);
                    }),
                    "explosive.satchel.entity" or "explosive.timed.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.Explosive, 17.5f, 10f);
                    }),
                    "grenade.beancan.entity" or "grenade.f1.entity" or "grenade.molotov.entity" or "grenade.flashbang.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.Explosive, 17.5f, 5f);
                    }),
                    "mgl.entity" => (Action)(() =>
                    {
                        launcher = _attackEntity as BaseProjectile;
                        SetAttackRestrictions(AttackType.Explosive, 100f, 2f, 50f);
                    }),
                    "rocket_launcher.entity" => (Action)(() =>
                    {
                        launcher = _attackEntity as BaseProjectile;
                        SetAttackRestrictions(AttackType.Explosive, 300f, 6f, 150f);
                    }),
                    "flamethrower.entity" or "militaryflamethrower.entity" => (Action)(() =>
                    {
                        flameThrower = _attackEntity as FlameThrower;
                        SetAttackRestrictions(AttackType.FlameThrower, 10f, (_attackEntity.animationDelay + _attackEntity.deployDelay) * 2f);
                    }),
                    "compound_bow.entity" or "crossbow.entity" or "bow_hunting.entity" or "legacybow.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.BaseProjectile, 200f, (_attackEntity.animationDelay + _attackEntity.deployDelay) * 1.25f, 150f);
                    }),
                    "mini_crossbow.entity" or "speargun.entity" or "blowpipe.entity" or "boomerang.entity" => (Action)(() =>
                    {
                        SetAttackRestrictions(AttackType.BaseProjectile, 50f, (_attackEntity.animationDelay + _attackEntity.deployDelay) * 1.25f, 20f);
                    }),
                    "watergun.entity" or "waterpistol.entity" => (Action)(() =>
                    {
                        if ((liquidWeapon = _attackEntity as LiquidWeapon) != null)
                        {
                            liquidWeapon.AutoPump = true;
                            SetAttackRestrictions(AttackType.Water, 10f, 2f);
                        }
                    }),
                    _ => (Action)(() => _attackEntity = null)
                })();
            }

            private void SetAttackRestrictions(AttackType attackType, float attackRange, float attackCooldown, float effectiveRange = 0f)
            {
                if (attackType == AttackType.BaseProjectile && _attackEntity is BaseProjectile projectile)
                {
                    baseProjectile = projectile;

                    if (!baseProjectile.MuzzlePoint)
                    {
                        baseProjectile.MuzzlePoint = baseProjectile.transform;
                    }
                }

                if (effectiveRange != 0f)
                {
                    _attackEntity.effectiveRange = effectiveRange;
                }

                (this.attackType, this.attackRange, this.attackCooldown) = (attackType, attackRange, attackCooldown);
            }

            public bool ValidTarget => AttackTransform != null && !AttackTarget.IsKilled() && !ShouldForgetTarget(AttackTarget);

            public bool CannotSchedule() => isKilled || !movementStarted || npc == null || npc.IsDestroyed || raid == null || raid.IsDespawning || Settings == null || Navigator == null || NpcTransform == null;

            public override void OnDestroy()
            {
                movementStarted = false;
                if (!Rust.Application.isQuitting && !isKilled)
                {
                    BaseEntity.Query.Server.RemoveBrain(GetEntity());
                    LeaveGroup();
                    if (IsInvoking(TickMovement))
                    {
                        CancelInvoke(TickMovement);
                    }
                }
                ResetToPool(ref RandomRoamPositions);
                ResetToPool(ref RandomNearPositions);
                ResetToPool(ref BaseRoute);
                ResetToPool(ref AttackWeapons);
                ResetToPool(ref MedicalTools);
                Count--;
            }

            public override void InitializeAI()
            {
                base.InitializeAI();
                base.ForceSetAge(0f);

                Pet = false;
                sleeping = false;
                UseAIDesign = true;
                AllowedToSleep = false;
                HostileTargetsOnly = false;
                AttackRangeMultiplier = 2f;
                MaxGroupSize = 0;

                Senses.Init(
                    owner: GetEntity(),
                    brain: this,
                    memoryDuration: 5f,
                    range: 50f,
                    targetLostRange: 75f,
                    visionCone: -1f,
                    checkVision: false,
                    checkLOS: true,
                    ignoreNonVisionSneakers: true,
                    listenRange: 15f,
                    hostileTargetsOnly: false,
                    senseFriendlies: false,
                    ignoreSafeZonePlayers: false,
                    senseTypes: config.Settings.Management.TargetNpcs ? EntityType.Player | EntityType.BasePlayerNPC : EntityType.Player,
                    refreshKnownLOS: true
                );

                CanUseHealingItems = true;
            }

            public void TryStartSleeping()
            {
                if (Settings.Inside.Sleepers.Enabled && isStationary)
                {
                    SetSleeping(true);
                    isSleeper = true;

                    if (Settings.Inside.Sleepers.IsUnwakeable)
                    {
                        DisableShouldThink();
                        unwakeable = true;
                    }
                }
            }

            public void SetSleeping(bool state)
            {
                SetEnabled(!state);
                sleeping = state;
                AllowedToSleep = state;
                npc.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, state);
            }

            public override void AddStates()
            {
                base.AddStates();

                states[AIState.Attack] = new AttackState(this);
            }

            public class AttackState : BaseAttackState
            {
                private new HumanoidBrain brain;
                private global::HumanNPC npc;
                private Transform NpcTransform;

                private new IAIAttack attack => brain.Senses.ownerAttack;

                public AttackState(HumanoidBrain humanoidBrain)
                {
                    base.brain = brain = humanoidBrain;
                    base.AgrresiveState = true;
                    npc = brain.GetBrainBaseEntity() as global::HumanNPC;
                    NpcTransform = npc.transform;
                }

                public override void StateEnter(BaseAIBrain _brain, BaseEntity _entity)
                {
                    if (_brain != null && NpcTransform != null && brain.ValidTarget && InAttackRange())
                    {
                        StartAttacking();
                    }
                }

                public override void StateLeave(BaseAIBrain _brain, BaseEntity _entity)
                {

                }

                private new void StopAttacking()
                {
                    if (attack != null)
                    {
                        attack.StopAttacking();
                        brain.TryReturnHome();
                        brain.AttackTarget = null;
                        brain.AttackTransform = null;
                        brain.Navigator.ClearFacingDirectionOverride();
                    }
                }

                public override StateStatus StateThink(float delta, BaseAIBrain _brain, BaseEntity _entity)
                {
                    if (_brain == null || NpcTransform == null || attack == null)
                    {
                        return StateStatus.Error;
                    }
                    if (brain.isKilled || !brain.ValidTarget)
                    {
                        StopAttacking();

                        return StateStatus.Finished;
                    }
                    if (brain.Senses.ignoreSafeZonePlayers && brain.AttackTarget.InSafeZone())
                    {
                        return StateStatus.Error;
                    }
                    if (brain.CanShoot())
                    {
                        if (InAttackRange())
                        {
                            StartAttacking();
                        }

                        return StateStatus.Running;
                    }
                    else
                    {
                        StopAttacking();

                        return StateStatus.Finished;
                    }
                }

                private bool InAttackRange()
                {
                    if (brain.AttackTransform == null)
                    {
                        return false;
                    }
                    float range = brain.attackRange;
                    if (brain.raid.IsMounted(brain.AttackTarget))
                    {
                        range += 3f;
                    }
                    return brain.IsInAttackRange(range) && brain.CanSeeTarget(brain.AttackTarget);
                }

                private void StartAttacking()
                {
                    if (brain.AttackTarget == null || brain.AttackTransform == null)
                    {
                        return;
                    }

                    brain.SetAimDirection();

                    if (!brain.CanShoot() || brain.IsAttackOnCooldown() || brain.TryThrowWeapon())
                    {
                        return;
                    }
                    if (brain.attackType == AttackType.Explosive && !brain.launcher.IsNull())
                    {
                        brain.EmulatedFire();
                    }
                    else if (brain.attackType == AttackType.BaseProjectile)
                    {
                        RealisticShotTest();
                    }
                    else if (brain.attackType == AttackType.FlameThrower)
                    {
                        brain.UseFlameThrower();
                    }
                    else if (brain.attackType == AttackType.Water)
                    {
                        brain.UseWaterGun();
                    }
                    else brain.MeleeAttack();
                    brain.lastAttackTime = Time.timeAsDouble;
                }

                private void RealisticShotTest()
                {
                    if (brain.AttackTarget.IsNpc)
                    {
                        var faction = brain.AttackTarget.faction;
                        brain.AttackTarget.faction = BaseCombatEntity.Faction.Horror;
                        npc.ShotTest(brain.AttackPosition.Distance(brain.ServerPosition));
                        if (brain.AttackTarget != null) brain.AttackTarget.faction = faction;
                    }
                    else npc.ShotTest(brain.AttackPosition.Distance(brain.ServerPosition));
                }
            }

            private bool init;
            private global::HumanNPC owner;

            public void Init()
            {
                if (init) return;
                init = true;
                lastWarpTime = Time.timeAsDouble;
                npc.spawnPos = raid.Location;
                npc.AdditionalLosBlockingLayer = visibleMask;
                owner = GetEntity();
                SetupNavigator(raid.ProtectionRadius, isStationary);
            }

            private void Converge()
            {
                foreach (var brain in Instance.HumanoidBrains.Values)
                {
                    if (brain != null && brain.NpcTransform != null && brain != this && brain.CanConverge(npc))
                    {
                        brain.SetTarget(AttackTarget, false);
                    }
                }
            }

            public void Forget(bool roam = true)
            {
                Senses.Players.Clear();
                Senses.Memory.LOS.Clear();
                Senses.Memory.All.Clear();
                Senses.Memory.Threats.Clear();
                Senses.Memory.Targets.Clear();
                Senses.Memory.Players.Clear();
                Events?.Memory?.Clear();
                Navigator.ClearFacingDirectionOverride();

                AttackTarget = null;
                AttackTransform = null;

                if (!isStationary && roam)
                {
                    if (spawnedInside)
                    {
                        baseRouteDirty = true;
                        baseRoutePauseNodeIndex = -1;
                        nextBaseRouteResumeTime = 0d;
                    }
                    else DestinationOverride = GetRandomRoamPosition();
                }
            }

            public void SetRange(float range)
            {
                SenseRange = ListenRange = range;
                if (range < raid.ProtectionRadius)
                {
                    range = raid.ProtectionRadius;
                }
                ScientistChaseRange = range * 1.25f;
                Senses.targetLostRange = TargetLostRange = range * 1.5f;
            }

            private void RandomMove(float radius) => RandomMove(AttackPosition, radius);

            private void RandomMove(Vector3 origin, float radius)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                Vector3 destination = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                destination.y = TerrainMeta.HeightMap.GetHeight(destination);

                SetDestination(destination);
            }

            public void RandomMove(float radius, float margin, float maxAngle = 100f)
            {
                Vector3 direction = ServerPosition - AttackPosition;
                direction.y = 0f;

                if (SecondsSinceLastAttack > 2f || direction.sqrMagnitude > radius * radius)
                {
                    RandomMove(radius);
                    return;
                }

                direction.Normalize();

                float halfAngleRadians = maxAngle * 0.5f * Mathf.Deg2Rad;
                float finalAngleRadians = Mathf.Atan2(direction.z, direction.x) + UnityEngine.Random.Range(-halfAngleRadians, halfAngleRadians);
                float distance = UnityEngine.Random.Range(Mathf.Max(0f, radius - margin), radius + margin);
                Vector3 destination = AttackPosition + new Vector3(Mathf.Cos(finalAngleRadians), 0f, Mathf.Sin(finalAngleRadians)) * distance;
                destination.y = TerrainMeta.HeightMap.GetHeight(destination);

                SetDestination(destination);
            }

            public void SetupNavigator(float distance, bool isStationary)
            {
                BaseNavigator navigator = Navigator;
                if (navigator == null && TryGetComponent(out navigator))
                {
                    Navigator = navigator;
                }

                navigator.CanUseNavMesh = !isStationary && !Rust.Ai.AiManager.nav_disable;
                navigator.CanUseBaseNav = spawnedInside && !isStationary;

                if (isStationary)
                {
                    navigator.MaxRoamDistanceFromHome = navigator.BestMovementPointMaxDistance = navigator.BestRoamPointMaxDistance = 0f;
                    navigator.DefaultArea = "Not Walkable";
                }
                else
                {
                    navigator.MaxRoamDistanceFromHome = navigator.BestMovementPointMaxDistance = navigator.BestRoamPointMaxDistance = distance * 0.85f;
                    navigator.DefaultArea = "Walkable";
                    navigator.topologyPreference = ((TerrainTopology.Enum)TerrainTopology.EVERYTHING);
                }

                navigator.Agent.agentTypeID = NavMesh.GetSettingsByIndex(1).agentTypeID; // 0:0, 1: -1372625422, 2: 1479372276, 3: -1923039037
                navigator.MaxWaterDepth = config.Settings.Management.WaterDepth;

                if (navigator.CanUseNavMesh)
                {
                    navigator.Init(owner, navigator.Agent);
                }
            }

            public Vector3 GetAimDirection()
            {
                if (Navigator.IsOverridingFacingDirection)
                {
                    return Navigator.FacingDirectionOverride;
                }
                if (InRange2D(AttackPosition, ServerPosition, 1f))
                {
                    return npc.eyes.BodyForward();
                }
                return (AttackPosition - ServerPosition).normalized;
            }

            private void SetAimDirection()
            {
                Navigator.SetFacingDirectionEntity(AttackTarget);
                npc.SetAimDirection(GetAimDirection());
            }

            private void SetDestination()
            {
                SetDestination(GetRandomRoamPosition(), true);
            }

            private void SetDestination(Vector3 destination, bool preserveHeight = false)
            {
                if (!IsInChaseRange(destination))
                {
                    if (isMurderer)
                    {
                        float range = Settings.CanLeave ? TargetLostRange : raid.ProtectionRadius * 0.9f;
                        float distance = UnityEngine.Random.Range(range - 5f, range - 1f);
                        Vector2 u = UnityEngine.Random.insideUnitCircle.normalized;

                        destination = raid.Location + new Vector3(u.x, 0f, u.y) * distance;
                    }
                    else
                    {
                        float range = Settings.CanLeave ? ScientistChaseRange : raid.ProtectionRadius * 0.9f;
                        Vector3 offset = (destination - raid.Location).normalized * range;

                        destination = raid.Location + offset;
                    }
                }

                if (destination != DestinationOverride)
                {
                    if (!preserveHeight)
                    {
                        destination.y = TerrainMeta.HeightMap.GetHeight(destination);

                        if (destination.y < -1f)
                        {
                            destination = GetRandomRoamPosition();
                        }
                    }

                    DestinationOverride = destination;
                }

                Navigator.SetCurrentSpeed(CurrentSpeed);

                if (Navigator.CurrentNavigationType == BaseNavigator.NavigationType.None && !Rust.Ai.AiManager.ai_dormant && !Rust.Ai.AiManager.nav_disable)
                {
                    Navigator.SetCurrentNavigationType(BaseNavigator.NavigationType.NavMesh);
                }

                if (CanUseNavMesh() && !Navigator.SetDestination(destination, CurrentSpeed))
                {
                    Navigator.Destination = destination;
                    npc.finalDestination = destination;
                }
            }

            public bool SetTarget(BasePlayer player, bool converge = true)
            {
                if (unwakeable || npc == null || npc.IsWounded())
                {
                    return false;
                }

                if (NpcTransform == null)
                {
                    DisableShouldThink();
                    Destroy(this);
                    return false;
                }

                if (player.IsKilled() || IsVanished(player))
                {
                    return false;
                }

                if (AttackTarget == player)
                {
                    return true;
                }

                TrySetKnown(player);
                npc.lastAttacker = player;
                AttackTarget = player;
                AttackTransform = player.transform;

                if (converge)
                {
                    Converge();
                }

                return true;
            }

            private bool TryReturnHome()
            {
                if (isStationary || IsInEventRange(ServerPosition))
                {
                    return true;
                }

                if ((!Settings.CanShoot && !IsInEventRange(AttackPosition) && !IsInEventRange(ServerPosition)) || !IsInTargetRange(AttackPosition))
                {
                    CurrentSpeed = BaseNavigator.NavigationSpeed.Normal;

                    if (Warp())
                    {
                        npc.Heal(npc.MaxHealth());
                    }
                    else
                    {
                        DestinationOverride = GetRandomRoamPosition();
                    }

                    return true;
                }

                return false;
            }

            internal void TryToAttack()
            {
                if (unwakeable || npc == null || npc.IsWounded())
                {
                    return;
                }

                if (UsesBaseNavigation && IsDormant)
                {
                    return;
                }

                BasePlayer attacker = GetBestTarget();

                if (attacker.IsNull())
                {
                    if (UsesBaseNavigation)
                    {
                        return;
                    }

                    if (!TryReturnHome())
                    {
                        RandomMove(ServerPosition, 15f);
                    }

                    return;
                }

                if (ShouldForgetTarget(attacker))
                {
                    Forget();

                    return;
                }

                if (!SetTarget(attacker) || !CanAnySeeTarget(attacker))
                {
                    return;
                }

                if (isStationary)
                {
                    SetAimDirection();
                }
                else if (attackType == AttackType.BaseProjectile)
                {
                    TryRangedActions();
                }
                else
                {
                    TryCloseRangeActions();
                }

                SwitchToState(AIState.Attack, -1);
            }

            private void TryCloseRangeActions()
            {
                if (!ValidTarget)
                {
                    TryReturnHome();
                    SetDestination();
                    return;
                }

                CurrentSpeed = UsesBaseNavigation ? BaseNavigator.NavigationSpeed.Normal : BaseNavigator.NavigationSpeed.Fast;

                if (attackType == AttackType.Explosive)
                {
                    if (IsInAttackRange(20f))
                    {
                        RandomMove(15f);
                    }
                    else
                    {
                        MoveTowardsTarget();
                    }

                    return;
                }

                if (!IsInReachableRange())
                {
                    RandomMove(15f);
                    return;
                }

                MoveForCloseCombat();
            }

            private void TryRangedActions()
            {
                if (!ValidTarget)
                {
                    TryReturnHome();
                    SetDestination();
                    return;
                }

                CurrentSpeed = UsesBaseNavigation ? BaseNavigator.NavigationSpeed.Normal : BaseNavigator.NavigationSpeed.Fast;

                if (!CanSeeTarget(AttackTarget))
                {
                    if (ShouldMoveCloserAroundTarget())
                    {
                        MoveCloserAroundTarget();
                    }
                    else
                    {
                        MoveTowardsTarget();
                    }
                }
                else
                {
                    RandomMove(15f, 1f);
                }
            }

            private bool ShouldMoveCloserAroundTarget() => SecondsSinceLastAttack > 2f && InRange2D(ServerPosition, AttackPosition, 10f);

            private void MoveCloserAroundTarget()
            {
                const float minimumRange = 3f;
                const float closingStep = 2f;

                float distance = ServerPosition.Distance2D(AttackPosition);
                float radius = Mathf.Max(minimumRange, distance - closingStep);

                RandomMove(radius);
            }

            private void MoveTowardsTarget()
            {
                if (isMurderer && !AttackTarget.IsOnGround())
                {
                    RandomMove(10f);
                    return;
                }

                SetDestination(AttackPosition);
            }

            private void MaintainAttackRange()
            {
                float range = Mathf.Max(1.5f, attackRange - 0.5f);

                RandomMove(range, 0.5f);
            }

            private void MoveForCloseCombat()
            {
                if (isMurderer)
                {
                    MoveTowardsTarget();
                }
                else
                {
                    MaintainAttackRange();
                }
            }

            public void SetupMovement()
            {
                if (npc == null || npc.IsDestroyed)
                {
                    DisableShouldThink();
                    Destroy(this);
                    return;
                }

                if (RandomRoamPositions.IsNullOrEmpty() || spawnedInside && (BaseRoute == null || BaseRoute.Count < 2))
                {
                    isStationary = true;
                }

                double now = Time.realtimeSinceStartupAsDouble;
                lastEquipmentThinkTime = now;
                nextEquipmentThinkTime = now + 0.25d;
                nextAttackThinkTime = now + 1d;
                nextBaseRouteNavigationTime = now;
                nextRoamThinkTime = isStationary || spawnedInside ? double.MaxValue : now;
                baseRouteProgressPosition = ServerPosition;
                nextBaseRouteProgressTime = now + 4d;
                nextBaseRouteResumeTime = 0d;
                baseRoutePauseNodeIndex = -1;
                movementStarted = true;
            }

            private bool IsOnExposedBaseRoute() => raid.IsBaseRouteExposed(BaseRoute, baseRouteIndex, baseRouteDirection, ServerPosition);

            internal void InvalidateBaseRoute(int nodeIndex)
            {
                if (BaseRoute != null && BaseRoute.Contains(nodeIndex))
                {
                    baseRouteDirty = true;
                    baseRoutePauseNodeIndex = -1;
                    nextBaseRouteResumeTime = 0d;
                }
            }

            internal void TickBaseNavigation(double now)
            {
                if (!UsesBaseNavigation || now < nextBaseRouteNavigationTime || npc == null || npc.IsDestroyed || npc.IsWounded())
                {
                    return;
                }

                nextBaseRouteNavigationTime = now + 0.25d;

                if (IsDormant && !IsOnExposedBaseRoute())
                {
                    Navigator.ClearFacingDirectionOverride();

                    if (Navigator.CurrentNavigationType != BaseNavigator.NavigationType.None)
                    {
                        Navigator.SetCurrentNavigationType(BaseNavigator.NavigationType.None);
                    }

                    baseRouteProgressPosition = ServerPosition;
                    nextBaseRouteProgressTime = now + 4d;
                    baseRouteDirty = true;
                    return;
                }

                CurrentSpeed = AttackTarget == null ? BaseNavigator.NavigationSpeed.Slow : BaseNavigator.NavigationSpeed.Normal;
                Navigator.SetCurrentSpeed(CurrentSpeed);

                if (ValidTarget)
                {
                    nextBaseRouteResumeTime = 0d;
                    baseRoutePauseNodeIndex = -1;
                    CheckRouteDoors(now, DestinationOverride);
                    return;
                }

                if (now < nextBaseRouteResumeTime)
                {
                    return;
                }

                if (baseRouteDirty)
                {
                    List<int> route = raid.CreateBaseRoute(ServerPosition);
                    ResetToPool(ref BaseRoute);
                    BaseRoute = route;
                    baseRouteIndex = 0;
                    baseRouteDirection = 1;
                    baseRoutePauseNodeIndex = -1;
                    nextBaseRouteResumeTime = 0d;
                    baseRouteDirty = false;
                }

                if (BaseRoute == null || BaseRoute.Count < 2)
                {
                    return;
                }

                if (!raid.TryGetBaseRoutePosition(BaseRoute[baseRouteIndex], out var destination))
                {
                    baseRouteDirty = true;
                    return;
                }

                if (InRange(ServerPosition, destination, 0.75f))
                {
                    if (TryPauseAtBaseRouteNode(now))
                    {
                        return;
                    }

                    AdvanceBaseRouteIndex();

                    if (!raid.TryGetBaseRoutePosition(BaseRoute[baseRouteIndex], out destination))
                    {
                        baseRouteDirty = true;
                        return;
                    }
                }

                if (IsBaseRouteNavigationStuck(now, destination))
                {
                    return;
                }

                CheckRouteDoors(now, destination);

                if (DestinationOverride != destination || Navigator.CurrentNavigationType == BaseNavigator.NavigationType.None)
                {
                    SetDestination(destination, true);
                }
            }

            private bool TryPauseAtBaseRouteNode(double now)
            {
                if (AttackTarget != null || baseRoutePauseNodeIndex == baseRouteIndex)
                {
                    return false;
                }

                baseRoutePauseNodeIndex = baseRouteIndex;

                if (UnityEngine.Random.value >= 0.1f)
                {
                    return false;
                }

                nextBaseRouteResumeTime = now + UnityEngine.Random.Range(1f, 2.25f);
                Navigator.ClearFacingDirectionOverride();
                npc.SignalBroadcast(BaseEntity.Signal.Gesture, Instance.BaseRouteIdleGestures.GetRandom(), null);

                return true;
            }

            private void AdvanceBaseRouteIndex()
            {
                if (baseRouteIndex <= 0)
                {
                    baseRouteDirection = 1;
                }
                else if (baseRouteIndex >= BaseRoute.Count - 1)
                {
                    baseRouteDirection = -1;
                }

                baseRouteIndex += baseRouteDirection;
                baseRoutePauseNodeIndex = -1;
            }

            private bool IsBaseRouteNavigationStuck(double now, Vector3 destination)
            {
                if (now < nextBaseRouteProgressTime)
                {
                    return false;
                }

                nextBaseRouteProgressTime = now + 4d;

                bool stuck = !InRange(ServerPosition, destination, 0.75f) && InRange2D(ServerPosition, baseRouteProgressPosition, 0.2f);

                baseRouteProgressPosition = ServerPosition;

                if (!stuck)
                {
                    return false;
                }

                baseRouteDirty = true;
                Navigator.ClearFacingDirectionOverride();

                if (Navigator.CurrentNavigationType != BaseNavigator.NavigationType.None)
                {
                    Navigator.SetCurrentNavigationType(BaseNavigator.NavigationType.None);
                }

                return true;
            }

            private void CheckRouteDoors(double now, Vector3 destination)
            {
                if (now < nextDoorCheckTime || IsDormant)
                {
                    return;
                }

                nextDoorCheckTime = now + 1d;

                if (now < lastDoorCheckTime)
                {
                    return;
                }

                raid.OpenDoorsForNpc(this, ServerPosition, destination);
            }

            internal void TryToRoam()
            {
                if (npc == null || npc.IsDestroyed || npc.IsWounded())
                {
                    return;
                }

                if (Settings.KillUnderwater && npc.playerCollider != null && npc.IsSwimming())
                {
                    DisableShouldThink();
                    SafelyKillNpc(npc);
                    Destroy(this);
                    return;
                }

                if (ValidTarget && (attackType == AttackType.Explosive || CanSeeTarget(AttackTarget)))
                {
                    return;
                }

                CurrentSpeed = BaseNavigator.NavigationSpeed.Normal;

                if (spawnedInside)
                {
                    TickBaseNavigation(Time.realtimeSinceStartupAsDouble);
                }
                else SetDestination();
            }

            public bool Warp()
            {
                if (isKilled || Time.timeAsDouble < lastWarpTime)
                {
                    return false;
                }

                Forget(false);

                DestinationOverride = RandomNearPositions.GetRandom();

                if (!npc.IsWounded() && Navigator.Warp(DestinationOverride))
                {
                    lastWarpTime = Time.timeAsDouble + 15d;
                    return true;
                }

                lastWarpTime = Time.timeAsDouble + 1d;
                return false;
            }

            private void UseFlameThrower()
            {
                if (flameThrower == null || npc == null)
                {
                    return;
                }
                if (flameThrower.ammo < flameThrower.maxAmmo * 0.25)
                {
                    flameThrower.SetFlameState(false);
                    flameThrower.ServerReload();
                    return;
                }
                npc.triggerEndTime = Time.time + attackCooldown;
                flameThrower.SetFlameState(true);
                flameThrower.Invoke(() => flameThrower.SetFlameState(false), 2f);
            }

            private void UseWaterGun()
            {
                if (Physics.Raycast(npc.eyes.BodyRay(), out var hit, 10f, 1218652417))
                {
                    WaterBall.DoSplash(hit.point, 2f, ItemManager.FindItemDefinition("water"), 10);
                    DamageUtil.RadiusDamage(npc, liquidWeapon.LookupPrefab(), hit.point, 0.15f, 0.15f, new(), 131072, true);
                }
            }

            private void UseChainsaw()
            {
                AttackEntity.TopUpAmmo();
                AttackEntity.ServerUse();
                AttackTarget.Hurt(10f * AttackEntity.npcDamageScale, DamageType.Bleeding, npc);
            }

            private void EmulatedFire()
            {
                if (launcher.HasAttackCooldown()) return;
                float dist;
                string prefab;
                switch (launcher.ShortPrefabName)
                {
                    case "rocket_launcher.entity":
                        prefab = "assets/prefabs/ammo/rocket/rocket_basic.prefab";
                        dist = ServerPosition.Distance(AttackPosition);
                        launcher.repeatDelay = 6f;
                        break;
                    case "mgl.entity":
                        prefab = "assets/prefabs/ammo/40mmgrenade/40mm_grenade_he.prefab";
                        launcher.repeatDelay = 4f;
                        dist = ServerPosition.Distance(AttackPosition) + 5f;
                        break;
                    default: return;
                }
                Vector3 euler = launcher.MuzzlePoint.transform.forward + Vector3.up;
                Vector3 position = launcher.MuzzlePoint.transform.position + (Vector3.up * 1.6f);
                BaseEntity entity = GameManager.server.CreateEntity(prefab, position, owner.eyes.GetLookRotation());
                if (entity == null) return;
                entity.creatorEntity = owner;
                if (entity.TryGetComponent(out ServerProjectile serverProjectile))
                {
                    serverProjectile.InitializeVelocity(Quaternion.Euler(euler) * entity.transform.forward * dist);
                }
                if (entity.Is(out TimedExplosive explosive))
                {
                    explosive.timerAmountMin = 1;
                    explosive.timerAmountMax = 15;
                }
                entity.Spawn();
                launcher.StartAttackCooldown(launcher.repeatDelay);
            }

            private void MeleeAttack()
            {
                if (baseMelee.IsNull())
                {
                    return;
                }

                if (AttackEntity is Chainsaw)
                {
                    UseChainsaw();
                    return;
                }

                Vector3 position = AttackPosition;
                AttackEntity.StartAttackCooldown(AttackEntity.repeatDelay * 2f);
                npc.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty, null);
                if (baseMelee.swingEffect.isValid)
                {
                    Effect.server.Run(baseMelee.swingEffect.resourcePath, position, Vector3.forward, npc.Connection, false);
                }
                HitInfo info = new()
                {
                    damageTypes = new(),
                    DidHit = true,
                    Initiator = npc,
                    HitEntity = AttackTarget,
                    HitPositionWorld = position,
                    HitPositionLocal = AttackTransform.InverseTransformPoint(position),
                    HitNormalWorld = npc.eyes.BodyForward(),
                    HitMaterial = StringPool.Get("Flesh"),
                    PointStart = ServerPosition,
                    PointEnd = position,
                    Weapon = AttackEntity,
                    WeaponPrefab = AttackEntity
                };

                info.damageTypes.Set(DamageType.Fall, 0f);
                info.damageTypes.Set(DamageType.Slash, baseMelee.TotalDamage() * AttackEntity.npcDamageScale);
                Effect.server.ImpactEffect(info);
                AttackTarget.OnAttacked(info);
            }

            public bool TryThrowWeapon()
            {
                if (!IsInThrowRange() || !(AttackEntity is ThrownWeapon thrownWeapon))
                {
                    return false;
                }

                npc.SetAiming(true);
                SetAimDirection();

                npc.Invoke(() =>
                {
                    if (!ValidTarget)
                    {
                        CurrentSpeed = UsesBaseNavigation ? BaseNavigator.NavigationSpeed.Slowest : BaseNavigator.NavigationSpeed.Normal;

                        Forget();
                        SetDestination();
                        npc.SetAiming(false);

                        return;
                    }

                    if (IsInThrowRange())
                    {
                        Item item = thrownWeapon.GetItem();
                        if (item != null) item.amount++;
                        thrownWeapon.ServerThrow(AttackPosition);
                    }
                    else nextAttackTime = Time.realtimeSinceStartupAsDouble + 1d;

                    npc.SetAiming(false);
                    RandomMove(15f);
                }, 1f);

                return true;
            }

            private bool CanConverge(HumanoidNPC other)
            {
                if (ValidTarget || other.IsKilled() || other.IsDead()) return false;
                return IsInTargetRange(other.transform.position);
            }

            private bool CanAnySeeTarget(BasePlayer target)
            {
                foreach (var npc in raid.npcs)
                {
                    if (npc != null && !npc.IsDestroyed && npc != this.npc && npc.Brain != null && npc.Brain.AttackTarget == target && npc.Brain.SecondsSinceLastAttack < 2)
                    {
                        return true;
                    }
                }
                return CanSeeTarget(target);
            }

            private bool HasLineOfSight(BasePlayer target)
            {
                if (isStationary)
                {
                    return Senses.Memory.IsLOS(target);
                }

                if (attackType == AttackType.Explosive && raid.Options.NPC.CounterRaid && raid.IsInForwardOperatingBase(target.transform.position))
                {
                    return true;
                }

                if (Navigator.CurrentNavigationType == BaseNavigator.NavigationType.None && attackType is AttackType.FlameThrower or AttackType.Melee)
                {
                    return true;
                }

                return Senses.Memory.IsLOS(target);
            }

            private bool CanSeeTarget(BasePlayer target)
            {
                if (HasLineOfSight(target))
                {
                    return true;
                }

                nextAttackTime = Time.timeAsDouble + 1d;

                return false;
            }

            public bool CanRoam(Vector3 destination) => destination == DestinationOverride && IsInSenseRange(destination);

            private bool CanShoot()
            {
                if (attackType == AttackType.None)
                {
                    return false;
                }

                return Settings.CanShoot || attackType != AttackType.BaseProjectile && attackType != AttackType.Explosive || IsInEventRange(AttackPosition);
            }

            private void TrySetKnown(BasePlayer player)
            {
                if (Senses.ownerAttack != null && !Senses.Memory.IsPlayerKnown(player) && !Senses.Memory.Targets.Contains(player))
                {
                    Senses.Memory.SetKnown(player, npc, Senses);
                }
            }

            public BasePlayer GetBestTarget()
            {
                if (npc.IsWounded())
                {
                    return null;
                }
                if (AttackTarget != null)
                {
                    return AttackTarget;
                }
                float sqrSenseRange = SenseRange * SenseRange;
                float delta = -1f;
                BasePlayer target = null;
                foreach (var entity in Senses.Memory.Players)
                {
                    if (!entity.Is(out BasePlayer player) || ShouldForgetTarget(player) || !IsInSenseRange(player.transform.position)) continue;
                    if (!config.Settings.Management.TargetNpcs && !player.IsHuman()) continue;
                    float sqrDist = (player.transform.position - npc.transform.position).sqrMagnitude;
                    float rangeDelta = 1f - Mathf.InverseLerp(1f, sqrSenseRange, sqrDist);
                    rangeDelta += HasLineOfSight(player) ? 2f : 0f;
                    if (rangeDelta <= delta) continue;
                    target = player;
                    delta = rangeDelta;
                }
                if (delta <= 0)
                    return null;
                return target;
            }

            private bool IsAttackOnCooldown()
            {
                if (isStationary && attackType == AttackType.BaseProjectile)
                {
                    return false;
                }

                if (attackType == AttackType.None || Time.timeAsDouble < nextAttackTime)
                {
                    return true;
                }

                if (attackCooldown > 0d)
                {
                    nextAttackTime = Time.timeAsDouble + attackCooldown;
                }

                return false;
            }

            private Vector3 GetRandomRoamPosition() => RandomRoamPositions.GetRandom();

            private bool CanUseNavMesh() => !isStationary && Navigator.CanUseNavMesh && !Navigator.StuckOffNavmesh;

            private bool IsInAttackRange(float range = 0f) => InRange(ServerPosition, AttackPosition, range == 0f ? attackRange : range);

            private bool IsInEventRange(Vector3 destination) => InRange(raid.Location, destination, Mathf.Min(raid.ProtectionRadius, TargetLostRange));

            private bool IsInReachableRange() => AttackPosition.y - ServerPosition.y <= attackRange && (attackType != AttackType.Melee || InRange(AttackPosition, ServerPosition, 15f));

            private bool IsInSenseRange(Vector3 destination) => InRange2D(raid.Location, destination, SenseRange);

            private bool IsInTargetRange(Vector3 destination) => InRange2D(raid.Location, destination, !Settings.CanShoot ? Mathf.Min(raid.ProtectionRadius, TargetLostRange) : TargetLostRange);

            private bool IsInChaseRange(Vector3 destination) => InRange(raid.Location, destination, !Settings.CanShoot || !Settings.CanLeave ? raid.ProtectionRadius : isMurderer ? TargetLostRange : ScientistChaseRange);

            private bool IsInThrowRange() => InRange(ServerPosition, AttackPosition, attackRange);

            private bool ShouldForgetTarget(BasePlayer target) => target.IsKilled() || target.health <= 0f || IsVanished(target) || target.IsDead() || target.skinID == RB_SKIN_ID || !IsInTargetRange(target.transform.position);
        }

        public class Raider
        {
            public bool HasDestroyed, IsAdmin, IsFlying, IsVanished, IsAllowed, IsAlly, IsParticipant, PreEnter = true, eligible = true, rewards = true;
            public double participantTime, lastActiveTime;
            public float TotalDamage;
            public string id, displayName;
            public ulong userid;
            public PlayerInputEx Input;
            private BasePlayer _player;
            public Vector3 lastPosition;
            public IClan clan;
            public long clanId;
            public BasePlayer player { get { if (_player == null) { _player = RustCore.FindPlayerById(userid); } return _player; } }
            public bool IsConnected => player != null && player.IsConnected;
            public bool IsInRustClanWith(ulong other)
            {
                if (player != null) clanId = player.clanId;
                if (clan == null)
                {
                    if (clanId != 0) ClanManager.ServerInstance?.Backend?.TryGet(clanId, out clan);
                    if (clan == null) return false;
                }
                bool foundSelf = clan.Creator == userid;
                bool foundOther = clan.Creator == other;
                foreach (var member in clan.Members)
                {
                    if (member.SteamId == userid) foundSelf = true;
                    if (member.SteamId == other) foundOther = true;
                    if (foundSelf && foundOther) break;
                }
                return foundSelf && foundOther;
            }
            public Raider(ulong userid, string username, bool admin)
            {
                IsAdmin = admin;
                this.userid = userid;
                id = userid.ToString();
                displayName = username;
            }
            public Raider(BasePlayer target)
            {
                _player = target;
                userid = target.userID;
                id = target.UserIDString;
                IsAdmin = target.IsAdmin;
                displayName = target.displayName;
                if (target.clanId != 0)
                {
                    clan = GetClan(target);
                    clanId = target.clanId;
                }
            }
            public void DestroyInput()
            {
                if (Input != null)
                {
                    Input.enabled = false;
                    UnityEngine.Object.Destroy(Input);
                    Input = null;
                }
            }
            public void CheckInput(BasePlayer player, RaidableBase raid)
            {
                if (Input != null || player == null || !player.IsConnected)
                {
                    return;
                }

                _player = player;
                Input = player.GetComponent<PlayerInputEx>();
                if (Input == null)
                {
                    Input = player.gameObject.AddComponent<PlayerInputEx>();
                    Input.Setup(raid, this);
                }
            }
        }

        public class RaidableBase : FacepunchBehaviour
        {
            public HashSet<ulong> alliance = Pool.Get<HashSet<ulong>>();
            public HashSet<ulong> cooldowns = Pool.Get<HashSet<ulong>>();
            public HashSet<ulong> intruders = Pool.Get<HashSet<ulong>>();
            public Dictionary<ulong, Raider> raiders = Pool.Get<Dictionary<ulong, Raider>>();
            public Dictionary<ItemId, float> conditions = Pool.Get<Dictionary<ItemId, float>>();
            internal List<Fridge> fridges = Pool.Get<List<Fridge>>();
            internal HashSet<StorageContainer> _containers = new(), _allcontainers = new();
            internal HashSet<IItemContainerEntity> _iice = new();
            public List<HumanoidNPC> npcs = Pool.Get<List<HumanoidNPC>>();
            public List<PressButton> buttons = Pool.Get<List<PressButton>>();
            public List<WeaponRack> weaponRacks = Pool.Get<List<WeaponRack>>();
            public List<BackpackData> backpacks = Pool.Get<List<BackpackData>>();
            public List<Vector3> compound = new(), foundations = new(), floors = new();
            public List<BaseEntity> locks = Pool.Get<List<BaseEntity>>();
            private List<BuildingBlock> blocks = Pool.Get<List<BuildingBlock>>();
            private List<Vector3> _inside = Pool.Get<List<Vector3>>();
            private List<SphereEntity> spheres = Pool.Get<List<SphereEntity>>();
            private List<IOEntity> lights = Pool.Get<List<IOEntity>>();
            private List<BaseOven> ovens = Pool.Get<List<BaseOven>>();
            public Dictionary<ulong, TurretInfo> turrets = Pool.Get<Dictionary<ulong, TurretInfo>>();
            private List<Door> doors = Pool.Get<List<Door>>();
            public List<string> ids = Pool.Get<List<string>>();
            private List<CustomDoorManipulator> doorControllers = Pool.Get<List<CustomDoorManipulator>>();
            private List<Locker> lockers = Pool.Get<List<Locker>>();
            private List<Vector3> NpcSpawnExclusions = Pool.Get<List<Vector3>>();
            private List<BaseEntity> _decorDeployables = Pool.Get<List<BaseEntity>>();
            private List<BaseRouteNode> _baseRouteNodes = new();
            private List<Vector3> _baseStairs = new();
            private Dictionary<NetworkableId, int> _baseRouteLookup = new();
            private RaycastHit[] _baseRouteHitBuffer = new RaycastHit[32];
            private const float BaseRouteInnerLaneOffset = 0.6f;
            private const float BaseRouteOuterLaneOffset = 1f;
            private const float BaseRouteAgentRadius = 0.35f;
            private bool _baseRoutesBuilt;
            private Dictionary<string, Dictionary<SkinType, ulong>> _shortnameToSkin = Pool.Get<Dictionary<string, Dictionary<SkinType, ulong>>>();
            private Dictionary<uint, ulong> _prefabToSkin = Pool.Get<Dictionary<uint, ulong>>();
            private Dictionary<int, ulong> _itemIdToSkin = Pool.Get<Dictionary<int, ulong>>();
            internal Dictionary<TriggerBase, BaseEntity> triggers = Pool.Get<Dictionary<TriggerBase, BaseEntity>>();
            private List<SleepingBag> _beds = Pool.Get<List<SleepingBag>>();
            private Dictionary<SleepingBag, ulong> _bags = Pool.Get<Dictionary<SleepingBag, ulong>>();
            private List<BaseCombatEntity> _allRugs = Pool.Get<List<BaseCombatEntity>>();
            private List<BaseCombatEntity> _rugSpawns = Pool.Get<List<BaseCombatEntity>>();
            public List<SamSite> samsites = Pool.Get<List<SamSite>>();
            public List<VendingMachine> vms = Pool.Get<List<VendingMachine>>();
            public List<DamageMultiplier> PlayerDamageMultiplier = new();
            public List<ulong> HintCooldowns = Pool.Get<List<ulong>>();
            private List<BuildingPrivlidge> privs = Pool.Get<List<BuildingPrivlidge>>(); 
            public BuildingPrivlidge priv;
            public List<ulong> TeleportExceptions = new();
            private List<string> murdererKits = new(), scientistKits = new();
            private MapMarkerExplosion explosionMarker;
            private MapMarkerGenericRadius genericMarker;
            private VendingMachineMapMarker vendingMarker;
            public Coroutine setupRoutine, turretsCoroutine;
            public GameObject go;
            private bool IsPrivDestroyed;
            public bool IsDespawning;
            public Vector3 Location;
            public Vector3 LocationXZ3D;
            public string ProfileName;
            public float BaseHeight;
            public string BaseName;
            public Color NoneColor;
            public bool ownerFlag;
            public string ID = "0";
            public ulong ownerId;
            public string ownerName;
            public float loadTime;
            public DateTime spawnDateTime, despawnDateTime = DateTime.MaxValue;
            private double despawnDeadline = double.PositiveInfinity;
            public float AddNearTime;
            public bool AllowPVP;
            public BuildingOptions Options;
            public bool FloorsAreFoundations;
            public bool IsAuthed;
            public bool IsOpened = true;
            public bool IsResetting;
            public bool IsPayLocked;
            public RaidableType Type;
            public bool IsLoading;
            public bool IsWaterSpawn;
            public bool InitiateTurretOnSpawn;
            private bool markerCreated;
            private int itemAmountSpawned;
            public bool privSpawned;
            public bool privHadLoot;
            public string markerName;
            public string NoMode;
            public bool IsEngaged;
            public int _undoLimit;
            public Dictionary<NetworkableId, RaidElevator> Elevators = Pool.Get<Dictionary<NetworkableId, RaidElevator>>();
            private const string ElevatorHealthPanel = "RB_UI_Elevator";
            private const string ElevatorHealthValue = "RB_UI_Elevator_HealthValue";
            private const string ElevatorHealthTrack = "RB_UI_Elevator_HealthTrack";
            private const string ElevatorHealthFill = "RB_UI_Elevator_HealthFill";
            private Dictionary<ulong, ElevatorHealthUiState> _elevatorHealthUi = new();
            public HashSet<BaseEntity> Entities = new(), DespawnExceptions = new(), BuiltList = new(), RaidEntities = new();
            public RaidableSpawns spawns;
            public RandomBase rb = new();
            public float RemoveNearDistance;
            public bool IsAnyLooted;
            public bool IsDamaged;
            public bool IsEligible = true;
            public bool IsCompleted;
            public Payments payments = new();
            public float ProtectionRadius = 50f, SqrProtectionRadius = 2500f;
            public RaidableBases Instance;
            public bool stability;
            public int lootAmountTracked;
            private int lootAmountCounted;
            public List<ulong> NotifiedNearby = new();
            public BasePlayer cached_attacker;
            public ulong cached_attacker_id;
            public double cached_attack_time;
            internal bool protectionStarted;
            internal double nextProtectorTime;
            private ProtectorQuery protectorQuery = new();
            private ProtectorQuery synchronousProtectorQuery = new();

            private class ElevatorHealthUiState
            {
                internal BasePlayer Player;
                internal NetworkableId ElevatorId;
                internal string HealthText;
                internal float HealthFraction;
                internal bool ShowHealthBar;
            }

            private class BaseRouteNode
            {
                internal NetworkableId FloorId;
                internal Vector3 Position;
                internal bool IsExposed;
                internal List<int> Neighbors = new();
                internal bool IsValid = true;

                internal BaseRouteNode(NetworkableId floorId, Vector3 position, bool isExposed)
                {
                    FloorId = floorId;
                    Position = position;
                    IsExposed = isExposed;
                }
            }

            private class ProtectorQuery
            {
                internal List<Raider> Raiders = new();
                internal int RaiderIndex;
                internal int Step;
                internal bool Started, RaidersInitialized;

                internal void Reset()
                {
                    Raiders.Clear();
                    RaiderIndex = 0;
                    Step = 0;
                    Started = false;
                    RaidersInitialized = false;
                }
            }

            public float ProtectionRadiusSqr(float tolerance) => (ProtectionRadius + tolerance) * (ProtectionRadius + tolerance);
            public bool EjectBackpacksPVE => !AllowPVP && Options.EjectBackpacksPVE;
            public bool PlayersLootable => AllowPVP ? config.Settings.Management.PlayersLootableInPVP : config.Settings.Management.PlayersLootableInPVE;
            public List<string> BlacklistedCommands => AllowPVP ? Options.BlacklistedPVPCommands : Options.BlacklistedPVECommands;
            public SpawnsControllerManager SpawnsController => Instance.SpawnsController;
            public StoredData data => Instance.data;
            public Configuration config => Instance.config;
            public bool IsUnloading => Instance.IsUnloading;
            public bool IsShuttingDown => Instance.IsShuttingDown;

            private double nextHookTime;
            private object[] _hookObjects;
            public object[] hookObjects
            {
                get
                {
                    double time = Time.timeAsDouble;
                    if (time > nextHookTime)
                    {
                        nextHookTime = time + 0.1d;
                        _hookObjects = new object[17] { Location, Options.Level, AllowPVP, ID, 0f, 0f, loadTime, ownerId, GetOwner(), GetRaiders(), GetIntruders(), Entities.ToList(), BaseName, spawnDateTime, despawnDateTime, ProtectionRadius, GetLootAmountCounted() };
                    }
                    return _hookObjects;
                }
            }

            public int DespawnMinutes => Options.DespawnOptions.OverrideConfig ? Options.DespawnOptions.DespawnMinutes : config.Settings.Management.DespawnMinutes;

            public bool DespawnMinutesReset => Options.DespawnOptions.OverrideConfig ? Options.DespawnOptions.DespawnMinutesReset : config.Settings.Management.DespawnMinutesReset;

            public int DespawnMinutesInactive => Options.DespawnOptions.OverrideConfig ? Options.DespawnOptions.DespawnMinutesInactive : config.Settings.Management.DespawnMinutesInactive;

            public bool DespawnMinutesInactiveReset => Options.DespawnOptions.OverrideConfig ? Options.DespawnOptions.DespawnMinutesInactiveReset : config.Settings.Management.DespawnMinutesInactiveReset;

            public bool EngageOnBaseDamage => Options.DespawnOptions.OverrideConfig ? Options.DespawnOptions.Engaged : config.Settings.Management.Engaged;

            public bool EngageOnNpcDeath => Options.DespawnOptions.OverrideConfig ? Options.DespawnOptions.EngagedNpc : config.Settings.Management.EngagedNpc;

            public string GetPercentCompleteMessage() => IsDespawning ? "DESPAWNING" : IsLoading ? "LOADING" : string.Join(", ", GetRaiders().Select(x => x.displayName)) is string str && !string.IsNullOrEmpty(str) ? str : "INACTIVE";

            public double GetPercentComplete() => IsDespawning ? 100.0 : IsLoading || lootAmountTracked <= 0 ? 0.0 : Math.Max(0.0, Math.Round(((lootAmountTracked - (double)GetLootAmountCounted()) / lootAmountTracked) * 100.0, 2));

            public int GetLootAmountCounted() => lootAmountCounted;

            internal int UpdateLootAmountCounted()
            {
                int amount = 0;

                foreach (StorageContainer container in _containers)
                {
                    if (IsContainerKilled(container))
                    {
                        continue;
                    }

                    amount += container.inventory.itemList.Count;
                }

                if (lootAmountCounted != amount)
                {
                    lootAmountCounted = amount;

                    if (_hookObjects?.Length > 16)
                    {
                        _hookObjects[16] = amount;
                    }
                }

                if (amount > lootAmountTracked)
                {
                    lootAmountTracked = amount;
                }

                return amount;
            }

            public bool Has(BaseEntity entity) => Instance.Get(entity, out var raid) && raid == this;

            public bool IsBox(BaseEntity entity, bool inherit) => Instance.IsBox(entity, inherit);

            public string FormatGridReference(BasePlayer player, Vector3 v) => Instance.FormatGridReference(player, v);

            public bool IsRaider(BasePlayer target) => intruders.Contains(target.userID) || raiders.ContainsKey(target.userID);

            private void OnDestroy()
            {
                Despawn();
            }

            public bool CanDropRustBackpack(ulong userid)
            {
                if (AllowPVP ? config.Settings.Management.RustBackpacksPVP : config.Settings.Management.RustBackpacksPVE)
                {
                    return !userid.HasPermission("raidablebases.keepbackpackrust") && raiders.TryGetValue(userid, out var ri);
                }
                return false;
            }

            public bool CanDropBackpack(ulong userid)
            {
                if (AllowPVP ? config.Settings.Management.BackpacksPVP : config.Settings.Management.BackpacksPVE)
                {
                    return !userid.HasPermission("raidablebases.keepbackpackplugin") && raiders.TryGetValue(userid, out var ri);
                }
                return false;
            }

            public void UpdateUi(BasePlayer player, UiType type) => Instance.UI.UpdateUi(player, type);

            public void DestroyUi(BasePlayer player, UiType type) => Instance.UI.DestroyUi(player, type);

            public Raider GetRaider(BasePlayer player)
            {
                if (!raiders.TryGetValue(player.userID, out var ri))
                {
                    raiders[player.userID] = ri = new(player);
                }
                return ri;
            }

            public bool CanHurtBox(BaseEntity entity)
            {
                if (Options.InvulnerableUntilCupboardIsDestroyed && IsBox(entity, false) && !priv.IsKilled()) return false;
                if (Options.Invulnerable && IsBox(entity, false)) return false;
                return true;
            }

            public void DestroyGroundCheck(BaseEntity entity)
            {
                if (entity.GetParentEntity() is Tugboat) return;
                if (entity.TryGetComponent<GroundWatch>(out var obj1)) Destroy(obj1);
                if (entity.TryGetComponent<DestroyOnGroundMissing>(out var obj2)) Destroy(obj2);
            }

            public void SetupEntity(BaseEntity entity, bool skipCheck = true)
            {
                if (entity == null) return;
                if (entity.net == null) entity.net = Net.sv.CreateNetworkable();
                if (skipCheck) AddEntity(entity);
            }

            internal HashSet<NetworkableId> EntityIds = Pool.Get<HashSet<NetworkableId>>();
            public void AddEntity(BaseEntity entity)
            {
                if (TryGetNetworkId(entity, out NetworkableId id))
                {
                    EntityIds.Add(id);
                    Entities.Add(entity);
                    Instance.EntityToRaid[id] = this;
                }
            }

            public void FreeToPool()
            {
                HarmonyModInterface.CallHook("OnRaidableBaseEnded", hookObjects);

                if (EntityIds != null)
                {
                    foreach (var id in EntityIds)
                    {
                        Instance.EntityToRaid.Remove(id);
                    }
                }

                ResetToPool(ref EntityIds);
                ResetToPool(ref ids);
                ResetToPool(ref vms);
                ResetToPool(ref npcs);
                ResetToPool(ref _bags);
                ResetToPool(ref _beds);
                ResetToPool(ref doors);
                ResetToPool(ref locks);
                ResetToPool(ref ovens);
                ResetToPool(ref blocks);
                ResetToPool(ref lights);
                ResetToPool(ref lockers);
                ResetToPool(ref raiders);
                ResetToPool(ref spheres);
                ResetToPool(ref turrets);
                ResetToPool(ref fridges);
                ResetToPool(ref _inside);
                ResetToPool(ref buttons);
                ResetToPool(ref _allRugs);
                ResetToPool(ref alliance);
                ResetToPool(ref samsites);
                ResetToPool(ref triggers);
                ResetToPool(ref Elevators);
                ResetToPool(ref intruders);
                ResetToPool(ref cooldowns);
                ResetToPool(ref conditions);
                ResetToPool(ref weaponRacks);
                ResetToPool(ref HintCooldowns);
                ResetToPool(ref _prefabToSkin);
                ResetToPool(ref _itemIdToSkin);
                ResetToPool(ref doorControllers);
                ResetToPool(ref privs);
                ResetToPool(ref _shortnameToSkin);
                ResetToPool(ref _decorDeployables);
                ResetToPool(ref NpcSpawnExclusions);
                ResetToPool(ref _rugSpawns);

                _baseRouteNodes.Clear();
                _baseStairs.Clear();
                _baseRouteLookup.Clear();
                _baseRoutesBuilt = false;

                protectorQuery.Reset();
                synchronousProtectorQuery.Reset();

                if (backpacks != null)
                {
                    for (var i = 0; i < backpacks.Count; i++)
                    {
                        var backpack = backpacks[i];
                        ResetToPool(ref backpack);
                    }
                }

                ResetToPool(ref backpacks);

                _respawns.Clear();
                ExtendHookSubscription = false;
            }

            public void Notify(string key)
            {
                foreach (var raider in raiders.Values)
                {
                    Notify(raider.player, key);
                }
            }

            public void Notify(BasePlayer player, string key) => Instance.SendNotification(player, key);

            public void Notify<T>(BasePlayer player, string key, T arg) => Instance.SendNotification(player, key, arg);

            public void Notify<T1, T2>(BasePlayer player, string key, T1 arg, T2 arg2) => Instance.SendNotification(player, key, arg, arg2);

            public void NotifyOnce(BasePlayer player, string key) => Instance.NotifyOnce(player, key);

            public void NotifyOnce<T>(BasePlayer player, string key, T arg) => Instance.NotifyOnce(player, key, arg);

            public void NotifyOnce<T1, T2>(BasePlayer player, string key, T1 arg, T2 arg2) => Instance.NotifyOnce(player, key, arg, arg2);

            public void NotifyUnlessSmart(BasePlayer player, string key)
            {
                if (!Options.Smart)
                {
                    Instance.SendNotification(player, key);
                }
            }

            public void NotifyUnlessSmart<T>(BasePlayer player, string key, T arg)
            {
                if (!Options.Smart)
                {
                    Instance.SendNotification(player, key, arg);
                }
            }

            public void NotifyUnlessSmart<T1, T2>(BasePlayer player, string key, T1 arg, T2 arg2)
            {
                if (!Options.Smart)
                {
                    Instance.SendNotification(player, key, arg, arg2);
                }
            }

            public void NotifyUnlessSmart<T1, T2, T3>(BasePlayer player, string key, T1 arg, T2 arg2, T3 arg3)
            {
                if (!Options.Smart)
                {
                    Instance.SendNotification(player, key, arg, arg2, arg3);
                }
            }

            public string mx(string key, string id = null, params object[] args) => Instance.mx(key, id, args);

            public void SetupCollider()
            {
                go.transform.position = Location;
                go.layer = (int)Layer.Reserved1;

                if (!go.TryGetComponent<SphereCollider>(out var collider))
                {
                    collider = go.AddComponent<SphereCollider>();
                }

                if (collider != null)
                {
                    collider.radius = ProtectionRadius;
                    collider.isTrigger = true;
                    collider.center = Vector3.zero;
                }

                if (!go.TryGetComponent<Rigidbody>(out var rigidbody))
                {
                    rigidbody = go.AddComponent<Rigidbody>();
                }

                if (rigidbody != null)
                {
                    rigidbody.isKinematic = true;
                    rigidbody.useGravity = false;
                    rigidbody.detectCollisions = true;
                    rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                }
            }

            public HashSet<BaseEntity> enteredEntities = new();

            private void OnTriggerEnter(Collider collider)
            {
                if (collider == null || collider.ObjectName() == "ZoneManager")
                    return;

                var entity = collider.ToBaseEntity();
                if (entity == null || entity.IsDestroyed)
                    return;

                if (IsUnderground(entity.transform.position))
                    return;

                switch (entity)
                {
                    case BasePlayer player when player.IsHuman() && !(player.GetMounted() is ZiplineMountable):
                        HandlePlayerEntering(player);
                        break;

                    case BaseMountable mount when !(mount is ZiplineMountable) && !(mount is BaseChair && !mount.OwnerID.IsSteamId()):
                        HandleMountableEntering(mount);
                        break;

                    case HotAirBalloon hab:
                        HandleHotAirBalloonEntering(hab);
                        break;

                    default:
                        HandleDefaultEntity(entity, Options.Mounts.Other);
                        break;
                }
            }

            private void HandleDefaultEntity(BaseEntity entity, bool enabled)
            {
                if (enabled && IsCustomEntity(entity))
                {
                    Eject(entity, Location, ProtectionRadius + 15f, false);
                }
                else if (entity is Drone && !(entity is DeliveryDrone))
                {
                    if (Options.Mounts.Drones) Eject(entity, Location, ProtectionRadius + 15, false);
                    else BuiltList.Add(entity);
                }
                else if (Options.Mounts.RFExplosivesAboveDome && entity.Is(out RFTimedExplosive te) && NearFoundation(entity.transform.position, 15f))
                {
                    HandleEntityToItem(te);
                }
            }

            private void OnTriggerExit(Collider collider)
            {
                if (collider == null || collider.ObjectName() == "ZoneManager")
                    return;

                var entity = collider.ToBaseEntity();
                if (entity == null)
                {
                    if (!ReferenceEquals(entity, null))
                    {
                        enteredEntities.Remove(entity);
                    }
                    return;
                }

                if (!enteredEntities.Remove(entity) || entity.IsDestroyed)
                    return;

                switch (entity)
                {
                    case BasePlayer player:
                        HandlePlayerExiting(player);
                        break;

                    case BaseMountable mount:
                        HandleMountableExiting(mount);
                        break;

                    case HotAirBalloon hab:
                        HandleHotAirBalloonExiting(hab);
                        break;
                }
            }

            public void HandleEntityToItem(RFTimedExplosive te)
            {
                if (InRange(te.transform.position, Location, ProtectionRadius - 3f))
                {
                    return;
                }
                ItemDefinition itemToGive = te.pickupDefinition;
                if (itemToGive == null)
                {
                    Eject(te, Location, ProtectionRadius + 15f, false);
                    return;
                }
                Item item = ItemManager.Create(itemToGive, 1, te.skinID);
                if (te.ItemOwnership.IsValid())
                {
                    item.SetItemOwnership(te.ItemOwnership);
                }
                item.Drop(te.transform.position, te.GetDropVelocity());
                te.Invoke(te.SafelyKill, 0.01f);
            }

            public void HandlePlayerEntering(BasePlayer player)
            {
                if (enteredEntities.Add(player))
                {
                    OnPreEnterRaid(player);
                }
            }

            public void HandlePlayerExiting(BasePlayer player)
            {
                OnPlayerExit(player, player.IsDead());
                intruders.Remove(player.userID);
                enteredEntities.Remove(player);
            }

            private void HandleMountableEntering(BaseMountable m)
            {
                if (enteredEntities.Add(m))
                {
                    using var players = GetMountedPlayers(m);

                    if (TryRemoveMountable(m, players))
                    {
                        players.ForEach(HandlePlayerExiting);
                    }
                }
            }

            private void HandleMountableExiting(BaseMountable m)
            {
                using var players = GetMountedPlayers(m);
                players.ForEach(HandlePlayerExiting);
                if (players.Count > 0)
                {
                    RemoveMountedEntity(m);
                }
            }

            private void HandleHotAirBalloonEntering(HotAirBalloon hab)
            {
                if (enteredEntities.Add(hab))
                {
                    using var players = GetMountedPlayers(hab);

                    if (TryRemoveMountable(hab, players))
                    {
                        players.ForEach(HandlePlayerExiting);
                    }
                }
            }

            private void HandleHotAirBalloonExiting(HotAirBalloon hab)
            {
                using var players = GetMountedPlayers(hab);

                players.ForEach(HandlePlayerExiting);

                if (players.Count > 0)
                {
                    RemoveMountedEntity(hab);
                }
            }

            public void RemoveMountedEntity(BaseEntity entity)
            {
                if (!config.Settings.Management.DespawnMounts && entity != null)
                {
                    if (entity.skinID == RB_SKIN_ID) entity.skinID = 0;
                    DespawnExceptions.Add(entity);
                    BuiltList.Add(entity);
                    if (entity.children != null)
                    {
                        foreach (var child in entity.children)
                        {
                            if (child.skinID == RB_SKIN_ID) child.skinID = 0;
                            BuiltList.Add(child);
                            DespawnExceptions.Add(child);
                        }
                    }
                }
            }

            public bool IsUnderground(Vector3 a) => !isEventUnderground && Location.y - a.y > 15f && EnvironmentManager.Check(a, EnvironmentType.TrainTunnels | EnvironmentType.Underground);

            public bool CanRespawnAt(BasePlayer target) => config.Settings.Management.AllowRespawn && FreshSpawnedIntoRaid(target);

            public bool WasConnected(BasePlayer target) => raiders.TryGetValue(target.userID, out var raider) && raider.IsParticipant && InRange(raider.lastPosition, Location, ProtectionRadius);

            public bool IsParticipant(BasePlayer target) => raiders.TryGetValue(target.userID, out var raider) && raider.IsParticipant;

            public void HandleTurretSight(BasePlayer target)
            {
                if (turrets.Count > 0)
                {
                    using var keys = DisposableList<ulong>();
                    foreach (var pair in turrets)
                    {
                        var info = pair.Value;
                        if (IsContainerKilled(info))
                        {
                            keys.Add(pair.Key);
                            continue;
                        }
                        var turret = info.turret;
                        if (turret.sightRange > Options.AutoTurret.SightRange)
                        {
                            SetupSightRange(turret, Options.AutoTurret.SightRange);
                        }
                        if (turret.target != null && turret.target == target)
                        {
                            turret.SetNoTarget();
                        }
                    }
                    for (int i = keys.Count - 1; i >= 0; i--)
                    {
                        turrets.Remove(keys[i]);
                    }
                }
            }

            public DamageResult OnTurretTarget(AutoTurret turret, BasePlayer victim)
            {
                if (IsEventDrone(turret))
                {
                    return DamageResult.None;
                }
                if (turret.skinID == RB_SKIN_ID)
                {
                    return DamageResult.Allowed;
                }
                if (Options.BlockOutsideTurrets && !InRange(turret.transform.position, Location, ProtectionRadius - 0.5f))
                {
                    if (turret.OwnerID.IsSteamId())
                    {
                        turret.SetNoTarget();
                        return DamageResult.Blocked;
                    }
                    return DamageResult.None;
                }
                if (victim != null && !victim.IsHuman())
                {
                    return DamageResult.Allowed;
                }
                return AllowPVP ? DamageResult.Allowed : (turret.OwnerID.IsSteamId() ? DamageResult.Blocked : DamageResult.None);
            }

            private void OnPreEnterRaid(BasePlayer target)
            {
                if (target.IsNull() || !target.IsHuman())
                {
                    return;
                }

                if (target.IsDead())
                {
                    intruders.Remove(target.userID);
                    enteredEntities.Remove(target);
                    return;
                }

                if (intruders.Contains(target.userID) && raiders.ContainsKey(target.userID))
                {
                    GetRaider(target).CheckInput(target, this);
                    return;
                }

                if (!raiders.ContainsKey(target.userID) && !TeleportExceptions.Contains(target.userID) && FreshSpawnedIntoRaid(target))
                {
                    RelocateFreshSpawn(target);
                    return;
                }

                if (!Options.Permission.Has(target, Type))
                {
                    Notify(target, "No Permission To Enter");
                    RemovePlayer(target, Location, ProtectionRadius, Type);
                    return;
                }

                if (IsLoading && Type != RaidableType.None && !CanBypass(target))
                {
                    RemovePlayer(target, Location, ProtectionRadius, Type);
                    return;
                }

                if (RemoveFauxAdmin(target) || IsScavenging(target))
                {
                    return;
                }

                if (!TeleportExceptions.Contains(target.userID) && CanRespawnAt(target))
                {
                    TeleportExceptions.Add(target.userID);
                }

                OnEnterRaid(target, false);
            }

            private void RelocateFreshSpawn(BasePlayer target)
            {
                for (int i = 0; i < 3; i++)
                {
                    BasePlayer.SpawnPoint point = ServerMgr.FindSpawnPoint(target);

                    if (!Instance.EventTerritory(point.pos, M_RADIUS))
                    {
                        target.Teleport(point.pos);
                        target.SendNetworkUpdateImmediate();
                        return;
                    }
                }

                RemovePlayer(target, Location, ProtectionRadius * 2f, Type, towardsZero: true, distance: 100f);
            }

            private static bool FreshSpawnedIntoRaid(BasePlayer target)
            {
                var story = target.lifeStory;
                if (story == null)
                {
                    return false;
                }

                return story.secondsAlive <= story.secondsSleeping + 2f && story.metersWalked + story.metersRun <= 2f;
            }

            public void HandlePlayerUnderground(BasePlayer player)
            {
                if (raiders.TryGetValue(player.userID, out var ri))
                {
                    ri.DestroyInput();

                    if (!ri.PreEnter)
                    {
                        UpdateTime(player, false);
                    }

                    ri.PreEnter = true;
                }

                DestroyUi(player, UiType.Status);
                intruders.Remove(player.userID);
                enteredEntities.Remove(player);
            }

            public void OnEnterRaid(BasePlayer target, bool checkUnderground = true)
            {
                if (checkUnderground && IsUnderground(target.transform.position))
                {
                    HandlePlayerUnderground(target);
                    return;
                }

                if (Type != RaidableType.None && CannotEnter(target, true))
                {
                    return;
                }

                Raider ri = GetRaider(target);

                ri.CheckInput(target, this);

                if (!intruders.Add(target.userID) && raiders.ContainsKey(target.userID))
                {
                    return;
                }

                Protector();

                if (!intruders.Contains(target.userID))
                {
                    return;
                }

                ri.PreEnter = false;

                if (config.UI.Status.Enabled)
                {
                    bool isMovingUi = Instance.UI.IsMovingUi(target, UiType.Status);
                    Instance.UI.ShowStatusUi(target, this, isMovingUi);
                }

                StopUsingWeapon(target);

                if (config.EventMessages.AnnounceEnterExit)
                {
                    NotifyUnlessSmart(target, AllowPVP ? (Options.Eco.Enabled ? "OnPlayerEnteredEco" : "OnPlayerEntered") : (Options.Eco.Enabled ? "OnPlayerEnteredPVEEco" : "OnPlayerEnteredPVE"));
                    if (Options.BlocksImmune && config.EventMessages.BlocksImmune) NotifyUnlessSmart(target, "Blocks Immune");
                }

                UpdateTime(target, true);

                HolsterWeapon(target);

                foreach (var brain in Instance.HumanoidBrains.Values)
                {
                    if (!brain.states.IsNullOrEmpty() && InRange2D(brain.DestinationOverride, Location, brain.SenseRange))
                    {
                        brain.SwitchToState(AIState.Attack, -1);
                    }
                }

                if (mapNote != null && target.userID == ownerId)
                {
                    DestroyMapNote(target);
                }

                HarmonyModInterface.CallHook("OnPlayerEnteredRaidableBase", new object[] { target, Location, AllowPVP, Options.Level, ID, 0f, 0f, loadTime, ownerId, BaseName, spawnDateTime, despawnDateTime, ProtectionRadius, GetLootAmountCounted() });
            }

            public void HolsterWeapon(BasePlayer player)
            {
                if (!AllowPVP || !Options.Holster || !player.svActiveItemID.IsValid || Instance.HasPVPDelay(player.userID))
                {
                    return;
                }
                player.equippingBlocked = true;
                player.UpdateActiveItem(default);
                player.Invoke(() =>
                {
                    player.equippingBlocked = false;
                }, 0.2f);
            }

            public void OnPlayerExit(BasePlayer target, bool skipDelay = true)
            {
                if (IsUnloading || target == null || !target.IsHuman())
                {
                    return;
                }

                intruders.Remove(target.userID);
                Raider ri = GetRaider(target);
                ri.DestroyInput();
                DestroyUi(target, UiType.Status);

                if (ri.PreEnter)
                {
                    return;
                }

                ri.PreEnter = true;

                UpdateTime(target, false);
                OnPlayerExited(target);
                TrySetPVPDelay(target, false, skipDelay);

                if (config.EventMessages.AnnounceEnterExit)
                {
                    NotifyUnlessSmart(target, AllowPVP ? "OnPlayerExit" : "OnPlayerExitPVE");
                }
            }

            public void OnPlayerExited(BasePlayer target)
            {
                HarmonyModInterface.CallHook("OnPlayerExitedRaidableBase", new object[] { target, Location, AllowPVP, Options.Level, ID, 0f, 0f, loadTime, ownerId, BaseName, spawnDateTime, despawnDateTime, ProtectionRadius, GetLootAmountCounted() });
            }

            public void AddHintCooldown(BasePlayer target, float cooldown)
            {
                ulong userid = target.userID;
                HintCooldowns.Add(userid);
                Invoke(() =>
                {
                    if (IsDespawning) return;
                    HintCooldowns.Remove(userid);
                }, cooldown);
            }

            public bool CanSetPVPDelay(BasePlayer target)
            {
                return AllowPVP && config.Settings.Management.PVPDelayTrigger && target.userID.IsSteamId() && !InRange(target.transform.position, Location, ProtectionRadius);
            }

            public void TrySetPVPDelay(BasePlayer target, bool isFireDamage, bool skipDelay = true, string key = "DoomAndGloom")
            {
                if (config.Settings.Management.PVPDelay <= 0f || skipDelay || !Instance.IsPVE() || !AllowPVP || target.IsFlying || IsVanished(target))
                {
                    return;
                }

                if (config.EventMessages.AnnounceEnterExit)
                {
                    string arg = mx(GetAllowKey(), target.UserIDString).Replace("[", string.Empty).Replace("] ", string.Empty);
                    NotifyUnlessSmart(target, key, arg, config.Settings.Management.PVPDelay);
                }

                SetPVPDelay(target, isFireDamage);
            }

            public void ExpireAllDelays()
            {
                if (!config.Settings.Management.PVPDelayPersists && Instance.PvpDelay.Count > 0)
                {
                    using var tmp = Instance.PvpDelay.ToPooledList();
                    foreach (var (userid, ds) in tmp)
                    {
                        if (ds == null || ds.raid == null || ds.raid == this)
                        {
                            Instance.RemovePVPDelay(userid, ds);
                        }
                    }
                }
            }

            internal object[] GetDelayHookObjects(BasePlayer target, ulong userid) => new object[] { target, userid, Name, Options.Level, Location, AllowPVP, ID, ownerId, BaseName, spawnDateTime, despawnDateTime, GetLootAmountCounted() };

            public void SetPVPDelay(BasePlayer target, bool isFireDamage)
            {
                if (IsDespawning)
                {
                    return;
                }

                ulong userid = target.userID;
                bool reset = Instance.GetPVPDelay(userid, true, out DelaySettings ds);

                if (!reset)
                {
                    if (ds != null)
                    {
                        Instance.RemovePVPDelay(userid, ds);
                    }

                    Instance.PvpDelay[userid] = ds = new();
                }
                else if (ds.Timer != null && !ds.Timer.Destroyed)
                {
                    ds.Timer.Destroy();
                }

                double delay = config.Settings.Management.PVPDelay;
                object[] hookObjects = GetDelayHookObjects(target, userid);

                ds.raid = this;
                ds.time = Time.timeAsDouble + delay;
                ds.hookObjectsFallback = hookObjects;
                ds.Timer = Instance.timer.Once((float)delay, () => Instance.ExpirePVPDelay(userid, ds));

                if (reset)
                {
                    float currentDealtDamageTime = Time.time;

                    if (!isFireDamage || currentDealtDamageTime - target.lastDealtDamageTime >= 0.1f)
                    {
                        HarmonyModInterface.CallHook("OnPlayerPvpDelayReset", hookObjects);
                        target.lastDealtDamageTime = currentDealtDamageTime;
                    }
                }
                else
                {
                    HarmonyModInterface.CallHook("OnPlayerPvpDelayStart", hookObjects);
                }

                UpdateUi(target, UiType.Delay);
            }

            public string GetAllowKey()
            {
                if (Options.Eco.Enabled)
                {
                    return AllowPVP ? "PVPFlagEco" : "PVEFlagEco";
                }
                return AllowPVP ? "PVPFlag" : "PVEFlag";
            }

            private bool IsScavenging(BasePlayer player)
            {
                if (IsOpened || !config.Settings.Management.EjectScavengers || !ownerId.IsSteamId() || CanBypass(player))
                {
                    return false;
                }

                return !Any(player.userID) && !IsAlly(player) && RemovePlayer(player, Location, ProtectionRadius, Type);
            }

            private bool RemoveFauxAdmin(BasePlayer player)
            {
                if (Instance.FauxAdmin != null && player != null && player.IsConnected && player.IsDeveloper && player.HasPermission("fauxadmin.allowed") && player.HasPermission("raidablebases.block.fauxadmin") && player.IsCheating())
                {
                    RemovePlayer(player, Location, ProtectionRadius, Type);
                    Notify(player, "NoFauxAdmin");
                    return true;
                }

                return false;
            }

            private bool IsBanned(BasePlayer player)
            {
                if (player.HasPermission("raidablebases.banned") || IsPayLocked && player.HasPermission("raidablebases.buyraid.banned"))
                {
                    Notify(player, player.IsAdmin ? "BannedAdmin" : "Banned");
                    return true;
                }

                return false;
            }

            private bool Teleported(BasePlayer player)
            {
                if (!config.Settings.Management.AllowTeleport && !TeleportExceptions.Contains(player.userID) && player.IsConnected && !CanBypass(player) && NearFoundation(player.transform.position) && !IsMounted(player) && HarmonyModInterface.CallHook("OnBlockRaidableBasesTeleport", player, Location) == null)
                {
                    Notify(player, "CannotTeleport");
                    return true;
                }

                return false;
            }

            public bool IsMounted(BasePlayer player, bool ignoreSiege = false)
            {
                BaseEntity m = player.GetMounted();
                if (m != null)
                {
                    return !ignoreSiege || !(m is BatteringRamSeat or BallistaGun or BaseSiegeWeapon);
                }
                BaseEntity parent = player.GetParentEntity();
                if (parent == null) return false;
                return parent is BaseMountable || IsCustomEntity(parent);
            }

            public bool IsMountable(BaseEntity entity)
            {
                if (entity is BaseMountable) return true;
                BaseEntity parent = entity.GetParentEntity();
                if (parent == null) return IsCustomEntity(entity);
                return parent is BaseMountable || IsCustomEntity(parent);
            }

            public bool BypassUseOwners()
            {
                if (Type == RaidableType.Manual)
                {
                    return AllowPVP ? config.Settings.Manual.BypassUseOwnersForPVP : config.Settings.Manual.BypassUseOwnersForPVE;
                }
                return AllowPVP ? config.Settings.Management.BypassUseOwnersForPVP : config.Settings.Management.BypassUseOwnersForPVE;
            }

            public bool IsHogging(BasePlayer player)
            {
                if (CanBypass(player) || player.HasPermission("raidablebases.hoggingbypass"))
                {
                    return false;
                }

                foreach (var raid in Instance.Raids)
                {
                    if (raid.BypassUseOwners())
                    {
                        continue;
                    }
                    if (raid.Type == RaidableType.Purchased && !config.Settings.Buyable.PreventHogging)
                    {
                        continue;
                    }
                    if (raid.Type != RaidableType.Purchased && !config.Settings.Management.PreventHogging)
                    {
                        continue;
                    }
                    if (raid.IsOpened && raid.Location != Location && raid.Any(player.userID, false))
                    {
                        NotifyOnce(player, "HoggingFinishYourRaid", FormatGridReference(player, raid.Location));
                        return true;
                    }
                }

                if (!config.Settings.Management.Lockout.IsBlocking() || player.HasPermission("raidablebases.blockbypass"))
                {
                    return false;
                }

                return IsAllyHogging(player);
            }

            public bool IsAllyHogging(BasePlayer player)
            {
                foreach (var raid in Instance.Raids)
                {
                    if (!raid.IsOpened || raid.Type == RaidableType.None || raid.Location.Distance(Location) < 0.1f)
                    {
                        continue;
                    }
                    if (raid.BypassUseOwners())
                    {
                        continue;
                    }
                    if (config.Settings.Management.PreventHogging && raid.Type != RaidableType.Purchased && IsAllyHogging(player, raid))
                    {
                        NotifyOnce(player, "HoggingFinishYourRaid", FormatGridReference(player, raid.Location));
                        return true;
                    }
                    if (config.Settings.Buyable.PreventHogging && raid.Type == RaidableType.Purchased && IsAllyHogging(player, raid))
                    {
                        NotifyOnce(player, "HoggingFinishYourRaid", FormatGridReference(player, raid.Location));
                        return true;
                    }
                }

                return false;
            }

            private bool IsAllyHogging(BasePlayer player, RaidableBase raid)
            {
                if (raid.BypassUseOwners() || CanBypass(player))
                {
                    return false;
                }

                foreach (var target in raid.GetIntruders())
                {
                    if (target == player || CanBypass(target))
                        continue;
                    if (config.Settings.Management.Lockout.BlockTeams && raid.IsAlly(player, target, false, false, true))
                    {
                        NotifyOnce(player, "HoggingFinishYourRaidTeam", target.displayName, FormatGridReference(player, raid.Location));
                        return true;
                    }

                    if (config.Settings.Management.Lockout.BlockFriends && raid.IsAlly(player, target, false, true, false))
                    {
                        NotifyOnce(player, "HoggingFinishYourRaidFriend", target.displayName, FormatGridReference(player, raid.Location));
                        return true;
                    }

                    if (config.Settings.Management.Lockout.BlockClans && raid.IsAlly(player, target, true, false, false, "IsClanMember"))
                    {
                        NotifyOnce(player, "HoggingFinishYourRaidClan", target.displayName, FormatGridReference(player, raid.Location));
                        return true;
                    }
                }

                return false;
            }

            private void CheckBackpacks(bool bypass = false)
            {
                for (int i = backpacks.Count - 1; i >= 0; i--)
                {
                    var backpack = backpacks[i];

                    EjectBackpack(backpack, bypass);

                    if (backpack.IsEmpty)
                    {
                        backpacks.Remove(backpack);
                        ResetToPool(ref backpack);
                    }
                }
            }

            private float RadiationProtection(BasePlayer player)
            {
                float protection = Mathf.Ceil(player.RadiationProtection());

                if (player.modifiers == null)
                {
                    return protection;
                }

                return protection + (protection * Mathf.Clamp01(player.modifiers.GetValue(Modifier.ModifierType.Radiation_Exposure_Resistance)));
            }

            private void CheckRads(BasePlayer player)
            {
                if (!Options.Radiation.Enabled)
                {
                    return;
                }
                if (RadiationProtection(player) < Options.Radiation.Protection)
                {
                    if (Options.Radiation.Rads > 0f)
                    {
                        player.metabolism.radiation_poison.Add(Options.Radiation.Rads);
                        player.metabolism.radiation_level.value = Mathf.Max(1f, player.metabolism.radiation_poison.value / 5f);
                    }
                    if (Options.Radiation.Damage > 0)
                    {
                        player.Hurt(Options.Radiation.Damage, DamageType.Radiation);
                    }
                }
                else player.metabolism.radiation_level.value = 0f;
            }

            private bool IsNullOrVoid(BaseEntity entity) => entity.IsNull();

            public bool InRangeTolerance(Vector3 v, float t = 1f) => (v.XZ2D() - Location.XZ2D()).sqrMagnitude <= ProtectionRadiusSqr(t);
            public bool InRangeTolerance(Raider ri, float t = 20f) => InRangeTolerance(ri.player.transform.position, t);

            private bool requiredLootPercentageMet;

            public bool RunProtector(long deadline)
            {
                try
                {
                    return StepProtector(protectorQuery, deadline);
                }
                catch (Exception ex)
                {
                    protectorQuery.Reset();
                    Instance.QueueExceptionMessage($"{nameof(Protector)} ERROR: {ex}");
                    return true;
                }
            }

            private void Protector()
            {
                synchronousProtectorQuery.Reset();

                try
                {
                    while (!StepProtector(synchronousProtectorQuery, 0L)) { }
                }
                finally
                {
                    synchronousProtectorQuery.Reset();
                }
            }

            private bool StepProtector(ProtectorQuery query, long deadline)
            {
                if (IsDespawning)
                {
                    query.Reset();
                    return true;
                }

                if (!query.Started)
                {
                    if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    query.Reset();
                    query.Started = true;
                }

                const int TOTAL_QUERY_STEPS = 8;
                while (query.Step < TOTAL_QUERY_STEPS)
                {
                    if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    switch (query.Step++)
                    {
                        case 0:
                            if (raidWindowPrivs && privs.Count > 0)
                            {
                                RefreshRaidWindowPrivileges();
                            }
                            break;
                        
                        case 1:
                            if (!requiredLootPercentageMet && IsCompleted && IsEligible && RequiredLootPercentageMet(Options.RequiredLootPercentage, out _))
                            {
                                requiredLootPercentageMet = true;
                                HandleAwards();
                            }
                            break;

                        case 2:
                            if (Time.realtimeSinceStartupAsDouble >= despawnDeadline) //if (DateTime.Now >= despawnDateTime)
                            {
                                Despawn();
                                query.Reset();
                                return true;
                            }
                            break;

                        case 3:
                            if (despawnTimeUpdated) OnRaidableDespawnUpdate();
                            break;

                        case 4:
                            if (enteredEntities.Count > 0) enteredEntities.RemoveWhere(IsNullOrVoid);
                            break;

                        case 5:
                            if (backpacks.Count > 0) CheckBackpacks(!AllowPVP && Options.EjectBackpacksPVE);
                            break;

                        case 6:
                            if (_respawns.Count > 0) CheckNpcRespawns();
                            break;

                        case 7:
                            if (Type == RaidableType.None || intruders.Count == 0)
                            {
                                query.Reset();
                                return true;
                            }

                            query.Raiders.AddRange(raiders.Values);
                            break;
                    }
                }

                if (!query.RaidersInitialized)
                {
                    if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    query.RaidersInitialized = true;

                    if (Type == RaidableType.None || intruders.Count == 0)
                    {
                        query.Reset();
                        return true;
                    }

                    query.Raiders.AddRange(raiders.Values);
                }

                bool statusUiEnabled = config.UI.Status.Enabled;

                while (query.RaiderIndex < query.Raiders.Count)
                {
                    if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    Raider ri = query.Raiders[query.RaiderIndex++];

                    if (!intruders.Contains(ri.userid))
                    {
                        continue;
                    }

                    BasePlayer player = ri.player;

                    if (!ri.IsConnected)
                    {
                        if (player != null && player.IsDead())
                        {
                            HandlePlayerExiting(player);
                        }

                        continue;
                    }

                    if (!InRangeTolerance(ri))
                    {
                        HandlePlayerExiting(player);
                        continue;
                    }

                    if (RemoveFauxAdmin(player))
                    {
                        continue;
                    }

                    if (IsBanned(player))
                    {
                        RejectPlayer(ri);
                        continue;
                    }

                    if (Options.Mounts.Jetpacks && IsWearingJetpack(player))
                    {
                        RemovePlayer(player, Location, ProtectionRadius, Type, true);
                        continue;
                    }

                    CheckRads(player);

                    if (ri.IsAllowed || ri.userid == ownerId || CanBypass(player))
                    {
                        ri.IsAllowed = true;
                        if (!statusUiEnabled) continue;
                        bool isMovingUi = Instance.UI.IsMovingUi(player, UiType.Status);
                        Instance.UI.ShowStatusUi(player, this, isMovingUi);
                        continue;
                    }

                    if (CanEject(player))
                    {
                        RejectPlayer(ri);
                        continue;
                    }

                    if (config.Settings.Management.LockToRaidOnEnter && !ri.IsParticipant)
                    {
                        NotifyUnlessSmart(ri.player, "OnLockedToRaid");

                        ri.IsParticipant = true;
                    }

                    ri.IsAllowed = true;

                    if (statusUiEnabled)
                    {
                        bool isMovingUi = Instance.UI.IsMovingUi(player, UiType.Status);
                        Instance.UI.ShowStatusUi(player, this, isMovingUi);
                    }
                }

                query.Reset();
                return true;
            }

            private void RejectPlayerOld(Raider ri)
            {
                OnPlayerExit(ri.player);
                raiders.Remove(ri.userid);
                intruders.Remove(ri.userid);
                RemovePlayer(ri.player, Location, ProtectionRadius, Type);
            }

            private void RejectPlayer(Raider ri)
            {
                HandlePlayerExiting(ri.player);
                raiders.Remove(ri.userid);
                RemovePlayer(ri.player, Location, ProtectionRadius, Type);
            }

            public void AddMember(ulong userid)
            {
                if (IsPayLocked && !cooldowns.Contains(userid) && config.Settings.Buyable.Cooldowns.CanBeApplied(userid, Options.Mode))
                {
                    cooldowns.Add(userid);
                }
                alliance.Add(userid);
            }

            public void FinalizeUi()
            {
                if (!raiders.IsNullOrEmpty())
                {
                    raiders.Values.ForEach(ri =>
                    {
                        if (ri.IsConnected)
                        {
                            if (intruders.Contains(ri.userid))
                            {
                                DestroyUi(ri.player, UiType.Status);
                            }
                            if (data.BuyableCooldowns.ContainsKey(ri.userid))
                            {
                                UpdateUi(ri.player, UiType.Cooldown);
                            }
                            else if (Instance.UI.Teleport.ContainsKey(ri.userid))
                            {
                                DestroyUi(ri.player, UiType.Teleport);
                            }
                        }
                        TrySetLockout(ri);
                    });
                }
            }

            public void StopSetupCoroutine()
            {
                if (setupRoutine != null)
                {
                    StopCoroutine(setupRoutine);
                    setupRoutine = null;
                }
                if (turretsCoroutine != null)
                {
                    StopCoroutine(turretsCoroutine);
                    turretsCoroutine = null;
                }
                if (SpawnNpcsRequestCo != null)
                {
                    StopCoroutine(SpawnNpcsRequestCo);
                    SpawnNpcsRequestCo = null;
                }
            }

            public void Despawn()
            {
                if (!IsDespawning)
                {
                    IsDespawning = true;
                    IsOpened = false;
                    TryInvokeMethod(SetNoDrops);
                    TryInvokeMethod(RemoveAllFromEvent);
                    TryInvokeMethod(StopSetupCoroutine);
                    TryInvokeMethod(TryStartPurchaseCooldown);
                    TryInvokeMethod(FinalizeUi);
                    TryInvokeMethod(DestroyLocks);
                    TryInvokeMethod(DestroyNpcs);
                    TryInvokeMethod(DestroyInputs);
                    TryInvokeMethod(DestroySpheres);
                    TryInvokeMethod(DestroyMapMarkers);
                    TryInvokeMethod(ResetSleepingBags);
                    TryInvokeMethod(ExpireAllDelays);
                    TryInvokeMethod(DestroyElevators);
                    TryInvokeMethod(DestroyEntities);
                    TryInvokeMethod(CheckSubscribe);
                    TryInvokeMethod(RespawnEntities);
                    TryInvokeMethod(FreeToPool);
                    Destroy(go);
                    LogEvent();
                    CancellDrone(rb);
                }
            }

            public void LogEvent()
            {
                TryInvokeMethod(() => Instance.LogToFile("despawn", $"{BaseName} {ownerName ?? "N/A"} ({ownerId}) @ approx. {Instance.PositionToGrid(Location, true)} {Type}", Instance, true, true));
            }

            public void TryInvokeMethod(Action action)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Instance.QueueExceptionMessage($"{action.Method.Name} ERROR: {ex}");
                }
            }

            public void RemoveAllFromEvent()
            {
                HarmonyModInterface.CallHook("OnRaidableBaseDespawn", hookObjects);

                GetIntruders().ForEach(HandlePlayerExiting);
            }

            public void SendDronePatrol(RandomBase rb)
            {
                if (Options.DronePatrols.UseDronePatrol && Instance.IQDronePatrol != null && rb != null && rb.Position != default)
                {
                    Instance.IQDronePatrol?.Call("SendPatrolPoint", JsonConvert.SerializeObject(new CustomPatrol()
                    {
                        pluginName = Name,
                        position = rb.Position,
                        settingDrone = new()
                        {
                            droneCountSpawned = Options.DronePatrols.droneCountSpawned,
                            droneAttackedCount = Options.DronePatrols.droneAttackedCount,
                            keyDrones = Options.DronePatrols.keyDrones,
                        },
                        settingPosition = new()
                        {
                            countSpawnPoint = 200,
                            radiusFindedPoints = 50
                        },
                    }), false);
                }
            }

            private void CancellDrone(RandomBase rb)
            {
                if (Instance.IQDronePatrol != null && Options.DronePatrols.UseDronePatrol && rb != null && rb.Position != default)
                    Instance.IQDronePatrol.Call("CancellPatrol", rb.Position);
            }

            public void CheckSubscribe()
            {
                Instance.Raids.Remove(this);

                if (Instance.Raids.Count == 0)
                {
                    Instance.harmonyEngine?.SetEnabled(HarmonyEngine.PatchGroup.RaidWindow, false);

                    if (IsUnloading)
                    {
                        Instance.UnsetStatics();
                    }
                    else
                    {
                        Instance.UnsubscribeHooks();
                        if (Instance.IsScheduledReload && Instance.Queues.Paused)
                        {
                            Puts("Scheduled reload completed");
                            Instance.NextTick(() => { });
                            return;
                        }
                    }
                }

                if (!IsUnloading)
                {
                    CheckPurchasedAmount();
                    if (!IsShuttingDown && Entities.Count > 0)
                    {
                        float estimate = (Entities.Count / (float)_undoLimit * 0.1f) + 15f;
                        if (AddNearTime < estimate)
                        {
                            AddNearTime = estimate;
                        }
                    }

                    spawns?.AddNear(Location, RemoveNearDistance, rb.FromCacheType, rb.ToCacheType, AddNearTime);
                }
            }

            private void CheckPurchasedAmount()
            {
                if (Options.Silent || Type != RaidableType.Purchased || !config.EventMessages.PurchaseAvailable)
                {
                    return;
                }
                if (Instance.Get(Type) != config.Settings.Buyable.Max - 1)
                {
                    return;
                }
                Instance.Automated.DelayUntilNextSpawn = Time.timeAsDouble + 60d;
                foreach (var target in BasePlayer.activePlayerList)
                {
                    NotifyUnlessSmart(target, "Purchase Available");
                }
            }

            public void DestroyElevators()
            {
                CancelInvoke(ShowElevatorHealthUi);
                ClearElevatorHealthUi();

                if (Elevators.Count == 0)
                {
                    return;
                }
                TryInvokeMethod(PrepareElevatorsForDespawn);
                foreach (var (key, ele) in Elevators)
                {
                    if (ele.IsBMG())
                    {
                        ele.BMG._elevator.SafelyKill();
                    }
                }
                Elevators.Clear();
                if (IsUnloading || Instance == null || Instance.Manager == null)
                {
                    return;
                }
                foreach (var raid in Instance.Raids)
                {
                    if (raid.Elevators?.Count > 0)
                    {
                        return;
                    }
                }
                Instance.Unsubscribe(nameof(OnElevatorMove));
                Instance.Unsubscribe(nameof(OnElevatorCall));
                Instance.Unsubscribe(nameof(OnButtonPress));
                Instance.Unsubscribe(nameof(OnElevatorButtonPress));
            }

            public void DestroyEntities()
            {
                if (!IsShuttingDown)
                {
                    if (Entities.Count > 0)
                    {
                        Entities.RemoveWhere(DespawnExceptions.Contains);
                        Instance.UndoLoop(Entities.ToList(), _undoLimit, hookObjects);
                    }

                    SetPreventLooting();
                }
            }

            public void SetPreventLooting()
            {
                if (Options.PreventLooting <= 0f) return;
                ulong userid = ownerId;
                if (userid == 0uL)
                {
                    var owner = GetOwner();
                    if (owner == null) return;
                    userid = owner.userID;
                }
                foreach (var e in DespawnExceptions)
                {
                    if (e.IsKilled() || e.OwnerID != 0uL) continue;
                    if (e.ShortPrefabName != "item_drop") continue;
                    e.Invoke(() =>
                    {
                        e.OwnerID = 0uL;
                        e.skinID = 0uL;
                    }, Options.PreventLooting);
                    e.skinID = RB_SKIN_ID;
                    e.OwnerID = userid;
                }
            }

            public void OnBuildingPrivilegeDestroyed()
            {
                HarmonyModInterface.CallHook("OnRaidableBasePrivilegeDestroyed", hookObjects);
                IsPrivDestroyed = true;
                CreateSpheres();
                TryToEnd();
            }

            public void UpdateTime(BasePlayer player, bool state)
            {
                if (!player.IsConnected || !player.HasPermission("raidablebases.time") || player.HasPermission("raidablebases.timebypass"))
                {
                    return;
                }

                int time = state ? Options.ForcedTime : -1;

                if (player.IsAdmin)
                {
                    player.SendConsoleCommand("admintime", time);
                }
                else if (!player.IsFlying)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdateImmediate();
                    player.SendConsoleCommand("admintime", time);
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                }

                player.SendNetworkUpdateImmediate();
            }

            public BasePlayer GetOwner()
            {
                if (ownerId.IsSteamId() && RustCore.FindPlayerById(ownerId) is BasePlayer player)
                {
                    return player;
                }
                if (raiders == null)
                {
                    return null;
                }
                BasePlayer owner = null;
                foreach (var x in raiders.Values)
                {
                    if (x.player.IsNull()) continue;
                    if (x.player.userID == ownerId) return x.player;
                    if (x.IsParticipant) owner = x.player;
                }
                return owner;
            }

            private List<BasePlayer> _intruders = new();
            public List<BasePlayer> GetIntruders()
            {
                _intruders ??= new();
                _intruders.Clear();
                if (raiders == null || raiders.Count == 0 || intruders == null || intruders.Count == 0)
                {
                    return _intruders;
                }
                foreach (var raider in raiders.Values)
                {
                    if (raider != null && intruders.Contains(raider.userid) && raider.player != null)
                    {
                        _intruders.Add(raider.player);
                    }
                }
                return _intruders;
            }

            private List<BasePlayer> _raiders = new();
            public List<BasePlayer> GetRaiders(bool participantOnly = true)
            {
                _raiders ??= new();
                _raiders.Clear();
                if (raiders == null || raiders.Count == 0)
                {
                    return _raiders;
                }
                foreach (var raider in raiders.Values)
                {
                    if (raider != null && raider.player != null && (!participantOnly || raider.IsParticipant))
                    {
                        _raiders.Add(raider.player);
                    }
                }
                return _raiders;
            }


            private List<ulong> _timeRequirement = new();
            public List<ulong> GetRaidersByTimeRequirement(double threshold = 0.25d)
            {
                _timeRequirement.Clear();
                double maxParticipantTime = 0f;
                foreach (var raider in raiders.Values)
                {
                    if (raider.IsParticipant && raider.participantTime > maxParticipantTime)
                    {
                        maxParticipantTime = raider.participantTime;
                    }
                }
                if (maxParticipantTime > 0d)
                {
                    double requiredDuration = maxParticipantTime * threshold;
                    foreach (var raider in raiders.Values)
                    {
                        if (raider.IsParticipant && raider.participantTime >= requiredDuration)
                        {
                            _timeRequirement.Add(raider.userid);
                        }
                    }
                }
                return _timeRequirement;
            }

            public int GetParticipantAmount()
            {
                int num = 0;
                foreach (var raider in raiders.Values)
                {
                    if (raider.player != null && raider.IsParticipant)
                    {
                        num++;
                    }
                }
                return num;
            }

            public bool AddLooter(BasePlayer looter, HitInfo info = null)
            {
                if (!looter.IsHuman())
                {
                    return false;
                }

                if (looter.IsFlying || IsVanished(looter))
                {
                    return false;
                }

                if (!IsAlly(looter))
                {
                    if (info != null)
                    {
                        NullifyDamage(info);
                        if (!info.damageTypes.Has(DamageType.Heat))
                        {
                            NotifyOnce(looter, "NoDamageToEnemyBase");
                        }
                    }
                    else
                    {
                        Notify(looter, "OwnerLocked");
                    }
                    return false;
                }

                if (IsHogging(looter))
                {
                    return NullifyDamage(info);
                }

                if (HasLockout(looter, false))
                {
                    return false;
                }

                if (!Options.Permission.Has(looter, Type))
                {
                    return NullifyDamage(info);
                }

                GetRaider(looter).IsParticipant = true;

                return true;
            }

            public bool IsDamageBlocked(BaseEntity entity)
            {
                if (Options.BlockedEntityDamage.Count > 0)
                {
                    string name = Instance.GetTypeName(entity);
                    foreach (var value in Options.BlockedEntityDamage)
                    {
                        if (name == value || entity.ShortPrefabName.StartsWith(value, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                return false;
            }

            public bool IsPickupAllowed(string name)
            {
                foreach (var value in Options.WhitelistedPickupItems)
                {
                    if (!string.IsNullOrWhiteSpace(value) && name.Contains(value, CompareOptions.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool IsPickupBlacklisted(string name)
            {
                foreach (var value in Options.BlacklistedPickupItems)
                {
                    if (!string.IsNullOrWhiteSpace(value) && name.Contains(value, CompareOptions.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }

            private void FillAmmoTurret(TurretInfo info)
            {
                if (isAuthorized || IsUnloading || IsDespawning || Type == RaidableType.None) return;
                if (info == null || info.infiniteAmmoDisabled || info.IsKilled()) return;

                foreach (ulong userid in info.turret.authorizedPlayers)
                {
                    if (userid.IsSteamId() && !CanBypassAuthorized(userid))
                    {
                        info.infiniteAmmoDisabled = true;
                        isAuthorized = true;
                        return;
                    }
                }

                DisableInterference(info);

                BaseProjectile attachedWeapon = info.turret.GetAttachedWeapon();
                if (attachedWeapon == null)
                {
                    if (++info.refillAttempts <= 20) QueueTurretRefill(info, 0.2f);
                    return;
                }

                if (IsRocketLauncher(attachedWeapon))
                {
                    UsableByTurret = true;
                }

                ItemDefinition ammoType = attachedWeapon.primaryMagazine.ammoType;
                if (ammoType == null)
                {
                    return;
                }

                int amount = Math.Max(config.Weapons.Ammo.AutoTurret, attachedWeapon.primaryMagazine.capacity);
                Item ammo = ItemManager.Create(ammoType, amount, 0uL);
                if (ammo == null)
                {
                    return;
                }

                if (!ammo.MoveToContainer(info.turret.inventory, -1, true, true, null, true))
                {
                    ammo.Remove();
                    return;
                }

                info.refillAttempts = 0;
                attachedWeapon.primaryMagazine.contents = attachedWeapon.primaryMagazine.capacity;
                attachedWeapon.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                info.turret.Invoke(() => FinalizeTurretAmmo(info), 0.25f);
            }

            private void QueueTurretRefill(TurretInfo info, float delay)
            {
                if (info == null || info.refillPending || info.infiniteAmmoDisabled || info.IsKilled())
                {
                    return;
                }
                info.refillPending = true;
                info.turret.Invoke(() =>
                {
                    info.refillPending = false;
                    FillAmmoTurret(info);
                }, delay);
            }

            private void FinalizeTurretAmmo(TurretInfo info)
            {
                if (IsUnloading || IsDespawning || info == null || info.IsKilled())
                {
                    return;
                }
                info.turret.UpdateTotalAmmo();
            }

            private static void DisableInterference(TurretInfo info)
            {
                if (!info.IsKilled() && info.turret.HasFlag(BaseEntity.Flags.OnFire))
                {
                    info.turret.SetFlagLocal(BaseEntity.Flags.OnFire, false);
                    info.turret.nearbyTurrets.Clear();
                    info.turret.interferringTurrets.Clear();
                    info.turret.SendNetworkUpdate();
                }
            }

            private void FillAmmoGunTrap(GunTrap gt)
            {
                if (IsUnloading || isAuthorized || gt.IsKilled())
                {
                    return;
                }

                gt.ammoType ??= ItemManager.FindItemDefinition("ammo.handmade.shell");

                var ammo = gt.inventory.GetSlot(0);

                if (ammo == null)
                {
                    gt.inventory.AddItem(gt.ammoType, config.Weapons.Ammo.GunTrap);
                }
                else ammo.amount = config.Weapons.Ammo.GunTrap;
            }

            private ItemDefinition lowgradefuel;

            private void FillAmmoFogMachine(FogMachine fm)
            {
                if (IsUnloading || isAuthorized || fm.IsKilled())
                {
                    return;
                }

                lowgradefuel ??= ItemManager.FindItemDefinition("lowgradefuel");

                Item slot = fm.inventory.GetSlot(0);
                if (slot == null)
                {
                    fm.inventory.AddItem(lowgradefuel, config.Weapons.Ammo.FogMachine);
                }
                else slot.amount = config.Weapons.Ammo.FogMachine;
            }

            private void FillAmmoFlameTurret(FlameTurret ft)
            {
                if (IsUnloading || isAuthorized || ft.IsKilled())
                {
                    return;
                }

                lowgradefuel ??= ItemManager.FindItemDefinition("lowgradefuel");

                Item slot = ft.inventory.GetSlot(0);
                if (slot == null)
                {
                    ft.inventory.AddItem(lowgradefuel, config.Weapons.Ammo.FlameTurret);
                }
                else slot.amount = config.Weapons.Ammo.FlameTurret;
            }

            private void FillAmmoSamSite(SamSite ss)
            {
                if (IsUnloading || isAuthorized || ss.IsKilled())
                {
                    return;
                }

                if (ss.ammoItem == null || !ss.HasAmmo())
                {
                    Item item = ItemManager.Create(ss.ammoType, config.Weapons.Ammo.SamSite);

                    if (!item.MoveToContainer(ss.inventory))
                    {
                        item.Remove();
                    }
                    else ss.ammoItem = item;
                }
                else if (ss.ammoItem.amount < config.Weapons.Ammo.SamSite)
                {
                    ss.ammoItem.amount = config.Weapons.Ammo.SamSite;
                }
            }

            private bool IsAuthorized()
            {
                foreach (var id in priv.authorizedPlayers)
                {
                    if (id.IsSteamId() && !CanBypassAuthorized(id))
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool isAuthorized;
            private void OnWeaponItemPreRemove(Item item)
            {
                if (isAuthorized || IsUnloading || IsDespawning)
                {
                    return;
                }
                else if (!priv.IsKilled() && IsAuthorized())
                {
                    isAuthorized = true;
                    return;
                }
                else if (privSpawned && priv.IsKilled())
                {
                    isAuthorized = true;
                    return;
                }

                var weapon = item.parent?.entityOwner;

                if (weapon is AutoTurret turret)
                {
                    if (turret.net != null && turrets.TryGetValue(turret.net.ID.Value, out var info))
                    {
                        QueueTurretRefill(info, 0.1f);
                    }
                }
                else if (weapon is GunTrap gt)
                {
                    weapon.Invoke(() => FillAmmoGunTrap(gt), 0.1f);
                }
                else if (weapon is SamSite ss)
                {
                    weapon.Invoke(() => FillAmmoSamSite(ss), 0.1f);
                }
            }

            public void TryToEnd()
            {
                if (IsOpened && !IsLoading && !IsCompleted && CanUndo())
                {
                    if (Options.DropPrivilegeLoot && privHadLoot && !priv.IsKilled())
                    {
                        Instance.DropOrRemoveItems(priv, this, true, true);
                        UpdateLootAmountCounted();
                    }
                    UnlockEverything();
                    AwardRaiders();
                    Undo();
                }
            }

            private void UnlockEverything()
            {
                if (Options.UnlockEverything)
                {
                    DestroyLocks();
                }
            }

            public bool GetInitiatorPlayer(HitInfo info, DamageType damageType, BaseCombatEntity entity, out BasePlayer target)
            {
                if (info == null)
                {
                    target = entity.lastAttacker as BasePlayer;
                    return target != null;
                }

                var weapon = info.Initiator ?? info.WeaponPrefab ?? info.Weapon;

                target = weapon switch
                {
                    BasePlayer player => player,
                    { creatorEntity: BasePlayer player } => player,
                    { parentEntity: EntityRef parentEntity } when parentEntity.Get(true) is BasePlayer player => player,
                    _ => damageType == DamageType.Heat ? entity.lastAttacker as BasePlayer ?? GetArsonist() : null
                };

                return target != null && !target.IsDestroyed;
            }

            private List<string> fireAmmoTypes = new() { "arrow.fire", "ammo.pistol.fire", "ammo.rifle.explosive", "ammo.rifle.incendiary", "ammo.shotgun.fire" };

            public BasePlayer GetArsonist()
            {
                foreach (var raider in raiders.Values)
                {
                    if (!raider.IsParticipant || raider.player == null)
                    {
                        continue;
                    }
                    if (!raider.player.svActiveItemID.IsValid || !raider.player.TryGetActiveItem(out Item item) || !item.GetHeldEntity().Is(out BaseEntity e))
                    {
                        continue;
                    }
                    if (e is FlameThrower || (e.Is(out BaseProjectile p) && p.primaryMagazine.ammoType != null && fireAmmoTypes.Contains(p.primaryMagazine.ammoType.shortname)))
                    {
                        return raider.player;
                    }
                }
                return null;
            }

            public void SetAllowPVP(RandomBase rb)
            {
                Type = rb.type;

                AllowPVP = Type switch
                {
                    RaidableType.Purchased when rb.payments.type != 0 => rb.payments.type == 2,
                    RaidableType.Maintained when config.Settings.Maintained.Chance > 0 => Convert.ToDecimal(UnityEngine.Random.Range(0f, 100f)) <= config.Settings.Maintained.Chance,
                    RaidableType.Scheduled when config.Settings.Schedule.Chance > 0 => Convert.ToDecimal(UnityEngine.Random.Range(0f, 100f)) <= config.Settings.Schedule.Chance,
                    RaidableType.Purchased when config.Settings.Buyable.ConvertPVP => false,
                    RaidableType.Purchased when config.Settings.Buyable.ConvertPVE => true,
                    RaidableType.Maintained when config.Settings.Maintained.ConvertPVP => false,
                    RaidableType.Maintained when config.Settings.Maintained.ConvertPVE => true,
                    RaidableType.Scheduled when config.Settings.Schedule.ConvertPVP => false,
                    RaidableType.Scheduled when config.Settings.Schedule.ConvertPVE => true,
                    RaidableType.Manual when config.Settings.Manual.ConvertPVP => false,
                    RaidableType.Manual when config.Settings.Manual.ConvertPVE => true,
                    _ => rb.options.AllowPVP
                };
            }

            private bool CancelOnServerRestart()
            {
                return config.Settings.Management.Restart && IsShuttingDown;
            }

            public void AwardRaiders()
            {
                if (config.Settings.Buyable.Cooldowns.ApplyOnRewards)
                {
                    StartPurchaseCooldown();
                }

                var sb = new StringBuilder();

                foreach (var ri in raiders.Values)
                {
                    if (CancelOnServerRestart() || !IsEligible)
                    {
                        ri.eligible = false;
                        continue;
                    }

                    if (ri.player == null ? ri.IsFlying : ri.player.IsFlying)
                    {
                        if (config.EventMessages.Rewards.Flying) Notify(ri.player, "No Reward: Flying");
                        ri.eligible = false;
                        continue;
                    }

                    if (ri.player == null ? ri.IsVanished : ri.player._limitedNetworking)
                    {
                        if (config.EventMessages.Rewards.Vanished) Notify(ri.player, "No Reward: Vanished");
                        ri.eligible = false;
                        continue;
                    }

                    if (!IsPlayerActive(ri.userid))
                    {
                        if (config.EventMessages.Rewards.Inactive) Notify(ri.player, "No Reward: Inactive");
                        ri.eligible = false;
                        continue;
                    }

                    if (config.Settings.Management.OnlyAwardOwner && ri.userid != ownerId && ownerId.IsSteamId())
                    {
                        if (config.EventMessages.Rewards.NotOwner) Notify(ri.player, "No Reward: Not Owner");
                        ri.rewards = false;
                    }

                    if (!ri.IsParticipant || Options.RequiredDestroyEntity && !ri.HasDestroyed)
                    {
                        if (config.EventMessages.Rewards.NotParticipant) Notify(ri.player, "No Reward: Not A Participant");
                        ri.rewards = false;
                        continue;
                    }

                    if (config.Settings.Management.OnlyAwardAllies && ownerId.IsSteamId() && ri.userid != ownerId && !IsAlly(ri, ownerId))
                    {
                        if (config.EventMessages.Rewards.NotAlly) Notify(ri.player, "No Reward: Not Ally");
                        ri.rewards = false;
                    }

                    if (config.Settings.RemoveAdminRaiders && ri.IsAdmin && Type != RaidableType.None)
                    {
                        if (config.EventMessages.Rewards.RemoveAdmin) Notify(ri.player, "No Reward: Admin");
                        ri.rewards = false;
                        continue;
                    }

                    sb.Append(ri.displayName).Append(", ");
                }

                if (IsEligible)
                {
                    if (!CancelOnServerRestart())
                    {
                        HarmonyModInterface.CallHook("OnRaidableBaseCompleted", hookObjects);
                        HarmonyModInterface.CallHook("OnRaidableBaseCompleted", Location, Options.Level, AllowPVP, ownerId, GetRaidersByTimeRequirement());
                    }

                    if (!IsUnloading && Options.Levels.Level2 && npcMaxAmountMurderers + npcMaxAmountScientists > 0)
                    {
                        failedUpdates = 0;
                        SpawnNpcs();
                    }

                    if (!requiredLootPercentageMet && IsCompleted && RequiredLootPercentageMet(Options.RequiredLootPercentage, out _))
                    {
                        requiredLootPercentageMet = true;
                        HandleAwards();
                    }
                }

                if (sb.Length == 0)
                {
                    return;
                }

                sb.Length -= 2;
                string thieves = sb.ToString();
                string con = mx(IsEligible ? "Thieves" : "ThievesDespawn", null, $"{LangMode()} ({BaseName})", Instance.PositionToGrid(Location), thieves);

                Puts(con);

                if (config.EventMessages.AnnounceThief && IsEligible)
                {
                    foreach (var target in BasePlayer.activePlayerList)
                    {
                        if (!IsRaider(target) && target.HasPermission("raidablebases.limitedannouncements")) continue;
                        NotifyUnlessSmart(target, "Thieves", LangMode(target.UserIDString), FormatGridReference(target, Location), thieves);
                    }
                }

                if (config.EventMessages.LogThieves)
                {
                    Instance.LogToFile("treasurehunters", $"{DateTime.Now} : {con}", Instance, false);
                }
            }

            public bool RequiredLootPercentageMet(double requiredLootPercentage, out double percentageMet)
            {
                percentageMet = 0;
                if (requiredLootPercentage > 0 && lootAmountTracked > 0)
                {
                    int lootAmountRemaining = GetLootAmountCounted();
                    if (lootAmountRemaining > 0)
                    {
                        double numLooted = lootAmountTracked - lootAmountRemaining;
                        percentageMet = (numLooted / lootAmountTracked) * 100.0;
                        if (percentageMet < requiredLootPercentage)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }

            private void RunCommands(BuildingOptionsCommands boc, ulong userid)
            {
                if (!boc.Enabled)
                {
                    return;
                }
                foreach (var command in boc.Commands)
                {
                    if (string.IsNullOrWhiteSpace(command)) continue;
                    if (!CanAssignTo(userid, ownerId > 0 ? ownerId : userid, boc.Owner)) continue;
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, command.Replace("{userid}", userid.ToString()));
                }
            }

            private int GetRankedLadderPointsForDifficulty(ulong id) => !CanAssignTo(id, ownerId, config.RankedLadder.Points.Owner) ? 0 : config.RankedLadder.Points.Get(Options.Mode);

            private void HandleAwards()
            {
                if (raiders.TryGetValue(ownerId, out Raider ownerRi) && ownerRi.IsParticipant && ownerRi.eligible && ownerRi.rewards && !CancelOnServerRestart())
                {
                    HarmonyModInterface.CallHook("OnRaidableAwardOwner", hookObjects);
                    HarmonyModInterface.CallHook("OnRaidableAwardOwner", Location, Options.Level, AllowPVP, ownerId, GetRaidersByTimeRequirement());
                }

                foreach (var ri in raiders.Values)
                {
                    TrySetLockout(ri);

                    if (!ri.IsParticipant || !ri.eligible)
                    {
                        continue;
                    }

                    if (config.RankedLadder.Enabled)
                    {
                        PlayerInfo info = data.GetPlayerInfo(ri.id);

                        info.ResetExpiredDate(config.RankedLadder.Days);

                        int points = GetRankedLadderPointsForDifficulty(ri.userid);

                        info.Name = ri.displayName.ToFriendlyJson();
                        info.AddPoints(Options.Mode, points);

                        int mv = config.RankedLadder.Assign.Get(Options.Mode);
                        if (info.Get(Options.Mode) >= mv && CanAssignTo(ri.userid, ownerId, config.RankedLadder.Assign.Owner))
                        {
                            var record = config.RankedLadder.GetRecord(Options.Mode);
                            if (record != null)
                            {
                                AddGroupedPermission(ri.id, record.Group, record.Permission);
                                RunCommands(Options.EventRankedAwards, ri.userid);
                            }
                        }

                        HarmonyModInterface.CallHook("OnRaidableAwardGiven", ri.displayName, ri.id, JsonConvert.SerializeObject(info));
                    }

                    if (!ri.rewards || Options.Rewards.NoBuyableRewards && IsPayLocked && payments.valid)
                    {
                        continue;
                    }

                    RunCommands(Options.EventCompletion, ri.userid);

                    int total = raiders.Values.Count(x => x.IsParticipant && x.eligible && x.rewards);

                    if (!ri.player.IsNull())
                    {
                        if (Options.Rewards.Custom.isItem)
                        {
                            int amount = config.Settings.Management.DivideRewards ? Math.Max(1, Options.Rewards.Custom.Amount / total) : Options.Rewards.Custom.Amount;
                            if (amount > 0)
                            {
                                if (Options.Rewards.IsDoubledAtNighttime()) amount *= 2;
                                ulong rewardSkin = Options.Rewards.Custom.Skin;
                                if (string.Equals(Options.Rewards.Custom.Shortname, "paper", StringComparison.OrdinalIgnoreCase))
                                    rewardSkin = GRIMM_PAPER_SKIN;
                                Item item = ItemManager.Create(Options.Rewards.Custom.Definition, amount, rewardSkin);
                                if (item.skin != 0 && item.GetHeldEntity()) item.GetHeldEntity().skinID = item.skin;
                                if (!string.IsNullOrWhiteSpace(Options.Rewards.Custom.Name)) item.name = Options.Rewards.Custom.Name;
                                if (!ri.player.inventory.GiveItem(item)) item.DropAndTossUpwards(ri.player.eyes.position);
                                string name = string.IsNullOrWhiteSpace(Options.Rewards.Custom.Name) ? item.info.displayName.english : Options.Rewards.Custom.Name;
                                NotifyUnlessSmart(ri.player, "CustomDeposit", mx("CustomDepositFormat", ri.id, amount, name));
                            }
                        }

                        if (Options.Rewards.Custom.isPlugin)
                        {
                            var option = Options.Rewards.Custom;
                            var plugin = Instance.plugins.Find(option.Plugin.PluginName);

                            if (plugin != null)
                            {
                                string shopName = Options.Rewards.Custom.Plugin.ShoppyStockShopName;
                                double amount = config.Settings.Management.DivideRewards ? Math.Max(1, Options.Rewards.Custom.Plugin.Amount / total) : Options.Rewards.Custom.Plugin.Amount;
                                if (Options.Rewards.IsDoubledAtNighttime()) amount *= 2;

                                if (!string.IsNullOrWhiteSpace(shopName))
                                {
                                    plugin?.Call(option.Plugin.DepositHookName, shopName, option.Plugin.PlayerDataType switch
                                    {
                                        2 => ri.player,
                                        1 => ri.id,
                                        0 or _ => ri.userid
                                    }, option.Plugin.AmountDataType switch
                                    {
                                        2 => (object)(int)amount,
                                        1 => (object)(float)amount,
                                        0 or _ => (object)(double)amount
                                    });
                                }
                                else plugin?.Call(option.Plugin.DepositHookName, option.Plugin.PlayerDataType switch
                                {
                                    2 => ri.player,
                                    1 => ri.id,
                                    0 or _ => ri.userid
                                }, option.Plugin.AmountDataType switch
                                {
                                    2 => (object)(int)amount,
                                    1 => (object)(float)amount,
                                    0 or _ => (object)(double)amount
                                });

                                NotifyUnlessSmart(ri.player, "CustomDeposit", mx("CustomDepositFormat", ri.id, amount, option.GetCurrencyName()));
                            }
                        }
                    }

                    if (Options.Rewards.ClanScorePoints > 0 && ri.player != null && TryGetClan(ri.player, out _)) // implement when possible
                    {
                        int points = config.Settings.Management.DivideRewards ? Options.Rewards.ClanScorePoints / total : Options.Rewards.ClanScorePoints;
                        if (Options.Rewards.IsDoubledAtNighttime()) points *= 2;
                        ri.player.AddClanScore(ClanScoreEventType.Generic, points);
                        NotifyUnlessSmart(ri.player, "ClanScorePoints", points);
                    }

                    if (Options.Rewards.Money > 0 && Instance.Economics.CanCall())
                    {
                        double money = config.Settings.Management.DivideRewards ? Options.Rewards.Money / (double)total : Options.Rewards.Money;
                        if (Options.Rewards.IsDoubledAtNighttime()) money *= 2;
                        Instance.Economics?.Call("Deposit", ri.userid, money);
                        NotifyUnlessSmart(ri.player, "EconomicsDeposit", money);
                    }

                    if (Options.Rewards.Money > 0 && Instance.BankSystem.CanCall())
                    {
                        int money = Convert.ToInt32(config.Settings.Management.DivideRewards ? Options.Rewards.Money / total : Options.Rewards.Money);
                        if (Options.Rewards.IsDoubledAtNighttime()) money *= 2;
                        Instance.BankSystem?.Call("Deposit", ri.id, money);
                        NotifyUnlessSmart(ri.player, "EconomicsDeposit", money);
                    }

                    if (Options.Rewards.Money > 0 && Instance.IQEconomic.CanCall())
                    {
                        int money = Convert.ToInt32(config.Settings.Management.DivideRewards ? Options.Rewards.Money / total : Options.Rewards.Money);
                        if (Options.Rewards.IsDoubledAtNighttime()) money *= 2;
                        Instance.IQEconomic?.Call("API_SET_BALANCE", ri.userid, money);
                        NotifyUnlessSmart(ri.player, "EconomicsDeposit", money);
                    }

                    if (Options.Rewards.Points > 0 && Instance.ServerRewards.CanCall())
                    {
                        int points = config.Settings.Management.DivideRewards ? Options.Rewards.Points / total : Options.Rewards.Points;
                        if (Options.Rewards.IsDoubledAtNighttime()) points *= 2;
                        Instance.ServerRewards?.Call("AddPoints", ri.userid, points);
                        NotifyUnlessSmart(ri.player, "ServerRewardPoints", points);
                    }

                    if (Options.Rewards.SkillTree > 0 && Instance.SkillTree.CanCall())
                    {
                        double xp = config.Settings.Management.DivideRewards ? Options.Rewards.SkillTree / (double)total : Options.Rewards.SkillTree;
                        if (Options.Rewards.IsDoubledAtNighttime()) xp *= 2;
                        if (ri.player != null)
                        {
                            NotifyUnlessSmart(ri.player, "SkillTreeXP", xp);
                            Instance.SkillTree?.Call("AwardXP", ri.player, xp, Name);
                        }
                        else Instance.SkillTree?.Call("AwardXP", ri.userid, xp, Name);
                    }

                    if (Options.Rewards.XPerience > 0 && Instance.XPerience.CanCall())
                    {
                        double xp = config.Settings.Management.DivideRewards ? Options.Rewards.XPerience / (double)total : Options.Rewards.XPerience;
                        if (Options.Rewards.IsDoubledAtNighttime()) xp *= 2;
                        NotifyUnlessSmart(ri.player, "XPerienceXP", xp);
                        Instance.XPerience?.Call("GiveXPID", ri.userid, xp);
                    }

                    if (Options.Rewards.XLevels > 0 && ri.player != null && Instance.XLevels.CanCall())
                    {
                        double xp = config.Settings.Management.DivideRewards ? Options.Rewards.XLevels / (double)total : Options.Rewards.XLevels;
                        if (Options.Rewards.IsDoubledAtNighttime()) xp *= 2;
                        NotifyUnlessSmart(ri.player, "XLevelsXP", xp);
                        Instance.XLevels?.Call("API_GiveXP", ri.player, (float)xp);
                    }
                }
            }

            private void AddGroupedPermission(string userid, string group, string perm)
            {
                if (userid.HasPermission("raidablebases.notitle"))
                {
                    return;
                }

                if (!userid.HasPermission(perm))
                {
                    Instance.permission.GrantUserPermission(userid, perm, Instance);
                }

                if (!Instance.permission.UserHasGroup(userid, group))
                {
                    Instance.permission.AddUserGroup(userid, group);
                }
            }

            private bool CanAssignTo(ulong userid, ulong owner, bool only)
            {
                return only == false || owner == 0uL || userid == owner;
            }

            public bool CanBypass(BasePlayer player)
            {
                return !player.IsHuman() || player.IsFlying || IsVanished(player) || player.HasPermission("raidablebases.canbypass");
            }

            private bool Exceeds(BasePlayer player)
            {
                if (IsPayLocked && player.userID == ownerId || CanBypass(player) || config.Settings.Management.Players.BypassPVP && AllowPVP)
                {
                    return false;
                }

                int amount = config.Settings.Management.Players.Get(Options.Mode, Type);

                if (amount == -1 || amount > 0 && GetParticipantsAmount() > amount)
                {
                    Notify(player, "Event is full");
                    return true;
                }

                return false;
            }

            public int GetParticipantsAmount()
            {
                int count = 0;

                foreach (Raider raider in raiders.Values)
                {
                    if (raider.player != null && !CanBypass(raider.player))
                    {
                        count++;
                    }
                }

                return count;
            }

            public bool HasLockout(BasePlayer player, bool canMessage = true)
            {
                if (!config.Settings.Management.Lockout.Any() || player.IsNull() || CanBypass(player) || player.HasPermission("raidablebases.lockoutbypass") || Type == RaidableType.None)
                {
                    return false;
                }

                if (!IsOpened && Any(player.userID))
                {
                    return false;
                }

                if (player.userID == ownerId)
                {
                    return false;
                }

                if (config.Settings.Buyable.AllowAlly && Type == RaidableType.Purchased && IsAlly(player, ownerId))
                {
                    return false;
                }

                if (data.Lockouts.TryGetValue(player.UserIDString, out var lo))
                {
                    double time = lo.Get(Options.Mode);

                    if (time > 0f)
                    {
                        if (canMessage)
                        {
                            NotifyOnce(player, "LockedOut", LangMode(player.UserIDString), Instance.FormatTime(time, player.UserIDString));
                        }
                        return true;
                    }

                    if (!lo.Any())
                    {
                        data.Lockouts.Remove(player.UserIDString);
                    }
                }

                DestroyUi(player, UiType.Lockout);

                return false;
            }

            private void TrySetGlobalLockout(string playerId, BasePlayer player)
            {
                if (!data.Lockouts.TryGetValue(playerId, out var lo))
                {
                    data.Lockouts[playerId] = lo = new();
                }
                foreach (var mode in Instance.GetRaidableModes())
                {
                    lo.Set(mode, GetLockoutTime(mode));
                }
                if (lo.Any())
                {
                    UpdateUi(player, UiType.Lockout);
                }
                else data.Lockouts.Remove(playerId);
            }

            private List<ulong> lockFlags = new();

            private void TrySetLockout(Raider ri)
            {
                if (IsUnloading || IsPayLocked || IsResetting || ri == null || !ri.IsParticipant || Type == RaidableType.None || ri.id.HasPermission("raidablebases.canbypass") || ri.id.HasPermission("raidablebases.lockoutbypass"))
                {
                    return;
                }

                if (lockFlags.Contains(ri.userid) || AllowPVP && !config.Settings.Management.Lockout.PVP || !AllowPVP && !config.Settings.Management.Lockout.PVE)
                {
                    return;
                }

                if (!ri.player.IsNull() && (ri.player.IsFlying || ri.player._limitedNetworking))
                {
                    return;
                }

                lockFlags.Add(ri.userid);

                if (config.Settings.Management.Lockout.Global)
                {
                    TrySetGlobalLockout(ri.id, ri.player);
                    return;
                }

                double time = GetLockoutTime(Options.Mode);

                if (time <= 0)
                {
                    return;
                }

                if (!data.Lockouts.TryGetValue(ri.id, out var lo))
                {
                    data.Lockouts[ri.id] = lo = new();
                }

                lo.Set(Options.Mode, time);

                if (lo.Any())
                {
                    UpdateUi(ri.player, UiType.Lockout);
                }
                else data.Lockouts.Remove(ri.id);
            }

            private double GetLockoutTime(string mode)
            {
                return config.Settings.Management.Lockout.Get(en ? $"Time Between Raids In Minutes ({mode})" : $"Время между рейдами в минутах ({mode})") * 60;
            }

            public string LangMode(string userid = null, bool strip = false)
            {
                string text = mx($"Mode{Options.Mode}", userid);
                return strip ? rf(text) : text;
            }

            public string Mode(string userid = null, bool forceShowName = false)
            {
                string text = LangMode(userid, true);
                if (config.Settings.Management.TitleCase == true)
                {
                    text = text.TitleCase();
                }
                if (ownerId.IsSteamId())
                {
                    return Instance.mx("Map Marker", null, config.Settings.Markers.ShowPurchased && IsPayLocked ? mx("Purchased") : string.Empty, config.Settings.Markers.ShowOwnersName || forceShowName ? ownerName : mx("Claimed"), text).Trim();
                }
                if (config.Settings.Markers.LootPVE && !AllowPVP || config.Settings.Markers.LootPVP && AllowPVP)
                {
                    return $"{text} {mx("Loot")} {GetLootAmountCounted()}";
                }
                return text;
            }

            private void TrySetPayLock()
            {
                if (config.Settings.Buyable.UsePayLock && rb.type == RaidableType.Purchased && rb.payments.valid)
                {
                    cooldowns.UnionWith(rb.alliance);
                    TrySetPayLock(rb.payments);
                }
            }

            public bool TrySetPayLock(Payments payments, bool paylock = true, bool forced = false)
            {
                IsPayLocked = paylock;
                SetOwnerInternal(payments);
                ClearEnemies();
                //GetRaider(payments.userid.ToString()).HasDestroyed = true;
                return true;
            }

            private void SetOwnerInternal(Payments payments)
            {
                if (config.Settings.Management.LockTime > 0d)
                {
                    if (IsInvoking(ResetPublicOwner))
                    {
                        CancelInvoke(ResetPublicOwner);
                    }
                    Invoke(ResetPublicOwner, config.Settings.Management.LockTimeFloat * 60f);
                }
                this.payments = payments;
                if (!raiders.TryGetValue(payments.userid, out var ri))
                {
                    raiders[payments.userid] = ri = new(payments.userid, payments.username, payments.admin);
                    if (ri.player != null && ri.player.clanId != 0)
                    {
                        ri.clan = GetClan(ri.player);
                        ri.clanId = ri.player.clanId;
                    }
                }
                _currentSphereColor = SphereColor.None;
                ownerId = payments.userid;
                ownerName = payments.username;
                UpdateMarker();
                CreateSpheres();
                if (IsPayLocked) HarmonyModInterface.CallHook("OnRaidableBasePurchased", new object[] { payments.userid.ToString(), Location, Instance.PositionToGrid(Location, false), Options.Level, AllowPVP, BaseName, spawnDateTime, despawnDateTime });
                else HarmonyModInterface.CallHook("OnRaidableBaseLocked", new object[] { payments.userid.ToString(), Location, Instance.PositionToGrid(Location, false), Options.Level, AllowPVP, loadTime, BaseName, spawnDateTime, despawnDateTime });
            }

            private void SetOwner(BasePlayer owner)
            {
                if (!owner.IsKilled()) SetOwnerInternal(new(owner) { Economics = new(Instance, owner) });
                ResetRaiderRelations();
                Protector();
            }

            public void TryStartPurchaseCooldown()
            {
                if (config.Settings.Buyable.Cooldowns.ApplyOnDespawn)
                {
                    StartPurchaseCooldown();
                }
            }

            public void StartPurchaseCooldown()
            {
                if (!IsResetting && !IsUnloading && IsPayLocked && ownerId.IsSteamId())
                {
                    config.Settings.Buyable.Cooldowns.Process(Instance, alliance, ownerId, rb.clan, Options.Mode, set: true);
                }
            }

            public bool HasBuyableCooldown(BasePlayer buyer, string mode)
            {
                if (!IsDespawning && mode.Equals(Options.Mode, StringComparison.OrdinalIgnoreCase) && cooldowns.Contains(buyer.userID))
                {
                    Notify(buyer, "BuyableAlreadyOwner");
                    return true;
                }
                return false;
            }

            public void Refund(BasePlayer player)
            {
                if (config.Settings.Buyable.Refunds.Percentage == 0 || !config.Settings.Buyable.Refunds.Enabled || !payments.valid)
                {
                    return;
                }

                IsResetting = config.Settings.Buyable.Refunds.Reset;
                StartPurchaseCooldown();
                IsPayLocked = false;
                Reset(player);

                double percentage = config.Settings.Buyable.Refunds.Percentage;

                if (payments.Custom?.Options?.Count > 0 && payments.Custom.paid)
                {
                    if (payments.Custom.buyer == null && payments.Custom.owner == null) payments.Custom.buyerId = player.userID;
                    string message = payments.Custom.RefundItems(percentage);
                    if (payments.Custom.buyer != player) NotifyUnlessSmart(player, message);
                }

                if (payments.ServerRewards?.RP > 0)
                {
                    int points = payments.ServerRewards.RefundPoints(percentage);
                    if (payments.ServerRewards.buyer != player) NotifyUnlessSmart(player, "Refunded RP", points);
                }

                if (payments.Economics?.money > 0)
                {
                    double amount = payments.Economics.RefundMoney(percentage);
                    if (payments.Economics.buyer != player) NotifyUnlessSmart(player, "Refunded Money", amount);
                }
            }

            private void Reset(BasePlayer player)
            {
                if (config.Settings.Buyable.Refunds.Reset && data.BuyableCooldowns.TryGetValue(player.userID, out var info))
                {
                    info.Modes.Remove(Options.Mode);
                    if (!BuyableInfo.HasTimeRemaining(Instance, player.userID))
                    {
                        data.BuyableCooldowns.Remove(player.userID);
                    }
                }
            }

            private double PlayerActivityTimeLeft(ulong userid)
            {
                if (config.Settings.Management.LockTime <= 0d)
                {
                    return double.PositiveInfinity;
                }

                if (!raiders.TryGetValue(userid, out var raider))
                {
                    return double.PositiveInfinity;
                }

                return (config.Settings.Management.LockTime * 60d) - (Time.timeAsDouble - raider.lastActiveTime);
            }

            internal bool TryGetOwnerActivityTimeLeft(out double secondsLeft)
            {
                secondsLeft = 0d;

                if (!IsOpened || IsPayLocked || !ownerId.IsSteamId() || config.Settings.Management.LockTime <= 0d)
                {
                    return false;
                }

                double remaining = PlayerActivityTimeLeft(ownerId);

                if (double.IsPositiveInfinity(remaining))
                {
                    return false;
                }

                secondsLeft = Math.Max(0d, remaining);
                return true;
            }

            public bool IsPlayerActive(ulong userid)
            {
                return PlayerActivityTimeLeft(userid) > 0d;
            }

            public void TrySetOwner(BasePlayer attacker, BaseEntity entity, HitInfo info, bool isFireDamage)
            {
                if (!config.Settings.Management.UseOwners)
                {
                    CreateSpheres();
                    return;
                }

                if (!IsOpened || ownerId.IsSteamId() || BypassUseOwners() || config.Settings.Management.PreventHogging && Instance.IsEventOwner(attacker, false))
                {
                    return;
                }

                if (HasLockout(attacker, !isFireDamage) || IsHogging(attacker))
                {
                    NullifyDamage(info);
                    return;
                }

                if (entity is HumanoidNPC)
                {
                    SetOwner(attacker);
                    return;
                }

                if (!(entity is BuildingBlock or Door or SimpleBuildingBlock))
                {
                    return;
                }

                if (InRange2D(attacker.transform.position, Location, ProtectionRadius) || IsLootingWeapon(info))
                {
                    SetOwner(attacker);
                }
            }

            public void ResetRaiderRelations()
            {
                foreach (var ri in raiders.Values)
                {
                    if (ri.userid == ownerId)
                    {
                        continue;
                    }

                    ri.IsAllowed = false;
                    ri.IsAlly = false;
                }
            }

            public void ClearEnemies()
            {
                raiders.RemoveAll((uid, ri) => !IsAlly(ri, ownerId));
            }

            public void CheckDespawn()
            {
                if (!IsOpened)
                {
                    if (DespawnMinutesReset)
                    {
                        UpdateDespawnDateTime(DespawnMinutes);
                    }
                    return;
                }

                if (IsDespawning || DespawnMinutesInactive <= 0 || !IsEngaged && EngageOnBaseDamage)
                {
                    return;
                }

                if (DespawnMinutesInactiveReset || double.IsPositiveInfinity(despawnDeadline)) //if (DespawnMinutesInactiveReset || despawnDateTime == DateTime.MaxValue)
                {
                    UpdateDespawnDateTime(DespawnMinutesInactive);
                }
            }

            private bool despawnTimeUpdated;
            public void UpdateDespawnDateTime(float time)
            {
                //if (time > 0f)
                //{
                //    despawnDateTime = DateTime.Now.AddSeconds(time * 60f);
                //}
                //else
                //{
                //    despawnDateTime = DateTime.Now;
                //}
                double seconds = Math.Max(0d, time * 60d);
                despawnDeadline = Time.realtimeSinceStartupAsDouble + seconds;
                despawnDateTime = DateTime.Now.AddSeconds(seconds);
                despawnTimeUpdated = true;
            }

            private void OnRaidableDespawnUpdate()
            {
                despawnTimeUpdated = false;
                HarmonyModInterface.CallHook("OnRaidableDespawnUpdate", new object[8] { Location, Options.Level, AllowPVP, ownerId, BaseName, ProtectionRadius, GetLootAmountCounted(), despawnDateTime });
            }

            public bool EndWhenCupboardIsDestroyed()
            {
                if (config.Settings.Management.EndWhenCupboardIsDestroyed && privSpawned)
                {
                    return IsCompleted = IsPrivDestroyed || priv.IsKilled() || privHadLoot && priv.inventory.IsEmpty();
                }

                return false;
            }

            public bool CanUndo()
            {
                if (EndWhenCupboardIsDestroyed())
                {
                    return IsCompleted = true;
                }

                if (UpdateLootAmountCounted() > 0)
                {
                    return false;
                }

                return IsCompleted = true;
            }

            private bool CanPlayerBeLooted(BasePlayer looter, ulong target)
            {
                return PlayersLootable || IsAlly(looter, target);
            }

            private bool CanBeLooted(BasePlayer player, BaseEntity e)
            {
                if (IsLoading)
                {
                    return CanBypassAuthorized(player.userID);
                }

                if (IsProtectedWeapon(e, true))
                {
                    if (config.Settings.Management.LootableTraps)
                    {
                        if (!CanBypassAuthorized(player.userID)) isAuthorized = true;

                        return true;
                    }

                    return false;
                }

                if (e is NPCPlayerCorpse)
                {
                    return true;
                }

                if (e is LootableCorpse corpse)
                {
                    if (CanBypass(player) || !corpse.playerSteamID.IsSteamId() || corpse.playerSteamID == player.userID || corpse.playerName == player.displayName)
                    {
                        return true;
                    }

                    return CanPlayerBeLooted(player, corpse.playerSteamID);
                }
                else if (e is DroppedItemContainer container)
                {
                    if (CanBypass(player) || !container.playerSteamID.IsSteamId() || container.playerSteamID == player.userID || container.playerName == player.displayName)
                    {
                        return true;
                    }

                    return CanPlayerBeLooted(player, container.playerSteamID);
                }

                return true;
            }

            public bool IsProtectedWeapon(BaseEntity e, bool checkBuiltList = false)
            {
                if (e.IsNull() || checkBuiltList && BuiltList.Contains(e))
                {
                    return false;
                }

                return IsWeapon(e);
            }

            public bool IsWeapon(BaseEntity e) => e is GunTrap or FlameTurret or FogMachine or SamSite or AutoTurret or TeslaCoil;

            public bool IsFoundation(BaseEntity e) => e.ShortPrefabName is "foundation.triangle" or "foundation" || IsWaterBasesFoundation(e);

            public bool IsCompound(BaseEntity e) => IsFoundation(e) || e.ShortPrefabName.Contains("floor") || e.ShortPrefabName.Contains("wall");

            public object CanLootEntityInternal(BasePlayer player, BaseEntity entity)
            {
                if (player == null || entity.OwnerID == player.userID || !entity.OwnerID.IsSteamId() && !Has(entity))
                {
                    return null;
                }

                //if (!IsVanished(player) && IsPickupBlacklisted(entity.ShortPrefabName))
                //{
                //    return true;
                //}

                if (entity.ShortPrefabName == "coffinstorage" && Mathf.Approximately(entity.transform.position.Distance(new(0f, -50f, 0f)), 0f))
                {
                    return null;
                }

                if (IsMountable(entity))
                {
                    return null;
                }

                if (!IsVanished(player) && !CanBeLooted(player, entity))
                {
                    return true;
                }

                if (entity is LootableCorpse || entity is DroppedItemContainer)
                {
                    return null;
                }

                if (player.GetMounted() != null)
                {
                    Notify(player, "CannotBeMounted");
                    return true;
                }

                if (Options.RequiresCupboardAccess && !CanBuild(player))
                {
                    Notify(player, "MustBeAuthorized");
                    return true;
                }

                if (Type != RaidableType.None)
                {
                    foreach (var ri in raiders.Values)
                    {
                        if (ri.IsParticipant)
                        {
                            CheckDespawn();
                            break;
                        }
                    }
                }

                if (player.IsFlying || IsVanished(player) || entity.OwnerID != 0)
                {
                    return null;
                }

                if (!Options.NPC.Inside.Sleepers.Lootable && entity.Is(out HumanoidNPC npc) && npc.IsSleeping())
                {
                    Notify(player, "This sleeper cannot be looted.");
                    return true;
                }

                if (!AddLooter(player))
                {
                    return true;
                }

                AddMember(player.userID);

                return null;
            }

            public bool CanBuild(BasePlayer player)
            {
                if (privSpawned)
                {
                    return priv.IsKilled() || priv.IsAuthed(player);
                }
                return true;
            }

            public static void ClearInventory(ItemContainer container)
            {
                if (container == null || container.itemList == null)
                {
                    return;
                }
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    item.GetHeldEntity().SafelyKill();
                    item.RemoveFromContainer();
                    item.Remove(0f);
                }
            }

            public void SetNoDrops()
            {
                foreach (var container in _allcontainers)
                {
                    if (container.IsKilled())
                    {
                        continue;
                    }
                    if (!IsShuttingDown && IsCompleted && Options != null && Options.DropPrivilegeLoot && container is BuildingPrivlidge)
                    {
                        Instance.DropOrRemoveItems(container, this, true, true);
                    }
                    else
                    {
                        container.dropsLoot = false;
                        ClearInventory(container.inventory);
                    }
                }

                if (Type != RaidableType.None)
                {
                    foreach (var info in turrets.Values)
                    {
                        if (!info.IsKilled())
                        {
                            var turret = info.turret;
                            ClearInventory(turret.inventory);
                            try { if (turret.IsInvoking(turret.UpdateAttachedWeapon)) turret.CancelInvoke(turret.UpdateAttachedWeapon); } catch { }
                        }
                    }
                }

                ItemManager.DoRemoves();
                UpdateLootAmountCounted();
            }

            public void DestroyInputs()
            {
                raiders.Values.ForEach(ri => ri.DestroyInput());
            }

            public void Init(RandomBase rb, List<BaseEntity> entities = null)
            {
                RemoveNearDistance = spawns == null ? rb.options.ProtectionRadius(rb.type) : spawns.RemoveNear(rb.Position, rb.options.ProtectionRadius(rb.type), rb.FromCacheType, rb.ToCacheType, rb.type);

                data.Cycle.Add(rb.type, rb.options.Mode, rb.BaseName, rb.owner);

                alliance.UnionWith(rb.alliance);

                if (!Options.Setup.BlockedPrefabs.IsNullOrEmpty())
                {
                    setupBlockedPrefabs.AddRange(Options.Setup.BlockedPrefabs);
                }

                if (Options.Elevators.BMGOnly)
                {
                    TryInvokeMethod(() =>
                    {
                        using var bmgs = BMGELEVATOR.FixElevators(this);
                        foreach (var bmg in bmgs)
                        {
                            Elevators[bmg.Key] = new() { BMG = bmg.Value, raid = this };
                        }
                    });
                }

                TryInvokeMethod(() => AddEntities(entities));

                HarmonyModInterface.Mods.NextTick(() =>
                {
                    if (IsUnloading) return;

                    TryInvokeMethod(SetCenterFromMultiplePoints);
                    TryInvokeMethod(TrySetPayLock);
                    TryInvokeMethod(SetupElevators);

                    setupRoutine = ServerMgr.Instance.StartCoroutine(EntitySetup());
                });
            }

            private void Teleport()
            {
                if (rb.IsTeleportPending(rb.owner, Location))
                {
                    if (rb.options.CustomSpawns.BuyableUiDuration > 0f)
                    {
                        Instance.UI.ShowBuyableTeleportUi(rb.owner, false, rb.options.CustomSpawns.BuyableUiDuration, rb.options.Mode);
                    }
                    else
                    {
                        Instance.BuyableTeleport(rb.owner);
                    }
                    InitiateTurretOnSpawn = true;
                }
            }

            private void SetupElevators()
            {
                CancelInvoke(ShowElevatorHealthUi);
                ClearElevatorHealthUi();

                foreach (RaidElevator elevator in Elevators.Values)
                {
                    if (elevator.IsBMG())
                    {
                        elevator.BMG.Init(this);
                    }
                }

                if (Options.Elevators.Enabled && Elevators.Count > 0)
                {
                    InvokeRepeating(ShowElevatorHealthUi, 1f, 1f);
                }
            }

            private void ShowElevatorHealthUi()
            {
                BuildingOptionsElevators options = Options.Elevators;

                if (IsDespawning || !options.Enabled || Elevators.Count == 0)
                {
                    ClearElevatorHealthUi();
                    return;
                }

                float maxDistance = Mathf.Max(1f, options.HealthUiDistance);
                float maxDistanceSqr = maxDistance * maxDistance;
                using var visible = DisposableHashSet<ulong>();

                foreach (Raider raiderInfo in raiders.Values)
                {
                    BasePlayer player = raiderInfo.player;
                    ulong userid = raiderInfo.userid;
                    Elevator elevator = null;
                    bool canSee = intruders.Contains(userid) && player != null && player.IsConnected && !player.IsKilled() && !player.IsSleeping() && TryGetNearbyElevator(player, maxDistanceSqr, out elevator);

                    if (!canSee)
                    {
                        RemoveElevatorHealthUi(userid, player);
                        continue;
                    }

                    visible.Add(userid);
                    UpdateElevatorHealthUi(player, raiderInfo.id, elevator);
                }

                if (_elevatorHealthUi.Count == 0)
                {
                    return;
                }

                using var remove = DisposableList<ulong>();

                foreach (KeyValuePair<ulong, ElevatorHealthUiState> pair in _elevatorHealthUi)
                {
                    if (!visible.Contains(pair.Key))
                    {
                        remove.Add(pair.Key);
                    }
                }

                for (int index = 0; index < remove.Count; index++)
                {
                    RemoveElevatorHealthUi(remove[index]);
                }
            }

            private bool TryGetNearbyElevator(BasePlayer player, float maxDistanceSqr, out Elevator nearest)
            {
                nearest = null;
                float nearestDistanceSqr = maxDistanceSqr;
                Vector3 playerPosition = player.transform.position;

                foreach (RaidElevator raidElevator in Elevators.Values)
                {
                    Elevator elevator = raidElevator?.Entity;

                    if (elevator.IsKilled())
                    {
                        continue;
                    }

                    float distanceSqr = (playerPosition - elevator.transform.position).sqrMagnitude;

                    if (BMGELEVATOR.GetElevatorLift(elevator, out ElevatorLift lift))
                    {
                        distanceSqr = Mathf.Min(distanceSqr, (playerPosition - lift.transform.position).sqrMagnitude);
                    }

                    if (distanceSqr > nearestDistanceSqr)
                    {
                        continue;
                    }

                    nearestDistanceSqr = distanceSqr;
                    nearest = elevator;
                }

                return nearest != null;
            }

            private void UpdateElevatorHealthUi(BasePlayer player, string language, Elevator elevator)
            {
                if (elevator?.net == null)
                {
                    RemoveElevatorHealthUi(player.userID, player);
                    return;
                }

                BuildingOptionsElevators options = Options.Elevators;
                ulong userid = player.userID;
                float maxHealth = Mathf.Max(1f, elevator._maxHealth);
                float health = Mathf.Clamp(elevator._health, 0f, maxHealth);
                float fraction = Mathf.Clamp01(health / maxHealth);
                string healthText = FormattableString.Invariant($"{health:0.#} / {maxHealth:0.#}");
                NetworkableId elevatorId = elevator.net.ID;

                if (!_elevatorHealthUi.TryGetValue(userid, out ElevatorHealthUiState state) || state.ElevatorId != elevatorId || state.ShowHealthBar != options.ShowHealthBar)
                {
                    CreateElevatorHealthUi(player, language, elevatorId, healthText, fraction);
                    return;
                }

                CuiElementContainer updates = new();

                if (!string.Equals(state.HealthText, healthText, StringComparison.Ordinal))
                {
                    UiHandler.AddCuiTextUpdate(updates, healthText, ElevatorHealthPanel, ElevatorHealthValue);
                }

                if (options.ShowHealthBar && Mathf.Abs(state.HealthFraction - fraction) > 0.0001f)
                {
                    UiHandler.AddCuiRectTransformUpdate(updates, FormattableString.Invariant($"{fraction:0.####} 1"), ElevatorHealthTrack, ElevatorHealthFill);
                }

                if (updates.Count > 0 && !CuiHelper.AddUi(player, updates))
                {
                    RemoveElevatorHealthUi(userid, player);
                    return;
                }

                state.HealthText = healthText;
                state.HealthFraction = fraction;
            }

            private void CreateElevatorHealthUi(BasePlayer player, string language, NetworkableId elevatorId, string healthText, float fraction)
            {
                DestroyElevatorHealthUi(player);

                BuildingOptionsElevators options = Options.Elevators;
                UiHandler.UiPalette palette = Instance.UI.GetPalette();
                bool useTheme = options.UseThemeColors;
                string background = useTheme ? palette.Background : UiHandler.ConvertHexToRGBA(options.PanelColor, options.PanelAlpha ?? 0.98f);
                string panel = useTheme ? palette.Panel : UiHandler.ConvertHexToRGBA(options.TitlePanelColor, 1f);
                string accent = useTheme ? palette.Accent : panel;
                string textColor = useTheme ? palette.Text : "1 1 1 1";
                string mutedColor = useTheme ? palette.Muted : "0.75 0.75 0.75 1";
                string progressBackground = useTheme ? palette.ProgressBackground : UiHandler.ConvertHexToRGBA(options.TitlePanelColor, 0.55f);
                string offsetMin = FormattableString.Invariant($"{options.OffsetMin.x:0.###} {options.OffsetMin.y:0.###}");
                string offsetMax = FormattableString.Invariant($"{options.OffsetMax.x:0.###} {options.OffsetMax.y:0.###}");
                int fontSize = Mathf.Clamp(options.FontSize, 9, 18);
                CuiElementContainer container = new();

                UiHandler.AddCuiPanel(container, background, options.AnchorMin, options.AnchorMax, offsetMin, offsetMax, Instance.UI.ELEVATOR_PARENT, ElevatorHealthPanel);
                UiHandler.AddCuiPanel(container, accent, "0 1", "1 1", "0 -2", "0 0", ElevatorHealthPanel, ElevatorHealthPanel + "_Accent");
                UiHandler.AddCuiPanel(container, accent, "0 0.5", "0 0.5", "10 -3", "16 3", ElevatorHealthPanel, ElevatorHealthPanel + "_Dot");
                UiHandler.AddCuiElement(container, Instance.mx("Elevator Health", language), fontSize, TextAnchor.MiddleLeft, mutedColor, "0 0", "0.68 1", "23 7", "-4 -2", ElevatorHealthPanel, ElevatorHealthPanel + "_Title", false);
                UiHandler.AddCuiElement(container, healthText, fontSize, TextAnchor.MiddleRight, textColor, "0.64 0", "1 1", "0 7", "-10 -2", ElevatorHealthPanel, ElevatorHealthValue);

                if (options.ShowHealthBar)
                {
                    UiHandler.AddCuiPanel(container, progressBackground, "0 0", "1 0", "10 5", "-10 9", ElevatorHealthPanel, ElevatorHealthTrack);
                    UiHandler.AddCuiPanel(container, accent, "0 0", FormattableString.Invariant($"{fraction:0.####} 1"), "0 0", "0 0", ElevatorHealthTrack, ElevatorHealthFill);
                }

                if (!CuiHelper.AddUi(player, container))
                {
                    _elevatorHealthUi.Remove(player.userID);
                    return;
                }

                _elevatorHealthUi[player.userID] = new ElevatorHealthUiState
                {
                    Player = player,
                    ElevatorId = elevatorId,
                    HealthText = healthText,
                    HealthFraction = fraction,
                    ShowHealthBar = options.ShowHealthBar
                };
            }

            private void RemoveElevatorHealthUi(ulong userid, BasePlayer player = null)
            {
                if (!_elevatorHealthUi.TryGetValue(userid, out ElevatorHealthUiState state))
                {
                    return;
                }

                player ??= state.Player;
                _elevatorHealthUi.Remove(userid);
                DestroyElevatorHealthUi(player);
            }

            private void ClearElevatorHealthUi()
            {
                if (_elevatorHealthUi.Count == 0)
                {
                    return;
                }

                foreach (ElevatorHealthUiState state in _elevatorHealthUi.Values)
                {
                    DestroyElevatorHealthUi(state.Player);
                }

                _elevatorHealthUi.Clear();
            }

            private static void DestroyElevatorHealthUi(BasePlayer player)
            {
                if (player != null)
                {
                    CuiHelper.DestroyUi(player, ElevatorHealthPanel);
                }
            }

            private HashSet<ulong> _granted = new();

            public bool HasCardPermission(BasePlayer player)
            {
                var options = Options.Elevators;
                if (options.RequiredAccessLevel == 0 || _granted.Contains(player.userID) || player.HasPermission("raidablebases.elevators.bypass.card"))
                {
                    return true;
                }

                string shortname = options.RequiredAccessLevel == 1 ? "keycard_green" : options.RequiredAccessLevel == 2 ? "keycard_blue" : "keycard_red";
                Item item = player.inventory.FindItemByItemName(shortname) ?? player.GetActiveItem();

                if (item == null || item.skin != options.SkinID)
                {
                    Notify(player, options.RequiredAccessLevel == 1 ? "Elevator Green Card" : options.RequiredAccessLevel == 2 ? "Elevator Blue Card" : options.RequiredAccessLevel == 3 ? "Elevator Red Card" : "Elevator Special Card");
                    return false;
                }

                if (item.GetHeldEntity() is Keycard keycard && keycard != null && keycard.accessLevel == options.RequiredAccessLevel)
                {
                    if (options.RequiredAccessLevelOnce)
                    {
                        _granted.Add(player.userID);
                    }

                    return true;
                }

                Notify(player, options.RequiredAccessLevel == 1 ? "Elevator Green Card" : options.RequiredAccessLevel == 2 ? "Elevator Blue Card" : options.RequiredAccessLevel == 3 ? "Elevator Red Card" : "Elevator Special Card");
                return false;
            }

            public bool HasBuildingPermission(BasePlayer player)
            {
                if (!Options.Elevators.RequiresBuildingPermission || player.HasPermission("raidablebases.elevators.bypass.building") || priv.IsKilled() || priv.IsAuthed(player))
                {
                    return true;
                }

                Notify(player, "Elevator Privileges");
                return false;
            }

            private List<string> setupBlockedPrefabs = new();

            private void AddEntities(List<BaseEntity> entities)
            {
                if (entities.IsNullOrEmpty())
                {
                    return;
                }
                foreach (var e in entities)
                {
                    if (e.IsKilled())
                    {
                        continue;
                    }
                    if (setupBlockedPrefabs.Any(e.ShortPrefabName))
                    {
                        e.DelayedSafeKill();
                        continue;
                    }
                    Vector3 position = e.transform.position;
                    if (IsFoundation(e))
                    {
                        foundations.Add(position);
                    }
                    if (e.ShortPrefabName.StartsWith("floor"))
                    {
                        floors.Add(position);
                    }
                    if (IsCompound(e))
                    {
                        compound.Add(position);
                    }
                    e.OwnerID = 0;
                    AddEntity(e);
                }
            }

            private bool centerSetFromMultiplePoints, isEventUnderground;
            public void SetCenterFromMultiplePoints()
            {
                Vector3 vector = Location;

                if (compound.Count > 1)
                {
                    var bounds = new Bounds(compound[0], Vector3.zero);

                    for (int i = 1; i < compound.Count; i++)
                    {
                        bounds.Encapsulate(compound[i]);
                    }

                    vector.x = bounds.center.x;
                    vector.z = bounds.center.z;
                }

                Location = vector;
                LocationXZ3D = vector.XZ3D();

                go.transform.position = Location;

                centerSetFromMultiplePoints = true;
                isEventUnderground = EnvironmentManager.Check(Location, EnvironmentType.TrainTunnels | EnvironmentType.Underground);
            }

            public bool SpawnLegacyShelter()
            {
                if (Options.Mode != RaidableMode.Legacy)
                {
                    return false;
                }

                Location.y = Instance.GetSpawnHeight(Location, false, true);

                Quaternion rot = new(0f, 0.07062808f, 0f, -0.9975027f);
                LegacyShelter shelter = GameManager.server.CreateEntity("assets/prefabs/building/legacy.shelter.wood/legacy.shelter.wood.deployed.prefab", Location, rot) as LegacyShelter;
                if (shelter == null)
                {
                    return false;
                }

                var priv = shelter.GetComponentInChildren<EntityPrivilege>();
                if (priv == null)
                {
                    return false;
                }

                bool spawned = false;
                try
                {
                    priv.SetFlagLocal(SimplePrivilege.Flag_MaxAuths, true);
                    shelter.enableSaving = false;
                    shelter.Spawn();
                    shelter.decay = null;
                    shelter.upkeepTimer = float.MinValue;

                    if (shelter.GetChildDoor().Is(out LegacyShelterDoor door) && door.GetSlot(BaseEntity.Slot.Lock) is KeyLock keyLock)
                    {
                        keyLock.keyCode = UnityEngine.Random.Range(1, 100000);
                        keyLock.OwnerID = 0;
                        keyLock.firstKeyCreated = true;
                        keyLock.SetFlagLocal(BaseEntity.Flags.Locked, true);
                        keyLock.SendNetworkUpdate();
                    }

                    using var containers = DisposableList<(Vector3 position, Quaternion rotation, string prefab)>();
                    containers.Add((new(0.96f, -0.11f, 0.52f), new(-0.001644136f, 0.7649058f, -0.0191349f, -0.6438558f), "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"));
                    containers.Add((new(-0.95f, -0.14f, 0.87f), new(-0.01356944f, 0.9988341f, 0.0006293782f, 0.0463252f), "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"));
                    containers.Add((new(-1.02f, -0.15f, 0.06f), new(0.001078943f, -0.9967492f, 0.01354115f, -0.0794142f), "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"));
                    containers.Add((new(0f, -0.13f, 0.55f), new(-0.002029082f, 0.7777112f, -0.01909789f, -0.6283283f), "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"));

                    foreach (var (position, rotation, prefab) in containers)
                    {
                        BoxStorage box = GameManager.server.CreateEntity(prefab, Location - position, rot * rotation) as BoxStorage;
                        if (box != null)
                        {
                            if (box.TryGetComponent<GroundWatch>(out var gw)) Destroy(gw);
                            if (box.TryGetComponent<DestroyOnGroundMissing>(out var gm)) Destroy(gm);
                            box.enableSaving = false;
                            box.Spawn();
                            spawned = true;
                            _iice.Add(box);
                            Entities.Add(box);
                            _containers.Add(box);
                            _allcontainers.Add(box);
                            Instance.CreateSpawnCallback(this)(box);
                        }
                    }

                    Instance.CreateSpawnCallback(this)(shelter);
                }
                catch (Exception ex)
                {
                    Puts(ex);
                    return false;
                }
                return spawned;
            }

            private SphereColor GetSphereColor(SphereColorSettings sc)
            {
                if (sc.Unlocked != SphereColor.None && !ownerId.IsSteamId()) return sc.Unlocked;
                if (sc.Locked != SphereColor.None && ownerId.IsSteamId()) return sc.Locked;
                if (sc.PVPState != SphereColor.None && AllowPVP) return sc.PVPState;
                if (sc.PVEState != SphereColor.None && !AllowPVP) return sc.PVEState;
                return GetParticipantAmount() > 0 ? sc.Active : sc.Inactive;
            }

            public void CreateSpheres()
            {
                if (Options.Silent)
                {
                    return;
                }

                if (!centerSetFromMultiplePoints)
                {
                    if (!IsInvoking(CreateSpheres))
                    {
                        Invoke(CreateSpheres, 0.1f);
                    }
                    return;
                }

                SphereColor sphereColor = GetSphereColor(Options.SphereColor);

                if (_currentSphereColor != SphereColor.None && _currentSphereColor == sphereColor)
                {
                    return;
                }

                if (_currentSphereColor == SphereColor.None && sphereColor == SphereColor.None && spheres.Count > 0)
                {
                    return;
                }

                DestroySpheres();

                if (IsDespawning)
                {
                    return;
                }

                if (Instance.ColorPrefabMap.TryGetValue(sphereColor, out var prefabs))
                {
                    prefabs.ForEach(SpawnSphere);
                }

                if (Options.SphereAmount > 0)
                {
                    for (int i = 0; i < Options.SphereAmount; i++)
                    {
                        SpawnSphere("assets/prefabs/visualization/sphere.prefab");
                    }
                }

                _currentSphereColor = sphereColor;
            }

            internal SphereColor _currentSphereColor;

            private void SpawnSphere(string prefab)
            {
                if (StringPool.toNumber.ContainsKey(prefab) && GameManager.server.CreateEntity(prefab, Location) is SphereEntity sphere)
                {
                    sphere.currentRadius = ProtectionRadius * 2f;
                    sphere.lerpRadius = sphere.currentRadius;
                    sphere.enableSaving = false;
                    sphere.skinID = RB_SKIN_ID;
                    sphere.OwnerID = RB_SKIN_ID;
                    sphere.Spawn();
                    spheres.Add(sphere);
                }
            }

            private IEnumerator CreateZoneWallsCo()
            {
                bool debug = DebugMode;
                long memoryBefore = 0, gen0Before = 0, gen1Before = 0, gen2Before = 0;
                Stopwatch terrainStopwatch = null;
                Stopwatch wallStopwatch = null;
                int wallCount = 0;

                if (debug)
                {
                    memoryBefore = System.GC.GetTotalMemory(false);
                    gen0Before = System.GC.CollectionCount(0);
                    gen1Before = System.GC.CollectionCount(1);
                    gen2Before = System.GC.CollectionCount(2);
                    terrainStopwatch = Stopwatch.StartNew();
                }

                const float defaultWallStep = 6f;
                const float frontierWallStep = 4.5f;
                const float tallWallHeight = 8.018669f;
                const float woodWallHeight = 6.824915f;

                FrameDeadline deadline = new(wallFrameBudgetMilliseconds);
                float minHeight = float.MaxValue;
                float maxHeight = float.MinValue;
                float maxTerrainHeight = float.MinValue;
                float maxWaterHeight = float.MinValue;
                float maxDistance = 48f;
                int stacks = Options.ArenaWalls.Stacks;
                Vector3 center = new(Location.x, Location.y, Location.z);
                string prefab = GetWallPrefabName(center, out bool frontier);
                float wallStep = frontier ? frontierWallStep : defaultWallStep;
                float wallHeight = prefab == external_wood ? woodWallHeight : tallWallHeight;
                float gap = prefab switch
                {
                    external_ice or external_stone or external_adobe => 0.3f,
                    _ => 0.5f
                };
                int next1 = Mathf.CeilToInt(360 / Options.ArenaWalls.Radius * 0.1375f);
                float next2 = 360 / Options.ArenaWalls.Radius - gap;
                bool adjusted = false;

                using var wallDebug = debug ? DisposableBuilder.Get() : null;
                if (debug && frontier)
                {
                    wallDebug.AppendLine($"Frontier wall metrics: height={wallHeight:0.######}, step={wallStep:0.######}, overlap={wallHeight - wallStep:0.######}, dynamic stacks={Options.ArenaWalls.IgnoreWhenClippingTerrain}");
                }

                if (Options.ArenaWalls.IgnoreForcedHeight && Options.Setup.ForcedHeight >= 0 && center.y >= Options.Setup.ForcedHeight)
                {
                    center.y = TerrainMeta.HeightMap.GetHeight(center);
                    adjusted = true;
                }

                using (var vectors1 = SpawnsController.GetCircumferencePositions(center, Options.ArenaWalls.Radius, next1, false, false, 1f))
                {
                    foreach (var position in vectors1)
                    {
                        if (IsDespawning)
                        {
                            if (debug)
                            {
                                terrainStopwatch.Stop();
                            }

                            yield break;
                        }

                        if (deadline.Expired)
                        {
                            if (debug) terrainStopwatch.Stop();
                            yield return null;
                            deadline.Reset();
                            if (debug) terrainStopwatch.Start();
                        }

                        float y = SpawnsController.GetSpawnHeight(position, false, false, targetMask | Layers.Mask.Construction);
                        float waterHeight = TerrainMeta.WaterMap.GetHeight(position);

                        maxTerrainHeight = Mathf.Max(y, maxTerrainHeight);
                        maxWaterHeight = Mathf.Max(waterHeight, maxWaterHeight);
                        maxHeight = Mathf.Max(maxTerrainHeight, maxWaterHeight);
                        minHeight = Mathf.Min(y, minHeight);
                        center.y = minHeight;
                    }
                }

                if (spawns != null && spawns.IsCustomSpawn)
                {
                    center.y = Location.y - (wallStep * 0.5f);
                    maxDistance += Location.y;
                }

                if (Options.Setup.ForcedHeight >= 0)
                {
                    maxDistance += Options.Setup.ForcedHeight + Options.Setup.PasteHeightAdjustment;

                    if (Options.ArenaWalls.LeastAmount && adjusted)
                    {
                        stacks += Mathf.FloorToInt((maxHeight - minHeight) / wallStep);
                    }
                    else
                    {
                        stacks = Mathf.FloorToInt((Options.Setup.ForcedHeight + Options.Setup.PasteHeightAdjustment) / wallStep);
                    }
                }
                else if (Options.ArenaWalls.IgnoreWhenClippingTerrain)
                {
                    stacks += Mathf.FloorToInt((maxHeight - minHeight) / wallStep);
                }

                if (debug && frontier && stacks > 1)
                {
                    wallDebug.Append(string.Format("Frontier stack setup: configured={0}, calculated={1}, wallStep={2}, wallHeight={3}, terrainMin={4}, terrainMax={5}, waterMax={6}, effectiveMax={7}, range={8}, forcedHeight={9}, pasteAdjustment={10}, leastAmount={11}, ignoreForcedHeight={12}, adjusted={13}, ignoreClipping={14}, customSpawn={15}, centerY={16}, locationY={17}, maxDistance={18}",
                        Options.ArenaWalls.Stacks, stacks, wallStep, wallHeight, minHeight, maxTerrainHeight, maxWaterHeight, maxHeight, maxHeight - minHeight, Options.Setup.ForcedHeight, Options.Setup.PasteHeightAdjustment, Options.ArenaWalls.LeastAmount, Options.ArenaWalls.IgnoreForcedHeight, adjusted, Options.ArenaWalls.IgnoreWhenClippingTerrain, spawns != null && spawns.IsCustomSpawn, center.y, Location.y, maxDistance
                    ));
                }

                using var vectors2 = SpawnsController.GetCircumferencePositions(center, Options.ArenaWalls.Radius, next2, false, false, center.y);

                if (debug)
                {
                    terrainStopwatch.Stop();
                    wallStopwatch = Stopwatch.StartNew();
                }

                for (int i = 0; i < stacks; i++)
                {
                    float currentY = center.y + (i * wallStep);

                    if (currentY - Location.y > maxDistance)
                    {
                        break;
                    }

                    if (Options.ArenaWalls.LeastAmount && !Options.ArenaWalls.IgnoreForcedHeight && Options.Setup.ForcedHeight != -1 && i + 1 < stacks * 0.75)
                    {
                        continue;
                    }

                    foreach (var v in vectors2)
                    {
                        if (IsDespawning)
                        {
                            if (debug)
                            {
                                wallStopwatch.Stop();
                            }

                            yield break;
                        }

                        if (deadline.Expired)
                        {
                            if (debug) wallStopwatch.Stop();
                            yield return null;
                            deadline.Reset();
                            if (debug) wallStopwatch.Start();
                        }

                        Vector3 position = new(v.x, currentY, v.z);
                        float terrainHeight = TerrainMeta.HeightMap.GetHeight(position);

                        if (terrainHeight - currentY > wallHeight)
                        {
                            continue;
                        }

                        if (Options.ArenaWalls.LeastAmount)
                        {
                            float h = SpawnsController.GetSpawnHeight(position, !IsWaterSpawn, false, targetMask | Layers.Mask.Construction);
                            float j = stacks * wallStep + wallStep;

                            if (position.y - terrainHeight > j && position.y < h)
                            {
                                continue;
                            }
                        }

                        if (Options.ArenaWalls.UseUFOWalls)
                        {
                            position.y += 1f;
                        }

                        //Vector3 dir = center - position;
                        //dir.y = 0f;
                        //var rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
                        //var e = GameManager.server.CreateEntity(prefab, position, rotation) as SimpleBuildingBlock;

                        var e = GameManager.server.CreateEntity(prefab, position, Quaternion.identity) as SimpleBuildingBlock;
                        if (e == null)
                        {
                            continue;
                        }

                        e.transform.LookAt(center.WithY(position.y), Vector3.up);

                        if (Options.ArenaWalls.UseUFOWalls)
                        {
                            e.transform.Rotate(-66.6f, 0f, 0f);
                        }
                        else
                        {
                            e.transform.Rotate(0f, 180f, 0f);
                        }

                        e.enableSaving = false;
                        e.Spawn();

                        if (e == null)
                        {
                            continue;
                        }

                        wallCount++;
                        SetupEntity(e);

                        if (e.variants != null && e.variants.Length != 0)
                        {
                            e.SetVariant(0);
                        }

                        e.debrisPrefab.guid = null;
                        e.canBeDemolished = false;
                        e.StopBeingDemolishable();

                        float fractionUnder = Mathf.Clamp01((terrainHeight - currentY) / wallHeight);

                        if (fractionUnder > 0.2f)
                        {
                            FixNav(frontier, e);
                        }

                        if (Options.ArenaWalls.IgnoreWhenClippingTerrain && i == stacks - 1 && ShouldAddZoneWallStack(v, currentY, terrainHeight, wallHeight, wallStep, frontier, wallDebug))
                        {
                            stacks++;
                        }
                    }
                }

                if (debug)
                {
                    wallStopwatch.Stop();

                    long terrainMilliseconds = terrainStopwatch.ElapsedMilliseconds;
                    long wallMilliseconds = wallStopwatch.ElapsedMilliseconds;

                    wallDebug.Append(string.Format("CreateZoneWalls: terrain/preparation {0}ms, spawned {1} walls in {2}ms, active total {3}ms; managed memory delta (server-wide across coroutine): {4:N0} bytes; GC: gen0={5}, gen1={6}, gen2={7}, frontier result: stacks={stacks}, walls={wallCount}",
                        terrainMilliseconds, wallCount, wallMilliseconds, terrainMilliseconds + wallMilliseconds, System.GC.GetTotalMemory(false) - memoryBefore, System.GC.CollectionCount(0) - gen0Before, System.GC.CollectionCount(1) - gen1Before, System.GC.CollectionCount(2) - gen2Before
                    ));

                    Puts(wallDebug.ToString());
                }
            }

            private bool ShouldAddZoneWallStack(Vector3 position, float currentY, float terrainHeight, float wallHeight, float wallStep, bool frontier, DisposableBuilder debug)
            {
                const float tolerance = 0.1f;
                float threshold = frontier ? wallHeight - wallStep : wallHeight * 0.6f;
                float obstructionY = currentY + threshold;

                if (terrainHeight >= obstructionY - tolerance)
                {
                    debug?.AppendLine($"Terrain added stack: position={position}, currentY={currentY:0.###}, terrainY={terrainHeight:0.###}, delta={terrainHeight - currentY:0.###}, threshold={threshold:0.###}");
                    return true;
                }

                if (!Physics.Raycast(new(position.x, currentY + 6.5f, position.z), Vector3.down, out var hit, 13f, targetMask))
                {
                    return false;
                }

                string objectName = hit.collider.ObjectName();
                bool obstruction = objectName.Contains("rock") || objectName.Contains("formation", CompareOptions.OrdinalIgnoreCase);

                if (!obstruction)
                {
                    return false;
                }

                bool above = hit.point.y >= obstructionY - tolerance;

                debug?.AppendLine($"Rock candidate: object={objectName}, position={position}, currentY={currentY:0.###}, hitY={hit.point.y:0.###}, delta={hit.point.y - currentY:0.###}, obstructionY={obstructionY:0.###}, above={above}, distance={hit.distance:0.###}");

                return above;
            }

            const string external_adobe = "assets/prefabs/building/wall.external.high.adobe/wall.external.high.adobe.prefab";
            const string external_ice = "assets/prefabs/misc/xmas/icewalls/wall.external.high.ice.prefab";
            const string external_legacy = "assets/prefabs/building/wall.external.high.legacy/wall.external.high.legacy.prefab";
            const string external_stone = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab";
            const string external_wood = "assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab";

            private string GetWallPrefabName(Vector3 center, out bool frontier)
            {
                var opt = Options.ArenaWalls;
                string prefab = (opt.Ice, opt.Stone, opt.Adobe, opt.Frontier) switch
                {
                    (true, true, true, true) => (TerrainBiome.Enum)(TerrainMeta.BiomeMap?.GetBiomeMaxType(center) ?? -1) switch
                    {
                        TerrainBiome.Enum.Arid => UnityEngine.Random.Range(0, 2) == 0 ? external_adobe : external_legacy,
                        TerrainBiome.Enum.Arctic or TerrainBiome.Enum.Tundra => external_ice,
                        _ => external_stone
                    },
                    (true, false, false, false) => external_ice,
                    (false, true, false, false) => external_stone,
                    (false, false, true, false) => external_adobe,
                    (false, false, false, true) => external_legacy,
                    _ => external_wood
                };
                frontier = prefab == external_legacy;
                return prefab;
            }

            private static void FixNav(bool frontier, SimpleBuildingBlock e)
            {
                MeshCollider mesh = e.GetComponentInChildren<MeshCollider>();
                if (mesh == null || !e.TryGetComponent(out NavMeshObstacle nav))
                {
                    return;
                }
                if (frontier)
                {
                    nav.size = mesh.bounds.size * 1.1f;
                    nav.center += nav.transform.InverseTransformDirection(new Vector3(0f, 2.25f, -0.5f));
                }
                else
                {
                    nav.size = nav.size.WithY(mesh.bounds.size.y);
                    nav.center = e.transform.InverseTransformPoint(mesh.bounds.center);
                }
            }

            private List<RespawnInfo> respawns = new();
            private List<NaturalBeehive> hives = new();

            public class RespawnInfo
            {
                public Vector3 pos;
                public Quaternion rot;
                public string addition;
                public string prefab;
                public string guid;
                public float chance;
                public BaseEntity ent;
                public RespawnInfo(BaseEntity entity)
                {
                    if (entity.Is(out VineSwingingTree vine))
                    {
                        if (vine.StumpPrefab.isValid)
                        {
                            guid = vine.StumpPrefab.guid;
                            vine.StumpPrefab.guid = string.Empty;
                        }
                    }
                    else if (entity.Is(out TreeEntity tree))
                    {
                        if (tree.spawnTreeAddition && tree.treeAdditionPrefab.isValid)
                        {
                            chance = 1f;
                            addition = tree.treeAdditionPrefab.guid;
                        }
                    }
                    pos = entity.transform.position;
                    rot = entity.transform.rotation;
                    prefab = entity.PrefabName;
                    ent = entity;
                }
                public void Respawn()
                {
                    if (!ent.IsKilled())
                    {
                        if (ent is VineSwingingTree vine && !vine.StumpPrefab.isValid)
                        {
                            vine.StumpPrefab.guid = guid;
                        }
                        ent.transform.position = pos;
                    }
                    else
                    {
                        BaseEntity entity = GameManager.server.CreateEntity(prefab, pos, rot);
                        if (entity.Is(out TreeEntity tree))
                        {
                            if (chance != 0)
                            {
                                tree.spawnTreeAddition = true;
                                tree.treeAdditionSpawnChance = chance;
                                tree.treeAdditionPrefab.guid = addition;
                            }
                            else tree.spawnTreeAddition = false;
                        }
                        entity.Spawn();
                    }
                }
            }

            public IEnumerator RemoveClutter(int limit)
            {
                long memoryBefore = 0, gen0Before = 0, gen1Before = 0, gen2Before = 0;
                Stopwatch clutterStopwatch = null;
                bool debug = DebugMode;

                if (debug)
                {
                    memoryBefore = System.GC.GetTotalMemory(false);
                    gen0Before = System.GC.CollectionCount(0);
                    gen1Before = System.GC.CollectionCount(1);
                    gen2Before = System.GC.CollectionCount(2);
                    clutterStopwatch = Stopwatch.StartNew();
                }

                using var tmp = FindEntitiesOfType<BaseEntity>(Location, ProtectionRadius, -1, QueryTriggerInteraction.Ignore);
                using var players = DisposableList<BasePlayer>();
                int checks = 0;
                var instruction = CoroutineEx.waitForSeconds(0.0375f);
                FrameDeadline deadline = new(clutterFrameBudgetMilliseconds);
                int entityCount = tmp.Count;

                for (int i = tmp.Count - 1; i >= 0; i--)
                {
                    if (deadline.Expired)
                    {
                        if (debug) clutterStopwatch.Stop();
                        yield return instruction;
                        deadline.Reset();
                        if (debug) clutterStopwatch.Start();
                    }
                    if (tmp[i].Is(out NaturalBeehive hive))
                    {
                        if (!hive.IsDestroyed) hives.Add(hive);
                        tmp.RemoveAt(i);
                    }
                }

                foreach (var e in tmp)
                {
                    if (++checks >= limit || deadline.Expired)
                    {
                        if (debug) clutterStopwatch.Stop();
                        checks = 0;
                        yield return instruction;
                        deadline.Reset();
                        if (debug) clutterStopwatch.Start();
                    }

                    if (e == null || e.IsDestroyed)
                    {
                        continue;
                    }

                    if (e is TreeEntity t)
                    {
                        if (!Entities.Contains(e))
                        {
                            if (Options.DeleteRadius > 0f && e.Distance(Location) <= Options.DeleteRadius) ScheduledRespawn(e);
                            else if (Options.TreeRadius > 0f) { Eject(t, Location, Options.TreeRadius, true); HandleHiveAt(t, false); }
                            else if (Options.DeleteRadius <= 0f) ScheduledRespawn(e);
                        }
                    }
                    else if ((e is ResourceEntity or CollectibleEntity or JunkPileWater) && NearFoundation(e.transform.position))
                    {
                        Eject(e, Location, ProtectionRadius, e is not JunkPileWater);
                    }
                    else if (e.GetParentEntity() is Tugboat)
                    {
                        continue;
                    }
                    else if (e is HotAirBalloon && NearFoundation(e.transform.position, 10f) && !Entities.Contains(e))
                    {
                        TryEjectMountable(e);
                    }
                    else if (e is BaseSiegeWeapon or ConstructableEntity)
                    {
                        Eject(e, Location, ProtectionRadius, true);
                    }
                    else if (e is BaseMountable m && CanEjectMountable(m, players))
                    {
                        TryEjectMountable(e);
                    }
                    else if (e is ScientistNPC && NearFoundation(e.transform.position, 15f))
                    {
                        e.SafelyKill();
                    }
                    else if (Instance.DeployableItems.ContainsKey(e.PrefabName) && !Entities.Contains(e))
                    {
                        DeployableItemHandler(e);
                    }
                    else if (e is DroppedItemContainer container && e.ShortPrefabName == "item_drop_backpack" && e.IsValid())
                    {
                        EjectContainer(container, container.playerSteamID);
                    }
                    else if (e is LootableCorpse corpse)
                    {
                        EjectContainer(corpse, corpse.playerSteamID);
                    }
                    else HandleDefaultEntity(e, config.Settings.Management.EjectMountables);
                }

                if (debug)
                {
                    clutterStopwatch.Stop();

                    long clutterMilliseconds = clutterStopwatch.ElapsedMilliseconds;

                    Puts("Processed clutter for {0} entities in {1}ms, managed memory delta (server-wide): {2:N0} bytes; GC: gen0={3}, gen1={4}, gen2={5}",
                        entityCount, clutterMilliseconds, System.GC.GetTotalMemory(false) - memoryBefore, System.GC.CollectionCount(0) - gen0Before, System.GC.CollectionCount(1) - gen1Before, System.GC.CollectionCount(2) - gen2Before);
                }
            }

            private void ScheduledRespawn(BaseEntity ent)
            {
                if (!Options.RespawnTrees)
                {
                    if (ent is VineSwingingTree vine) // this should never be optional or the jungle will become baron over time.
                    {
                        if (!respawns.Exists(x => InRange(x.pos, ent.transform.position, 0.01f)))
                        {
                            respawns.Add(new(ent));
                        }
                        vine.StumpPrefab.guid = string.Empty;
                    }
                    else if (ent is TreeEntity tree) HandleHiveAt(tree, true);
                    ent.SafelyKill();
                    return;
                }
                if (!respawns.Exists(x => InRange(x.pos, ent.transform.position, 0.01f)))
                {
                    if (ent is TreeEntity tree) HandleHiveAt(tree, true);
                    respawns.Add(new(ent));
                }
                ent.SafelyKill();
            }

            private void RespawnEntities()
            {
                foreach (var ent in respawns)
                {
                    ent.Respawn();
                }
                respawns.Clear();
            }

            private void HandleHiveAt(TreeEntity tree, bool kill)
            {
                if (hives.Count == 0)
                {
                    return;
                }
                foreach (var hive in hives)
                {
                    if (!hive.IsKilled() && tree.treeAdditionRef == hive)
                    {
                        hives.Remove(hive);
                        if (kill)
                        {
                            ClearInventory(hive.inventory);
                            hive.transform.position = Vector3.zero;
                            hive.SendNetworkUpdateImmediate();
                            hive.DelayedSafeKill();
                        }
                        else
                        {
                            hive.transform.position = tree.transform.position;
                            hive.SendNetworkUpdate();
                        }
                        return;
                    }
                }
            }

            private bool CanEjectMountable(BaseEntity m, PooledList<BasePlayer> players)
            {
                if (m is BaseChair && !m.OwnerID.IsSteamId()) return false;
                if (m is ZiplineMountable or TrainCar) return false;
                if (Entities.Contains(m)) return false;
                if (m.HasParent())
                {
                    var parent = GetParentEntity(m);
                    if (parent is TrainCar or ZiplineMountable) return false;
                    if (Entities.Contains(parent)) return false;
                }
                if (NearFoundation(m.transform.position, 10f)) return true;
                if (config.Settings.Management.EjectMountables) return true;
                return TryRemoveMountable(m, players);
            }

            private void DeployableItemHandler(BaseEntity e)
            {
                if (e is SleepingBag bag)
                {
                    if (spawns != null && spawns.IsCustomSpawn && Options.CustomSpawns.KillSleepingBags)
                    {
                        if (HarmonyModInterface.CallHook("CanRaidableBasesRemoveClutter", e, Location, ProtectionRadius, true) != null) return;
                        bag.SafelyKill();
                        return;
                    }
                    _bags[bag] = bag.deployerUserID;
                    bag.deployerUserID = 0uL;
                    SleepingBag.OnBagChangedOwnership(bag, _bags[bag]);
                    bag.unlockTime = UnityEngine.Time.realtimeSinceStartup + 99999f;
                }
                if (config.Settings.Management.KillDeployables && e.OwnerID.IsSteamId())
                {
                    if (HarmonyModInterface.CallHook("CanRaidableBasesRemoveClutter", e, Location, ProtectionRadius, true) != null) return;
                    e.DelayedSafeKill();
                }
                else if (config.Settings.Management.EjectDeployables && e.OwnerID.IsSteamId())
                {
                    if (HarmonyModInterface.CallHook("CanRaidableBasesRemoveClutter", e, Location, ProtectionRadius, false) != null) return;
                    Eject(e, Location, ProtectionRadius + 10f, true);
                }
            }

            public void ResetSleepingBags()
            {
                foreach (var (bag, userid) in _bags)
                {
                    if (bag.IsNull()) continue;
                    ulong oldID = bag.deployerUserID;
                    bag.deployerUserID = userid;
                    SleepingBag.OnBagChangedOwnership(bag, oldID);
                    bag.unlockTime = UnityEngine.Time.realtimeSinceStartup;
                }
            }

            private IEnumerator EntitySetup()
            {
                long memoryBefore = 0, gen0Before = 0, gen1Before = 0, gen2Before = 0;
                Stopwatch setupStopwatch = null;
                bool debug = DebugMode;

                if (debug)
                {
                    memoryBefore = System.GC.GetTotalMemory(false);
                    gen0Before = System.GC.CollectionCount(0);
                    gen1Before = System.GC.CollectionCount(1);
                    gen2Before = System.GC.CollectionCount(2);
                    setupStopwatch = Stopwatch.StartNew();
                }

                int limit = Mathf.Clamp(Options.Setup.SpawnLimit, 1, 500);
                int checks = 0;
                using var tmp = Entities.ToPooledList();
                var instruction = CoroutineEx.waitForSeconds(0.0375f);
                FrameDeadline deadline = new(1d, setupFrameBudgetMilliseconds);
                int entityCount = tmp.Count;
                int processed = 0;

                Instance._pasteEngine?.UpdateEntitySetupProgress(this, 0f, FormattableString.Invariant($"Configuring raid entities (0/{entityCount:N0})"), true);

                foreach (var e in tmp)
                {
                    TrySetupEntitySafe(e);
                    processed++;

                    bool delay = ++checks >= limit;
                    if (delay || limit < 500 && deadline.Expired)
                    {
                        checks = 0;
                        float progress = entityCount == 0 ? 0.65f : 0.65f * processed / entityCount;
                        Instance._pasteEngine?.UpdateEntitySetupProgress(this, progress, FormattableString.Invariant($"Configuring raid entities ({processed:N0}/{entityCount:N0})"));
                        if (debug) setupStopwatch.Stop();
                        yield return delay ? instruction : null;
                        deadline.Reset();
                        if (debug) setupStopwatch.Start();
                    }
                }

                Instance._pasteEngine?.UpdateEntitySetupProgress(this, 0.65f, FormattableString.Invariant($"Configuring raid entities ({entityCount:N0}/{entityCount:N0})"), true);

                if (Options.EmptyAll && Type != RaidableType.None)
                    ItemManager.DoRemoves();

                if (debug)
                {
                    setupStopwatch.Stop();

                    long setupMilliseconds = setupStopwatch.ElapsedMilliseconds;

                    Puts("Processed setup for {0} entities in {1}ms, managed memory delta (server-wide): {2:N0} bytes; GC: gen0={3}, gen1={4}, gen2={5}",
                        entityCount, setupMilliseconds, System.GC.GetTotalMemory(false) - memoryBefore, System.GC.CollectionCount(0) - gen0Before, System.GC.CollectionCount(1) - gen1Before, System.GC.CollectionCount(2) - gen2Before);
                }

                Instance._pasteEngine?.UpdateEntitySetupProgress(this, 0.72f, "Preparing raid loot", true);
                yield return CoroutineEx.waitForSeconds(2f);

                if (SetupLoot())
                {
                    lootAmountTracked = UpdateLootAmountCounted();
                    Instance._pasteEngine?.UpdateEntitySetupProgress(this, 0.80f, "Configuring raid defenses", true);
                    RunSetupStep(Subscribe);
                    RunSetupStep(FilterRugSpawns);
                    RunSetupStep(SetupTurrets);
                    Instance._pasteEngine?.UpdateEntitySetupProgress(this, 0.88f, "Finalizing raid systems", true);
                    RunSetupStep(CreateGenericMarker);
                    RunSetupStep(UpdateMarker);
                    RunSetupStep(EjectSleepers);
                    RunSetupStep(CreateSpheres);
                    RunSetupStep(SetupLights);
                    RunSetupStep(SetupDoorControllers);
                    RunSetupStep(SetupDoors);
                    RunSetupStep(CheckDespawn);
                    RunSetupStep(SetupContainers);
                    protectionStarted = true;
                    nextProtectorTime = Time.realtimeSinceStartupAsDouble + 1d;
                    if (Options.ArenaWalls.Enabled)
                    {
                        Instance._pasteEngine?.UpdateEntitySetupProgress(this, 0.94f, "Creating arena walls", true);
                        yield return null;
                        yield return CreateZoneWallsCo();
                        yield return null;
                    }
                    Instance._pasteEngine?.UpdateEntitySetupProgress(this, 0.98f, "Starting raid event", true);
                    RunSetupStep(MakeAnnouncements);
                    HarmonyModInterface.CallHook("OnRaidableBaseStarted", hookObjects);
                    HarmonyModInterface.CallHook("OnRaidableBaseStarted", rb);
                }
                else
                {
                    IsResetting = true;
                    payments?.Refund();
                    Despawn();
                    loadTime = Time.time - loadTime;
                    IsLoading = false;
                    Instance.IsSpawnerBusy = false;
                    setupRoutine = null;
                    yield break;
                }

                TryInvokeMethod(Teleport);
                Instance._pasteEngine?.UpdateEntitySetupProgress(this, 1f, "Finalizing raid setup", true);

                loadTime = Time.time - loadTime;
                IsLoading = false;
                Instance.IsSpawnerBusy = false;
                setupRoutine = null;
            }

            private bool DebugMode => Instance != null && Instance.DebugMode;
            internal void RunSetupStep(Action action)
            {
                if (!DebugMode)
                {
                    TryInvokeMethod(action);
                    return;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();

                TryInvokeMethod(action);

                stopwatch.Stop();

                if (stopwatch.ElapsedMilliseconds > 0)
                {
                    Puts("Setup step {0}: {1}ms", action.Method.Name, stopwatch.ElapsedMilliseconds);
                }
            }

            private void TrySetupEntitySafe(BaseEntity entity)
            {
                try
                {
                    TrySetupEntity(entity);
                }
                catch (Exception ex)
                {
                    Puts("{0} ERROR: {1}", nameof(TrySetupEntity), ex);
                }
            }

            private void TrySetupEntity(BaseEntity e)
            {
                if (!CanSetupEntity(e))
                {
                    return;
                }

                SetupEntity(e, false);
                DestroyGroundCheck(e);

                e.OwnerID = 0;

                if (e is CollectibleEntity ce && IsWaterBasesBarrel(e))
                {
                    ce.itemList = null; // WaterBases barrels are visual-only.
                }

                if (!Options.AllowPickup && e is BaseCombatEntity bce && !IsPickupAllowed(bce.ShortPrefabName))
                {
                    SetupPickup(bce);
                }

                if (npcEnabled && (e is BaseDetector or BaseTrap or TinCanAlarm))
                {
                    NpcSpawnExclusions.Add(e.transform.position);
                }

                if ((e is DecorDeployable || Instance.DeployableItems.ContainsKey(e.PrefabName)) && !_decorDeployables.Contains(e))
                {
                    _decorDeployables.Add(e);
                }

                if (config.Weapons.Burn.Any(e.ShortPrefabName))
                {
                    SetupBurnVisuals(e);
                }

                if (e is IOEntity io)
                {
                    SetupLight(io);

                    if (io is ContainerIOEntity cio)
                    {
                        SetupIO(cio);
                    }
                    else if (io is ElectricBattery eb)
                    {
                        SetupBattery(eb);
                    }
                    if (io is AutoTurret turret)
                    {
                        SetupTurret(turret);
                    }
                    else if (io is Igniter igniter)
                    {
                        SetupIgniter(igniter);
                    }
                    else if (io is SamSite ss)
                    {
                        SetupSamSite(ss);
                    }
                    else if (io is TeslaCoil tc)
                    {
                        SetupTeslaCoil(tc);
                    }
                    else if (io is CustomDoorManipulator cdm)
                    {
                        doorControllers.Add(cdm);
                    }
                    else if (io is HBHFSensor sensor)
                    {
                        SetupHBHFSensor(sensor);
                    }
                    else if (io is ElectricGenerator generator)
                    {
                        SetupGenerator(generator);
                    }
                    else if (io is PressButton button)
                    {
                        SetupButton(button);
                    }
                    else if (io is FogMachine fm)
                    {
                        SetupFogMachine(fm);
                    }
                    else if (io is Sprinkler sprinkler)
                    {
                        SetupSprinkler(sprinkler);
                    }
                    else if (io is Fridge fridge)
                    {
                        SetupFridge(fridge);
                    }
                    else if (io is VendingMachine vm)
                    {
                        SetupVendingMachine(vm);
                    }
                    else if (io is IIndustrialStorage st)
                    {
                        TryEmptyIndustrialStorage(io, st);
                    }
                }
                else if (e is StorageContainer c)
                {
                    SetupContainer(c);

                    if (c is BaseOven oven)
                    {
                        SetupOven(oven);
                    }
                    else if (c is FlameTurret ft)
                    {
                        SetupFlameTurret(ft);
                    }
                    else if (c is BuildingPrivlidge priv)
                    {
                        SetupBuildingPriviledge(priv);
                    }
                    else if (c is Locker locker)
                    {
                        SetupLocker(locker);
                    }
                    else if (c is GunTrap gt)
                    {
                        SetupGunTrap(gt);
                    }
                    else if (c is WeaponRack wr)
                    {
                        SetupWeaponRack(wr);
                    }
                }
                else if (e is BuildingBlock block)
                {
                    SetupBuildingBlock(block);
                }
                else if (e is BaseLock)
                {
                    SetupLock(e);
                }
                else if (e is SleepingBag bag)
                {
                    SetupSleepingBag(bag);
                }
                else if (e is CollectibleEntity ce2)
                {
                    SetupCollectible(ce2);
                }
                else if (e is SpookySpeaker speaker)
                {
                    SetupSpookySpeaker(speaker);
                }
                else if (e is ComputerStation cs)
                {
                    SetupComputerStation(cs);
                }

                if (e is DecayEntity de)
                {
                    SetupDecayEntity(de);
                }
                if (e is Door door)
                {
                    SetupDoor(door);
                }
                else SetupSkin(e);
            }

            private void SetupLights()
            {
                if (!config.Settings.Management.NightLantern && Instance.NightLantern.CanCall())
                {
                    return;
                }

                if (config.Settings.Management.Lights || config.Settings.Management.AlwaysLights)
                {
                    ToggleLights();
                }
            }

            public bool IsPasted;

            public void CheckPaste()
            {
                if (IsPasted || !IsLoading)
                {
                    return;
                }
                if (Time.timeAsDouble - loadTime > 900d)
                {
                    Puts("{0} @ {1} timed out after 15 minutes while the internal paste engine was running; despawning...", BaseName, Instance.PositionToGrid(Location, true));
                    IsLoading = false;
                    Despawn();
                    return;
                }
                Invoke(CheckPaste, 1f);
            }

            private void SetupContainers()
            {
                foreach (var container in _containers)
                {
                    if (!container.IsKilled())
                    {
                        container.SendNetworkUpdate();
                    }
                }
            }

            private void SetupWeaponRack(WeaponRack rack)
            {
                if (!config.Settings.Management.Racks || rack.inventory == null)
                {
                    return;
                }
                if (Options.IgnoreContainedLoot && !rack.inventory.IsEmpty())
                {
                    return;
                }
                weaponRacks.Add(rack);
            }

            public void SetupElevator(Elevator elevator)
            {
                if (!elevator.IsValid() || Elevators.ContainsKey(elevator.net.ID))
                {
                    return;
                }

                Elevators[elevator.net.ID] = new() { Elevator = elevator, raid = this };
                elevator._maxHealth = Options.Elevators.ElevatorHealth;
                elevator.InitializeHealth(Options.Elevators.ElevatorHealth, Options.Elevators.ElevatorHealth);

                if (!Options.Elevators.RequiresPower && !elevator.IsPowered())
                {
                    if (elevator.previousPowerAmount?.Length >= 3) elevator.previousPowerAmount[2] = elevator.DesiredPower();
                    elevator.SetFlagLocal(BaseEntity.Flags.Reserved8, true);
                }

                if (Options.Elevators.RequiresBuildingPermission || Options.Elevators.RequiredAccessLevel > 0)
                {
                    Instance.Subscribe(nameof(OnElevatorButtonPress));
                    Instance.Subscribe(nameof(OnButtonPress));
                }

                elevator.SendNetworkUpdate();
            }

            private void SetupPickup(BaseCombatEntity e)
            {
                e.pickup.enabled = false;
            }

            private void AddContainer(StorageContainer container)
            {
                if (IsBox(container, true) || container is BuildingPrivlidge)
                {
                    _containers.Add(container);
                }

                _iice.Add(container);
                _allcontainers.Add(container);

                AddEntity(container);
            }

            private void RemoveContainer(StorageContainer container)
            {
                if (!container.IsKilled())
                {
                    container.skinID = 102201;
                    _allcontainers.Remove(container);
                    _containers.Remove(container);
                    Entities.Remove(container);
                    container.dropsLoot = false;
                    container.DelayedSafeKill();
                }
            }

            public void TryEmptyContainer(StorageContainer container)
            {
                if (ShouldEmptyAll(container))
                {
                    ClearInventory(container.inventory);
                }
                container.dropsLoot = false;
                container.dropFloats = false;
            }

            public void TryEmptyIndustrialStorage(IOEntity io, IIndustrialStorage storage)
            {
                if (ShouldEmptyAll(io) && storage != null)
                {
                    try { ClearInventory(storage.Container); } catch { }
                }
            }

            public void TryEmptyContainer(ContainerIOEntity container)
            {
                if (ShouldEmptyAll(container))
                {
                    ClearInventory(container.inventory);
                }
                if (container is Fridge)
                {
                    return;
                }
                container.dropsLoot = false;
                container.dropFloats = false;
            }

            private bool ShouldEmptyAll(BaseEntity container)
            {
                if (!Options.EmptyAll || Type == RaidableType.None)
                    return false;
                if (Options.EmptyExemptions.Any(container.ShortPrefabName))
                    return false;
                return true;
            }

            private void SetupContainer(StorageContainer container)
            {
                AddContainer(container);

                if (container.inventory == null)
                {
                    container.CreateInventory(true);
                }
                else TryEmptyContainer(container);

                SetupBoxSkin(container);

                if (Type == RaidableType.None && container.inventory.itemList.Count > 0)
                {
                    return;
                }

                container.dropsLoot = false;
                container.dropFloats = false;

                if (container is BuildingPrivlidge)
                {
                    container.dropsLoot = config.Settings.Management.AllowCupboardLoot;
                }
                else if (!IsProtectedWeapon(container))
                {
                    container.dropsLoot = true;
                }

                if (IsBox(container, false) || container is BuildingPrivlidge || config.Settings.Management.Racks && container is WeaponRack)
                {
                    container.inventory.SetFlag(ItemContainer.Flag.NoItemInput, Options.NoItemInput);
                }

                if (IsBox(container, false))
                {
                    CreateLock(container, Options.KeyLockBoxes, Options.CodeLockBoxes);
                }

                if (container is Locker)
                {
                    CreateLock(container, Options.KeyLockLockers, Options.CodeLockLockers);
                }
            }

            private void SetupIO(ContainerIOEntity io)
            {
                _iice.Add(io);
                io.dropFloats = false;
                io.inventory.SetFlag(ItemContainer.Flag.NoItemInput, true);
                io.dropsLoot = !IsProtectedWeapon(io) || config.Settings.Management.DropLoot.Get(io);
            }

            private void SetupIO(IOEntity io)
            {
                using (var update = io.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                {
                    update.Set(IOEntity.Flag_HasPower, true);
                }
            }

            private void SetupLock(BaseEntity e, bool justCreated = false)
            {
                AddEntity(e);
                locks.Add(e);

                if (Type == RaidableType.None)
                {
                    return;
                }

                if (e is CodeLock codeLock)
                {
                    if (config.Settings.Management.RandomCodes || justCreated)
                    {
                        codeLock.code = UnityEngine.Random.Range(1000, 9999).ToString();
                        codeLock.hasCode = true;
                    }

                    codeLock.OwnerID = 0;
                    codeLock.guestCode = string.Empty;
                    codeLock.hasGuestCode = false;
                    codeLock.guestPlayers.Clear();
                    codeLock.whitelistPlayers.Clear();
                    codeLock.SetFlagLocal(BaseEntity.Flags.Locked, true);
                    codeLock.SendNetworkUpdate();
                }
                else if (e is KeyLock keyLock)
                {
                    if (config.Settings.Management.RandomCodes)
                    {
                        keyLock.keyCode = UnityEngine.Random.Range(1, 100000);
                    }

                    keyLock.OwnerID = 0;
                    keyLock.firstKeyCreated = true;
                    keyLock.SetFlagLocal(BaseEntity.Flags.Locked, true);
                    keyLock.SendNetworkUpdate();
                }
            }

            private void SetupVendingMachine(VendingMachine vm)
            {
                vms.Add(vm);
                TryEmptyContainer(vm);
                vm.dropsLoot = false;
                vm.SetFlagLocal(BaseEntity.Flags.Reserved4, config.Settings.Management.AllowBroadcasting);
                vm.FullUpdate();
                SetupIO((IOEntity)vm);
            }

            private void SetupLight(IOEntity light)
            {
                if (light == null || light is XORSwitch or BaseDetector)
                {
                    return;
                }

                bool lightsEnabled = config.Settings.Management.Lights || config.Settings.Management.AlwaysLights;
                bool isIgnored = config.Settings.Management.IgnoredLights.Exists(light.ShortPrefabName.Contains);
                bool isSupportedLight = light is SimpleLight or CeilingLight or SearchLight or SirenLight or FlasherLight or Chandelier or StringLights or ElectricalHeater or AudioVisualisationEntity or NeonSign or StrobeLight
                    || light is ContainerIOEntity && light.ShortPrefabName.Contains("wallcabinet")
                    || light.PrefabName.Contains("light"); // BaseSculpture is being changed next Rust update, fix is to remove it until I can inspect the new class.

                if (!lightsEnabled || isIgnored || !isSupportedLight)
                {
                    return;
                }

                if (!lights.Contains(light))
                {
                    lights.Add(light);
                }
            }

            private void SetupHBHFSensor(HBHFSensor sensor)
            {
                if (!sensor.HasConnections())
                {
                    return;
                }
                triggers[sensor.myTrigger] = sensor;
                using var update = sensor.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                update.Set(IOEntity.Flag_HasPower, true);
                update.Set(HBHFSensor.Flag_IncludeAuthed, true);
                update.Set(HBHFSensor.Flag_IncludeOthers, true);
            }

            private void SetupBattery(ElectricBattery eb)
            {
                eb.rustWattSeconds = eb.maxCapactiySeconds - 1f;
            }

            private void SetupGenerator(ElectricGenerator generator)
            {
                generator.electricAmount = config.Weapons.TestGeneratorPower;
            }

            private void SetupButton(PressButton button)
            {
                if (!Options.Elevators.RequiresPower && !button.IsPowered())
                {
                    SetupIO(button);
                }
                buttons.Add(button);
                button._maxHealth = Options.Elevators.ButtonHealth;
                button.InitializeHealth(Options.Elevators.ButtonHealth, Options.Elevators.ButtonHealth);
            }

            private void SetupBuildingBlock(BuildingBlock block)
            {
                if (block.IsKilled())
                {
                    return;
                }

                if (IsNpcStair(block))
                {
                    _baseStairs.Add(block.transform.position);
                }

                if (blockPrefabs.Contains(block.ShortPrefabName))
                {
                    blocks.Add(block);
                }

                if (!IsUnloading)
                {
                    ChangeTier(block);
                    block.StopBeingDemolishable();
                    block.StopBeingRotatable();
                }
            }

            private List<string> blockPrefabs = new() { "foundation.triangle", "foundation", "floor.triangle", "floor", "roof", "roof.triangle" };

            private void ChangeTier(BuildingBlock block)
            {
                if (Options.Blocks.SkipTwig && block.grade == BuildingGrade.Enum.Twigs)
                {
                    return;
                }
                if (Options.Blocks.Exclusions.Contains(BaseName))
                {
                    if (!Options.Blocks.HasSkin(block, block.grade, block.skinID))
                    {
                        block.skinID = 0uL;
                        block.UpdateSkin();
                    }
                    return;
                }
                BuildingGrade.Enum grade = Options.Blocks switch
                {
                    { HQM: true } => BuildingGrade.Enum.TopTier,
                    { Metal: true } => BuildingGrade.Enum.Metal,
                    { Stone: true } => BuildingGrade.Enum.Stone,
                    { Wooden: true } => BuildingGrade.Enum.Wood,
                    _ => block.grade
                };
                ulong skinID = Options.Blocks.GetSkin(block, grade, block.skinID);
                if (Options.Blocks.RandomWhole)
                {
                    if (!skinWhole.TryGetValue(grade, out var skin))
                    {
                        skinWhole[grade] = skin = skinID;
                    }
                    skinID = skin;
                }
                if (!Options.Blocks.HasSkin(block, grade, skinID))
                {
                    skinID = 0uL;
                }
                block.skinID = skinID;
                block.SetGrade(grade);
                block.SetHealthToMax();
                block.UpdateSkin();
                block.SendNetworkUpdate();
                if (grade != BuildingGrade.Enum.Metal)
                {
                    return;
                }
                if (Options.Blocks.IdenticalColour)
                {
                    if (!skinColors.TryGetValue(block.grade, out var color))
                    {
                        skinColors[block.grade] = color = block.currentSkin.GetStartingDetailColour(0u);
                    }
                    block.SetCustomColour(color);
                }
                else if (Options.Blocks.RandomColour)
                {
                    block.SetCustomColour(block.currentSkin.GetStartingDetailColour(0u));
                }
            }

            private Dictionary<BuildingGrade.Enum, ulong> skinWhole = new();
            private Dictionary<BuildingGrade.Enum, uint> skinColors = new();

            private void SetupTeslaCoil(TeslaCoil tc)
            {
                tc.InitializeHealth(Options.TeslaCoil.Health, Options.TeslaCoil.Health);
                tc.maxDischargeSelfDamageSeconds = Mathf.Clamp(Options.TeslaCoil.MaxDischargeSelfDamageSeconds, 0f, 9999f);
                tc.maxDamageOutput = Mathf.Clamp(Options.TeslaCoil.MaxDamageOutput, 0f, 9999f);

                if (!Options.TeslaCoil.RequiresPower)
                {
                    tc.UpdateFromInput(Mathf.Max(tc.ConsumptionAmount(), 35), 0);
                }
            }

            private void SetupIgniter(Igniter igniter)
            {
                igniter.SelfDamagePerIgnite = 0f;
            }

            public void PreSetupTurret(AutoTurret turret)
            {
                if (Options.AutoTurret.TurretHours > 0)
                {
                    double elapsedHours = (DateTime.UtcNow - SaveRestore.SaveCreatedTime).TotalHours;
                    if (elapsedHours < Options.AutoTurret.TurretHours)
                    {
                        //Puts("Remove turret, elapsed {0}h, required {1}h", elapsedHours, Options.AutoTurret.TurretHours);
                        RemoveTurret(turret);
                        return;
                    }
                }
                turret.skinID = RB_SKIN_ID;
                turret.dropsLoot = false;
                if (turret.targetTrigger != null)
                {
                    triggers[turret.targetTrigger] = turret;
                }
            }

            //private void ApplyTurretLimit(int max)
            //{
            //    if (max <= 0 || turrets.Count <= max)
            //        return;

            //    using var orderedTurrets = GetTurretsOrderedByAngle(Location);

            //    if (orderedTurrets.Count <= max)
            //        return;

            //    using var keep = DisposableList<AutoTurret>();

            //    if (max == 1)
            //    {
            //        keep.Add(orderedTurrets[orderedTurrets.Count / 2]);
            //    }
            //    else
            //    {
            //        double step = (double)(orderedTurrets.Count - 1) / (max - 1);

            //        for (int i = 0; i < max; i++)
            //        {
            //            int index = (int)Math.Round(i * step);
            //            index = Math.Max(0, Math.Min(index, orderedTurrets.Count - 1));
            //            AutoTurret turret = orderedTurrets[index];
            //            if (!keep.Contains(turret)) keep.Add(turret);
            //        }
            //    }

            //    for (int i = orderedTurrets.Count - 1; i >= 0; i--)
            //    {
            //        AutoTurret turret = orderedTurrets[i];
            //        if (!keep.Contains(turret))
            //        {
            //            RemoveTurret(turret);
            //        }
            //    }
            //}

            //private PooledList<AutoTurret> GetTurretsOrderedByAngle(Vector3 center)
            //{
            //    PooledList<AutoTurret> orderedTurrets = DisposableList<AutoTurret>();

            //    for (int i = 0; i < turrets.Count; i++)
            //    {
            //        AutoTurret turret = turrets[i];
            //        if (turret == null || turret.IsDestroyed)
            //            continue;

            //        orderedTurrets.Add(turret);
            //    }

            //    orderedTurrets.Sort((a, b) =>
            //    {
            //        Vector3 dirA = a.transform.position - center;
            //        Vector3 dirB = b.transform.position - center;

            //        float angleA = Mathf.Atan2(dirA.z, dirA.x);
            //        float angleB = Mathf.Atan2(dirB.z, dirB.x);

            //        return angleA.CompareTo(angleB);
            //    });

            //    return orderedTurrets;
            //}

            public class TurretInfo
            {
                public AutoTurret turret;
                public bool refillPending;
                public bool infiniteAmmoDisabled;
                public int refillAttempts;
                public bool IsKilled() => turret.IsKilled() || turret.inventory == null || turret.inventory.itemList == null;
            }

            private void SetupTurret(AutoTurret turret)
            {
                if (!turret.IsValid())
                {
                    RemoveTurret(turret);
                    return;
                }

                triggers[turret.targetTrigger] = turret;

                if (IsUnloading || Type == RaidableType.None)
                {
                    return;
                }

                if (config.Settings.Management.ClippedTurrets && turret.RCEyes != null)
                {
                    var position = turret.RCEyes.position;

                    if (IsRockFaceUpwards(position))
                    {
                        RemoveTurret(turret);
                        return;
                    }
                }

                if (turret is NPCAutoTurret)
                {
                    turret.baseProtection = Instance.GetTurretProtection();
                    BMGELEVATOR.RemoveImmortality(turret.baseProtection, 1f, 1f, 1f, 1f, 1f, 0.8f, 1f, 1f, 1f, 0.9f, 0.5f, 0.5f, 1f, 1f, 0f, 0.5f, 0f, 1f, 1f, 0f, 1f, 0.9f, 0f, 1f, 0f);
                }

                SetupIO((IOEntity)turret);

                if (Type != RaidableType.None)
                {
                    turret.authorizedPlayers.Clear();
                }

                turret.InitializeHealth(Options.AutoTurret.Health, Options.AutoTurret.Health);
                SetupSightRange(turret, Options.AutoTurret.SightRange);
                turret.aimCone = Options.AutoTurret.AimCone;

                turrets[turret.net.ID.Value] = new()
                {
                    turret = turret,
                };

                if (turret.AttachedWeapon != null)
                {
                    turret.AttachedWeapon.EnableSaving(true);
                }

                if (Options.AutoTurret.RemoveWeapon)
                {
                    turret.AttachedWeapon = null;
                    Item slot = turret.inventory.GetSlot(0);
                    if (slot != null && (slot.info.category == ItemCategory.Weapon || slot.info.category == ItemCategory.Fun))
                    {
                        slot.RemoveFromContainer();
                        slot.Remove();
                    }
                }

                if (Options.AutoTurret.Hostile)
                {
                    turret.SetPeacekeepermode(false);
                }

                if (config.Weapons.InfiniteAmmo.AutoTurret)
                {
                    turret.inventory.onPreItemRemove += new Action<Item>(OnWeaponItemPreRemove);
                }
            }

            public bool TryRemoveOrConfirmTurret(AutoTurret turret) => turret.net != null && turrets.Remove(turret.net.ID.Value) || turret.skinID == RB_SKIN_ID;

            private void RemoveTurret(AutoTurret turret)
            {
                turret.skinID = 102201;
                Entities.Remove(turret);
                if (turret.IsValid())
                {
                    turrets.Remove(turret.net.ID.Value);
                }
                turret.dropsLoot = false;
                turret.DelayedSafeKill();
                if (turret.targetTrigger != null)
                {
                    triggers.Remove(turret.targetTrigger);
                }
            }

            private Dictionary<NetworkableId, SphereCollider> _turretColliders = new();

            public void SetupSightRange(AutoTurret turret, float sightRange, int multi = 1)
            {
                if (turret.net != null && turret.targetTrigger != null)
                {
                    if (!_turretColliders.TryGetValue(turret.net.ID, out var collider) && turret.targetTrigger.TryGetComponent<SphereCollider>(out var val))
                    {
                        _turretColliders[turret.net.ID] = collider = val;
                    }
                    if (collider != null)
                    {
                        if (multi > 1)
                        {
                            turret.Invoke(() =>
                            {
                                if (collider != null) collider.radius = sightRange;
                                if (turret != null) turret.sightRange = sightRange;
                            }, 15f);
                        }
                        collider.radius = sightRange * multi;
                    }
                }
                turret.sightRange = sightRange * multi;
            }

            private struct TurretWeaponOption
            {
                public ItemDefinition Definition;
                public List<ulong> Skins;

                public TurretWeaponOption(ItemDefinition def, List<ulong> skins)
                {
                    Definition = def;
                    Skins = skins;
                }
            }

            private void SetupTurrets()
            {
                if (Type != RaidableType.None && turrets.Count > 0)
                {
                    //ApplyTurretLimit(Options.AutoTurret.MaxTurrets);
                    turretsCoroutine = ServerMgr.Instance.StartCoroutine(TurretsCoroutine());
                }
                else SetupNpcKits();
            }

            private IEnumerator TurretsCoroutine()
            {
                if (InitiateTurretOnSpawn)
                {
                    while (IsLoading)
                    {
                        yield return CoroutineEx.waitForSeconds(0.1f);
                    }
                }

                bool equipWeapon = Options.AutoTurret.Shortnames.Count > 0;

                using var weapons = DisposableList<TurretWeaponOption>();

                if (equipWeapon)
                {
                    BuildTurretWeaponOptions(Options.AutoTurret.Shortnames, weapons);
                }

                using var tmp = turrets.Values.ToPooledList();
                var instruction = CoroutineEx.waitForSeconds(0.025f);

                foreach (var info in tmp)
                {
                    yield return instruction;

                    if (equipWeapon)
                    {
                        EquipTurretWeapon(info, weapons);
                    }

                    yield return instruction;

                    UpdateAttachedWeapon(info);

                    yield return instruction;

                    InitiateStartup(info);

                    yield return instruction;

                    FillAmmoTurret(info);

                    DisableInterference(info);
                }

                SetupNpcKits();

                HarmonyModInterface.CallHook("OnRaidableTurretsInitialized", new object[] { turrets, Location, ProtectionRadius, Options.Level, AllowPVP, ownerId });

                turretsCoroutine = null;
            }

            public bool UsableByTurret;
            private void BuildTurretWeaponOptions(Dictionary<string, List<ulong>> shortnames, List<TurretWeaponOption> weapons)
            {
                foreach (var entry in shortnames)
                {
                    string shortname = entry.Key;
                    List<ulong> skins = entry.Value;

                    if (string.IsNullOrWhiteSpace(shortname) || skins == null)
                    {
                        Puts("Invalid shortname in profile for turret weapon: {0}", shortname ?? "null");
                        continue;
                    }

                    if (shortname is "fun.trumpet" or "snowballgun" or "flamethrower" or "homingmissile.launcher")
                    {
                        continue;
                    }

                    ItemDefinition def = ItemManager.FindItemDefinition(shortname);

                    if (def == null)
                    {
                        Puts("Invalid shortname in profile for turret weapon: {0}", shortname);
                        continue;
                    }

                    if (!IsValidWeapon(def))
                    {
                        continue;
                    }

                    weapons.Add(new(def, skins));
                }

                if (weapons.Count == 0)
                {
                    ItemDefinition fallback = ItemManager.FindItemDefinition("pistol.python");

                    if (fallback != null)
                    {
                        weapons.Add(new(fallback, null));
                    }
                }
            }

            private ulong GetTurretWeaponSkin(AutoTurret turret, ItemDefinition def, List<ulong> configuredSkins)
            {
                if (configuredSkins != null && configuredSkins.Count > 0 && (configuredSkins.Count > 1 || configuredSkins[0] != 0uL))
                {
                    if (!config.BlockPaidContent)
                    {
                        return configuredSkins.GetRandom();
                    }

                    int available = 0;

                    for (int i = 0; i < configuredSkins.Count; i++)
                    {
                        if (!Instance.RequiresOwnership(def, configuredSkins[i]))
                        {
                            available++;
                        }
                    }

                    if (available == 0)
                    {
                        return 0uL;
                    }

                    int selected = Core.Random.Range(0, available);

                    for (int i = 0; i < configuredSkins.Count; i++)
                    {
                        ulong sk = configuredSkins[i];
                        if (Instance.RequiresOwnership(def, sk)) continue;
                        if (selected-- == 0) return sk;
                    }

                    return 0uL;
                }

                ulong skin = GetItemSkin(def, SkinType.Loot, 0, config.Skins.Loot.Stackable, config.Skins.Loot.NonStackable, config.Skins.Loot.Random, config.Skins.Loot.Workshop, config.Skins.Loot.ImportedWorkshop, config.Skins.Loot.ApprovedOnly, 1);
                if (skin != 0 && !config.Skins.Deployables.SkinEverything && !config.Skins.Deployables.PartialNames.Exists(turret.ShortPrefabName.Contains)) skin = 0;
                else if (config.Skins.Deployables.Unique && _itemIdToSkin.TryGetValue(def.itemid, out ulong existingSkin)) skin = existingSkin;
                if (skin != 0 && Instance.RequiresOwnership(def, skin)) skin = 0;
                if (skin != 0) _itemIdToSkin.TryAdd(def.itemid, skin);
                return skin;
            }

            private void EquipTurretWeapon(TurretInfo info, List<TurretWeaponOption> weapons)
            {
                if (IsContainerKilled(info) || !info.turret.AttachedWeapon.IsNull() || weapons.Count == 0)
                {
                    return;
                }

                AutoTurret turret = info.turret;
                TurretWeaponOption weapon = weapons.GetRandom();
                ItemDefinition def = weapon.Definition;
                ulong skin = GetTurretWeaponSkin(turret, def, weapon.Skins);
                Item item = ItemManager.Create(def, 1, skin);
                BaseProjectile baseProjectile = item.GetHeldEntity() as BaseProjectile;

                if (baseProjectile != null)
                {
                    if (baseProjectile.MuzzlePoint == null)
                    {
                        baseProjectile.MuzzlePoint = baseProjectile.transform;
                    }

                    bool modified = false;

                    if (!baseProjectile.usableByTurret)
                    {
                        baseProjectile.usableByTurret = true;
                        UsableByTurret = true;
                        modified = true;
                    }
                    else if (baseProjectile is BaseLauncher)
                    {
                        UsableByTurret = true;
                    }
                    if (modified && def.shortname != "pistol.python")
                    {
                        turret.inventory.canAcceptItem -= turret.CanAcceptItem;
                        turret.inventory.canAcceptItem += CanAcceptItem;
                    }
                }

                if (!item.MoveToContainer(turret.inventory, 0, false))
                {
                    item.Remove();
                }
                else
                {
                    item.SwitchOnOff(true);
                }
            }

            public bool IsValidWeapon(ItemDefinition itemDef)
            {
                ItemModEntity component = itemDef.GetComponent<ItemModEntity>();
                if (component == null)
                {
                    return false;
                }
                GameObjectRef objRef = component.entityPrefab;
                if (objRef == null)
                {
                    if (!Instance.DestroyedPrefabs.Contains(itemDef.shortname))
                    {
                        Puts("The game object reference for '{0}' has been broken by another plugin.", itemDef.shortname);
                        Instance.DestroyedPrefabs.Add(itemDef.shortname);
                    }
                    return false;
                }
                GameObject obj = objRef.Get();
                if (obj == null)
                {
                    if (!Instance.DestroyedPrefabs.Contains(itemDef.shortname))
                    {
                        Puts("The game object for '{0}' has been broken by another plugin.", itemDef.shortname);
                        Instance.DestroyedPrefabs.Add(itemDef.shortname);
                    }
                    return false;
                }
                HeldEntity component2 = obj.GetComponent<HeldEntity>();
                if (component2 == null)
                {
                    return false;
                }
                if (!component2.IsUsableByTurret && !IsRocketLauncher(component2))
                {
                    return false;
                }
                return true;
            }

            private bool CanAcceptItem(Item item, int targetPos)
            {
                if (targetPos == 0)
                {
                    return item.info.category == ItemCategory.Weapon;
                }
                return item.info.category == ItemCategory.Ammunition;
            }

            private bool CanAcceptItem(BasePlayer player, Item item, int targetPos)
            {
                if (targetPos == 0)
                {
                    return item.info.category == ItemCategory.Weapon;
                }
                return item.info.category == ItemCategory.Ammunition;
            }

            private void UpdateAttachedWeapon(TurretInfo info)
            {
                if (!IsUnloading && !IsDespawning && !info.IsKilled())
                {
                    try { info.turret.UpdateAttachedWeapon(); } catch { }
                }
            }

            private void InitiateStartup(TurretInfo info)
            {
                if (!Options.AutoTurret.RequiresPower && !info.IsKilled())
                {
                    info.turret.InitiateStartup();
                }
            }

            private void Authorize(BasePlayer player)
            {
                foreach (var info in turrets.Values)
                {
                    if (!info.IsKilled())
                    {
                        info.turret.authorizedPlayers.Add(player.userID);
                    }
                }
                if (privSpawned && !priv.IsKilled())
                {
                    priv.authorizedPlayers.Add(player.userID);
                }
            }

            private bool CanBypassAuthorized(ulong userid) => userid.BelongsToGroup("admin") || userid.HasPermission("raidablebases.canbypass");

            private void SetupGunTrap(GunTrap gt)
            {
                if (config.Weapons.Ammo.GunTrap > 0)
                {
                    FillAmmoGunTrap(gt);
                }

                if (config.Weapons.InfiniteAmmo.GunTrap)
                {
                    gt.inventory.onPreItemRemove += new Action<Item>(OnWeaponItemPreRemove);
                }

                triggers[gt.trigger] = gt;
            }

            private void SetupFogMachine(FogMachine fm)
            {
                if (config.Weapons.Ammo.FogMachine > 0)
                {
                    FillAmmoFogMachine(fm);
                }

                if (config.Weapons.InfiniteAmmo.FogMachine)
                {
                    fm.fuelPerSec = 0f;
                }

                if (config.Weapons.FogMotion || !config.Weapons.FogRequiresPower)
                {
                    using var update = fm.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);

                    if (config.Weapons.FogMotion)
                    {
                        update.Set(BaseEntity.Flags.Reserved9, true);
                    }

                    if (!config.Weapons.FogRequiresPower)
                    {
                        fm.CancelInvoke(fm.CheckTrigger);
                        update.Set(BaseEntity.Flags.Reserved5, true);
                        update.Set(BaseEntity.Flags.Reserved6, true);
                        update.Set(BaseEntity.Flags.Reserved10, true);
                        update.Set(BaseEntity.Flags.On, true);
                    }
                }
            }

            private void SetupSprinkler(Sprinkler sprinkler)
            {
                if (!config.Weapons.SprinklerRequiresPower)
                {
                    sprinkler.SetFlagLocal(BaseEntity.Flags.On, false);
                    try { sprinkler.SetFuelType(WaterTypes.WaterItemDef, null); } catch { }
                    sprinkler.UpdateFromInput(sprinkler.ConsumptionAmount(), 0);
                }
            }

            private void SetupFridge(Fridge fridge)
            {
                TryEmptyContainer(fridge);
                if (config.Settings.Management.Food)
                {
                    fridges.Add(fridge);
                }
                SetupIO((IOEntity)fridge);
            }

            private void SetupBurnVisuals(BaseEntity entity)
            {
                if (entity.Is(out BaseOven oven))
                {
                    oven.SetFlagLocal(BaseEntity.Flags.On, true);
                    oven.SendNetworkUpdate();
                }

                if (entity.Is(out IOEntity io))
                {
                    SetupIO(io);
                }
            }

            private void SetupOven(BaseOven oven)
            {
                if (oven != null && !ovens.Contains(oven))
                {
                    ovens.Add(oven);
                }
            }

            private void SetupFlameTurret(FlameTurret ft)
            {
                triggers[ft.trigger] = ft;
                ft.InitializeHealth(Options.FlameTurretHealth, Options.FlameTurretHealth);

                if (config.Weapons.Ammo.FlameTurret > 0)
                {
                    FillAmmoFlameTurret(ft);
                }

                if (config.Weapons.InfiniteAmmo.FlameTurret)
                {
                    ft.fuelPerSec = 0f;
                }
            }

            private void SetupSamSite(SamSite ss)
            {
                samsites.Add(ss);

                ss.vehicleScanRadius = ss.missileScanRadius = Options.SamSite.Range;

                if (Options.SamSite.Repair > 0f)
                {
                    ss.staticRespawn = true;
                    ss.InvokeRepeating(ss.SelfHeal, Options.SamSite.Repair * 60f, Options.SamSite.Repair * 60f);
                }
                else
                {
                    ss.SetFlagLocal(BaseEntity.Flags.Reserved1, false);
                    ss.CancelInvoke(ss.SelfHeal);
                    ss.staticRespawn = false;
                }

                if (!Options.SamSite.RequiresPower)
                {
                    SetupIO((IOEntity)ss);
                }

                if (config.Weapons.Ammo.SamSite > 0)
                {
                    FillAmmoSamSite(ss);
                }

                if (config.Weapons.InfiniteAmmo.SamSite)
                {
                    ss.inventory.onPreItemRemove += new Action<Item>(OnWeaponItemPreRemove);
                }

                ss.startHealth = UnityEngine.Random.Range(Options.SamSite.Min, Options.SamSite.Max);
                ss.InitializeHealth(ss.startHealth, ss.startHealth);
                ss.SendNetworkUpdate();
            }

            private bool ChangeTier(Door door)
            {
                uint prefabID = door.ShortPrefabName switch
                {
                    "door.hinged.toptier" => Options.Doors.Metal ? 202293038u : Options.Doors.Wooden ? 1343928398u : 0u,
                    "door.hinged.metal" or "door.hinged.industrial.a" or "door.hinged.industrial.d" => Options.Doors.HQM ? 170207918u : Options.Doors.Wooden ? 1343928398u : 0u,
                    "door.hinged.wood" => Options.Doors.HQM ? 170207918u : Options.Doors.Metal ? 202293038u : 0u,
                    "door.double.hinged.toptier" => Options.Doors.Metal ? 1418678061u : Options.Doors.Wooden ? 43442943u : 0u,
                    "wall.frame.garagedoor" when !Options.Doors.GarageDoor => 0u,
                    "wall.frame.garagedoor" => Options.Doors.HQM ? 201071098u : Options.Doors.Wooden ? 43442943u : 0u,
                    "door.double.hinged.metal" => Options.Doors.HQM ? 201071098u : Options.Doors.Wooden ? 43442943u : 0u,
                    "door.double.hinged.wood" => Options.Doors.HQM ? 201071098u : Options.Doors.Metal ? 1418678061u : 0u,
                    _ => 0u,
                };

                return prefabID != 0u && StringPool.toString.TryGetValue(prefabID, out var prefab) && SetDoorType(door, prefab);
            }

            private bool SetDoorType(Door door, string prefab)
            {
                Door other = GameManager.server.CreateEntity(prefab, door.transform.position, door.transform.rotation) as Door;
                if (other != null)
                {
                    var parent = door.HasParent() ? door.GetParentEntity() : null;
                    if (parent != null)
                    {
                        other.gameObject.Identity();

                        if (door.parentBone != 0) other.SetParent(parent, StringPool.Get(door.parentBone));
                        else other.SetParent(parent);
                    }

                    var building = door.GetBuilding();
                    if (building != null)
                    {
                        other.AttachToBuilding(building.ID);
                    }
                    else if (priv != null)
                    {
                        other.AttachToBuilding(priv.buildingID);
                    }

                    other.enableSaving = false;
                    other.Spawn();

                    if (other != null)
                    {
                        door.SafelyKill();
                        SetupEntity(other);
                        SetupDoor(other, true);
                        other.RefreshEntityLinks();
                        other.SendNetworkUpdate();
                        return true;
                    }
                }

                return false;
            }

            private void SetupDoor(Door door)
            {
                if (door.canTakeLock && !door.isSecurityDoor)
                {
                    doors.Add(door);
                }
            }

            private void SetupDoor(Door door, bool changed)
            {
                CreateLock(door, Options.KeyLockDoors, Options.CodeLockDoors);

                if (!changed && Options.Doors.Any())
                {
                    try
                    {
                        if (ChangeTier(door))
                        {
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Puts(ex);
                        if (door.IsKilled())
                        {
                            return;
                        }
                    }
                }

                SetupSkin(door);

                if (Options.CloseOpenDoors)
                {
                    CloseDoor(door);
                }
            }

            private void CloseDoor(Door door)
            {
                if (door.IsKilled() || !door.IsOpen())
                {
                    return;
                }

                if (door.IsBusy())
                {
                    bool reverseOpen = door.HasFlag(Door.ReverseOpen);
                    door.ReverseDoorAnimation(true, reverseOpen);
                    door.SetOpen(false, true);
                    door.StopCheckingForBlockages();
                    door.ClientRPC(RpcTarget.NetworkGroup("OnDoorInterrupted"), true, reverseOpen);
                    return;
                }

                door.SetOpen(false, true);
            }

            private void SetupDoors()
            {
                doors.RemoveAll(IsKilled);

                foreach (var door in doors)
                {
                    SetupDoor(door, false);
                }
            }

            private void SetupDoorControllers()
            {
                doorControllers.RemoveAll(IsKilled);

                foreach (var cdm in doorControllers)
                {
                    if (!Options.DoorControllersRequiresPower)
                    {
                        SetupIO(cdm);
                    }

                    Door door = cdm.targetDoor;

                    if (door != null)
                    {
                        SetupPairedDoor(door);
                        continue;
                    }

                    try { door = cdm.FindDoor(true); } catch { continue; }

                    if (door.IsValid())
                    {
                        SetupPairedDoor(door);
                        cdm.SetTargetDoor(door);
                    }
                }

                doorControllers.Clear();
            }

            private void SetupPairedDoor(Door door)
            {
                if (door.canTakeLock && !door.isSecurityDoor)
                {
                    CreateLock(door, Options.KeyLockDoors, Options.CodeLockDoors);
                }
                SetupSkin(door);
                doors.Remove(door);
            }

            private void CreateLock(BaseEntity entity, bool createKeyLock, bool createCodeLock)
            {
                if (Type == RaidableType.None || !createKeyLock && !createCodeLock || entity.IsKilled())
                {
                    return;
                }

                var slot = entity.GetSlot(BaseEntity.Slot.Lock);

                if (slot.IsNull())
                {
                    if (createKeyLock)
                    {
                        CreateKeyLock(entity);
                    }
                    else if (createCodeLock)
                    {
                        CreateCodeLock(entity);
                    }
                    return;
                }

                if (createKeyLock)
                {
                    if (slot is CodeLock codeLock)
                    {
                        codeLock.SetParent(null);
                        codeLock.SafelyKill();
                    }

                    if (!(slot is KeyLock keyLock))
                    {
                        CreateKeyLock(entity);
                    }
                    else SetupLock(keyLock);
                }
                else if (createCodeLock)
                {
                    if (slot is KeyLock keyLock)
                    {
                        keyLock.SetParent(null);
                        keyLock.SafelyKill();
                    }

                    if (!(slot is CodeLock codeLock))
                    {
                        CreateCodeLock(entity);
                    }
                    else SetupLock(codeLock, true);
                }
            }

            private void CreateKeyLock(BaseEntity entity)
            {
                if (GameManager.server.CreateEntity(StringPool.Get(2106860026)) is KeyLock keyLock)
                {
                    keyLock.gameObject.Identity();
                    keyLock.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                    keyLock.Spawn();
                    entity.SetSlot(BaseEntity.Slot.Lock, keyLock);
                    SetupLock(keyLock, true);
                }
            }

            private void CreateCodeLock(BaseEntity entity)
            {
                if (GameManager.server.CreateEntity(StringPool.Get(3518824735)) is CodeLock codeLock)
                {
                    codeLock.gameObject.Identity();
                    codeLock.SetParent(entity, entity.GetSlotAnchorName(BaseEntity.Slot.Lock));
                    codeLock.Spawn();
                    entity.SetSlot(BaseEntity.Slot.Lock, codeLock);
                    SetupLock(codeLock, true);
                }
            }

            private void SetupBuildingPriviledge(BuildingPrivlidge priv)
            {
                if (Type != RaidableType.None)
                {
                    priv.authorizedPlayers.Clear();
                    priv.SendNetworkUpdate();
                }

                CreateLock(priv, Options.KeyLockPrivilege, Options.CodeLockPrivilege);

                if (this.priv.IsKilled() || priv.Distance(Location) < this.priv.Distance(Location))
                {
                    this.priv = priv;
                    privSpawned = true;
                }

                if (privSpawned && !privHadLoot)
                {
                    privHadLoot = priv != null && priv.inventory != null && !priv.inventory.IsEmpty();
                }

                privs.Add(priv);

                if (ConVar.Softcore.raidwindow_enabled && BaseGameMode.GetActiveGameMode(serverside: true) is GameModeSoftcore)
                {
                    if (ConVar.Softcore.raidwindow_fresh_tc_seconds > 0f)
                    {
                        raidWindowPrivs = true;
                        priv.timePlaced = Time.time;
                        priv.UpdateRaidableFlag();
                    }
                    else
                    {
                        using var update = priv.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags);
                        update.Set(BuildingPrivlidge.Flag_Raidable, true);
                    }
                }
            }

            private bool raidWindowPrivs;
            private void RefreshRaidWindowPrivileges()
            {
                if (!ConVar.Softcore.raidwindow_enabled)
                {
                    return;
                }
                for (int i = privs.Count - 1; i >= 0; i--)
                {
                    BuildingPrivlidge priv = privs[i];
                    if (priv.IsKilled())
                    {
                        privs.RemoveAt(i);
                        continue;
                    }
                    if (ConVar.Softcore.raidwindow_fresh_tc_seconds > 0f)
                    {
                        priv.timePlaced = Time.time;
                        if (!priv.HasFlag(BuildingPrivlidge.Flag_Raidable))
                        {
                            priv.UpdateRaidableFlag();
                        }
                    }
                    else if (!priv.HasFlag(BuildingPrivlidge.Flag_Raidable))
                    {
                        using var update = priv.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags);
                        update.Set(BuildingPrivlidge.Flag_Raidable, true);
                    }
                }
            }

            private void SetupLocker(Locker locker)
            {
                if (config.Settings.Management.Lockers)
                {
                    lockers.Add(locker);
                }
            }

            private void FilterRugSpawns()
            {
                for (int i = _rugSpawns.Count - 1; i >= 0; i--)
                {
                    var rug = _rugSpawns[i];

                    if (rug.IsKilled() || IsRugCovered(rug))
                    {
                        _rugSpawns.RemoveAt(i);
                    }
                }
            }

            private bool IsRugCovered(BaseEntity rug)
            {
                foreach (var decor in _decorDeployables)
                {
                    if (decor.IsKilled() || decor == rug || decor.transform.position.y < rug.transform.position.y)
                    {
                        continue;
                    }

                    if (InRange(decor.transform.position, rug.transform.position, 1f))
                    {
                        return true;
                    }
                }

                return false;
            }

            private void SetupSleepingBag(SleepingBag bag)
            {
                if (Options.NPC.Inside.SpawnOnBeds)
                {
                    _beds.Add(bag);
                }

                if (Options.NPC.Inside.BedHealthMultiplier != 1f)
                {
                    bag.health *= Options.NPC.Inside.BedHealthMultiplier;
                }

                if (Type != RaidableType.None)
                {
                    ulong oldID = bag.deployerUserID;
                    bag.deployerUserID = 0uL;
                    SleepingBag.OnBagChangedOwnership(bag, oldID);
                }
            }

            private void SetupCollectible(CollectibleEntity ce)
            {
                if (IsPickupBlacklisted(ce.ShortPrefabName))
                {
                    ce.itemList = null;
                }
            }

            private void SetupSpookySpeaker(SpookySpeaker ss)
            {
                if (!config.Weapons.SpookySpeakersRequiresPower)
                {
                    ss.UpdateHasPower(25, 0);
                }
            }

            private void SetupComputerStation(ComputerStation cs)
            {
                if (cs.spawnedIo.IsValid(true))
                {
                    IOEntity io = cs.spawnedIo.Get(true);
                    if (io != null)
                    {
                        SetupIO(io);
                    }
                }
            }

            private void SetupDecayEntity(DecayEntity e)
            {
                e.decay = null;
                e.upkeepTimer = float.MinValue;

                if (e.ShortPrefabName.StartsWith("rug"))
                {
                    _allRugs.Add(e);
                }

                if (Options.NPC.Inside.SpawnOnRugs && e.ShortPrefabName.StartsWith("rug.") && Mathf.Approximately(e.transform.up.y, 1f) && IsAllowedNpcRugSkin(e))
                {
                    _rugSpawns.Add(e);

                    if (Options.NPC.Inside.RugHealthMultiplier != 1f)
                    {
                        e.health *= Options.NPC.Inside.RugHealthMultiplier;
                    }
                }

                Vector3 position = e.transform.position;
                switch (e)
                {
                    case Signage or BaseTrap or Barricade when !NearFoundation(position, 1.75f) && !Physics.Raycast(e.transform.position + new Vector3(0f, 0.15f, 0f), Vector3.down, 50f, Layers.Mask.Construction):
                        float spawnHeight = SpawnsController.GetSpawnHeight(position, false) + (e is Barricade ? 0f : 0.02f);
                        if (position.y - spawnHeight <= 3f)
                        {
                            position.y = spawnHeight;
                            e.transform.position = position;
                        }
                        break;
                }
            }

            private bool IsAllowedNpcRugSkin(BaseEntity rug) => Options.NPC.Inside.SpawnOnRugsSkin == 1 || rug.skinID == Options.NPC.Inside.SpawnOnRugsSkin;

            private void SetupBoxSkin(StorageContainer container)
            {
                if (!IsBox(container, false) || config.Skins.Boxes.IgnoreSkinned && container.skinID != 0uL)
                {
                    return;
                }

                if (!Instance.DeployableItems.TryGetValue(container.gameObject.name, out var def))
                {
                    return;
                }

                if (config.Skins.Boxes.Unique && _prefabToSkin.TryGetValue(container.prefabID, out var skin))
                {
                    container.skinID = skin;
                    return;
                }

                var si = GetItemSkins(def, config.Skins.Boxes.ApprovedOnly);

                if (config.Skins.Boxes.Skins.Count > 0 && SetItemSkin(config.Skins.Boxes.Skins, si, container, config.Skins.Boxes.Unique))
                {
                    return;
                }

                bool hasSkin = TrySelectItemSkin(si, config.Skins.Boxes.Random, config.Skins.Boxes.Workshop, config.Skins.Boxes.ImportedWorkshop, out ulong skin2);

                if (!_prefabToSkin.TryGetValue(container.prefabID, out ulong value))
                {
                    _prefabToSkin[container.prefabID] = value = hasSkin ? skin2 : container.skinID;
                }

                if (config.Skins.Boxes.Unique)
                {
                    container.skinID = value;
                }
                else if (hasSkin)
                {
                    container.skinID = skin2;
                }
            }

            private void SetupSkin(BaseEntity entity)
            {
                if (IsUnloading || IsBox(entity, false) || config.Skins.Deployables.IgnoreSkinned && entity.skinID != 0uL)
                {
                    return;
                }

                if (config.Skins.Deployables.Unique && _prefabToSkin.TryGetValue(entity.prefabID, out var skin))
                {
                    entity.skinID = skin;
                    return;
                }

                if (!Instance.DeployableItems.TryGetValue(entity.gameObject.name, out var def) || def == null)
                {
                    return;
                }

                var si = GetItemSkins(def, config.Skins.Deployables.ApprovedOnly);

                if (config.Skins.Deployables.Doors.Count > 0 && entity is Door && SetItemSkin(config.Skins.Deployables.Doors, si, entity, config.Skins.Deployables.Unique))
                {
                    return;
                }

                if (!config.Skins.Deployables.SkinEverything && !config.Skins.Deployables.PartialNames.Exists(entity.ShortPrefabName.Contains))
                {
                    return;
                }

                TrySelectItemSkin(si, config.Skins.Deployables.Random, config.Skins.Deployables.Workshop, config.Skins.Deployables.ImportedWorkshop, out ulong skin2);

                if (!_prefabToSkin.TryGetValue(entity.prefabID, out ulong value))
                {
                    _prefabToSkin[entity.prefabID] = value = skin2 == 0 ? entity.skinID : skin2;
                }

                if (config.Skins.Deployables.Unique && entity is Door)
                {
                    entity.skinID = value;
                    entity.SendNetworkUpdate();
                }
                else if (skin2 != 0)
                {
                    entity.skinID = skin2;
                    entity.SendNetworkUpdate();
                }
            }

            private bool npcEnabled;
            private void Subscribe()
            {
                if (IsUnloading)
                {
                    return;
                }

                if (Instance.BaseRepair.CanCall())
                {
                    Subscribe(nameof(OnBaseRepair));
                }

                if (config.Settings.Management.Lockout.AllyExploit)
                {
                    if (config.Settings.Management.Lockout.BlockClans) Subscribe(nameof(OnClanMemberJoined));
                    if (config.Settings.Management.Lockout.BlockTeams) Subscribe(nameof(OnTeamAcceptInvite));
                }

                if (Options.EnforceConditionLoss && !Instance.permission.GroupHasPermission("default", "raidablebases.durabilitybypass"))
                {
                    Subscribe(nameof(OnLoseCondition));
                    Subscribe(nameof(OnNeverWear));
                }

                if ((Options.NPC.SpawnAmountMurderers > 0 || Options.NPC.SpawnAmountScientists > 0) && Options.NPC.Enabled)
                {
                    npcMaxAmountMurderers = Options.NPC.SpawnRandomAmountMurderers && Options.NPC.SpawnAmountMurderers > 1 ? UnityEngine.Random.Range(Options.NPC.SpawnMinAmountMurderers, Options.NPC.SpawnAmountMurderers + 1) : Options.NPC.SpawnAmountMurderers;
                    npcMaxAmountScientists = Options.NPC.SpawnRandomAmountScientists && Options.NPC.SpawnAmountScientists > 1 ? UnityEngine.Random.Range(Options.NPC.SpawnMinAmountScientists, Options.NPC.SpawnAmountScientists + 1) : Options.NPC.SpawnAmountScientists;

                    if (Options.NPC.Inside.Max > 0)
                    {
                        npcMaxAmountInside = UnityEngine.Random.Range(Options.NPC.Inside.Min, Options.NPC.Inside.Max + 1);
                        npcMaxAmountInside = Mathf.Clamp(npcMaxAmountInside, -1, npcMaxAmountScientists);
                    }

                    if (npcMaxAmountMurderers > 0 || npcMaxAmountScientists > 0)
                    {
                        if (config.Settings.Management.BlockCustomLootNPC)
                        {
                            Subscribe(nameof(OnCustomLootNPC));
                        }

                        if (config.Settings.Management.BlockAlphaLoot)
                        {
                            Subscribe(nameof(CanPopulateLoot));
                        }

                        if (config.Settings.Management.BlockBetterLoot)
                        {
                            Subscribe(nameof(ShouldBLPopulate_NPC));
                        }

                        if (config.Settings.Management.BlockNpcKits)
                        {
                            Subscribe(nameof(OnNpcKits));
                        }

                        if (Options.NPC.PlayCatch)
                        {
                            Subscribe(nameof(OnExplosiveFuseSet));
                        }

                        Subscribe(nameof(OnNpcDuck));
                        Subscribe(nameof(OnNpcDestinationSet));
                        npcEnabled = true;
                    }
                }

                Instance.EnsureSchedulers(npcEnabled);

                if (config.Settings.Management.PreventFallDamage)
                {
                    Subscribe(nameof(OnPlayerLand));
                }

                if (!config.Settings.Management.AllowTeleport)
                {
                    Subscribe(nameof(CanTeleport));
                    Subscribe(nameof(canTeleport));
                }

                if (AllowPVP ? config.Settings.Management.BlockRevivePVP : config.Settings.Management.BlockRevivePVE)
                {
                    Subscribe(nameof(CanRevivePlayer));
                }

                if (AllowPVP ? config.Settings.Management.BlockRestorePVP : config.Settings.Management.BlockRestorePVE)
                {
                    Subscribe(nameof(OnRestoreUponDeath));
                }

                if (config.Settings.Management.NoLifeSupport)
                {
                    Subscribe(nameof(OnLifeSupportSavingLife));
                }

                if (config.Settings.Management.NoDoubleJump)
                {
                    Subscribe(nameof(CanDoubleJump));
                }

                if (!config.Settings.Management.BackpacksOpenPVP || !config.Settings.Management.BackpacksOpenPVE)
                {
                    Subscribe(nameof(CanOpenBackpack));
                }

                if (config.Settings.Management.PreventFireFromSpreading)
                {
                    Subscribe(nameof(OnFireBallSpread));
                }

                if (privSpawned)
                {
                    Subscribe(nameof(OnCupboardProtectionCalculated));
                }

                if (Options.BuildingRestrictions.Any() || !config.Settings.Management.AllowUpgrade)
                {
                    Subscribe(nameof(OnStructureUpgrade));
                }

                if (BlacklistedCommands.Exists(x => x.Equals("remove", StringComparison.OrdinalIgnoreCase)))
                {
                    Subscribe(nameof(canRemove));
                }

                if (Options.Invulnerable || Options.InvulnerableUntilCupboardIsDestroyed)
                {
                    Subscribe(nameof(OnEntityGroundMissing));
                }

                if (Options.RequiresCupboardAccess)
                {
                    Subscribe(nameof(OnCupboardAuthorize));
                }

                Subscribe(nameof(OnBackpackDrop));
                Subscribe(nameof(OnLootEntityEnd));
                Subscribe(nameof(OnFireBallDamage));
                Subscribe(nameof(CanPickupEntity));
                Subscribe(nameof(OnPlayerDropActiveItem));
                Subscribe(nameof(OnPlayerDeath));
                Subscribe(nameof(OnEntityDeath));
                Subscribe(nameof(OnEntityKill));
                Subscribe(nameof(CanBGrade));
                Subscribe(nameof(CanBePenalized));
                Subscribe(nameof(CanLootEntity));
                Subscribe(nameof(OnEntityBuilt));
                //Subscribe(nameof(CanGainXp));
            }

            private void Subscribe(string hook) => Instance.Subscribe(hook);

            private void MakeAnnouncements()
            {
                if (Type == RaidableType.None)
                {
                    _allcontainers.RemoveWhere(IsContainerKilled);

                    itemAmountSpawned = _allcontainers.Sum(x => x.inventory.itemList.Count);
                }

                Puts("{0} @ {1} : {2} items", BaseName, Instance.PositionToGrid(Location, true), itemAmountSpawned);

                if (Options.Silent || Options.Smart)
                {
                    return;
                }

                switch ((IsPayLocked, AllowPVP))
                {
                    case (false, false) when config.EventMessages.OpenedPVE:
                    case (false, true) when config.EventMessages.OpenedPVP:
                    case (true, _) when config.EventMessages.OpenedAndPaid:
                        break;
                    default:
                        return;
                }

                foreach (var target in BasePlayer.activePlayerList)
                {
                    if (target.HasPermission("raidablebases.limitedannouncements")) continue;
                    float distance = Mathf.Floor(target.transform.position.Distance(Location));
                    string mode = LangMode(target.UserIDString);
                    string flag = mx(GetAllowKey(), target.UserIDString).Replace("[", string.Empty).Replace("] ", string.Empty);
                    string posStr = FormatGridReference(target, Location);
                    string text = posStr != Location.ToString() ? mx("RaidOpenMessage", target.UserIDString, mode, posStr, distance, flag) : mx("RaidOpenNoMapMessage", target.UserIDString, mode, distance, flag);
                    if (Type == RaidableType.None) text = text.Replace(mode, NoMode);
                    string message = ownerId.IsSteamId() ? mx("RaidOpenAppendedFormat", target.UserIDString, text, mx("Owner", target.UserIDString), ownerName) : text;
                    if (config.GUIAnnouncement.Enabled && config.GUIAnnouncement.Distance > 0 && Instance.GUIAnnouncements != null)
                    {
                        if (distance <= config.GUIAnnouncement.Distance)
                        {
                            NotifyUnlessSmart(target, message);
                        }
                    }
                    else
                    {
                        NotifyUnlessSmart(target, message);
                    }
                }
            }

            public void ResetPublicOwner()
            {
                double remainingTime = ownerId.IsSteamId() ? PlayerActivityTimeLeft(ownerId) : 0d;
                if (!IsOpened || IsPayLocked || remainingTime > 0d)
                {
                    Invoke(ResetPublicOwner, (remainingTime > 0d && !double.IsPositiveInfinity(remainingTime)) ? (float)remainingTime : config.Settings.Management.LockTimeFloat * 60f);
                    return;
                }

                if (HarmonyModInterface.CallHook("OnRaidableResetPublicOwner", ownerId, Location, ProtectionRadius, GetRaiders(), Entities.ToList(), Options.Level) != null)
                {
                    return;
                }

                if (config.Settings.Management.SetLockout)
                {
                    if (raiders.TryGetValue(ownerId, out var ri))
                    {
                        TrySetLockout(ri);
                    }
                }

                ResetEventLock(true);
                CheckBackpacks(true);
            }

            public void ResetEventLock(bool cooldown)
            {
                if (IsInvoking(ResetPublicOwner))
                {
                    CancelInvoke(ResetPublicOwner);
                }
                if (!ResetPayLock(cooldown))
                {
                    return;
                }
                HarmonyModInterface.CallHook("OnRaidableBaseUnlocked", new object[] { payments.userid.IsSteamId() ? payments.userid.ToString() : ownerId.ToString(), Location, Instance.PositionToGrid(Location, false), Options.Level, AllowPVP, BaseName, spawnDateTime, despawnDateTime });
                raiders.Remove(ownerId);
                IsEngaged = true;
                IsPayLocked = false;
                ownerId = 0uL;
                ownerName = string.Empty;
                UpdateMarker();
                CreateSpheres();
            }

            private bool ResetPayLock(bool cooldown)
            {
                if (IsPayLocked)
                {
                    if (HarmonyModInterface.CallHook("OnRaidableResetPayLock", ownerId, Location, ProtectionRadius, GetRaiders(), Entities.ToList(), Options.Level) != null)
                    {
                        return false;
                    }
                    if (cooldown) StartPurchaseCooldown();
                    CheckBackpacks(true);
                    raiders.Values.ForEach(ri =>
                    {
                        ri.IsParticipant = false;
                        ri.participantTime = 0f;
                    });
                }
                return true;
            }

            public void SpawnDrops(ItemContainer[] containers, List<LootItem> lootList)
            {
                if (containers == null || containers.Length == 0)
                {
                    return;
                }

                lootList.ForEach(ti =>
                {
                    if (!string.IsNullOrWhiteSpace(ti.shortname) && ti.HasProbability())
                    {
                        if (ti.definition == null)
                        {
                            Puts("Invalid shortname in profile for npc: {0}", ti.shortname);
                            return;
                        }

                        Item item = CreateItem(ti, ti.amountMin < ti.amount ? Core.Random.Range(ti.amountMin, ti.amount + 1) : ti.amount);
                        if (item == null)
                            return;

                        foreach (var container in containers)
                        {
                            if (item.MoveToContainer(container))
                            {
                                return;
                            }
                        }

                        item.Remove();
                    }
                });
            }

            private bool SetupLoot()
            {
                _containers.RemoveWhere(IsContainerKilled);

                int amount = Options.GetLootAmount(Type);
                if (Options.SkipTreasureLoot || amount <= 0)
                {
                    ConvertVanillaPaperToGrimmCoin();
                    return true;
                }

                using var containers = DisposableList<StorageContainer>();
                if (!SetupLootContainers(containers))
                {
                    return false;
                }

                SetupMovableContainers();

                int capacity = GetAvailableLootSlots(containers);
                amount = Math.Min(amount, capacity);

                if (amount <= 0)
                {
                    ConvertVanillaPaperToGrimmCoin();
                    return true;
                }

                LootProfile loot = new()
                {
                    Unique = Instance.config.Loot,
                    BaseName = BaseName,
                    Amount = amount,
                    Instance = Instance,
                    Options = Options,
                    UserID = ownerId,
                    AllowPVP = AllowPVP,
                    Capacity = capacity,
                };

                long memoryBefore = 0;
                int gen0Before = 0, gen1Before = 0, gen2Before = 0;
                Stopwatch generationStopwatch = null;
                Stopwatch divisionStopwatch = null;
                bool debug = DebugMode;

                if (debug)
                {
                    memoryBefore = System.GC.GetTotalMemory(false);
                    gen0Before = System.GC.CollectionCount(0);
                    gen1Before = System.GC.CollectionCount(1);
                    gen2Before = System.GC.CollectionCount(2);
                    generationStopwatch = Stopwatch.StartNew();
                }

                TakeLootFromLootTables(loot);

                if (debug)
                {
                    generationStopwatch.Stop();
                }

                if (loot.Tables.Count == 0)
                {
                    Puts(mx("NoConfiguredLoot"));
                    ConvertVanillaPaperToGrimmCoin();
                    return true;
                }

                int generatedCount = loot.Tables.Count;

                if (debug)
                {
                    divisionStopwatch = Stopwatch.StartNew();
                }

                DivideLoot(loot.Tables, loot.Amount, containers);

                if (debug)
                {
                    divisionStopwatch.Stop();

                    long generationMilliseconds = generationStopwatch.ElapsedMilliseconds;
                    long divisionMilliseconds = divisionStopwatch.ElapsedMilliseconds;

                    Puts("Processed {0} loot entries: generated in {1}ms, divided in {2}ms, total {3}ms; managed memory delta (server-wide): {4:N0} bytes; GC: gen0={5}, gen1={6}, gen2={7}",
                        generatedCount, generationMilliseconds, divisionMilliseconds, generationMilliseconds + divisionMilliseconds, System.GC.GetTotalMemory(false) - memoryBefore,
                        System.GC.CollectionCount(0) - gen0Before, System.GC.CollectionCount(1) - gen1Before, System.GC.CollectionCount(2) - gen2Before);
                }

                ConvertVanillaPaperToGrimmCoin();

                SetupSellOrders();

                return true;
            }

            private bool SetupLootContainers(List<StorageContainer> containers)
            {
                if (_containers.Count == 0)
                {
                    Puts(mx(Entities.Exists() ? "NoContainersFound" : "NoEntitiesFound", null, BaseName, Instance.PositionToGrid(Location, true)));
                    return false;
                }

                TryInvokeMethod(CheckExpansionSettings);

                using var tmp = _containers.ToPooledList();
                int clipped = 0, skipped = 0;

                foreach (var container in tmp)
                {
                    if (container.IsKilled() || !IsBox(container, true) || Options.IgnoreContainedLoot && container.inventory != null && !container.inventory.IsEmpty())
                    {
                        skipped++;
                        continue;
                    }

                    if (config.Settings.Management.ClippedBoxes && IsRockFaceUpwards(container.transform.position + new Vector3(0f, container.bounds.extents.y)))
                    {
                        clipped++;
                        RemoveContainer(container);
                        continue;
                    }

                    if (Options.DivideLoot)
                    {
                        containers.Add(container);
                        continue;
                    }
                    else if (container.inventory == null || container.inventory.IsEmpty())
                    {
                        containers.Add(container);
                        break;
                    }
                }

                if (Options.IgnoreContainedLoot)
                {
                    lockers.RemoveAll(x => x.IsKilled() || x.inventory == null || !x.inventory.IsEmpty());
                }

                if (containers.Count == 0)
                {
                    foreach (var entity in Entities)
                    {
                        if (entity is not StorageContainer box || box.IsKilled() || !IsBox(box, true))
                        {
                            continue;
                        }

                        AddContainer(box);
                        containers.Add(box);
                    }
                }

                if (containers.Count == 0)
                {
                    Puts(mx("NoBoxesFound", null, BaseName, Instance.PositionToGrid(Location, true)));
                    Puts("Loot containers rejected for {0}: clipped={1}, skipped={2}, tracked={3}", BaseName, clipped, skipped, tmp.Count);
                    return false;
                }

                return true;
            }

            public class LootProfile
            {
                public List<LootItem> Base = new();
                public List<LootItem> Difficulty = new();
                public List<LootItem> Default = new();
                public List<LootItem> Tables = new();
                public TreasureSettings Unique;
                public BuildingOptions Options;
                public RaidableBases Instance;
                public string BaseName;
                public bool AllowPVP;
                public ulong UserID;
                public int Amount;
                public int Capacity = int.MaxValue;
                public Configuration config => Instance.config;
                public int Count => Base.Count + Difficulty.Count + Default.Count;
            }

            private bool IsItemBlockedInto(LootItem lootItem, StorageContainer container)
            {
                return container.IsKilled() || container is BaseOven && !IsCookable(lootItem.definition) || container is Locker && !IsLockerItem(lootItem.definition);
            }

            private LootItem TakeLootItem(List<LootItem> lootList)
            {
                for (int i = 0; i < lootList.Count; i++)
                {
                    LootItem lootItem = lootList[i];

                    if (!lootItem.hasPriority)
                    {
                        continue;
                    }

                    lootItem.hasPriority = false;
                    lootList.RemoveAt(i);
                    return lootItem;
                }

                int index = lootList.Count - 1;
                LootItem item = lootList[index];
                lootList.RemoveAt(index);
                return item;
            }

            private void DivideLoot(List<LootItem> lootList, int amount, List<StorageContainer> containers)
            {
                Shuffle(lootList);

                while (lootList.Count > 0 && containers.Count > 0 && itemAmountSpawned < amount)
                {
                    LootItem lootItem = TakeLootItem(lootList);

                    Item item = CreateItem(lootItem, lootItem.amount);

                    if (item == null)
                    {
                        continue;
                    }

                    if (MoveToCupboard(item) || MoveToBBQ(item) || MoveToOven(item) || MoveFood(item) || MoveToRack(item) || MoveToLocker(item))
                    {
                        itemAmountSpawned++;
                        continue;
                    }

                    bool itemMovedToContainer = false;

                    foreach (var container in containers)
                    {
                        if (container is WeaponRack || IsItemBlockedInto(lootItem, container))
                        {
                            continue;
                        }
                        if (item.MoveToContainer(container.inventory, -1, false))
                        {
                            if (item.info.category == ItemCategory.Weapon)
                            {
                                weaponsInBox++;
                            }
                            containers.Remove(container);
                            if (!container.inventory.IsFull())
                            {
                                containers.Add(container);
                            }
                            itemMovedToContainer = true;
                            itemAmountSpawned++;
                            break;
                        }
                    }

                    if (!itemMovedToContainer)
                    {
                        item.Remove();
                    }
                }

                if (itemAmountSpawned == 0)
                {
                    Puts(mx("NoLootSpawned"));
                }
            }

            private static void TakeLootFromBaseLoot(LootProfile loot)
            {
                foreach (var (key, profile) in loot.Instance.Buildings.Profiles)
                {
                    if (key != loot.BaseName && !profile.Options.AdditionalBases.ContainsKey(loot.BaseName))
                    {
                        continue;
                    }
                    if (loot.Options.AllowPVP != profile.Options.AllowPVP)
                    {
                        continue;
                    }
                    if (loot.Options.Mode != profile.Options.Mode)
                    {
                        continue;
                    }
                    TakeLootFrom(loot, profile.BaseLootList, loot.Base);
                    break;
                }

                if (loot.Options.AlwaysSpawnBaseLoot)
                {
                    using var tmp = loot.Base.ToPooledList();

                    foreach (var ti in tmp)
                    {
                        if (ti.HasProbability())
                        {
                            if (!loot.Options.AllowDuplicates)
                            {
                                loot.Base.Remove(ti);
                            }

                            AddToLoot(loot, ti, true);
                        }

                        if (loot.Options.EnforceProbability && ti.probability < 1f)
                        {
                            loot.Base.Remove(ti);
                        }
                    }

                    if (loot.Unique.Base)
                    {
                        loot.Base.Clear();
                    }
                }
            }

            private static void TakeLootFromDifficultyLoot(LootProfile loot)
            {
                if (loot.Instance.Buildings.DifficultyLootLists.TryGetValue(loot.Options.Mode, out var lootList))
                {
                    TakeLootFrom(loot, lootList, loot.Difficulty);
                }
            }

            private static void TakeLootFromWeekdayLoot(LootProfile loot)
            {
                if (loot.Instance.WeekdayLoot.Count > 0)
                {
                    TakeLootFrom(loot, loot.Instance.WeekdayLoot, loot.Default);
                }
            }

            private static void TakeLootFromDefaultLoot(LootProfile loot)
            {
                if (loot.Count < loot.Amount)
                {
                    TakeLootFrom(loot, loot.Instance.TreasureLoot, loot.Default);
                }
            }

            private static void TakeLootFrom(LootProfile loot, List<LootItem> lootList, List<LootItem> to)
            {
                if (lootList.Count == 0)
                {
                    return;
                }

                foreach (var ti in lootList)
                {
                    if (ti == null || ti.amount <= 0 || ti.probability <= 0f)
                    {
                        continue;
                    }

                    if (loot.Options.Primitive && ti.definition != null && !ti.definition.IsAllowedInEra(EraRestriction.Default, ConVar.Server.Era == Era.None ? Era.Primitive : ConVar.Server.Era))
                    {
                        continue;
                    }

                    if (loot.config.BlockPaidContent && loot.Instance.RequiresOwnership(ti.definition, 0))
                    {
                        continue;
                    }

                    to.Add(ti);
                }
            }

            private int GetAvailableLootSlots(List<StorageContainer> containers)
            {
                using var inventories = Pool.Get<PooledHashSet<ItemContainer>>();
                int available = 0;

                void Add(ItemContainer inventory)
                {
                    if (inventory == null || !inventories.Add(inventory))
                    {
                        return;
                    }

                    available += Math.Max(0, inventory.capacity - inventory.itemList.Count);
                }

                foreach (StorageContainer container in containers)
                {
                    if (!IsContainerKilled(container))
                    {
                        Add(container.inventory);
                    }
                }

                if (config.Settings.Management.Cupboard && privSpawned && !priv.IsKilled())
                {
                    Add(priv.inventory);
                }

                if (config.Settings.Management.Cook || config.Settings.Management.Food)
                {
                    foreach (BaseOven oven in ovens)
                    {
                        if (!IsContainerKilled(oven))
                        {
                            Add(oven.inventory);
                        }
                    }
                }

                if (config.Settings.Management.Food)
                {
                    foreach (Fridge fridge in fridges)
                    {
                        if (!IsContainerKilled(fridge))
                        {
                            Add(fridge.inventory);
                        }
                    }
                }

                foreach (Locker locker in lockers)
                {
                    if (!IsContainerKilled(locker))
                    {
                        Add(locker.inventory);
                    }
                }

                return available;
            }

            private static void TakeLootFromLootTables(LootProfile loot)
            {
                TakeLootFromBaseLoot(loot);
                TakeLootFromDifficultyLoot(loot);
                TakeLootFromWeekdayLoot(loot);
                TakeLootFromDefaultLoot(loot);

                int iterations = 0;

                using var source = DisposableList<LootItem>();

                source.AddRange(loot.Base);
                source.AddRange(loot.Difficulty);
                source.AddRange(loot.Default);
                Shuffle(source);

                if (loot.Unique.Base)
                {
                    loot.Base.Clear();
                }

                if (loot.Unique.Difficulty)
                {
                    loot.Difficulty.Clear();
                }

                if (loot.Unique.Default)
                {
                    loot.Default.Clear();
                }

                while (loot.Tables.Count < loot.Amount && source.Count > 0)
                {
                    int index = source.Count - 1;
                    LootItem ti = source[index];
                    source.RemoveAt(index);

                    if (ti.HasProbability())
                    {
                        if (!loot.Options.AllowDuplicates)
                        {
                            loot.Base.Remove(ti);
                            loot.Difficulty.Remove(ti);
                            loot.Default.Remove(ti);
                        }

                        AddToLoot(loot, ti);
                    }

                    if (loot.Options.EnforceProbability && ti.probability < 1f)
                    {
                        loot.Base.Remove(ti);
                        loot.Difficulty.Remove(ti);
                        loot.Default.Remove(ti);
                    }

                    if (source.Count == 0 && loot.Tables.Count < loot.Amount && ++iterations <= loot.Tables.Count)
                    {
                        source.AddRange(loot.Base);
                        source.AddRange(loot.Difficulty);
                        source.AddRange(loot.Default);
                        Shuffle(source);
                    }
                }
            }

            private static bool AddToLoot(LootProfile loot, LootItem ti, bool hasPriority = false)
            {
                int remainingCapacity = loot.Capacity - loot.Tables.Count;

                if (remainingCapacity <= 0)
                {
                    return false;
                }

                if (ti.definition == null)
                {
                    Puts("Invalid shortname in loot table: {0} for {1}", ti.shortname, loot.BaseName);
                    return false;
                }

                int amountMin = ti.amountMin;
                int amountMax = ti.amount;
                if (loot.Options.Multiplier != 1f || loot.Options.MultiplierPVE != 1f || loot.Options.MultiplierPVP != 1f)
                {
                    float multiplier = !loot.AllowPVP && loot.UserID.HasPermission("raidablebases.buyable.vip.pve") ? loot.Options.MultiplierPVE :
                                       loot.AllowPVP && loot.UserID.HasPermission("raidablebases.buyable.vip.pvp") ? loot.Options.MultiplierPVP :
                                       loot.Options.Multiplier;

                    if (amountMax > 1)
                    {
                        double scaledMax = Math.Ceiling(amountMax * (double)multiplier);
                        double scaledMin = Math.Ceiling(amountMin * (double)multiplier);

                        amountMax = (int)Math.Min(int.MaxValue - 1d, Math.Max(0d, scaledMax));
                        amountMin = (int)Math.Min(amountMax, Math.Max(0d, scaledMin));
                    }
                }

                int amount = amountMin < amountMax ? Core.Random.Range(amountMin, amountMax + 1) : amountMax;
                if (amount <= 0)
                {
                    return false;
                }

                ulong skin = ti.skin;
                if (string.Equals(ti.shortname, "paper", StringComparison.OrdinalIgnoreCase))
                {
                    skin = GRIMM_PAPER_SKIN;
                }
                else if (loot.config.BlockPaidContent && loot.Instance.RequiresOwnership(ti.definition, skin))
                {
                    skin = 0;
                }

                int maxStack = loot.Unique.Stacks ? ti.stacksize > 0 ? ti.stacksize : ti.definition.stackable : ti.stacksize;
                int totalStacks = maxStack > 0 ? ((amount - 1) / maxStack) + 1 : 1;
                int stackCount = Math.Min(totalStacks, remainingCapacity);

                if (loot.Options.Dynamic && stackCount > 1)
                {
                    loot.Amount = Math.Min(loot.Capacity, loot.Amount + stackCount - 1);
                }

                for (int i = 0; i < stackCount; i++)
                {
                    int stack = maxStack > 0 ? Math.Min(amount, maxStack) : amount;

                    amount -= stack;

                    loot.Tables.Add(new(ti.shortname, stack, stack, skin, ti.isBlueprint, ti.probability, ti.stacksize, ti.name, ti.text, hasPriority || ti.hasPriority, ti.slots) { isSplit = totalStacks > 1 });
                }

                return true;
            }

            private List<string> BuildingMaterials = new()
            {
                "hq.metal.ore", "metal.refined", "metal.fragments", "metal.ore", "stones", "sulfur.ore", "sulfur", "wood"
            };

            private void ConvertVanillaPaperToGrimmCoin()
            {
                foreach (var container in _allcontainers)
                {
                    if (container.IsKilled() || container.inventory == null) continue;
                    ConvertPaperInInventory(container.inventory);
                }
            }

            private static void ConvertPaperInInventory(ItemContainer inventory)
            {
                if (inventory?.itemList == null) return;
                var list = inventory.itemList;
                for (int i = 0; i < list.Count; i++)
                {
                    Item item = list[i];
                    if (item?.info == null || item.info.shortname != "paper") continue;
                    if (item.skin == GRIMM_PAPER_SKIN) continue;
                    item.skin = GRIMM_PAPER_SKIN;
                    item.MarkDirty();
                }
            }

            private Item CreateItem(LootItem ti, int amount)
            {
                if (amount <= 0 || ti.definition == null)
                {
                    return null;
                }

                Item item;
                if (ti.isBlueprint && ti.definition.Blueprint != null)
                {
                    item = ItemManager.Create(Workbench.GetBlueprintTemplate());
                    item.blueprintTarget = ti.definition.itemid;
                    item.amount = amount;
                }
                else
                {
                    bool isGrimmPaper = string.Equals(ti.shortname, "paper", StringComparison.OrdinalIgnoreCase);
                    ulong skin = isGrimmPaper
                        ? GRIMM_PAPER_SKIN
                        : GetItemSkin(ti.definition, SkinType.Loot, ti.skin, config.Skins.Loot.Stackable, config.Skins.Loot.NonStackable, config.Skins.Loot.Random, config.Skins.Loot.Workshop, config.Skins.Loot.ImportedWorkshop, config.Skins.Loot.ApprovedOnly, ti.definition.stackable);
                    item = ItemManager.Create(ti.definition, amount, skin);
                    if (item != null)
                        item.skin = skin;
                }

                if (!string.IsNullOrWhiteSpace(ti.name))
                {
                    item.name = ti.name;
                }

                if (!string.IsNullOrWhiteSpace(ti.text) && !BuildingMaterials.Contains(ti.shortname))
                {
                    item.text = ti.text;
                }

                var e = item.GetHeldEntity();

                if (e.IsValid())
                {
                    e.skinID = item.skin;
                    e.SendNetworkUpdate();
                }

                if (ti.slots != null)
                {
                    ti.slots.TryAdd(item);
                }

                item.MarkDirty();

                return item;
            }

            public static void GenerateLoot(RaidableBases Instance, IPlayer user, string mode, string[] args)
            {
                BaseProfile profile = args.Length >= 2 && Instance.Get(args[1], out var val) ? val.Item2 : null;

                if (profile == null)
                {
                    Instance.Reply(user, "Difficulty Not Available", mode);
                    return;
                }

                int amount = args.Length >= 3 && int.TryParse(args[2], out int val2) ? val2 : 0;

                LootProfile loot = new()
                {
                    Amount = amount != 0 ? amount : profile.Options.GetLootAmount(RaidableType.None),
                    BaseName = profile.Options.AdditionalBases.GetRandom().Key,
                    AllowPVP = profile.Options.AllowPVP,
                    Unique = Instance.config.Loot,
                    Options = profile.Options,
                    Instance = Instance
                };

                TakeLootFromLootTables(loot);

                string text = string.Format("{0} ({1} selected, {2} expected): {3}", mode, loot.Tables.Count, loot.Amount, string.Join(", ", loot.Tables.Select(ti => $"{ti.shortname} ({ti.amount})")));

                Instance.LogToFile("items", text, Instance, false, true);

                Puts(text);
            }

            private void SetupSellOrders()
            {
                if (!config.Settings.Management.Inherit.Exists("vendingmachine".Contains))
                {
                    return;
                }

                vms.RemoveAll(IsContainerKilled);

                foreach (var vm in vms)
                {
                    vm.InstallDefaultSellOrders();
                    vm.SetFlagLocal(BaseEntity.Flags.Reserved4, config.Settings.Management.AllowBroadcasting);
                    foreach (Item item in vm.inventory.itemList)
                    {
                        if (vm.sellOrders.sellOrders.Count < 8)
                        {
                            ItemDefinition itemToSellDef = ItemManager.FindItemDefinition(item.info.itemid);
                            ItemDefinition currencyDef = ItemManager.FindItemDefinition(-932201673);

                            if (!(itemToSellDef == null) && !(currencyDef == null))
                            {
                                int itemToSellAmount = Mathf.Clamp(item.amount, 1, itemToSellDef.stackable);

                                vm.sellOrders.sellOrders.Add(new()
                                {
                                    ShouldPool = false,
                                    itemToSellID = item.info.itemid,
                                    itemToSellAmount = itemToSellAmount,
                                    currencyID = -932201673,
                                    currencyAmountPerItem = 999999,
                                    currencyIsBP = true,
                                    itemToSellIsBP = item.IsBlueprint()
                                });

                                vm.RefreshSellOrderStockLevel(itemToSellDef);
                            }
                        }
                    }

                    vm.FullUpdate();
                }
            }

            private void SetupMovableContainers()
            {
                fridges.RemoveAll(IsContainerKilled);
                if (fridges.Count > 1)
                {
                    Shuffle(fridges);
                }

                ovens.RemoveAll(IsContainerKilled);
                if (ovens.Count > 1)
                {
                    Shuffle(ovens);
                }

                lockers.RemoveAll(IsContainerKilled);
                if (lockers.Count > 1 && !config.Settings.Management.DivideLockerLoot)
                {
                    Shuffle(lockers);
                }

                weaponRacks.RemoveAll(IsContainerKilled);
                if (weaponRacks.Count > 1)
                {
                    weaponRacks.Sort((a, b) => a.inventory.itemList.Count.CompareTo(b.inventory.itemList.Count));
                }
            }

            private bool MoveFood(Item item)
            {
                if (!config.Settings.Management.Food || fridges.Count == 0 || item.info.category != ItemCategory.Food || config.Settings.Management.Foods.Any(item.info.shortname))
                {
                    return false;
                }

                for (int i = 0; i < fridges.Count; i++)
                {
                    if (item.MoveToContainer(fridges[i].inventory, -1, true))
                    {
                        return true;
                    }
                }

                return false;
            }

            private int weaponsInBox, weaponsOnRack;
            private bool MoveToRack(Item item)
            {
#if OXIDE_PUBLICIZED || CARBON
                if (config.Settings.Management.DivideRackLoot && weaponsOnRack >= weaponsInBox || item.info.category != ItemCategory.Weapon || weaponRacks.Count <= 0)
                {
                    return false;
                }
                WeaponRack rack = weaponRacks[0];
                WorldModelRackMountConfig conf = WorldModelRackMountConfig.GetForItemDef(item.info);
                if (conf == null || !rack.CanAcceptWeaponType(conf))
                {
                    return false;
                }
                BasePlayer target = BasePlayer.bots.FirstOrDefault(bot => bot != null);
                if (target == null)
                {
                    return false;
                }
                for (int y = 0; y < rack.GridCellCountY; y++)
                {
                    for (int x = 0; x < rack.GridCellCountX; x++)
                    {
                        Vector2Int position = new(x, y);
                        int gridCellIndex = rack.GetBestPlacementCellIndex(position, conf, rotation: 0, ignoreSlot: null);
                        if (gridCellIndex != -1 && rack.GetWeaponAtIndex(gridCellIndex) == null && rack.MountWeapon(item, target, gridCellIndex, 0, true))
                        {
                            weaponsOnRack++;
                            return true;
                        }
                    }
                }
#endif
                return false;
            }

            private bool MoveToBBQ(Item item)
            {
                if (!config.Settings.Management.Food || ovens.Count == 0 || item.info.category != ItemCategory.Food || !IsCookable(item.info) || config.Settings.Management.Foods.Exists(item.info.shortname.Contains))
                {
                    return false;
                }

                for (int i = 0; i < ovens.Count; i++)
                {
                    BaseOven oven = ovens[i];
                    if (oven.ShortPrefabName.Contains("bbq") && item.MoveToContainer(oven.inventory, -1, true))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool MoveToCupboard(Item item)
            {
                if (!config.Settings.Management.Cupboard || !privSpawned || item.info.category != ItemCategory.Resources || config.Loot.ExcludeFromCupboard.Contains(item.info.shortname))
                {
                    return false;
                }

                if (config.Settings.Management.Cook && ovens.Count > 0 && item.info.shortname.Equals("crude.oil") && SplitIntoFurnaces(ovens, item))
                {
                    return true;
                }

                if (config.Settings.Management.Cook && item.info.shortname.EndsWith(".ore") && MoveToOven(item))
                {
                    return true;
                }

                if (!priv.IsKilled() && item.MoveToContainer(priv.inventory, -1, true))
                {
                    privHadLoot = true;
                    return true;
                }

                return false;
            }

            private bool IsCookable(ItemDefinition def)
            {
                if (def.shortname.EndsWith(".cooked") || def.shortname.EndsWith(".burned") || def.shortname.EndsWith(".spoiled") || def.shortname == "lowgradefuel")
                {
                    return false;
                }

                return def.shortname == "wood" || def.shortname == "crude.oil" || Instance.IsCookableItemDefinition(def);
            }

            private bool MoveToOven(Item item)
            {
                if (!config.Settings.Management.Cook || ovens.Count == 0 || !IsCookable(item.info))
                {
                    return false;
                }

                if ((item.info.shortname.EndsWith(".ore") || item.info.shortname.Equals("crude.oil")) && item.skin == 0 && SplitIntoFurnaces(ovens, item))
                {
                    return true;
                }

                foreach (var oven in ovens)
                {
                    if (oven.ShortPrefabName.Contains("bbq") ||
                        (item.info.shortname == "crude.oil" && !oven.IsMaterialInput(item)) ||
                        (item.info.shortname.EndsWith(".ore") && !oven.IsMaterialInput(item)) ||
                        (item.info.shortname == "lowgradefuel" && !oven.IsBurnableItem(item))) continue;

                    if (item.MoveToContainer(oven.inventory, -1, true))
                    {
                        if (!oven.IsOn() && oven.FindBurnable() != null)
                        {
                            using (var update = oven.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                            {
                                update.Set(BaseEntity.Flags.On, true);
                            }
                        }

                        if (oven.IsOn() && !item.HasFlag(global::Item.Flag.OnFire))
                        {
                            item.SetFlag(global::Item.Flag.OnFire, true);
                            item.MarkDirty();
                        }

                        return true;
                    }
                }

                return false;
            }

            private bool SplitIntoFurnaces(List<BaseOven> ovens, Item item)
            {
                using var furnaces = DisposableList<(BaseOven, int)>();
                foreach (var oven in ovens)
                {
                    int position = -1;
                    try { position = oven.GetIdealSlot(null, null, item); } catch { }
                    if (position != -1) furnaces.Add(new(oven, position));
                }
                if (item.amount <= 0 || furnaces.Count == 0)
                {
                    return false;
                }
                int remainingFurnaces = furnaces.Count;
                foreach (var (furnace, position) in furnaces)
                {
                    int amount = Mathf.CeilToInt(item.amount / (float)remainingFurnaces--);
                    Item target = amount < item.amount ? item.SplitItem(amount) : item;
                    if (target == null)
                    {
                        return false;
                    }
                    if (!target.MoveToContainer(furnace.inventory, position, true, true))
                    {
                        if (target != item)
                        {
                            item.amount += target.amount;
                            item.MarkDirty();
                            target.Remove();
                        }
                        return false;
                    }
                    if (furnace is ElectricOven eo && eo.spawnedIo.Get(true) is IOEntity io && !io.IsPowered())
                    {
                        io.Invoke(() =>
                        {
                            io.UpdateHasPower(25, 0);
                            eo.StartCooking();
                        }, 0.1f);
                    }
                    if (config.Weapons.Furnace > 0 && furnace.fuelType != null && !(furnace is ElectricOven) && furnace.inventory.GetSlot(0) == null)
                    {
                        ItemManager.Create(furnace.fuelType, config.Weapons.Furnace)?.MoveToContainer(furnace.inventory, 0);

                        if (!BaseOven.cookQueue.Contains(furnace))
                        {
                            furnace.Invoke(furnace.StartCooking, 0.2f);
                        }
                    }
                    if (target == item)
                    {
                        break;
                    }
                }
                return true;
            }

            private bool IsLockerItem(ItemDefinition def)
            {
                if (def.shortname.Contains("explosive") || def.shortname.Contains("rocket"))
                {
                    return false;
                }
                if (config.Settings.Management.Food && def.category == ItemCategory.Food && !config.Settings.Management.Foods.Exists(def.shortname.Contains))
                {
                    return fridges.Count == 0;
                }
                return def.category == ItemCategory.Attire || def.category == ItemCategory.Ammunition || def.category == ItemCategory.Medical || def.category == ItemCategory.Weapon;
            }

            private bool MoveToLocker(Item item)
            {
                if (!config.Settings.Management.Lockers || lockers.Count == 0 || !IsLockerItem(item.info))
                {
                    return false;
                }

                if (config.Settings.Management.DivideLockerLoot)
                {
                    if (itemAmountSpawned % _containers.Count != 0)
                    {
                        return false;
                    }

                    lockers.Sort((a, b) => a.inventory.itemList.Count.CompareTo(b.inventory.itemList.Count));
                }

                foreach (var locker in lockers)
                {
                    if (MoveToLocker(item, locker))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool MoveToLocker(Item item, Locker locker)
            {
                try
                {
                    int position = locker.GetIdealSlot(null, null, item);

                    if (position != int.MinValue)
                    {
                        return item.MoveToContainer(locker.inventory, position, true);
                    }
                }
                catch { }

                return false;
            }

            private void CheckExpansionSettings()
            {
                if (!config.Settings.ExpansionMode || !Instance.DangerousTreasures.CanCall())
                {
                    return;
                }
                var containers = _containers.Where(x => x.ShortPrefabName == "box.wooden.large");
                if (containers.Count > 0)
                {
                    Instance.DangerousTreasures?.Call("API_SetContainer", containers.GetRandom(), M_RADIUS, !Options.NPC.Enabled || Options.NPC.UseExpansionNpcs, Options.Level);
                }
            }

            private bool ToggleNpcMinerHat(HumanoidNPC npc, bool state)
            {
                if (npc.IsKilled() || npc.inventory == null || npc.IsDead())
                {
                    return false;
                }

                var slot = npc.inventory.FindItemByItemName("hat.miner");

                if (slot == null)
                {
                    return false;
                }

                if (state && slot.contents != null)
                {
                    slot.contents.AddItem(ItemManager.FindItemDefinition("lowgradefuel"), 50);
                }

                slot.SwitchOnOff(state);
                npc.inventory.ServerUpdate(0f);
                return true;
            }

            public void ToggleLights()
            {
                bool state = config.Settings.Management.AlwaysLights || TOD_Sky.Instance?.IsDay == false;

                if (lights?.Count > 0)
                {
                    foreach (var io in lights)
                    {
                        if (io.IsKilled() || io.HasConnections()) continue;

                        int inputAmount = state ? Math.Max(35, io.ConsumptionAmount()) : 0;
                        bool updatedInput = false;
                        var inputs = io.inputs;

                        if (io is StrobeLight strobeLight && state)
                        {
                            strobeLight.lifeTimeSeconds = float.MaxValue;
                        }

                        if (inputs != null)
                        {
                            for (int inputSlot = 0; inputSlot < inputs.Length; inputSlot++)
                            {
                                var input = inputs[inputSlot];
                                if (input == null || !input.mainPowerSlot) continue;
                                io.UpdateFromInput(inputAmount, inputSlot);
                                updatedInput = true;
                            }

                            if (!updatedInput)
                            {
                                for (int inputSlot = 0; inputSlot < inputs.Length; inputSlot++)
                                {
                                    if (inputs[inputSlot] == null) continue;
                                    io.UpdateFromInput(inputAmount, inputSlot);
                                    updatedInput = true;
                                    break;
                                }
                            }
                        }

                        if (!updatedInput)
                        {
                            io.currentEnergy = inputAmount;
                            io.UpdateHasPower(inputAmount, 0);
                            io.IOStateChanged(inputAmount, 0);
                        }

                        using var update = io.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.On, state);
                    }
                }

                if (ovens?.Count > 0)
                {
                    foreach (var oven in ovens)
                    {
                        if (oven.IsKilled()) continue;
                        if (state && oven.ShortPrefabName.Contains("furnace") && oven.inventory.IsEmpty()) continue;
                        if (!state && oven.ShortPrefabName.Contains("furnace") && BaseOven.cookQueue.Contains(oven)) continue;
                        if (config.Settings.Management.IgnoredLights.Exists(oven.ShortPrefabName.Contains)) continue;

                        using var update = oven.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.On, state);
                    }
                }

                if (npcs?.Count > 0)
                {
                    foreach (var npc in npcs)
                    {
                        if (npc.IsKilled()) continue;
                        ToggleNpcMinerHat(npc, state);
                    }
                }
            }

            public void Undo()
            {
                if (IsOpened)
                {
                    IsOpened = false;

                    if (DespawnMinutes > 0f)
                    {
                        UpdateDespawnDateTime(DespawnMinutes);

                        if (config.EventMessages.ShowWarning)
                        {
                            foreach (var target in BasePlayer.activePlayerList)
                            {
                                if (!IsRaider(target) && target.HasPermission("raidablebases.limitedannouncements")) continue;
                                NotifyUnlessSmart(target, "DestroyingBaseAt", FormatGridReference(target, Location), DespawnMinutes);
                            }
                        }
                    }
                    else
                    {
                        Despawn();
                    }
                }
            }

            public bool Any(ulong userid, bool checkAllies = true)
            {
                if (ownerId != 0 && ownerId == userid) return true;
                if (!raiders.TryGetValue(userid, out var ri)) return false;
                return ri.IsParticipant || checkAllies && ri.IsAlly;
            }

            public ulong GetItemSkin(ItemDefinition def, SkinType skinType, ulong defaultSkin, bool stackable, bool nonstackable, bool random, bool workshop, bool importedworkshop, bool approved, int stacksize)
            {
                ulong skin = defaultSkin;

                if (def.shortname != "explosive.satchel" && def.shortname != "grenade.f1" && skin == 0uL)
                {
                    if (stackable && stacksize > 1 && _shortnameToSkin.TryGetValue(def.shortname, out var dict) && dict.TryGetValue(skinType, out var skin2))
                    {
                        return skin2;
                    }

                    if (nonstackable && stacksize == 1 && _shortnameToSkin.TryGetValue(def.shortname, out var dict2) && dict2.TryGetValue(skinType, out var skin3))
                    {
                        return skin3;
                    }

                    var si = GetItemSkins(def, approved);
                    if (TrySelectItemSkin(si, random, workshop, importedworkshop, out ulong skin4))
                    {
                        if (!_shortnameToSkin.TryGetValue(def.shortname, out dict))
                        {
                            _shortnameToSkin[def.shortname] = dict = new();
                        }

                        dict[skinType] = skin = skin4;
                    }
                }

                return skin;
            }

            public SkinInfo GetItemSkins(ItemDefinition def, bool approvedOnly)
            {
                if (!Instance.Skins.TryGetValue(def.shortname, out var si))
                {
                    Instance.Skins[def.shortname] = si = new();

                    if (!config.BlockPaidContent && !def.skins.IsNullOrEmpty())
                    {
                        foreach (var skin in def.skins)
                        {
                            if (IsBlacklistedSkin(def, skin.id))
                            {
                                continue;
                            }
                            var id = Convert.ToUInt64(skin.id);
                            si.skins.Add(id);
                            si.allSkins.Add(id);
                        }
                    }

                    if (Instance.ImportedWorkshopSkins.SkinList.TryGetValue(def.shortname, out var value) && !value.IsNullOrEmpty())
                    {
                        foreach (var skin in value)
                        {
                            if (IsBlacklistedSkin(def, (int)skin))
                            {
                                continue;
                            }
                            if (approvedOnly && !IsApproved(def, skin))
                            {
                                continue;
                            }
                            si.importedWorkshopSkins.Add(skin);
                            si.allSkins.Add(skin);
                        }
                    }

                    var sp = Instance.skinsPlugin.Skins.FindAll(x => x.Shortname == def.shortname);
                    if (sp != null && sp.Count != 0)
                    {
                        foreach (var item in sp)
                        {
                            foreach (var skin in item.Skins)
                            {
                                if (IsBlacklistedSkin(def, (int)skin))
                                {
                                    continue;
                                }
                                if (approvedOnly && !IsApproved(def, skin))
                                {
                                    continue;
                                }
                                si.importedWorkshopSkins.Add(skin);
                                si.allSkins.Add(skin);
                            }
                        }
                    }

                    if (!config.BlockPaidContent && !def.skins2.IsNullOrEmpty())
                    {
                        foreach (var skin in def.skins2)
                        {
                            if (skin == null || IsBlacklistedSkin(def, (int)skin.WorkshopId))
                            {
                                continue;
                            }
                            if (!si.workshopSkins.Contains(skin.WorkshopId))
                            {
                                si.workshopSkins.Add(skin.WorkshopId);
                                si.allSkins.Add(skin.WorkshopId);
                            }
                        }
                    }
                }

                return si;
            }

            private bool IsBlacklistedSkin(ItemDefinition def, int num)
            {
                var skinId = ItemDefinition.FindSkin(def.isRedirectOf?.itemid ?? def.itemid, num);
                var dirSkin = def.isRedirectOf == null ? def.skins.FirstOrDefault(x => (ulong)x.id == skinId) : def.isRedirectOf.skins.FirstOrDefault(x => (ulong)x.id == skinId);
                var itemSkin = (dirSkin.id == 0) ? null : (dirSkin.invItem as ItemSkin);
                return itemSkin?.Redirect != null || def.isRedirectOf != null;
            }

            private bool IsApproved(ItemDefinition def, ulong skin)
            {
                if (def.skins != null && Array.Exists(def.skins, x => (ulong)x.id == skin)) return true;
                if (def.skins2 != null && Array.Exists(def.skins2, x => x.WorkshopId == skin)) return true;
                return false;
            }

            private static bool TrySelectItemSkin(SkinInfo si, bool random, bool workshop, bool importedWorkshop, out ulong skin)
            {
                List<ulong> selected = null;
                int count = 0;

                if (random && si.skins.Count > 0 && Core.Random.Range(0, ++count) == 0)
                {
                    selected = si.skins;
                }

                if (workshop && si.workshopSkins.Count > 0 && Core.Random.Range(0, ++count) == 0)
                {
                    selected = si.workshopSkins;
                }

                if (importedWorkshop && si.importedWorkshopSkins.Count > 0 && Core.Random.Range(0, ++count) == 0)
                {
                    selected = si.importedWorkshopSkins;
                }

                if (selected == null)
                {
                    skin = 0uL;
                    return false;
                }

                skin = selected.GetRandom();
                return true;
            }

            private bool SetItemSkin(List<ulong> skins, SkinInfo si, BaseEntity entity, bool unique)
            {
                ulong selectedSkin = 0;
                int validCount = 0;
                foreach (ulong skin in skins)
                {
                    if (!si.allSkins.Contains(skin)) continue;
                    if (Core.Random.Range(0, ++validCount) == 0) selectedSkin = skin;
                }
                if (validCount == 0)
                {
                    return false;
                }
                if (unique)
                {
                    _prefabToSkin[entity.prefabID] = selectedSkin;
                }
                entity.skinID = selectedSkin;
                entity.SendNetworkUpdate();
                return true;
            }

            private struct LookupTime
            {
                public bool Result;
                public long ExpiresAt;
                public LookupTime(bool result, long expiresAt)
                {
                    Result = result;
                    ExpiresAt = expiresAt;
                }
            }

            private Dictionary<(ulong first, ulong second), LookupTime> allyLookupTimes = new();
            private bool IsAlly(ulong first, ulong second, bool clans = true, bool friends = true, bool teams = true, string clanhook = "IsMemberOrAlly", long length = 1, int maxCount = 10)
            {
                if (!clans && !friends && !teams)
                    return false;

                var now = Stopwatch.GetTimestamp();
                var t = first < second ? (first, second) : (second, first);
                if (allyLookupTimes.TryGetValue(t, out var lookup) && lookup.ExpiresAt > now) return lookup.Result;
                if (allyLookupTimes.Count >= maxCount) allyLookupTimes.Clear();

                bool result = first switch
                {
                    _ when teams && RelationshipManager.ServerInstance.playerToTeam.TryGetValue(first, out var team) && team.members.Contains(second) => true,
                    _ when clans && Instance.Clans != null && Instance.Clans?.Call(clanhook, first, second) is true => true,
                    _ when friends && Instance.Friends != null && Instance.Friends?.Call("AreFriends", first, second) is true => true,
                    _ => false
                };

                allyLookupTimes[t] = new(result, now + Stopwatch.Frequency * length);
                return result;
            }

            public bool IsAlly(BasePlayer a, ulong b, bool clans = true, bool friends = true, bool teams = true, string arg = "IsMemberOrAlly")
            {
                if (clans && a.clanId != 0 && TryGetClan(a, out IClan clan))
                {
                    if (clan.Creator == b)
                    {
                        return true;
                    }
                    foreach (var member in clan.Members)
                    {
                        if (member.SteamId == b)
                        {
                            return true;
                        }
                    }
                }
                return IsAlly(a.userID, b, clans, friends, teams, arg);
            }

            public bool IsAlly(Raider raider, ulong targetId, bool clans = true, bool friends = true, bool teams = true, string arg = "IsMemberOrAlly")
            {
                if (ownerId == targetId && raider.IsAlly) return true;
                if (clans && raider.IsInRustClanWith(targetId)) return true;
                BasePlayer player = raider.player ?? RelationshipManager.FindByID(raider.userid);
                if (player != null) return IsAlly(player, targetId, clans, friends, teams, arg);
                return IsAlly(raider.userid, targetId, clans, friends, teams, arg);
            }

            public bool IsAlly(BasePlayer a, BasePlayer b, bool clans = true, bool friends = true, bool teams = true, string arg = "IsMemberOrAlly")
            {
                if (clans && TryGetClan(a, out IClan clan) && clan.ClanId == b.clanId) return true;
                return IsAlly(a, b.userID, clans, friends, teams, arg);
            }

            public bool IsAlly(BasePlayer player)
            {
                if (!ownerId.IsSteamId() || player.userID == ownerId || CanBypass(player))
                {
                    return true;
                }
                Raider ri = GetRaider(player);
                if (!ri.IsAlly)
                {
                    ri.IsAlly = IsAlly(player, ownerId);
                }
                return ri.IsAlly;
            }

            public bool IsEcoTool(BasePlayer attacker, HitInfo info)
            {
                if (info.WeaponPrefab is TimedExplosive)
                {
                    return false;
                }
                HeldEntity heldEntity = attacker.GetHeldEntity();
                if (heldEntity is BaseProjectile)
                {
                    if (info.WeaponPrefab is BowWeapon || heldEntity is BowWeapon)
                    {
                        return Options.Eco.Bows;
                    }
                    return false;
                }
                if (info.WeaponPrefab is FlameThrower || info.damageTypes.Has(DamageType.Heat))
                {
                    return Options.Eco.FlameThrowers;
                }
                return info.damageTypes.IsMeleeType() || info.WeaponPrefab is BaseMelee;
            }

            private bool IsAcceptedItem(string shortname)
            {
                if (string.IsNullOrWhiteSpace(shortname)) return false;
                foreach (string value in Options.AllowedWeaponAndAmmoShortnames)
                {
                    if (string.Equals(value, shortname, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool AreAcceptedItems(HashSet<string> shortnames)
            {
                if (shortnames == null || shortnames.Count == 0)
                {
                    return true;
                }
                foreach (string shortname in shortnames)
                {
                    if (!IsAcceptedItem(shortname))
                    {
                        return false;
                    }
                }
                return true;
            }

            private bool IsWeaponEntityAllowedByAcceptedItems(BaseEntity entity, out bool identified)
            {
                identified = false;
                if (entity == null)
                {
                    return true;
                }
                Item item = (entity as HeldEntity)?.GetCachedItem();
                if (item?.info != null && Instance.IsWeaponItemDefinition(item.info))
                {
                    identified = true;
                    return IsAcceptedItem(item.info.shortname);
                }
                if (Instance.WeaponEntityToShortnames.TryGetValue(entity.ShortPrefabName, out var shortnames))
                {
                    identified = true;
                    return AreAcceptedItems(shortnames);
                }
                return true;
            }

            public bool IsWeaponAllowedByAcceptedItems(BasePlayer attacker, HitInfo info)
            {
                if (!Options.RestrictByAcceptedItems())
                {
                    return true;
                }

                if (!IsWeaponEntityAllowedByAcceptedItems(info.Weapon, out bool weaponIdentified))
                {
                    return false;
                }

                if (!weaponIdentified && !IsWeaponEntityAllowedByAcceptedItems(info.WeaponPrefab, out _))
                {
                    return false;
                }

                if (info.WeaponPrefab != null && Instance.CatapultProjectilePrefabs.Contains(info.WeaponPrefab.ShortPrefabName) && !AreAcceptedItems(Instance.CatapultWeaponShortnames))
                {
                    return false;
                }

                if (attacker != null && info.ProjectileID != 0 && attacker.firedProjectiles.TryGetValue(info.ProjectileID, out var val) && val.itemDef != null)
                {
                    return !Instance.IsAmmoItemDefinition(val.itemDef) || IsAcceptedItem(val.itemDef.shortname);
                }

                if (info.ProjectilePrefab != null && Instance.ProjectileToShortnames.TryGetValue(info.ProjectilePrefab, out var shortnames))
                {
                    return AreAcceptedItems(shortnames);
                }

                if (info.WeaponPrefab != null && Instance.ProjectileEntityToShortnames.TryGetValue(info.WeaponPrefab.ShortPrefabName, out shortnames))
                {
                    return AreAcceptedItems(shortnames);
                }

                return true;
            }

            public bool IsWeaponAllowedByAcceptedItems(Item item)
            {
                if (!Options.RestrictByAcceptedItems() || item?.info == null || !Instance.IsWeaponItemDefinition(item.info))
                {
                    return true;
                }

                if (!IsAcceptedItem(item.info.shortname))
                {
                    return false;
                }

                if (item.GetHeldEntity() is BaseProjectile projectile && projectile.primaryMagazine?.ammoType is ItemDefinition ammoDefinition)
                {
                    return IsAcceptedItem(ammoDefinition.shortname);
                }

                return true;
            }

            private bool IsWeaponEntityAllowedByWorkbenchLevel(BaseEntity entity, out bool identified)
            {
                identified = false;

                if (entity == null)
                {
                    return true;
                }

                Item item = (entity as HeldEntity)?.GetCachedItem();
                if (item?.info != null && Instance.DefinitionToWorkbenchLevel.TryGetValue(item.info, out int itemLevel))
                {
                    identified = true;
                    return itemLevel <= Options.WorkbenchLevel;
                }

                if (Instance.AttackEntityToWorkbenchLevel.TryGetValue(entity.ShortPrefabName, out int entityLevel))
                {
                    identified = true;
                    return entityLevel <= Options.WorkbenchLevel;
                }

                return true;
            }

            public bool IsWeaponAllowedByWorkbenchLevel(BasePlayer attacker, HitInfo info)
            {
                if (info.Weapon == null && info.WeaponPrefab == null && info.ProjectilePrefab == null && (attacker == null || info.ProjectileID == 0))
                {
                    return true;
                }

                if (Options.CheckWeaponWorkbenchLevel())
                {
                    if (!IsWeaponEntityAllowedByWorkbenchLevel(info.Weapon, out bool weaponIdentified))
                    {
                        return false;
                    }

                    if (!weaponIdentified && !IsWeaponEntityAllowedByWorkbenchLevel(info.WeaponPrefab, out _))
                    {
                        return false;
                    }

                    if (info.WeaponPrefab != null && Instance.CatapultProjectilePrefabs.Contains(info.WeaponPrefab.ShortPrefabName) && Instance.CatapultWorkbenchLevel > Options.WorkbenchLevel)
                    {
                        return false;
                    }

                }

                if (!Options.CheckAmmoWorkbenchLevel())
                {
                    return true;
                }

                if (attacker != null && attacker.firedProjectiles.TryGetValue(info.ProjectileID, out var val) && val.itemDef != null && Instance.DefinitionToWorkbenchLevel.TryGetValue(val.itemDef, out int workbenchLevel))
                {
                    return workbenchLevel <= Options.WorkbenchLevel;
                }

                if (info.ProjectilePrefab != null && Instance.ProjectileToWorkbenchLevel.TryGetValue(info.ProjectilePrefab, out workbenchLevel) && workbenchLevel > Options.WorkbenchLevel)
                {
                    return false;
                }

                if (info.WeaponPrefab != null && Instance.ProjectileEntityToWorkbenchLevel.TryGetValue(info.WeaponPrefab.ShortPrefabName, out workbenchLevel) && workbenchLevel > Options.WorkbenchLevel)
                {
                    return false;
                }

                return true;
            }

            public bool IsWeaponAllowedByWorkbenchLevel(Item item)
            {
                int requiredLevel = 0;

                if (Options.CheckWeaponWorkbenchLevel() && Instance.DefinitionToWorkbenchLevel.TryGetValue(item.info, out int workbenchLevel))
                {
                    requiredLevel = workbenchLevel;
                }

                if (Options.CheckAmmoWorkbenchLevel() && item.GetHeldEntity() is BaseProjectile projectile)
                {
                    ItemDefinition ammoDefinition = projectile.primaryMagazine?.ammoType;

                    if (ammoDefinition != null && Instance.DefinitionToWorkbenchLevel.TryGetValue(ammoDefinition, out workbenchLevel))
                    {
                        requiredLevel = Math.Max(requiredLevel, workbenchLevel);
                    }
                }

                return requiredLevel <= Options.WorkbenchLevel;
            }

            public void StopUsingWeapon(BasePlayer player)
            {
                if (!player.svActiveItemID.IsValid)
                {
                    return;
                }

                Item item = null;
                if (Options.RestrictByAcceptedItems())
                {
                    item = player.GetActiveItem();
                    if (item != null && item.info != null && !IsWeaponAllowedByAcceptedItems(item))
                    {
                        StopUsingWeapon(player, item, false);
                        Notify(player, "ItemNotAccepted");
                        return;
                    }
                }
                else if (Options.RestrictByWorkbenchLevel(Instance.MaxConsideredWorkbenchLevel))
                {
                    item = player.GetActiveItem();
                    if (item != null && item.info != null && !IsWeaponAllowedByWorkbenchLevel(item))
                    {
                        StopUsingWeapon(player, item, false);
                        Notify(player, "WorkbenchLevelReq", Options.WorkbenchLevel);
                        return;
                    }
                }

                if (config.Settings.BlockedWeapons.Count > 0)
                {
                    config.Settings.BlockedWeapons.ForEach(weapon =>
                    {
                        if (!string.IsNullOrWhiteSpace(weapon))
                        {
                            StopUsingWeapon(player, weapon);
                        }
                    });
                }

                if (Options.Siege.Only)
                {
                    item ??= player.GetActiveItem();

                    if (item != null && item.info != null && !item.info.IsAllowedInEra(EraRestriction.Default, Era.Primitive))
                    {
                        StopUsingWeapon(player, item);
                        return;
                    }
                }

                if ((config.Settings.NoWizardryPVP && AllowPVP || config.Settings.NoWizardryPVE && !AllowPVP) && Instance.Wizardry.CanCall())
                {
                    StopUsingWeapon(player, "knife.bone");
                }

                if ((config.Settings.NoArcheryPVP && AllowPVP || config.Settings.NoArcheryPVE && !AllowPVP) && Instance.Archery.CanCall())
                {
                    StopUsingWeapon(player, "bow.compound", "bow.hunting", "crossbow");
                }
            }

            public void StopUsingWeapon(BasePlayer player, params string[] weapons)
            {
                Item item = player.GetActiveItem();

                if (item == null || !weapons.Contains(item.info.shortname))
                {
                    return;
                }

                StopUsingWeapon(player, item);
            }

            public void StopUsingWeapon(BasePlayer player, Item item, bool message = true)
            {
                if (!player.inventory.GiveItem(item, ItemMoveModifier.None, null, GiveItemOptions.BackpackOverflow))
                {
                    item.DropAndTossUpwards(player.GetDropPosition() + player.transform.forward, 2f);
                    if (message) Notify(player, "TooPowerfulDrop");
                }
                else if (message) Notify(player, "TooPowerful");
            }

            public BackpackData AddBackpack(DroppedItemContainer container, ulong playerSteamID, BasePlayer player)
            {
                int index = backpacks.FindIndex(x => x.userid == playerSteamID);
                BackpackData backpack;

                if (index == -1)
                {
                    backpack = Pool.Get<BackpackData>();
                    if (player != null)
                    {
                        backpack._player = player;
                        backpack.userid = player.userID;
                    }
                    else backpack.userid = playerSteamID;
                    backpacks.Add(backpack);
                }
                else backpack = backpacks[index];

                if (!backpack.containers.Contains(container))
                {
                    backpack.containers.Add(container);
                }

                return backpack;
            }

            private void PrepareElevatorsForDespawn()
            {
                using var tmp = FindEntitiesOfType<BaseEntity>(Location, ProtectionRadius);
                foreach (var e in tmp)
                {
                    if ((e is PlayerCorpse || e is DroppedItemContainer) && e.HasParent())
                    {
                        e.SetParent(null, false, true);
                    }
                    ElevatorLift lift = e as ElevatorLift;
                    if (lift.IsKilled())
                    {
                        continue;
                    }
                    foreach (var ele in Elevators.Values)
                    {
                        var elevator = ele.Entity;

                        if (lift.owner == elevator || lift.GetParentEntity() == elevator ||
                            lift is ElevatorLiftPlayer &&
                            lift.owner == null &&
                            !lift.HasParent() &&
                            lift.OwnerID == 0 &&
                            !lift.enableSaving) // TODO: Need definitive fix.
                        //Quaternion.Angle(lift.transform.rotation, elevator.transform.rotation) <= 1f &&
                        //InRange(lift.transform.position, elevator.GetWorldSpaceFloorPosition(elevator.Floor), 0.5f))
                        {
                            Entities.Add(lift);
                            break;
                        }
                    }
                }
            }

            public bool EjectBackpack(BackpackData backpack, bool bypass)
            {
                if (backpack.IsEmpty)
                {
                    return true;
                }

                if (!bypass && (!ownerId.IsSteamId() || Any(backpack.userid) || IsAlly(backpack.userid, ownerId)))
                {
                    return false;
                }

                backpack.containers.RemoveAll(container =>
                {
                    if (!container.IsKilled())
                    {
                        EjectContainer(container, backpack.userid);
                    }

                    return true;
                });

                return true;
            }

            private void EjectBackpackNotice(BasePlayer player, Vector3 position)
            {
                if (player == null || !player.IsConnected)
                {
                    return;
                }
                if (player.IsDead() || player.IsSleeping())
                {
                    player.Invoke(() => EjectBackpackNotice(player, position), 1f);
                    return;
                }
                NotifyUnlessSmart(player, "EjectedYourCorpse");
                if (config.Settings.Management.DrawTime > 0)
                {
                    AdminCommand(player, () => DrawText(player, config.Settings.Management.DrawTime, Color.red, position, mx("YourCorpse", player.UserIDString)));
                }
            }

            private void EjectSleepers()
            {
                if (!config.Settings.Management.EjectSleepers || Type == RaidableType.None)
                {
                    return;
                }
                using var tmp = FindEntitiesOfType<BasePlayer>(Location, ProtectionRadius, Layers.Mask.Player_Server);
                foreach (var player in tmp)
                {
                    if (player.IsSleeping() && !player.IsBuildingAuthed())
                    {
                        RemovePlayer(player, Location, ProtectionRadius, Type);
                    }
                }
            }

            public Vector3 GetEjectLocation(Vector3 a, float distance, Vector3 target, float radius, bool towardsZero, bool setHeight)
            {
                Vector3 direction = towardsZero ? Vector3.zero.XZ3D() - target.XZ3D() : a.XZ3D() - target.XZ3D();

                if (direction.sqrMagnitude <= 0.001f)
                {
                    direction = a.XZ3D() - target.XZ3D();

                    if (direction.sqrMagnitude <= 0.001f)
                    {
                        direction = Vector3.forward;
                    }
                }

                direction.Normalize();

                Vector3 position = target + direction * (radius + distance);

                if (towardsZero)
                {
                    float step = Mathf.Max(distance, M_RADIUS);

                    for (int i = 0; i < 4 && WaterLevel.GetOverallWaterDepth(position, waves: false, volumes: false) > 0.5f; i++)
                    {
                        position += direction * step;
                    }
                }

                if (setHeight)
                {
                    Vector3 origin = position;
                    origin.y = Instance.MaxTerrainY + 48f;

                    if (Physics.Raycast(origin, Vector3.down, out var hit, Mathf.Infinity, targetMask2, QueryTriggerInteraction.Ignore))
                    {
                        position.y = Mathf.Max(hit.point.y, TerrainMeta.WaterMap.GetHeight(hit.point), WaterSystem.OceanLevel) + 0.75f;
                    }
                    else
                    {
                        position.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(position), TerrainMeta.WaterMap.GetHeight(position), WaterSystem.OceanLevel) + 0.75f;
                    }
                }

                return position;
            }

            public bool RemovePlayer(BasePlayer player, Vector3 a, float radius, RaidableType type, bool special = false, bool towardsZero = false, float distance = 10f)
            {
                if (player.IsNull() || !player.IsHuman() || type == RaidableType.None && !player.IsSleeping())
                {
                    return false;
                }

                bool jetpack = IsWearingJetpack(player);
                if (special || jetpack)
                {
                    if (player.GetMounted() is BaseMountable b)
                    {
                        b.DismountPlayer(player, true);
                    }
                    else player.DismountObject();
                }

                if (player.GetMounted() is BaseMountable m)
                {
                    using var players = GetMountedPlayers(m);
                    return EjectMountable(m, players, a, radius, jetpack);
                }

                var parent = player.GetParentEntity();
                if (parent != null && IsCustomEntity(parent))
                {
                    return Eject(parent, Location, ProtectionRadius + 15f, false);
                }

                var position = GetEjectLocation(player.transform.position, distance, a, radius, towardsZero, true);
                if (player.IsFlying)
                {
                    position.y = player.transform.position.y;
                }

                player.Teleport(position);
                player.SendNetworkUpdateImmediate();

                return true;
            }

            public void Teleport(BasePlayer player)
            {
                if (!Options.CustomSpawns.GetBuyableTeleportPosition(Location, out var position))
                {
                    position = GetEjectLocation(player.transform.position, 10f, Location, ProtectionRadius, false, true);
                }
                TeleportExceptions.Add(player.userID);
                player.Teleport(position);
                player.SendNetworkUpdateImmediate();
            }

            public void DismountAllPlayers(BaseMountable m)
            {
                using var targets = GetMountedPlayers(m);
                foreach (var target in targets)
                {
                    if (target.IsNull()) continue;

                    m.DismountPlayer(target, true);

                    target.EnsureDismounted();
                }
            }

            public PooledList<BasePlayer> GetMountedPlayers(HotAirBalloon m)
            {
                var players = FindEntitiesOfType<BasePlayer>(m.CenterPoint(), 1.75f, Layers.Mask.Player_Server);
                players.RemoveAll(player => !player.IsHuman() || player.GetParentEntity() != m);
                return players;
            }

            public static PooledList<BasePlayer> GetMountedPlayers(BaseMountable m)
            {
                BaseVehicle vehicle = m.HasParent() ? m.VehicleParent() : m as BaseVehicle;
                PooledList<BasePlayer> players = DisposableList<BasePlayer>();

                if (vehicle == null)
                {
                    BasePlayer player = m.GetMounted();
                    if (player != null)
                    {
                        players.Add(player);
                    }
                }
                else vehicle.GetMountedPlayers(players);

                players.RemoveAll(x => !x.IsHuman());
                return players;
            }

            public static bool AnyMounted(BaseMountable m)
            {
                BaseVehicle vehicle = m.HasParent() ? m.VehicleParent() : m as BaseVehicle;

                if (vehicle == null)
                {
                    return m.GetMounted() != null;
                }

                return vehicle.AnyMounted();
            }

            private bool CanEject(PooledList<BasePlayer> players)
            {
                foreach (var player in players)
                {
                    if (!intruders.Contains(player.userID) && CanEject(player))
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool CanEject(BasePlayer target)
            {
                if (target.IsNull() || target.userID == ownerId)
                {
                    return false;
                }

                if (CannotEnter(target, false))
                {
                    return true;
                }

                if (CanEjectEnemy() && !IsAlly(target))
                {
                    Notify(target, "OnPlayerEntryRejected");
                    return true;
                }

                return false;
            }

            public bool CanEjectEnemy()
            {
                if (IsPayLocked) return AllowPVP ? Options.EjectPurchasedPVP : Options.EjectPurchasedPVE;
                if (ownerId.IsSteamId()) return AllowPVP ? Options.EjectLockedPVP : Options.EjectLockedPVE;
                return false;
            }

            private bool CannotEnter(BasePlayer target, bool justEntered)
            {
                bool special = false;

                if (GetRaider(target).IsAllowed)
                {
                    if (IsBanned(target))
                    {
                        return RemovePlayer(target, Location, ProtectionRadius, Type);
                    }
                }
                else if (Exceeds(target) || HasLockout(target) || IsBanned(target) || IsHogging(target) || (special = justEntered && Teleported(target)))
                {
                    return RemovePlayer(target, Location, ProtectionRadius, Type, special);
                }

                return false;
            }

            public bool IsControlledMount(BaseEntity m)
            {
                if (Options.Mounts.ControlledMounts)
                {
                    return false;
                }

                if (m is BaseChair chair)
                {
                    bool legacy = chair.legacyDismount;
                    chair.legacyDismount = true;
                    DismountAllPlayers(chair);
                    chair.legacyDismount = legacy;
                    return true;
                }

                if (!(m.GetParentEntity() is BaseEntity parent) || parent is HitchTrough.IHitchable)
                {
                    return false;
                }

                if (parent.GetType().Name.Contains("Controller"))
                {
                    DismountAllPlayers(m as BaseMountable);

                    return true;
                }

                return false;
            }

            private bool IsBlockingCampers(ModularCar car)
            {
                if (!Options.Mounts.Campers || car.AttachedModuleEntities == null)
                {
                    return false;
                }

                foreach (var module in car.AttachedModuleEntities)
                {
                    if (module is VehicleModuleCamper)
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool TryRemoveMountable(BaseEntity m, PooledList<BasePlayer> players)
            {
                if (m.IsNull() || Type == RaidableType.None || m is TrainCar || m.GetParentEntity() is TrainCar || IsControlledMount(m) || Entities.Contains(m))
                {
                    return false;
                }

                if (m is HotAirBalloon && (Options.Mounts.HotAirBalloon || CanEject(players)))
                {
                    return Eject(m, Location, ProtectionRadius, false);
                }

                if (Options.Mounts.Siege && !Options.Siege.Only)
                {
                    if (m is BaseSiegeWeapon or ConstructableEntity)
                    {
                        return Eject(m, Location, ProtectionRadius, true);
                    }
                }

                if (m is BaseMountable m2)
                {
                    bool jetpack = IsJetpack(m2);
                    bool carpet = !jetpack && m.ObjectName() == "FlyingCarpet";

                    if (ShouldEject(Options.Mounts, m, jetpack, carpet) || CanEject(players))
                    {
                        return EjectMountable(m2, players, Location, ProtectionRadius, jetpack || carpet);
                    }
                }

                return false;
            }

            private bool ShouldEject(ManagementMountableSettings ms, BaseEntity m, bool jetpack, bool carpet) => m switch
            {
                _ when IsInvisibleChair(m) => ms.Invisible,
                _ when jetpack => ms.Jetpacks,
                _ when carpet => ms.FlyingCarpet,
                global::Parachute _ => ms.Parachutes,
                BaseSiegeWeapon or ConstructableEntity => ms.Siege && !Options.Siege.Only,
                Tugboat => ms.Tugboats,
                Bike => ms.Bikes,
                BaseBoat => ms.Boats,
                BasicCar => ms.BasicCars,
                ModularCar car => ms.ModularCars || IsBlockingCampers(car),
                CH47Helicopter => ms.CH47,
                HitchTrough.IHitchable => ms.Hitchable,
                ScrapTransportHelicopter => ms.Scrap,
                AttackHelicopter => ms.AttackHelicopters,
                Minicopter => ms.MiniCopters,
                Snowmobile => ms.Snowmobile,
                StaticInstrument => ms.Pianos,
                _ => ms.Other
            };

            public static bool IsWearingJetpack(BasePlayer player) => !player.IsNull() && player.GetMounted() is BaseMountable m && IsJetpack(m);

            public static bool IsJetpack(BaseMountable m) => (m.ShortPrefabName == "testseat" || m.ShortPrefabName == "standingdriver") && m.GetParentEntity() is DroppedItem;

            public static bool IsInvisibleChair(BaseEntity m) => m.skinID == 1169930802;

            private static bool IsAirborne(BaseEntity m, float tolerance = 0.25f)
            {
                float waterY = TerrainMeta.WaterMap.GetHeight(m.transform.position);
                if (m.transform.position.y <= waterY + tolerance) return false;
                Vector3 start = new(m.bounds.center.x, m.bounds.min.y + 0.05f, m.bounds.center.z);
                float length = tolerance + 0.05f;
                return !Physics.Raycast(start, Vector3.down, length, Layers.Terrain | Layers.Construction | Layers.Solid, QueryTriggerInteraction.Ignore);
            }

            private static bool IsFlying(BasePlayer player)
            {
                return player != null && player.modelState != null && !player.modelState.onground && TerrainMeta.HeightMap.GetHeight(player.transform.position) < player.transform.position.y - 1f;
            }

            public bool EjectMountable(BaseEntity m, PooledList<BasePlayer> players, Vector3 position, float radius, bool special)
            {
                m = GetParentEntity(m);
                if (m is TrainCar { OwnerID: 0uL })
                {
                    return false;
                }

                var j = TerrainMeta.HeightMap.GetHeight(m.transform.position) - m.transform.position.y;
                var distance = m switch { HitchTrough.IHitchable => j > 5f ? j + 5f : 5f, _ => j > 5f ? j + 10f : 10f };
                var target = GetEjectLocation(m.transform.position, distance, position, radius, false, false);

                if (m is BaseHelicopter || players.Exists(IsFlying))
                {
                    target.y = Mathf.Max(m.transform.position.y, SpawnsController.GetSpawnHeight(target)) + 5f;
                }
                else if (m is Drone)
                {
                    target.y = Mathf.Max(target.y + 15f, position.y + radius);
                }
                else target.y = SpawnsController.GetSpawnHeight(target) + m switch { ModularCarSeat or ModularCar => 1f, _ => 0.5f };

                if (special)
                {
                    target.y += 15f;
                }

                BaseVehicle vehicle = m is BaseMountable b && b.HasParent() ? b.VehicleParent() : m as BaseVehicle;
                if (vehicle.IsNull() || m is HitchTrough.IHitchable || m is not (BaseSiegeWeapon or BaseHelicopter or global::Parachute) && InRange(m.transform.position, position, radius + 1f))
                {
                    m.transform.position = target;
                }
                else
                {
                    TryPushMountable(vehicle, target);
                }

                return true;
            }

            public void TryPushMountable(BaseVehicle vehicle, Vector3 target) // ApplyRepelForce
            {
                Rigidbody body = vehicle.rigidBody ?? vehicle.GetComponent<Rigidbody>();

                float forceMultiplier = vehicle switch
                {
                    BaseHelicopter => 2.5f,
                    GroundVehicle or BasicCar => 1f,
                    BaseSubmarine => 1.25f,
                    HitchTrough.IHitchable => 3f,
                    _ => 15f
                };

                switch (vehicle)
                {
                    case BaseSiegeWeapon:
                    case BaseBoat:
                        ApplyMassForce(vehicle, body, target);
                        break;

                    case ModularCar or _ when vehicle.PrefabName.Contains("modularcar"):
                        ApplyModularCarForce(vehicle, body, target, forceMultiplier: 150f);
                        break;

                    case BaseHelicopter:
                    case Parachute:
                        ApplyHelicopterOrParachuteForce(vehicle, body, target, forceMultiplier);
                        break;

                    default:
                        SetPositionAndRotation(vehicle, body);
                        break;
                }
            }

            private static void ApplyMassForce(BaseVehicle vehicle, Rigidbody body, Vector3 target)
            {
                Vector3 normalized = Vector3.ProjectOnPlane(vehicle.transform.position - target, vehicle.transform.up).normalized;
                float massAdjustedForce = vehicle is BaseSiegeWeapon ? body.mass * 25f : vehicle is PlayerBoat ? body.mass * 100f : body.mass * 50f;
                Vector3 forceDirection = vehicle is Tugboat ? normalized : -normalized;
                body.AddForce(forceDirection * massAdjustedForce + Vector3.up * 15f, ForceMode.Impulse);
            }

            private static void ApplyMassForce(BaseVehicle vehicle, Rigidbody body, Vector3 target, float maxSpeed = 30f)
            {
                Vector3 normalized = Vector3.ProjectOnPlane(vehicle.transform.position - target, Vector3.up).normalized;
                if (normalized.sqrMagnitude < 0.0001f)
                {
                    body.AddForce(Vector3.up * body.mass * 0.0003f, ForceMode.Impulse);
                    return;
                }
                Vector3 flatVel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
                Vector3 forceDir = vehicle is Tugboat ? normalized : -normalized;
                float currentSpeed = Vector3.Dot(flatVel, forceDir);
                float deltaV = Mathf.Clamp(maxSpeed - currentSpeed, 0f, maxSpeed);
                body.AddForce(forceDir * body.mass * deltaV + Vector3.up * 15f, ForceMode.Impulse);
            }

            private static void ApplyModularCarForce(BaseVehicle vehicle, Rigidbody body, Vector3 target, float forceMultiplier)
            {
                if (body != null)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(vehicle.transform.position - target, Vector3.up).normalized;
                    Vector3 horizontalForce = -direction * 140f * forceMultiplier;
                    Vector3 upwardForce = Vector3.up * 25f * forceMultiplier;
                    Vector3 totalForce = horizontalForce + upwardForce;

                    if (!body.isKinematic)
                    {
                        body.AddForce(totalForce, ForceMode.Impulse);
                    }
                    else
                    {
                        vehicle.transform.position += totalForce * 0.1f;
                        Quaternion rotationChange = Quaternion.LookRotation(direction);
                        vehicle.transform.rotation = Quaternion.Slerp(vehicle.transform.rotation, rotationChange, 0.1f);
                    }
                }
            }

            private static void ApplyHelicopterOrParachuteForce(BaseVehicle vehicle, Rigidbody body, Vector3 target, float forceMultiplier, bool b = false)
            {
                if (body != null)
                {
                    float baseForceMultiplier = 4f;
                    Vector3 direction = Vector3.ProjectOnPlane(vehicle.transform.position - target, Vector3.up).normalized;
                    Vector3 horizontalForce = -direction * baseForceMultiplier * forceMultiplier;
                    Vector3 multiForce = b ? direction * body.mass * forceMultiplier : Vector3.up * (vehicle is Parachute ? 100f : 25f) * forceMultiplier;
                    Vector3 totalForce = horizontalForce + multiForce;

                    if (!body.isKinematic)
                    {
                        body.AddForce(totalForce, ForceMode.VelocityChange);
                    }
                    else
                    {
                        vehicle.transform.position += totalForce * 0.1f;
                        Quaternion rotationChange = Quaternion.LookRotation(direction);
                        vehicle.transform.rotation = Quaternion.Slerp(vehicle.transform.rotation, rotationChange, 0.1f);
                    }
                }
            }

            private bool SetPositionAndRotation(BaseEntity m, Rigidbody rb)
            {
                m = GetParentEntity(m);
                if (m is ZiplineMountable or TrainCar { OwnerID: 0uL }) return false;

                float j = TerrainMeta.HeightMap.GetHeight(m.transform.position) - m.transform.position.y;
                float distance = 10f + m.bounds.size.Max() + (j > 5f ? j : 0f);
                Vector3 position = (m.transform.position.XZ3D() - LocationXZ3D).normalized * (ProtectionRadius + distance) + Location;
                Vector3 fwd = m.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.001f) fwd = m.transform.right;
                Quaternion yawOnly = Quaternion.LookRotation(-fwd.normalized, Vector3.up);
                float pitchDeg = Mathf.Asin(Mathf.Clamp(m.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                float newPitch = Mathf.Max(Mathf.Abs(pitchDeg), 10f);
                Quaternion rotation = yawOnly * Quaternion.AngleAxis(newPitch, Vector3.left);
                position.y = Instance.GetSpawnHeight(position) + 1f;
                if (IsAirborne(m)) position.y = Mathf.Max(position.y, m.transform.position.y + 5f) + m.bounds.extents.y + 0.25f;

                if (rb != null)
                {
                    if (!rb.isKinematic)
                    {
                        Vector3 v = yawOnly * new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                        rb.linearVelocity = new Vector3(v.x, rb.linearVelocity.y, v.z);
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.MovePosition(position);
                    rb.MoveRotation(rotation);
                }
                else m.transform.SetPositionAndRotation(position, rotation);

                return true;
            }

            private void TryEjectMountable(BaseEntity e)
            {
                if (e is BaseMountable m)
                {
                    using var players = GetMountedPlayers(m);
                    if (players.Count == 0)
                    {
                        Eject(m, Location, ProtectionRadius, true);
                    }
                }
                else if (e is HotAirBalloon hab)
                {
                    using var players = GetMountedPlayers(hab);
                    if (players.Count == 0)
                    {
                        Eject(hab, Location, ProtectionRadius, false);
                    }
                }
            }

            private void EjectContainer(BaseEntity container, ulong playerSteamID, bool notice = true)
            {
                var position = GetEjectLocation(container.transform.position, 5f, Location, ProtectionRadius, true, true);
                position.y = Mathf.Max(position.y, TerrainMeta.WaterMap.GetHeight(position) + 0.1f, WaterSystem.OceanLevel, 0.1f);

                container.transform.position = position;
                container.TransformChanged();

                if (notice)
                {
                    BasePlayer player = BasePlayer.FindByID(playerSteamID);

                    EjectBackpackNotice(player, position);

                    HarmonyModInterface.CallHook("OnRaidableBaseBackpackEjected", new object[] { player, playerSteamID, container, Location, AllowPVP, Options.Level, GetOwner(), GetRaiders(), BaseName });
                }
            }

            private float habdist = 15f;

            public bool Eject(BaseEntity m, Vector3 position, float radius, bool groundLevel)
            {
                if (m is HotAirBalloon)
                {
                    habdist += 15f;
                    radius += habdist;
                }

                m = GetParentEntity(m);
                var target = GetEjectLocation(m.transform.position, 10f, position, radius, false, false);
                var spawnHeight = SpawnsController.GetSpawnHeight(target);

                if (groundLevel)
                {
                    target.y = spawnHeight;
                }
                else if (m is Drone)
                {
                    target.y = Mathf.Max(target.y + 15f, position.y + radius);
                }
                else
                {
                    target.y = Mathf.Min(spawnHeight + radius, Mathf.Max(m.transform.position.y, SpawnsController.GetSpawnHeight(target))) + 5f;
                }

                if (m is PlayerCorpse)
                {
                    m.limitNetworking = true;
                }

                m.transform.position = target;
                m.TransformChanged();
                m.SendNetworkUpdate();

                if (m is PlayerCorpse)
                {
                    m.limitNetworking = false;
                }

                return true;
            }

            internal static BaseEntity GetParentEntity(BaseEntity m)
            {
                int n = 0;
                while (m != null && m.HasParent() && ++n < 30)
                {
                    if (!(m.GetParentEntity() is BaseEntity parent)) break;
                    m = parent;
                }

                return m;
            }

            public bool CanSetupEntity(BaseEntity e)
            {
                if (e.IsKilled() || setupBlockedPrefabs.Exists(e.ShortPrefabName.Contains))
                {
                    e.DelayedSafeKill();
                    Entities.Remove(e);
                    return false;
                }

                return true;
            }

            public int npcMaxAmountMurderers;
            public int npcMaxAmountScientists;
            public int npcMaxAmountInside = -1;
            public int npcAmountInside;
            public int npcAmountThrown;
            public bool ExtendHookSubscription;
            private List<RespawnEntry> _respawns = new(32);

            private struct RespawnEntry
            {
                public double Target;
                public int RespawnsRemaining;
                public byte Type;

                public RespawnEntry(double target, bool isMurderer, int respawnsRemaining)
                {
                    Target = target;
                    RespawnsRemaining = respawnsRemaining;
                    Type = (byte)(isMurderer ? 1 : 0);
                }

                public bool IsMurderer => Type != 0;
            }

            public void TryRespawnNpc(bool isMurderer, int respawnsRemaining)
            {
                if ((!IsOpened && !Options.Levels.Level2) || respawnsRemaining == 0)
                {
                    return;
                }

                float min = Mathf.Min(Options.RespawnRateMin, Options.RespawnRateMax);
                float max = Mathf.Max(Options.RespawnRateMin, Options.RespawnRateMax);
                float delay = min < max ? UnityEngine.Random.Range(min, max) : max;

                if (delay <= 0.5f)
                {
                    return;
                }

                if (respawnsRemaining > 0)
                {
                    respawnsRemaining--;
                }

                ExtendHookSubscription = true;

                double now = Time.realtimeSinceStartupAsDouble;
                double target = now + delay;

                if (DebugMode)
                {
                    Puts($"[Queue Respawn][{(isMurderer ? "Murderer" : "Scientist")}] delay={delay:0.###}s remaining={respawnsRemaining} now={now:0.###}s target={target:0.###}s range=[{min:0.###},{max:0.###}]s frame={Time.frameCount}");
                }

                _respawns.Add(new(target, isMurderer, respawnsRemaining));
            }

            private void CheckNpcRespawns()
            {
                if (_respawns.Count == 0)
                {
                    return;
                }
                double time = Time.realtimeSinceStartupAsDouble;
                for (int i = _respawns.Count - 1; i >= 0; i--)
                {
                    var e = _respawns[i];
                    if (time >= e.Target)
                    {
                        if (DebugMode) Puts($"[Fire Respawn][{Location}][{(e.IsMurderer ? "Murderer" : "Scientist")}] time={time:0.###}s scheduled={e.Target:0.###}s lateBy={(time - e.Target):0.###}s frame={Time.frameCount}");
                        int last = _respawns.Count - 1;
                        if (i != last) _respawns[i] = _respawns[last];
                        _respawns.RemoveAt(last);
                        RespawnNpcNow(e.IsMurderer, e.RespawnsRemaining);
                    }
                }
            }

            private void RespawnNpcNow(bool isMurderer, int respawnsRemaining)
            {
                if (IsUnloading || IsDespawning || (!IsOpened && !Options.Levels.Level2))
                {
                    ExtendHookSubscription = _respawns.Count > 0;
                    return;
                }

                int current = 0;
                int max = isMurderer ? npcMaxAmountMurderers : npcMaxAmountScientists;

                foreach (var npc in npcs)
                {
                    if (!npc.IsKilled() && npc.Brain != null && npc.Brain.isMurderer == isMurderer)
                    {
                        current++;
                    }
                }

                if (current < max)
                {
                    SpawnNpc(isMurderer, respawnsRemaining);
                }

                ExtendHookSubscription = _respawns.Count > 0;
            }

            public void SpawnNpcs()
            {
                if (!Options.NPC.Enabled || SpawnNpcsRequestCo != null || (Options.NPC.UseExpansionNpcs && config.Settings.ExpansionMode && Instance.DangerousTreasures.CanCall()))
                {
                    return;
                }

                SpawnNpcsRequestCo = ServerMgr.Instance.StartCoroutine(SpawnNpcsCo());
            }

            internal Coroutine SpawnNpcsRequestCo;
            private IEnumerator SpawnNpcsCo()
            {
                yield return BuildBaseRoutesCo();

                for (int i = 0; i < npcMaxAmountMurderers; i++)
                {
                    TryInvokeMethod(() => SpawnNpc(true));
                    yield return null;
                }
                for (int i = 0; i < npcMaxAmountScientists; i++)
                {
                    TryInvokeMethod(() => SpawnNpc(false));
                    yield return null;
                }
                SpawnNpcsRequestCo = null;
            }

            private IEnumerator BuildBaseRoutesCo()
            {
                if (_baseRoutesBuilt)
                {
                    yield break;
                }

                _baseRoutesBuilt = true;

                if (!Options.NPC.Inside.Roam || Options.NPC.Inside.Sleepers.Enabled || npcMaxAmountInside == 0)
                {
                    yield break;
                }

                FrameDeadline deadline = new(npcSchedulerFrameBudgetMilliseconds);
                using var platforms = DisposableList<BuildingBlock>();

                for (int i = 0; i < blocks.Count; i++)
                {
                    BuildingBlock block = blocks[i];

                    if (!ReferenceEquals(block, null) && !block.IsKilled() && IsNpcPlatform(block.ShortPrefabName))
                    {
                        platforms.Add(block);
                    }
                }

                for (int i = 0; i < blocks.Count; i++)
                {
                    BuildingBlock block = blocks[i];

                    if (TryGetBaseFloorPosition(block, out var position))
                    {
                        int index = _baseRouteNodes.Count;
                        _baseRouteNodes.Add(new(block.net.ID, position, IsBaseRouteNodeExposed(block, position, platforms)));
                        _baseRouteLookup[block.net.ID] = index;
                    }

                    if (deadline.Expired)
                    {
                        yield return null;
                        deadline.Reset();
                    }
                }

                for (int i = 0; i < _baseRouteNodes.Count; i++)
                {
                    for (int j = i + 1; j < _baseRouteNodes.Count; j++)
                    {
                        if (CanConnectBaseRoutePoints(_baseRouteNodes[i], _baseRouteNodes[j]))
                        {
                            _baseRouteNodes[i].Neighbors.Add(j);
                            _baseRouteNodes[j].Neighbors.Add(i);
                        }

                        if (deadline.Expired)
                        {
                            yield return null;
                            deadline.Reset();
                        }
                    }
                }
            }

            private static bool IsBaseRouteFloor(BuildingBlock block)
            {
                if (ReferenceEquals(block, null) || block.IsKilled())
                {
                    return false;
                }

                return IsBaseRouteFloorPrefab(block);
            }

            private static bool IsBaseRouteFloorPrefab(BuildingBlock block) => !ReferenceEquals(block, null) && block.net != null && (block.ShortPrefabName is "foundation" or "foundation.triangle" or "floor" or "floor.triangle");

            private static bool IsBaseRouteNodeExposed(BuildingBlock support, Vector3 position, List<BuildingBlock> platforms)
            {
                BuildingBlock highestPlatform = null;
                float highestPosition = float.MinValue;

                for (int i = 0; i < platforms.Count; i++)
                {
                    BuildingBlock platform = platforms[i];

                    OBB bounds = platform.WorldSpaceBounds();
                    float platformTop = bounds.position.y + bounds.extents.y;
                    Vector3 probe = position.WithY(bounds.position.y);

                    if (platformTop < position.y - 0.25f ||
                        (bounds.ClosestPoint(probe) - probe).SqrMagnitude2D() > 0.04f ||
                        platformTop <= highestPosition)
                    {
                        continue;
                    }

                    highestPlatform = platform;
                    highestPosition = platformTop;
                }

                return ReferenceEquals(highestPlatform, null) || ReferenceEquals(highestPlatform, support);
            }

            private bool TryGetBaseFloorPosition(BuildingBlock block, out Vector3 position)
            {
                position = default;

                if (!IsBaseRouteFloor(block) || block.net == null)
                {
                    return false;
                }

                Vector3 center = block.CenterPoint();
                bool isFoundation = block.ShortPrefabName is "foundation" or "foundation.triangle";
                float height = isFoundation ? block.bounds.extents.y + 0.1125f : 0.155f;
                center += Vector3.up * height;

                if (IsBaseRoutePositionUsable(block, center))
                {
                    position = center;
                    return true;
                }

                Vector3 right = block.transform.right;
                Vector3 forward = block.transform.forward;

                return TryGetOffsetBaseFloorPosition(block, center, right, BaseRouteInnerLaneOffset, out position)
                    || TryGetOffsetBaseFloorPosition(block, center, right, BaseRouteOuterLaneOffset, out position)
                    || TryGetOffsetBaseFloorPosition(block, center, forward, BaseRouteInnerLaneOffset, out position)
                    || TryGetOffsetBaseFloorPosition(block, center, forward, BaseRouteOuterLaneOffset, out position);
            }

            private bool TryGetOffsetBaseFloorPosition(BuildingBlock block, Vector3 center, Vector3 direction, float distance, out Vector3 position)
            {
                position = center + direction * distance;

                if (IsBaseRoutePositionUsable(block, position))
                {
                    return true;
                }

                position = center - direction * distance;
                return IsBaseRoutePositionUsable(block, position);
            }

            private bool IsBaseRoutePositionUsable(BuildingBlock block, Vector3 position)
            {
                if (Physics.Raycast(position + Vector3.up * 0.45f, Vector3.up, 1.75f, Layers.Mask.Construction | Layers.Mask.Deployed, QueryTriggerInteraction.Ignore))
                {
                    return false;
                }

                if (IsBaseRoutePointObstructed(position) || !TryGetBaseRouteFloorSupport(position, out var support))
                {
                    return false;
                }

                return support.net.ID == block.net.ID;
            }

            private static bool TryGetBaseRouteFloorSupport(Vector3 position, out BuildingBlock support)
            {
                support = null;

                if (!Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out var hit, 1f, Layers.Mask.Construction, QueryTriggerInteraction.Ignore))
                {
                    return false;
                }

                support = hit.GetEntity() as BuildingBlock;
                return IsBaseRouteFloor(support);
            }

            private bool IsBaseRoutePointObstructed(Vector3 position)
            {
                for (int i = 0; i < _decorDeployables.Count; i++)
                {
                    BaseEntity decor = _decorDeployables[i];

                    if (decor.IsKilled())
                    {
                        continue;
                    }

                    OBB bounds = decor.WorldSpaceBounds();

                    if (bounds.extents.y < 0.2f)
                    {
                        continue;
                    }

                    Vector3 probe = position.WithY(bounds.position.y);

                    if ((bounds.ClosestPoint(probe) - probe).SqrMagnitude2D() <= 0.36f)
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool CanConnectBaseRoutePoints(BaseRouteNode a, BaseRouteNode b)
            {
                Vector3 delta = b.Position - a.Position;
                float horizontalSqrDistance = delta.SqrMagnitude2D();

                if (horizontalSqrDistance < 0.25f || horizontalSqrDistance > 14.0625f)
                {
                    return false;
                }

                float verticalDistance = delta.y < 0f ? -delta.y : delta.y;
                bool changesLevel = verticalDistance > 0.75f;

                if (verticalDistance > 3.5f)
                {
                    return false;
                }

                if (changesLevel && !HasStairsBetween(_baseStairs, a.Position, b.Position))
                {
                    return false;
                }

                if (changesLevel)
                {
                    return IsBaseRouteLaneClear(a, b, Vector3.zero, false);
                }

                Vector3 horizontalDirection = delta;
                horizontalDirection.y = 0f;
                horizontalDirection.Normalize();
                Vector3 perpendicular = new(-horizontalDirection.z, 0f, horizontalDirection.x);

                return IsBaseRouteLaneClear(a, b, Vector3.zero, true)
                    || IsBaseRouteLaneClear(a, b, perpendicular * BaseRouteInnerLaneOffset, true)
                    || IsBaseRouteLaneClear(a, b, perpendicular * -BaseRouteInnerLaneOffset, true)
                    || IsBaseRouteLaneClear(a, b, perpendicular * BaseRouteOuterLaneOffset, true)
                    || IsBaseRouteLaneClear(a, b, perpendicular * -BaseRouteOuterLaneOffset, true);
            }

            private bool IsBaseRouteLaneClear(BaseRouteNode a, BaseRouteNode b, Vector3 offset, bool requireFloorSupport)
            {
                Vector3 start = a.Position + offset;
                Vector3 end = b.Position + offset;

                if (requireFloorSupport &&
                    (!TryGetBaseRouteFloorSupport(Vector3.Lerp(start, end, 0.25f), out _) ||
                     !TryGetBaseRouteFloorSupport(Vector3.Lerp(start, end, 0.5f), out _) ||
                     !TryGetBaseRouteFloorSupport(Vector3.Lerp(start, end, 0.75f), out _)))
                {
                    return false;
                }

                Vector3 origin = start + Vector3.up * 0.75f;
                Vector3 target = end + Vector3.up * 0.75f;
                Vector3 direction = target - origin;
                float distance = direction.magnitude;

                if (distance <= 0.01f)
                {
                    return false;
                }

                int count = Physics.SphereCastNonAlloc(origin, BaseRouteAgentRadius, direction / distance, _baseRouteHitBuffer, distance, Layers.Mask.Construction | Layers.Mask.Deployed, QueryTriggerInteraction.Ignore);

                if (count >= _baseRouteHitBuffer.Length)
                {
                    return false;
                }

                for (int i = 0; i < count; i++)
                {
                    BaseEntity entity = _baseRouteHitBuffer[i].GetEntity();

                    if (entity == null)
                    {
                        return false;
                    }

                    Door door = entity as Door ?? entity.GetParentEntity() as Door;

                    if (!ReferenceEquals(door, null) && !door.IsKilled())
                    {
                        continue;
                    }

                    if (entity.net != null && (entity.net.ID == a.FloorId || entity.net.ID == b.FloorId))
                    {
                        continue;
                    }

                    if (!requireFloorSupport && IsNpcStair(entity))
                    {
                        continue;
                    }

                    if (entity is DecorDeployable && entity.WorldSpaceBounds().extents.y < 0.2f)
                    {
                        continue;
                    }

                    return false;
                }

                return true;
            }

            private bool HasStairsBetween(List<Vector3> baseStairs, Vector3 a, Vector3 b)
            {
                float minY = Mathf.Min(a.y, b.y) - 1f;
                float maxY = Mathf.Max(a.y, b.y) + 1f;

                for (int i = 0; i < baseStairs.Count; i++)
                {
                    Vector3 stair = baseStairs[i];

                    if (stair.y < minY || stair.y > maxY)
                    {
                        continue;
                    }

                    if (DistanceToSegment2DSquared(stair, a, b) <= 4f)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool IsNpcStair(BaseEntity entity) => !entity.IsKilled() && IsNpcStairPrefab(entity);

            private static bool IsNpcStairPrefab(BaseEntity entity) => !ReferenceEquals(entity, null) && (entity.ShortPrefabName.Contains("stairs") || entity.ShortPrefabName.Contains("ramp"));

            private static float DistanceToSegment2DSquared(Vector3 point, Vector3 a, Vector3 b)
            {
                Vector2 start = new(a.x, a.z);
                Vector2 end = new(b.x, b.z);
                Vector2 target = new(point.x, point.z);
                Vector2 segment = end - start;
                float lengthSqr = segment.sqrMagnitude;

                if (lengthSqr <= 0.001f)
                {
                    return (target - start).sqrMagnitude;
                }

                float t = Mathf.Clamp01(Vector2.Dot(target - start, segment) / lengthSqr);
                return (target - (start + segment * t)).sqrMagnitude;
            }

            internal List<int> CreateBaseRoute(Vector3 origin)
            {
                List<int> route = Pool.Get<List<int>>();

                if (!_baseRoutesBuilt || _baseRouteNodes.Count == 0)
                {
                    return route;
                }

                int start = -1;
                float nearestSqrDistance = 36f;

                for (int i = 0; i < _baseRouteNodes.Count; i++)
                {
                    BaseRouteNode node = _baseRouteNodes[i];
                    if (!node.IsValid)
                    {
                        continue;
                    }

                    float verticalDelta = node.Position.y - origin.y;
                    if (verticalDelta < -3.5f || verticalDelta > 3.5f)
                    {
                        continue;
                    }

                    float sqrDistance = (node.Position - origin).sqrMagnitude;
                    if (sqrDistance < nearestSqrDistance)
                    {
                        nearestSqrDistance = sqrDistance;
                        start = i;
                    }
                }

                if (start < 0)
                {
                    return route;
                }

                float maximumDistance = Mathf.Clamp(Options.NPC.Inside.MaximumRoamDistance, 3f, 300f);
                float totalDistance = 0f;
                using var stack = DisposableList<int>();
                using var visited = DisposableHashSet<int>();
                using var candidates = DisposableList<int>();
                stack.Add(start);
                visited.Add(start);
                route.Add(start);

                while (stack.Count > 0 && totalDistance < maximumDistance)
                {
                    int current = stack[stack.Count - 1];
                    BaseRouteNode node = _baseRouteNodes[current];
                    candidates.Clear();

                    for (int i = 0; i < node.Neighbors.Count; i++)
                    {
                        int neighbor = node.Neighbors[i];

                        if (!_baseRouteNodes[neighbor].IsValid || visited.Contains(neighbor))
                        {
                            continue;
                        }

                        float distance = node.Position.Distance(_baseRouteNodes[neighbor].Position);

                        if (totalDistance + distance <= maximumDistance)
                        {
                            candidates.Add(neighbor);
                        }
                    }

                    if (candidates.Count > 0)
                    {
                        int next = candidates.GetRandom();
                        totalDistance += node.Position.Distance(_baseRouteNodes[next].Position);
                        route.Add(next);
                        stack.Add(next);
                        visited.Add(next);
                        continue;
                    }

                    if (stack.Count == 1)
                    {
                        break;
                    }

                    int previous = stack[stack.Count - 2];
                    float returnDistance = node.Position.Distance(_baseRouteNodes[previous].Position);

                    if (totalDistance + returnDistance > maximumDistance)
                    {
                        break;
                    }

                    stack.RemoveAt(stack.Count - 1);
                    route.Add(previous);
                    totalDistance += returnDistance;
                }

                return route;
            }

            internal bool TryGetBaseRoutePosition(int index, out Vector3 position)
            {
                if (index >= 0 && index < _baseRouteNodes.Count && _baseRouteNodes[index].IsValid)
                {
                    position = _baseRouteNodes[index].Position;
                    return true;
                }

                position = default;
                return false;
            }

            internal bool IsBaseRouteExposed(List<int> route, int routeIndex, int routeDirection, Vector3 position)
            {
                if (route == null || routeIndex < 0 || routeIndex >= route.Count)
                {
                    return false;
                }

                int nodeIndex = route[routeIndex];
                if (nodeIndex < 0 || nodeIndex >= _baseRouteNodes.Count)
                {
                    return false;
                }

                BaseRouteNode closest = _baseRouteNodes[nodeIndex];
                if (!closest.IsValid)
                {
                    return false;
                }

                int previousRouteIndex = routeIndex - routeDirection;

                if (previousRouteIndex >= 0 && previousRouteIndex < route.Count)
                {
                    int previousNodeIndex = route[previousRouteIndex];

                    if (previousNodeIndex >= 0 && previousNodeIndex < _baseRouteNodes.Count)
                    {
                        BaseRouteNode previous = _baseRouteNodes[previousNodeIndex];

                        if (previous.IsValid && (previous.Position - position).sqrMagnitude < (closest.Position - position).sqrMagnitude)
                        {
                            closest = previous;
                        }
                    }
                }

                return closest.IsExposed;
            }

            internal void InvalidateBaseRoute(StabilityEntity entity)
            {
                if (!entity.Is(out BuildingBlock block))
                {
                    return;
                }

                if (IsNpcStairPrefab(block))
                {
                    Vector3 stairPosition = block.transform.position;

                    using var baseStairs = DisposableList<Vector3>();
                    for (int i = 0; i < _baseStairs.Count; i++)
                    {
                        if (InRange(_baseStairs[i], stairPosition, 0.25f)) continue;
                        baseStairs.Add(_baseStairs[i]);
                    }

                    for (int i = 0; i < _baseRouteNodes.Count; i++)
                    {
                        BaseRouteNode routeNode = _baseRouteNodes[i];

                        for (int j = routeNode.Neighbors.Count - 1; j >= 0; j--)
                        {
                            int neighborIndex = routeNode.Neighbors[j];
                            BaseRouteNode neighbor = _baseRouteNodes[neighborIndex];

                            float verticalDelta = routeNode.Position.y - neighbor.Position.y;
                            if ((verticalDelta < -0.75f || verticalDelta > 0.75f) &&
                                DistanceToSegment2DSquared(stairPosition, routeNode.Position, neighbor.Position) <= 4f &&
                                !HasStairsBetween(baseStairs, routeNode.Position, neighbor.Position))
                            {
                                routeNode.Neighbors.RemoveAt(j);
                                neighbor.Neighbors.Remove(i);
                            }
                        }
                    }

                    foreach (HumanoidBrain brain in Instance.HumanoidBrains.Values)
                    {
                        if (brain != null && brain.raid == this && brain.spawnedInside)
                        {
                            brain.baseRouteDirty = true;
                        }
                    }

                    return;
                }

                if (!IsBaseRouteFloorPrefab(block))
                {
                    return;
                }

                floors.RemoveAll(position => InRange(position, block.transform.position, 0.25f));
                _inside.RemoveAll(position => InRange2D(position, block.transform.position, 1f) && Mathf.Abs(position.y - block.transform.position.y) <= 2f);

                if (!_baseRouteLookup.Remove(block.net.ID, out int index))
                {
                    return;
                }

                BaseRouteNode floorNode = _baseRouteNodes[index];
                floorNode.IsValid = false;
                floorNode.Neighbors.Clear();

                for (int i = 0; i < _baseRouteNodes.Count; i++)
                {
                    _baseRouteNodes[i].Neighbors.Remove(index);
                }

                foreach (HumanoidBrain brain in Instance.HumanoidBrains.Values)
                {
                    if (brain != null && brain.raid == this)
                    {
                        brain.InvalidateBaseRoute(index);
                    }
                }
            }

            internal void OpenDoorsForNpc(HumanoidBrain brain, Vector3 from, Vector3 to)
            {
                if (doors.Count == 0 || brain?.Navigator == null || (intruders.Count == 0 && brain.AttackTarget == null))
                {
                    return;
                }

                float speed = Mathf.Max(0.1f, brain.Navigator.GetTargetSpeed());
                float travelSeconds = from.Distance2D(to) / speed;
                float closeDelay = Mathf.Lerp(2f, 8f, Mathf.InverseLerp(0.5f, 6f, travelSeconds));
                float openRange = Mathf.Max(2f, speed * 0.75f);

                for (int i = doors.Count - 1; i >= 0; i--)
                {
                    Door door = doors[i];
                    if (door.IsKilled())
                    {
                        doors.RemoveAt(i);
                        continue;
                    }
                    if (!IsDoorOnNpcRouteLevel(door, from, to))
                    {
                        continue;
                    }
                    if (!InRange2D(from, door.transform.position, openRange))
                    {
                        continue;
                    }
                    if (DistanceToSegment2DSquared(door.transform.position, from, to) > 2.25f)
                    {
                        continue;
                    }
                    if (!door.IsOpen())
                    {
                        door.SetOpen(true);
                    }
                    if (door.IsInvoking(door.CloseRequest))
                    {
                        door.CancelInvoke(door.CloseRequest);
                    }
                    door.Invoke(door.CloseRequest, closeDelay);
                    brain.lastDoorCheckTime = Time.realtimeSinceStartupAsDouble + 60f;
                }
            }

            private static bool IsDoorOnNpcRouteLevel(Door door, Vector3 from, Vector3 to)
            {
                OBB bounds = door.WorldSpaceBounds();
                float routeMin = Mathf.Min(from.y, to.y) - 0.75f;
                float routeMax = Mathf.Max(from.y, to.y) + 1.75f;
                float doorMin = bounds.position.y - bounds.extents.y;
                float doorMax = bounds.position.y + bounds.extents.y;

                return doorMax >= routeMin && doorMin <= routeMax;
            }

            public bool IsInForwardOperatingBase(Vector3 from)
            {
                foreach (var ent in BuiltList)
                {
                    if (ent == null || ent.IsDestroyed)
                    {
                        continue;
                    }
                    if (InRange2D(from, ent.transform.position, 3f))
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool NearFoundation(Vector3 from, float range = 5f)
            {
                for (int i = 0; i < foundations.Count; i++)
                {
                    if (InRange2D(from, foundations[i], range))
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool FindPointOnNavmesh(Vector3 a, float radius, out Vector3 v)
            {
                if (!ConVar.AI.useUnityNavmesh && !RustNavigation.Instance.IsDefaultNavmeshBuilt())
                {
                    v = default;
                    return false;
                }
                if (RustNavMeshHelpers.SamplePosition(a, out var navHit, radius, 25) && !NearFoundation(navHit.position) && !IsNpcNearSpot(navHit.position) && IsAcceptableWaterDepth(navHit.position) && !TestInsideObject(navHit.position))
                {
                    v = navHit.position;
                    return true;
                }
                v = default;
                return false;
            }

            private bool IsAcceptableWaterDepth(Vector3 point) => WaterLevel.GetOverallWaterDepth(point, true, true, null) <= config.Settings.Management.WaterDepth;

            private bool TestInsideObject(Vector3 point) => GamePhysics.CheckSphere(point, 0.5f, Layers.Mask.Player_Server | Layers.Server.Deployed, QueryTriggerInteraction.Ignore) || IsPointInsideRock(point) || IsRockFaceUpwards(point) || IsRockFaceDownwards(point);

            private bool IsRockFaceDownwards(Vector3 point) => Array.Exists(Physics.RaycastAll(point + new Vector3(0f, 30f, 0f), Vector3.down, 31f, Layers.World), hit => hit.collider != null && IsRock(hit.collider.ObjectName()));

            private bool IsRockFaceUpwards(Vector3 point) => Array.Exists(Physics.RaycastAll(point + new Vector3(0f, 30f, 0f), Vector3.down, 31f, Layers.World | Layers.Terrain), hit => hit.collider != null && hit.point.y - point.y > 0.01f && (hit.collider.IsOnLayer(Layer.Terrain) || IsRock(hit.collider.ObjectName())));

            private bool IsPointInsideRock(Vector3 point) => Array.Exists(Physics.OverlapSphere(point, 0.01f, Layers.World), collider => collider != null && IsRock(collider.ObjectName()));

            private List<string> _prefabs = new() { "rock", "formation", "cliff" };

            private bool IsRock(string name) => _prefabs.Exists(value => name.Contains(value, CompareOptions.OrdinalIgnoreCase));

            private static void CopyScientistPrefabFields(ScientistNPC source, HumanoidNPC target)
            {
                // Scientist loot configuration
                target.LootSpawnSlots = source.LootSpawnSlots;

                // Player/NPC movement configuration
                target.movementMask = source.movementMask;
                target.LegacyNavigation = source.LegacyNavigation;

                // Combat and damage configuration
                target.damageScale = source.damageScale;
                target.fallDamageEffect = source.fallDamageEffect;
                target.impactEffect = source.impactEffect;
                target.skeletonProperties = source.skeletonProperties;
                target.startHealth = source.startHealth;
                target.sendsHitNotification = source.sendsHitNotification;

                // Entity/network prefab configuration
                target.bounds = source.bounds;
                target.syncPosition = source.syncPosition;
                target.prefabID = source.prefabID;
            }

            private bool InstantiateEntity(List<Vector3> wander, Vector3 position, bool isStationary, bool spawnedInside, List<int> baseRoute, out HumanoidBrain brain, out HumanoidNPC npc)
            {
                const string prefabName = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
                var prefab = GameManager.server.FindPrefab(prefabName);
                var go = Facepunch.Instantiate.GameObject(prefab, position, Quaternion.identity);

                go.SetActive(false);

                go.name = prefabName;

                ScientistBrain scientistBrain = go.GetComponent<ScientistBrain>();
                ScientistNPC scientistNpc = go.GetComponent<ScientistNPC>();

                //string json = JsonUtility.ToJson(scientistNpc); // requires reference to UnityEngine.JSONSerializeModule, pass :/

                npc = go.AddComponent<HumanoidNPC>();

                //JsonUtility.FromJsonOverwrite(json, npc);
                CopyScientistPrefabFields(scientistNpc, npc);

                npc.Instance = Instance;

                brain = go.AddComponent<HumanoidBrain>();

                brain.raid = this;
                brain.Instance = Instance;
                brain.SetRange(Options.NPC.AggressionRange);
                brain.RandomRoamPositions = wander;
                brain.RandomNearPositions = GetPositionsNearestTo(wander, Location, SqrProtectionRadius / 2f);
                brain.DestinationOverride = position;
                brain.CheckLOS = brain.RefreshKnownLOS = true;
                brain.Settings = Options.NPC;
                brain.BaseRoute = baseRoute;
                brain.isStationary = isStationary;
                brain.spawnedInside = spawnedInside;
                brain.UseAIDesign = false;
                brain._baseEntity = npc;
                brain.npc = npc;
                brain.thinker = npc;
                brain.NpcTransform = npc.transform;
                brain.states ??= new();

                npc.Brain = brain;

                DestroyImmediate(scientistBrain, true);
                DestroyImmediate(scientistNpc, true);

                SceneManager.MoveGameObjectToScene(go, Rust.Server.EntityScene);

                if (isStationary && go.TryGetComponent(out Rust.Ai.Gen2.RustNavMeshAgent navAgent))
                {
                    navAgent.updatePosition = false;
                    go.transform.position = position;
                }

                go.SetActive(true);

                return npc != null;
            }

            private List<Vector3> GetPositionsNearestTo(List<Vector3> positions, Vector3 origin, float sqrRange)
            {
                List<Vector3> nearby = Pool.Get<List<Vector3>>();
                foreach (var position in positions)
                {
                    if ((origin - position).sqrMagnitude < sqrRange)
                    {
                        nearby.Add(position);
                    }
                }
                if (nearby.Count == 0)
                {
                    nearby.AddRange(positions);
                }
                return nearby;
            }

            private List<Vector3> GetWanderPositions(float radius)
            {
                const int desiredPositions = 10;
                const int maxAttempts = 30;

                List<Vector3> positions = Pool.Get<List<Vector3>>();
                bool doNotRaycast = spawns != null && spawns.IsCustomSpawn; // use the intended vertical level

                for (int attempts = 0; attempts < maxAttempts && positions.Count < desiredPositions; attempts++)
                {
                    Vector3 target = Location + UnityEngine.Random.onUnitSphere * radius;

                    target.y = doNotRaycast ? Location.y : Instance.GetSpawnHeight(target);

                    if (FindPointOnNavmesh(target, radius, out var position))
                    {
                        positions.Add(position);
                    }
                }

                return positions;
            }

            private float GetRoamRadius() => Mathf.Clamp(Options.ArenaWalls.Radius, CELL_SIZE, Mathf.Min(Options.NPC.AggressionRange, ProtectionRadius * 0.9f));

            private float GetSpawnRadius() => Mathf.Clamp(Options.ArenaWalls.Radius, CELL_SIZE, ProtectionRadius * 0.9f);

            private HumanoidNPC SpawnNpc(bool isMurderer, int respawnsRemaining = int.MinValue)
            {
                if (!TryGetNpcSpawn(isMurderer, out bool isStationary, out bool spawnedInside, out var position, out var positions, out var baseRoute))
                {
                    return null;
                }

                if (!InstantiateEntity(positions, position, isStationary, spawnedInside, baseRoute, out var brain, out var npc))
                {
                    ResetToPool(ref positions);
                    ResetToPool(ref baseRoute);
                    return null;
                }

                if (spawnedInside)
                {
                    npcAmountInside++;
                    isMurderer = false;
                }

                if (respawnsRemaining == int.MinValue)
                {
                    int max = Options.RespawnAmountMax;

                    if (max < 0)
                    {
                        respawnsRemaining = -1;
                    }
                    else
                    {
                        int min = Mathf.Clamp(Options.RespawnAmountMin, 0, max);
                        respawnsRemaining = min < max ? UnityEngine.Random.Range(min, max + 1) : max;
                    }
                }

                brain.respawnsRemaining = respawnsRemaining;

                ulong userid = BotIdCounter++;

                SetupNpcIdentity(npc, brain, userid, isMurderer);

                Authorize(npc);
                npcs.Add(npc);

                npc.loadouts = Array.Empty<PlayerInventoryProperties>();
                npc.RadioChatterEffects = Array.Empty<GameObjectRef>();
                npc.radioChatterType = ScientistNPC.RadioChatterType.NONE;
                npc.EnableSaving(false);
                npc.Spawn();
                npc.CancelInvoke(npc.EquipTest);

                brain.TryStartSleeping();

                BasePlayer.bots.Remove(npc);

                SetupNpc(npc, brain);

                return npc;
            }

            private bool TryGetNpcSpawn(bool isMurderer, out bool isStationary, out bool spawnedInside, out Vector3 position, out List<Vector3> positions, out List<int> baseRoute)
            {
                isStationary = false;
                spawnedInside = false;
                position = default;
                positions = null;
                baseRoute = null;

                if (isMurderer && !Options.NPC.Inside.SpawnMurderersOutside)
                {
                    return false;
                }

                spawnedInside = SpawnInsideBase(isMurderer, out position);

                if (!isMurderer && !Options.NPC.Inside.SpawnScientistsOutside && !spawnedInside)
                {
                    return false;
                }

                if (spawnedInside)
                {
                    baseRoute = Options.NPC.Inside.Roam && !Options.NPC.Inside.Sleepers.Enabled ? CreateBaseRoute(position) : Pool.Get<List<int>>();

                    positions = Pool.Get<List<Vector3>>();

                    for (int i = 0; i < baseRoute.Count; i++)
                    {
                        if (TryGetBaseRoutePosition(baseRoute[i], out var routePosition))
                        {
                            positions.Add(routePosition);
                        }
                    }

                    if (positions.Count == 0)
                    {
                        positions.Add(position);
                    }

                    isStationary = baseRoute.Count < 2;
                    if (position != Vector3.zero)
                    {
                        return true;
                    }

                    ResetToPool(ref positions);
                    ResetToPool(ref baseRoute);
                    return false;
                }

                positions = GetWanderPositions(GetRoamRadius());

                if (positions.Count == 0)
                {
                    ResetToPool(ref positions);
                    positions = GetWanderPositions(GetSpawnRadius());
                }

                if (positions.Count == 0)
                {
                    ResetToPool(ref positions);
                    return false;
                }

                if (position == default)
                {
                    position = positions.GetRandom();
                }

                return position != Vector3.zero;
            }

            private void SetupNpcIdentity(HumanoidNPC npc, HumanoidBrain brain, ulong userid, bool isMurderer)
            {
                npc.skinID = RB_SKIN_ID;
                npc.userID = userid;
                npc.UserIDString = userid.ToString();

                if (Options.NPC.UseRandomNames)
                {
                    List<string> names = isMurderer
                        ? Options.NPC.RandomMurdererNames
                        : Options.NPC.RandomScientistNames;

                    string displayName = names.Count > 0
                        ? names.GetRandom()
                        : RandomUsernames.Get(userid);

                    if (Options.NPC.Capitalize)
                    {
                        displayName = displayName.TitleCase();
                    }

                    npc.displayName = npc.DisplayNameOverride = displayName;
                }

                brain.displayName = npc.displayName;
                brain.userid = userid;
                brain.isMurderer = isMurderer;

                Instance.HumanoidBrains[userid] = brain;
            }

            public class Loadout
            {
                public List<PlayerInventoryProperties.ItemAmountSkinned> belt = new();
                public List<PlayerInventoryProperties.ItemAmountSkinned> main = new();
                public List<PlayerInventoryProperties.ItemAmountSkinned> wear = new();
            }

            private PlayerInventoryProperties GetLoadout(HumanoidBrain brain)
            {
                var loadout = CreateLoadout(brain);
                var pip = ScriptableObject.CreateInstance<PlayerInventoryProperties>();

                if (pip.DeathIconPrefab == null)
                {
                    pip.DeathIconPrefab = new();
                    pip.DeathIconPrefab.guid = "6ff1ff9ea7408824ab5c8f6f3d9ab259";
                }

                pip.belt = loadout.belt;
                pip.main = loadout.main;
                pip.wear = loadout.wear;

                return pip;
            }

            private Loadout CreateLoadout(HumanoidBrain brain) => CreateLoadout(brain.isMurderer);

            private Loadout CreateLoadout(bool isMurderer)
            {
                var loadout = new Loadout();
                var items = isMurderer ? Options.NPC.MurdererLoadout : Options.NPC.ScientistLoadout;

                if (items == null)
                    return loadout;

                AddItemAmountSkinned(loadout.wear, items.Boots);
                AddItemAmountSkinned(loadout.wear, items.Gloves);
                AddItemAmountSkinned(loadout.wear, items.Helm);
                AddItemAmountSkinned(loadout.wear, items.Pants);
                AddItemAmountSkinned(loadout.wear, items.Shirt);
                AddItemAmountSkinned(loadout.wear, items.Torso);
                if (!items.Torso.Exists(v => v.Contains("suit")))
                {
                    AddItemAmountSkinned(loadout.wear, items.Kilts);
                }
                AddItemAmountSkinned(loadout.belt, items.Weapon);

                return loadout;
            }

            private void AddItemAmountSkinned(List<PlayerInventoryProperties.ItemAmountSkinned> source, List<string> shortnames)
            {
                if (shortnames.IsNullOrEmpty())
                {
                    return;
                }

                string shortname = shortnames.GetRandom();

                if (string.IsNullOrWhiteSpace(shortname))
                {
                    return;
                }

                ItemDefinition def = ItemManager.FindItemDefinition(shortname);

                if (def == null)
                {
                    Puts("Invalid shortname {0} in profile {1}", shortname, ProfileName);
                    return;
                }

                if (Instance.IsThrownWeaponItemDefinition(def))
                {
                    if (npcAmountThrown >= Options.NPC.Thrown)
                    {
                        shortnames.Remove(shortname);
                        AddItemAmountSkinned(source, shortnames);
                        return;
                    }
                    else npcAmountThrown++;
                }

                ulong skin = GetItemSkin(def, SkinType.Npc, 0uL, config.Skins.Npc.Unique, config.Skins.Npc.Unique, config.Skins.Npc.Random, config.Skins.Npc.Workshop, config.Skins.Npc.ImportedWorkshop, config.Skins.Npc.ApprovedOnly, def.stackable);

                source.Add(new()
                {
                    amount = 1,
                    itemDef = def,
                    skinOverride = skin,
                    startAmount = 1
                });
            }

            private List<string> _murdererPrefabNames = new() { "scarecrow", "scarecrow_dungeon", "scarecrow_dungeonnoroam" };

            private void SetupNpc(HumanoidNPC npc, HumanoidBrain brain)
            {
                if (!Options.NPC.AlternateScientistLoot.None)
                {
                    SetupAlternateLoot(npc, brain);
                }
                else npc.LootSpawnSlots = Array.Empty<LootContainer.LootSpawnSlot>();

                npc.CancelInvoke(npc.PlayRadioChatter);
                npc.DeathEffects = Array.Empty<GameObjectRef>();
                npc.RadioChatterEffects = Array.Empty<GameObjectRef>();
                npc.radioChatterType = ScientistNPC.RadioChatterType.NONE;
                npc.startHealth = brain.isMurderer ? Options.NPC.MurdererHealth : Options.NPC.ScientistHealth;
                npc.InitializeHealth(npc.startHealth, npc.startHealth);
                bool isMurderer = brain.isMurderer;
                npc.Invoke(() => GiveKit(npc, brain, isMurderer), 0.2f);
            }

            private void SetupAlternateLoot(HumanoidNPC npc, HumanoidBrain brain)
            {
                var loot = Options.NPC.AlternateScientistLoot;

                if (loot.Enabled && loot.IDs.Count > 0)
                {
                    using var ids = loot.IDs.ToPooledList();
                    if (!brain.isMurderer)
                    {
                        ids.RemoveAll(x => _murdererPrefabNames.Contains(x));
                    }
                    else if (ids.Exists(_murdererPrefabNames.Contains))
                    {
                        ids.RemoveAll(x => !_murdererPrefabNames.Contains(x));
                    }
                    if (ids.Count > 0 && loot.GetRandom(ids, out var prefabID) && StringPool.toString.TryGetValue(prefabID, out var prefab))
                    {
                        GameObject go = GameManager.server.FindPrefab(prefab);
                        if (go != null && go.TryGetComponent(out ScarecrowNPC obj2))
                        {
                            npc.LootSpawnSlots = CopyLootSpawnSlots(obj2.LootSpawnSlots);
                            brain.LootSpawnSlotsPrefabID = prefabID;
                        }
                        else if (go != null && go.TryGetComponent(out ScientistNPC obj1))
                        {
                            npc.LootSpawnSlots = CopyLootSpawnSlots(obj1.LootSpawnSlots);
                            brain.LootSpawnSlotsPrefabID = prefabID;
                        }
                    }
                }
            }

            private static LootContainer.LootSpawnSlot[] CopyLootSpawnSlots(LootContainer.LootSpawnSlot[] source)
            {
                if (source == null || source.Length == 0)
                {
                    return Array.Empty<LootContainer.LootSpawnSlot>();
                }

                var copy = new LootContainer.LootSpawnSlot[source.Length];
                Array.Copy(source, copy, source.Length);

                for (int i = 0; i < copy.Length; i++)
                {
                    Era[] eras = source[i].eras;

                    if (eras == null || eras.Length == 0)
                    {
                        continue;
                    }

                    copy[i].eras = new Era[eras.Length];
                    Array.Copy(eras, copy[i].eras, eras.Length);
                }

                return copy;
            }

            private void SetCopyLoadout(HumanoidBrain brain)
            {
                if (brain.isSleeper ? Options.NPC.Inside.Sleepers.CopyLoadout : Options.NPC.CopyLoadout)
                {
                    brain.keepInventory = true;
                }
            }

            private void SetCopyKit(HumanoidBrain brain)
            {
                if (brain.isSleeper ? Options.NPC.Inside.Sleepers.CopyKit : Options.NPC.CopyKit)
                {
                    brain.keepInventory = true;
                }
            }

            private void GiveKit(HumanoidNPC npc, HumanoidBrain brain, bool isMurderer)
            {
                if (npc.IsDestroyed || brain == null || brain.isKilled)
                    return;

                List<string> kits = isMurderer ? murdererKits : scientistKits;
                bool isKitted = false;

                try
                {
                    isKitted = GiveKit(npc, brain, isMurderer, kits);
                }
                catch (Exception ex)
                {
                    Puts("Kits plugin has thrown an error: {0}", ex);
                }

                using var itemList = npc.GetAllItems();

                bool isInventoryEmpty = itemList.Count == 0;

                if (isInventoryEmpty)
                {
                    var loadout = GetLoadout(brain);

                    if (loadout.belt.Count > 0 || loadout.main.Count > 0 || loadout.wear.Count > 0)
                    {
                        npc.loadouts = new PlayerInventoryProperties[1];
                        npc.loadouts[0] = loadout;
                        npc.EquipLoadout(npc.loadouts);
                        SetCopyLoadout(brain);
                        isInventoryEmpty = false;
                    }
                }

                if (isInventoryEmpty)
                {
                    npc.inventory.GiveItem(ItemManager.CreateByName(isMurderer ? "pants" : "hazmatsuit", 1, 0uL), npc.inventory.containerWear);
                    npc.inventory.GiveItem(ItemManager.CreateByName(isMurderer ? "machete" : "pistol.python", 1, 0), npc.inventory.containerBelt);
                }

                npc.Invoke(() => UpdateItemsAndMovement(npc, brain, isMurderer, isKitted), 0.2f);
            }

            private bool GiveKit(HumanoidNPC npc, HumanoidBrain brain, bool isMurderer, List<string> kits)
            {
                if (kits.Count > 0)
                {
                    string kit = kits.GetRandom();

                    if (Options.NPC.UniqueKits && (isMurderer && Options.NPC.MurdererKits.Count >= npcMaxAmountMurderers || !isMurderer && Options.NPC.ScientistKits.Count >= npcMaxAmountScientists) && kits.Count - 1 > 0)
                    {
                        kits.Remove(kit);
                    }

                    object plugin = Instance.Kits ?? Instance.Mercatura;

                    if (plugin?.Call("GiveKit", npc, kit) is string val)
                    {
                        if (val.Contains("Couldn't find the player"))
                        {
                            val = "Npcs cannot use the CopyPasteFile field in Kits";
                        }
                        Puts("Invalid kit '{0}' ({1})", kit, val);
                    }
                    else
                    {
                        SetCopyKit(brain);
                        return true;
                    }
                }
                return false;
            }

            private int failedUpdates;
            private void UpdateItemsAndMovement(HumanoidNPC npc, HumanoidBrain brain, bool isMurderer, bool isKitted)
            {
                if (npc.IsDestroyed || brain == null || brain.isKilled)
                    return;

                try
                {
                    brain.Init();
                }
                catch
                {
                    int respawnsRemaining = brain.respawnsRemaining;

                    brain.respawnsRemaining = 0;
                    SafelyKillNpc(npc);

                    if (++failedUpdates < npcMaxAmountMurderers + npcMaxAmountScientists)
                    {
                        Invoke(() => RespawnNpcNow(isMurderer, respawnsRemaining), 1f);
                    }

                    return;
                }

                EquipWeapon(npc, brain, isKitted);

                if (!ToggleNpcMinerHat(npc, TOD_Sky.Instance?.IsNight == true))
                {
                    npc.inventory.ServerUpdate(0f);
                }

                npc.Invoke(() =>
                {
                    if (!npc.IsDestroyed && brain != null && !brain.isKilled)
                    {
                        brain.SetupMovement();
                    }
                }, 0.1f);
            }

            public void EquipWeapon(HumanoidNPC npc, HumanoidBrain brain, bool isKitted)
            {
                bool isHoldingProjectileWeapon = false;

                using var itemList = npc.GetAllItems();

                foreach (Item item in itemList)
                {
                    if (item == null || item.info == null) continue;
                    if (isKitted && config.Skins.Npc.CanSkinKit(item.skin, brain.isMurderer))
                    {
                        item.skin = GetItemSkin(item.info, SkinType.Npc, 0uL, config.Skins.Npc.Unique, config.Skins.Npc.Unique, config.Skins.Npc.Random, config.Skins.Npc.Workshop, config.Skins.Npc.ImportedWorkshop, config.Skins.Npc.ApprovedOnly, item.info.stackable);
                    }

                    if (item.GetHeldEntity() is HeldEntity e && e.IsValid())
                    {
                        if (item.skin != 0)
                        {
                            e.skinID = item.skin;
                            e.SendNetworkUpdate();
                        }

                        if (e is Shield && (item.position != 7 || item.parent != npc.inventory.containerWear))
                        {
                            if (!item.MoveToContainer(npc.inventory.containerWear, 7, false))
                            {
                                item.Remove();
                            }
                            continue;
                        }

                        if (!(e is AttackEntity attackEntity))
                        {
                            continue;
                        }

                        if (!isHoldingProjectileWeapon && attackEntity.hostileScore >= 2f && item.GetRootContainer() == npc.inventory.containerBelt && brain._attackEntity.IsNull())
                        {
                            isHoldingProjectileWeapon = e is BaseProjectile;

                            brain.UpdateWeapon(attackEntity, item.uid);
                        }

                        if (attackEntity is MedicalTool tool)
                        {
                            brain.MedicalTools.Add(tool.GetItem());
                        }
                        else if (attackEntity.hostileScore >= 2f)
                        {
                            brain.AttackWeapons.Add(attackEntity);
                        }
                    }

                    item.MarkDirty();
                }

                brain.EnableMedicalTools();

                brain.IdentifyWeapon();
            }

            private static bool IsNpcPlatform(string shortPrefabName) => shortPrefabName is "floor" or "floor.triangle" or "roof" or "roof.triangle";

            private void SortRandomNpcSpots()
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    var block = blocks[i];
                    if (block.IsKilled() || !Options.NPC.Roofcampers && IsNpcPlatform(block.ShortPrefabName) && IsOutside(block))
                    {
                        continue;
                    }
                    var center = block.CenterPoint();
                    if (Physics.Raycast(center + new Vector3(0f, block.bounds.extents.y + 0.45f), Vector3.up, 1.75f, Layers.Mask.Construction | Layers.Mask.Deployed))
                    {
                        continue;
                    }
                    using var tmp = FindEntitiesOfType<BaseEntity>(block.transform.position + Vector3.up * 1.25f, 1.5f, blockLayers);
                    if (!tmp.Exists(e => Instance.DeployableItems.ContainsKey(e.PrefabName) && e.bounds.extents.y > 0.2f))
                    {
                        var walls = tmp.Count(e => e.ShortPrefabName == "wall");
                        if (walls < 3 && block.ShortPrefabName == "foundation.triangle" || walls < 4 && block.ShortPrefabName == "foundation")
                        {
                            _inside.Add(center + new Vector3(0f, block.bounds.extents.y + 0.1125f));
                        }
                        else if (walls < 3 && block.ShortPrefabName == "floor.triangle" || walls < 4 && block.ShortPrefabName == "floor")
                        {
                            _inside.Add(center + new Vector3(0f, 0.155f));
                        }
                    }
                }
            }

            private bool IsOutside(BaseEntity entity) => entity.IsOutside(entity.WorldSpaceBounds().position.WithY(entity.transform.position.y));

            public bool SpawnInsideBase(bool isMurderer, out Vector3 position)
            {
                position = default;
                if (isMurderer) return false;
                if (npcMaxAmountInside == -1) npcMaxAmountInside = npcMaxAmountScientists;
                if (npcAmountInside >= npcMaxAmountInside) return false;
                return TryGetRugSpawnPosition(out position) || TryGetBedSpawnPosition(out position) || TryGetFloorSpawnPosition(out position);
            }

            private bool TryGetRugSpawnPosition(out Vector3 v) => TryGetEntitySpawnPosition(_rugSpawns, Options.NPC.Inside.SpawnOnRugs, out v);

            private bool TryGetBedSpawnPosition(out Vector3 v) => TryGetEntitySpawnPosition(_beds, Options.NPC.Inside.SpawnOnBeds, out v);

            private bool TryGetFloorSpawnPosition(out Vector3 position)
            {
                using var positions = DisposableList<Vector3>();

                if (Options.NPC.Inside.SpawnOnFloors)
                {
                    if (_inside.Count == 0)
                    {
                        SortRandomNpcSpots();
                    }

                    foreach (var candidate in _inside)
                    {
                        if (IsFloorSpawnPositionAvailable(candidate))
                        {
                            positions.Add(candidate);
                        }
                    }
                }

                return TryGetRandomPosition(positions, out position);
            }

            private bool IsFloorSpawnPositionAvailable(Vector3 position) => position switch
            {
                _ when IsNpcSpawnExcluded(position) => false,
                _ when IsNpcNearSpot(position) => false,
                _ when Options.NPC.Inside.SpawnOnRugs && IsAnyRugNearSpot(position) => false,
                _ when Options.NPC.Inside.SpawnOnBeds && IsEntityNearSpot(_beds, position, 1f) => false,
                _ => true
            };

            private bool IsAnyRugNearSpot(Vector3 position) => IsEntityNearSpot(_allRugs, position, 1f);

            private bool IsEntityNearSpot<T>(List<T> source, Vector3 position, float radius) where T : BaseEntity
            {
                foreach (var entity in source)
                {
                    if (!entity.IsKilled() && InRange(entity.transform.position, position, radius))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool IsNpcSpawnExcluded(Vector3 position)
            {
                foreach (var exclusion in NpcSpawnExclusions)
                {
                    if (InRange(exclusion, position, 1.5f))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool IsNpcNearSpot(Vector3 position) => IsEntityNearSpot(npcs, position, 0.5f);

            private bool TryGetEntitySpawnPosition<T>(List<T> source, bool enabled, out Vector3 position) where T : BaseEntity
            {
                using var positions = DisposableList<Vector3>();

                if (enabled)
                {
                    foreach (var entity in source)
                    {
                        if (entity.IsKilled())
                        {
                            continue;
                        }

                        Vector3 candidate = entity.transform.position;

                        if (!IsNpcNearSpot(candidate) && !IsNpcSpawnExcluded(candidate))
                        {
                            positions.Add(candidate);
                        }
                    }
                }

                return TryGetRandomPosition(positions, out position);
            }

            private static bool TryGetRandomPosition(List<Vector3> positions, out Vector3 position)
            {
                if (positions.Count == 0)
                {
                    position = default;
                    return false;
                }

                position = positions.GetRandom();
                return true;
            }

            private void SetupNpcKits()
            {
                if (npcMaxAmountScientists > 0 || npcMaxAmountMurderers > 0)
                {
                    object plugin = Instance.Kits ?? Instance.Mercatura;
                    scientistKits.AddRange(Options.NPC.ScientistKits.Where(kit => Convert.ToBoolean(plugin?.Call("isKit", kit))));
                    murdererKits.AddRange(Options.NPC.MurdererKits.Where(kit => Convert.ToBoolean(plugin?.Call("isKit", kit))));
                    SpawnNpcs();
                }
            }

            //public string DespawnString => despawnDateTime == DateTime.MaxValue ? string.Empty : $"[{DespawnTime}m]";
            public string DespawnString => double.IsPositiveInfinity(despawnDeadline) ? string.Empty : $"[{DespawnTime}m]";

            //public double DespawnTime => despawnDateTime != DateTime.MaxValue && DespawnMinutesInactive > 0 && despawnDateTime.Subtract(DateTime.Now).TotalSeconds > 0 ? Math.Ceiling(despawnDateTime.Subtract(DateTime.Now).TotalMinutes) : 0;
            public double DespawnTime => DespawnSecondsRemaining > 0d ? Math.Ceiling(DespawnSecondsRemaining / 60d) : 0d;
            internal double DespawnSecondsRemaining => double.IsPositiveInfinity(despawnDeadline) ? 0d : Math.Max(0d, despawnDeadline - Time.realtimeSinceStartupAsDouble);

            public string MarkerName => string.IsNullOrWhiteSpace(Options.Setup.MarkerName) ? config.Settings.Markers.MarkerName : Options.Setup.MarkerName;

            public void ForceUpdateMarker()
            {
                _currentSphereColor = SphereColor.None;
                markerCreated = false;
                DestroyMapMarkers();
                CreateGenericMarker();
                UpdateMarker();
                DestroySpheres();
                CreateSpheres();
            }

            public void UpdateMarker()
            {
                if (IsDespawning)
                {
                    return;
                }

                if (IsLoading)
                {
                    Invoke(UpdateMarker, 1f);
                    return;
                }

                if (!genericMarker.IsKilled())
                {
                    genericMarker.SendUpdate();
                }

                if (!explosionMarker.IsKilled())
                {
                    explosionMarker.transform.position = Location;
                    explosionMarker.SendNetworkUpdate();
                }

                if (!vendingMarker.IsKilled())
                {
                    bool showDespawnTime = AllowPVP ? !Options.HideDespawnTimePVP : !Options.HideDespawnTimePVE;
                    string despawnText = showDespawnTime && DespawnTime > 0 ? string.Format(" [{0}]", mx("UIFormatLockoutMinutes", null, DespawnTime)) : null;
                    vendingMarker.transform.position = Location;
                    vendingMarker.markerShopName = (markerName == MarkerName ? mx("MapMarkerOrderWithMode", null, mx(GetAllowKey()), Mode(), markerName, despawnText) : string.Format("{0} {1}", mx(GetAllowKey()), markerName)).Replace("{basename}", BaseName).Trim();
                    vendingMarker.SendNetworkUpdate();
                }

                if (markerCreated || !IsMarkerAllowed())
                {
                    return;
                }

                if (config.Settings.Markers.UseVendingMarker)
                {
                    vendingMarker = GameManager.server.CreateEntity(StringPool.Get(3459945130), Location) as VendingMachineMapMarker;

                    if (!vendingMarker.IsNull())
                    {
                        string flag = mx(GetAllowKey());
                        string despawnText = DespawnMinutesInactive > 0 ? string.Format(" [{0}m]", DespawnMinutesInactive.ToString()) : null;

                        if (markerName == MarkerName)
                        {
                            vendingMarker.markerShopName = mx("MapMarkerOrderWithMode", null, flag, Mode(), markerName, despawnText).Replace("{basename}", BaseName);
                        }
                        else vendingMarker.markerShopName = mx("MapMarkerOrderWithoutMode", null, flag, markerName, despawnText).Replace("{basename}", BaseName);

                        vendingMarker.enableSaving = false;
                        vendingMarker.enabled = false;
                        vendingMarker.Spawn();

                        if (Options.AdditionalBases.TryGetValue(BaseName, out var currencies) && currencies.Any)
                        {
                            vendingMarker.SetVendingMachine(SpawnVendingMachine(currencies.Costs), vendingMarker.markerShopName);
                        }
                    }
                }
                else if (config.Settings.Markers.UseExplosionMarker)
                {
                    explosionMarker = GameManager.server.CreateEntity(StringPool.Get(4060989661), Location) as MapMarkerExplosion;

                    if (!explosionMarker.IsNull())
                    {
                        explosionMarker.Spawn();
                        explosionMarker.Invoke(() => explosionMarker.CancelInvoke(explosionMarker.DelayedDestroy), 1f);
                    }
                }

                markerCreated = true;
                UpdateMarker();
            }

            private VendingMachine SpawnVendingMachine(List<AdditionalBaseCosts> costs)
            {
                VendingMachine vm = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab", Location.WithY(-400f)) as VendingMachine;
                vm.dropsLoot = false;
                vm.enableSaving = false;
                vm.limitNetworking = true;
                vm.Spawn();
                vm.SetFlagLocal(BaseEntity.Flags.Reserved4, false);
                vm.FullUpdate();
                foreach (var cost in costs)
                {
                    if (!cost.Enabled) continue;
                    var def = ItemManager.FindItemDefinition(cost.currencyToUse);
                    if (def == null) continue;
                    vm.AddSellOrder(696029452, 1, def.itemid, cost.currencyAmount, 0);
                }
                return vm;
            }

            private void CreateGenericMarker()
            {
                if (IsMarkerAllowed() && (config.Settings.Markers.UseExplosionMarker || config.Settings.Markers.UseVendingMarker))
                {
                    float radius = Mathf.Min(2.5f, World.Size <= 3600 ? config.Settings.Markers.SubRadius : config.Settings.Markers.Radius);
                    if (radius > 0f)
                    {
                        genericMarker = GameManager.server.CreateEntity(StringPool.Get(2849728229), Location) as MapMarkerGenericRadius;
                        if (!genericMarker.IsNull())
                        {
                            genericMarker.alpha = 0.75f;
                            genericMarker.color1 = GetMarkerColor1();
                            genericMarker.color2 = GetMarkerColor2();
                            genericMarker.radius = radius;
                            genericMarker.enableSaving = false;
                            genericMarker.Spawn();
                            genericMarker.SendUpdate();
                        }
                    }
                }
                if (config.Settings.Buyable.PersonalMarker)
                {
                    var player = BasePlayer.FindByID(ownerId);
                    if (player == null || !player.IsConnected || player.State == null || player.State.pointsOfInterest == null) return;
                    if (InRange(player.transform.position, Location, ProtectionRadius + 50f)) return;
                    mapNote = Pool.Get<ProtoBuf.MapNote>();
                    mapNote.colourIndex = player.State.pointsOfInterest.Count;
                    mapNote.totalDuration = 3f;
                    mapNote.icon = 1;
                    mapNote.isPing = false;
                    mapNote.label = mx("My Purchased Raid Base", player.UserIDString);
                    mapNote.noteType = (int)BasePlayer.MapNoteType.PointOfInterest;
                    mapNote.worldPosition = Location;
                    mapNote.ShouldPool = true;
                    player.State.pointsOfInterest.Add(mapNote);
                    player.DirtyPlayerState();
                    player.SendMarkersToClient();
                }
            }

            private void DestroyMapNote(BasePlayer owner)
            {
                if (mapNote == null)
                {
                    return;
                }
                if (owner.State?.pointsOfInterest?.Remove(mapNote) == true)
                {
                    owner.DirtyPlayerState();
                    owner.SendMarkersToClient();
                    owner.TeamUpdate();
                }
                if (mapNote.ShouldPool)
                {
                    mapNote.Dispose();
                }
                mapNote = null;
            }

            private ProtoBuf.MapNote mapNote;

            private bool TryParseHtmlString(string value, out Color color) => ColorUtility.TryParseHtmlString(value.StartsWith('#') ? value : $"#{value}", out color);

            private Color GetMarkerColor1() => Type == RaidableType.None ? Color.clear : TryParseHtmlString(config.Settings.Management.Colors1.Get(Options.Mode), out var colorDefault) ? colorDefault : Color.cyan;

            private Color GetMarkerColor2() => Type == RaidableType.None ? NoneColor : TryParseHtmlString(config.Settings.Management.Colors2.Get(Options.Mode), out var color) ? color : Color.cyan;

            private bool IsMarkerAllowed() => !Options.Silent && Type switch
            {
                RaidableType.Maintained => config.Settings.Markers.Maintained,
                RaidableType.Purchased => config.Settings.Markers.Buyables,
                RaidableType.Scheduled => config.Settings.Markers.Scheduled,
                _ => config.Settings.Markers.Manual
            };

            public void DestroyLocks()
            {
                locks.ForEach(SafelyKill);
            }

            public void DestroyNpcs()
            {
                using var kill = npcs.ToPooledList();
                foreach (var npc in kill)
                {
                    if (!npc.IsRealNull() && Instance.HumanoidBrains.TryGetValue(npc.userID, out var brain))
                    {
                        brain.DisableShouldThink();
                        if (!brain.AttackEntity.IsKilled())
                        {
                            brain.AttackEntity.SetHeld(false);
                        }
                    }
                    SafelyKillNpc(npc);
                }
                npcs.Clear();
            }

            public void DestroySpheres()
            {
                spheres.ForEach(SafelyKill);
                spheres.Clear();
            }

            public void DestroyMapMarkers()
            {
                if (!explosionMarker.IsKilled())
                {
                    explosionMarker.CancelInvoke(explosionMarker.DelayedDestroy);
                    explosionMarker.Kill();
                }

                genericMarker.SafelyKill();
                vendingMarker?.server_vendingMachine.SafelyKill();
                vendingMarker.SafelyKill();
            }
        }

        public SpawnsControllerManager SpawnsController = new();

        public class SpawnsControllerManager
        {
            internal YieldInstruction instruction0;
            internal Dictionary<string, ZoneInfo> ManagedZones;
            internal List<string> assets;
            internal List<string> IgnoredColliderNames;
            internal List<string> _materialNames;
            internal List<MonumentInfoEx> Monuments = new();
            public RaidableBases Instance;
            internal Configuration config => Instance.config;

            public class MonumentInfoEx
            {
                public float radius;
                public string text;
                public Vector3 position;
                public MonumentInfoEx(string text, Vector3 position, float radius)
                {
                    this.text = text;
                    this.position = position;
                    this.radius = radius;
                }
            }

            public void Initialize()
            {
                ManagedZones = new();
                assets = new() { "perimeter_wall", "/props/", "/structures/", "/building/", "train_", "powerline_", "dune", "candy-cane", "assets/content/nature/", "assets/content/vehicles/", "walkway", "invisible_collider", "module_", "junkpile", "low_arc" };
                _materialNames = new() { "Generic (Instance)", "Concrete (Instance)", "Rock (Instance)", "Metal (Instance)", "Snow (Instance)", "Generic", "Concrete", "Rock", "Snow" }; // Fixed CreateSphere placement by removing "Metal"
                IgnoredColliderNames = new() { "powerline", "invisible", "TopCol", "swamp_", "floating_", "sentry", "walkway", "junkpile", "ore_node" };
                IgnoredColliderNames.AddRange(config.Settings.Management.IgnoredColliderNames);
            }

            public IEnumerator SetupMonuments()
            {
                int attempts = 0;
                while (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null || TerrainMeta.Path.Monuments.Count == 0)
                {
                    if (++attempts >= 30)
                    {
                        break;
                    }
                    yield return CoroutineEx.waitForSeconds(1f);
                }
                Monuments.Clear();
                if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null || TerrainMeta.Path.Monuments.Count == 0)
                {
                    yield break;
                }
                foreach (var monument in TerrainMeta.Path.Monuments)
                {
                    if (monument == null || monument.transform == null || monument.name == null || monument.name.Contains("monument_marker"))
                    {
                        continue;
                    }
                    string monumentName = monument.displayPhrase?.english == null ? "BROKEN MONUMENT" : monument.displayPhrase.english.Trim();
                    if (monumentName.Contains("Lake") || monumentName.Contains("Canyon") || monumentName.Contains("Oasis"))
                    {
                        continue;
                    }
                    float max = monument.Bounds.size.Max();
                    if (max <= 0f) max = 150f;
                    if (monumentName.Equals("Substation")) max = 50f;
                    if (max > 0f)
                    {
                        if (monumentName.Contains("Excavator") || monumentName.Contains("Airfield")) max /= 1.5f;
                        if (monumentName.Equals("Abandoned Cabins")) max /= 2f;
                        Monuments.Add(new(monumentName, monument.transform.position, max));
                        continue;
                    }
                    yield return CalculateMonumentSize(monument.transform.position, string.IsNullOrEmpty(monument.displayPhrase.english.Trim()) ? monument.name.Contains("cave") ? "Cave" : monument.name : monument.displayPhrase.english.Trim());
                }
            }

            public IEnumerator CalculateMonumentSize(Vector3 from, string text)
            {
                int checks = 0;
                float radius = 15f;
                while (radius < World.Size / 2f)
                {
                    int pointsOfTopology = 0;
                    using var vectors = GetCircumferencePositions(from, radius, degreeStep: 30f, spawnHeight: false, shouldSkipSmallRock: false, y: 0f);
                    foreach (var to in vectors)
                    {
                        if (ContainsTopology(TerrainTopology.Enum.Building | TerrainTopology.Enum.Monument, to, 5f))
                        {
                            pointsOfTopology++;
                        }
                        if (++checks >= 25)
                        {
                            yield return instruction0;
                            checks = 0;
                        }
                    }
                    if (pointsOfTopology < 4)
                    {
                        break;
                    }
                    radius += 15f;
                }
                if (radius <= 15f)
                {
                    radius = 100f;
                }
                Monuments.Add(new(text, from, radius));
            }

            public PooledList<Vector3> GetCircumferencePositions(Vector3 center, float radius, float degreeStep, bool spawnHeight = true, bool shouldSkipSmallRock = false, float y = 0f)
            {
                const float degrees = 360f;
                const float radiansPerDegree = 2f * Mathf.PI / degrees;
                var positions = DisposableList<Vector3>();

                for (float degree = 0f; degree < degrees; degree += degreeStep)
                {
                    float radian = radiansPerDegree * degree;
                    Vector3 a = new(center.x + radius * Mathf.Cos(radian), y, center.z + radius * Mathf.Sin(radian));
                    if (y == 0f)
                    {
                        a.y = spawnHeight ? GetSpawnHeight(a, true, shouldSkipSmallRock) : TerrainMeta.HeightMap.GetHeight(a);
                    }
                    positions.Add(a);
                }

                return positions;
            }

            private bool IsValidMaterial(string materialName) => materialName.Contains("rock_") || _materialNames.Contains(materialName);

            private bool ShouldSkipSmallRock(RaycastHit hit, string colName)
            {
                return (colName.Contains("rock_") || colName.Contains("formation_", CompareOptions.OrdinalIgnoreCase)) && hit.collider.bounds.size.y <= 2f;
            }

            private bool ShouldIgnoreCollider(string colName)
            {
                for (int i = 0; i < IgnoredColliderNames.Count; i++)
                {
                    if (colName.Contains(IgnoredColliderNames[i]))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool ShouldIgnoreEntityCollider(RaycastHit hit)
            {
                BaseEntity entity = hit.GetEntity();

                if (entity.IsNull())
                {
                    return false;
                }

                return entity.net == null || !Instance.EntityToRaid.TryGetValue(entity.net.ID, out var raid) || raid == null || raid.IsDespawning;
            }

            private const float spawnHeightRayOffset = 48f;
            private readonly RaycastHit[] hitBuffer = new RaycastHit[32768];

            public float GetSpawnHeight(Vector3 v, bool max = true, bool skip = false, int mask = targetMask, BasePlayer player = null)
            {
                float y = TerrainMeta.HeightMap.GetHeight(v);
                if (y > Instance.MaxTerrainY) Instance.MaxTerrainY = y;
                Vector3 origin = v;
                origin.y = Mathf.Max(v.y, Instance.MaxTerrainY) + spawnHeightRayOffset;
                if (player != null) DrawText(player, 30f, Color.cyan, v, "START");
                if (player != null) DrawText(player, 30f, Color.cyan, origin, "ADJUSTED");

                int num = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, Mathf.Infinity, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < num; i++)
                {
                    RaycastHit hit = hitBuffer[i];
                    if (hit.collider == null || hit.point.y <= y) continue;
                    if (ShouldIgnoreEntityCollider(hit))
                    {
                        if (player != null) DrawText(player, 30f, Color.red, hit.point, "E");
                        continue;
                    }
                    string colName = hit.collider.ObjectName();
                    if (string.IsNullOrEmpty(colName))
                    {
                        if (player != null) DrawText(player, 30f, Color.red, hit.point, "CNA");
                        if (max) y = Mathf.Max(y, WaterLevel.GetWaterOrTerrainSurface(hit.point, waves: false, volumes: false));
                        continue;
                    }
                    if (skip && ShouldSkipSmallRock(hit, colName))
                    {
                        if (player != null) DrawText(player, 30f, Color.red, hit.point, "R");
                        continue;
                    }
                    if (ShouldIgnoreCollider(colName))
                    {
                        if (player != null) DrawText(player, 30f, Color.red, hit.point, "C:" + colName + " : " + LayerMask.LayerToName(hit.collider.gameObject.layer));
                        continue;
                    }
                    string matName = hit.collider.MaterialName();
                    if (!string.IsNullOrEmpty(matName) && !IsValidMaterial(matName))
                    {
                        if (player != null) DrawText(player, 30f, Color.red, hit.point, "M:" + matName);
                        continue;
                    }
                    if (player != null) DrawText(player, 30f, Color.green, hit.point, "+");
                    y = Mathf.Max(y, hit.point.y);
                }
                if (player != null)
                {
                    DrawText(player, 30f, Color.magenta, player.transform.position, "#:" + num);
                }
                return max ? Mathf.Max(y, WaterSystem.OceanLevel, TerrainMeta.WaterMap.GetHeight(v)) : y;
            }

            public bool ContainsTopology(TerrainTopology.Enum mask, Vector3 position, float radius)
            {
                return (TerrainMeta.TopologyMap.GetTopology(position, radius) & (int)mask) != 0;
            }

            public bool ContainsTopology(TerrainTopology.Enum mask, Vector3 position, float radius, int topology)
            {
                return (topology & (int)mask) != 0;
            }

            public bool IsZoneBlocked(Vector3 v)
            {
                if (ManagedZones.Count == 0)
                {
                    return false;
                }
                foreach (var zone in ManagedZones.Values)
                {
                    if (zone.IsPositionInZone(v))
                    {
                        return zone.IsBlocked;
                    }
                }
                return config.Settings.UseZoneManagerOnly;
            }

            internal bool TopologyChecks(ManagementBiomeSettings biomes, int? t, Vector3 v, float radius, float railRadius, bool spawnOnSeabed, bool checkRailPaths, out string topology)
            {
                if (biomes != null && !biomes.IsBiomeEnabled(t, v, out var biome))
                {
                    topology = $"{biome} biome disabled";
                    return false;
                }

                int top = TerrainMeta.TopologyMap.GetTopology(v, radius);
                if (!config.Settings.Management.AllowOnBeach && ContainsTopology(TerrainTopology.Enum.Beach | TerrainTopology.Enum.Beachside, v, radius, top))
                {
                    topology = "Beach or Beachside";
                    return false;
                }

                if (!config.Settings.Management.AllowInland && !ContainsTopology(TerrainTopology.Enum.Beach | TerrainTopology.Enum.Beachside, v, radius, top))
                {
                    topology = "Inland";
                    return false;
                }

                if (!config.Settings.Management.AllowOnRailroads && ContainsTopology(TerrainTopology.Enum.Rail | TerrainTopology.Enum.Railside, v, radius, top))
                {
                    topology = "Rail or Railside";
                    return false;
                }

                if (!config.Settings.Management.AllowOnBuildingTopology && ContainsTopology(TerrainTopology.Enum.Building, v, radius, top))
                {
                    topology = "Building";
                    return false;
                }

                if (!config.Settings.Management.AllowOnMonumentTopology && ContainsTopology(TerrainTopology.Enum.Monument, v, radius, top))
                {
                    topology = "Monument";
                    return false;
                }

                if (!config.Settings.Management.AllowOnRivers && ContainsTopology(TerrainTopology.Enum.River | TerrainTopology.Enum.Riverside, v, radius, top))
                {
                    topology = "River or Riverside";
                    return false;
                }

                if (!config.Settings.Management.AllowOnRoads && ContainsTopology(TerrainTopology.Enum.Road | TerrainTopology.Enum.Roadside, v, radius, top))
                {
                    topology = "Road or Roadside";
                    return false;
                }

                if (!config.Settings.Management.AllowOnRailroads && checkRailPaths)
                {
                    int pathIndex = 0;
                    int pointIndex = 0;

                    StepRailPathCheck(v, railRadius, long.MaxValue, ref pathIndex, ref pointIndex, out bool blocked);
                    if (blocked)
                    {
                        topology = "Rail or Railside";
                        return false;
                    }
                }

                topology = "";
                return true;
            }

            internal bool StepRailPathCheck(Vector3 position, float railRadius, long deadline, ref int pathIndex, ref int pointIndex, out bool blocked)
            {
                blocked = false;

                List<PathList> paths = TerrainMeta.Path?.Rails;
                if (paths == null)
                {
                    return true;
                }

                while (pathIndex < paths.Count)
                {
                    var points = paths[pathIndex]?.Path?.Points;
                    if (points == null)
                    {
                        pathIndex++;
                        pointIndex = 0;
                        continue;
                    }

                    while (pointIndex < points.Length)
                    {
                        if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                        {
                            return false;
                        }

                        Vector3 point = points[pointIndex++];
                        if (InRange(position, point, railRadius))
                        {
                            blocked = true;
                            return true;
                        }
                    }

                    pathIndex++;
                    pointIndex = 0;
                }

                return true;
            }

            public bool IsBlockedByMapPrefab(List<(Vector3 pos, float dist)> prefabs, Vector3 position)
            {
                foreach (var prefab in prefabs)
                {
                    if (InRange(prefab.pos, position, prefab.dist))
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool IsSubmerged(BuildingWaterOptions options, RaidableSpawnLocation rsl)
            {
                if (rsl.WaterHeight - rsl.TerrainHeight > options.WaterDepth)
                {
                    if (!options.AllowSubmerged)
                    {
                        return true;
                    }

                    rsl.Location.y = rsl.WaterHeight;
                }

                return !options.AllowSubmerged && options.SubmergedAreaCheck && IsSubmerged(options, rsl, rsl.Radius);
            }

            public bool IsSubmerged(BuildingWaterOptions options, RaidableSpawnLocation rsl, float radius)
            {
                if (rsl.Surroundings.Count == 0)
                {
                    using var vectors = GetCircumferencePositions(rsl.Location, radius, 90f, true, false, 1f);
                    rsl.Surroundings.AddRange(vectors);
                }

                foreach (var vector in rsl.Surroundings)
                {
                    float w = Mathf.Max(WaterSystem.OceanLevel, TerrainMeta.WaterMap.GetHeight(vector));
                    float h = GetSpawnHeight(vector, false); // TerrainMeta.HeightMap.GetHeight(vector);

                    if (w - h > options.WaterDepth)
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool IsMonumentPosition(Vector3 a, float extra)
            {
                if (ConVar.Server.hostname == "Jitters Test Server")
                {
                    return false;
                }
                foreach (var mi in Monuments)
                {
                    var dist = a.Distance2D(mi.position);
                    var dir = (mi.position - a).normalized;

                    if (dist <= mi.radius + a.Distance2D(mi.position + dir * extra) - dist)
                    {
                        return true;
                    }
                }
                return false;
            }

            private List<(Vector3 position, float radius)> safeZones = new();

            public bool IsSafeZone(Vector3 a, float extra = 0f)
            {
                if (ConVar.Server.hostname == "Jitters Test Server")
                {
                    return false;
                }
                if (safeZones.Count == 0)
                {
                    foreach (var triggerSafeZone in TriggerSafeZone.allSafeZones)
                    {
                        float radius = triggerSafeZone.triggerCollider == null ? 200f : ColliderEx.GetRadius(triggerSafeZone.triggerCollider, triggerSafeZone.transform.localScale);
                        Vector3 center = triggerSafeZone.triggerCollider?.bounds.center ?? triggerSafeZone.transform.position;
                        safeZones.Add((center, radius));
                    }
                }
                for (int i = 0; i < safeZones.Count; i++)
                {
                    var zone = safeZones[i];
                    float radius = zone.radius + extra;

                    if ((zone.position - a).sqrMagnitude <= radius * radius)
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool IsAssetBlocked(BaseEntity entity, string colName, string entityName)
            {
                if (!entity.IsNull() && !entityName.Contains("/treessource/"))
                {
                    return false;
                }

                for (int i = 0; i < assets.Count; i++)
                {
                    if (colName.Contains(assets[i]))
                    {
                        return true;
                    }
                }

                return false;
            }

            internal class ObstructionQuery
            {
                internal Vector3 From;
                internal float LandLevel;
                internal float Radius;
                internal float Degree;
                internal int RingIndex;
                internal bool Result;
                internal bool SpawnOnSeabed;
                internal bool Started;
                internal BasePlayer Player;

                internal void Reset()
                {
                    From = default;
                    LandLevel = 0f;
                    Radius = 0f;
                    Degree = 0f;
                    RingIndex = 4;
                    Result = false;
                    SpawnOnSeabed = false;
                    Started = false;
                    Player = null;
                }
            }

            internal class AreaSafetyQuery
            {
                internal readonly Collider[] Buffer = new Collider[32768];
                internal ObstructionQuery Obstruction = new();
                internal int ColliderCount;
                internal int ColliderIndex;
                internal CacheType Result;
                internal bool CliffCheckCompleted;
                internal bool CliffObstructed;
                internal bool Started;

                internal void Reset()
                {
                    ColliderCount = 0;
                    ColliderIndex = 0;
                    Result = CacheType.Generic;
                    CliffCheckCompleted = false;
                    CliffObstructed = false;
                    Started = false;
                    Obstruction.Reset();
                }

                internal void Clear()
                {
                    if (ColliderCount > 0)
                    {
                        Array.Clear(Buffer, 0, Mathf.Min(ColliderCount, Buffer.Length));
                    }

                    Reset();
                }
            }

            private AreaSafetyQuery synchronousAreaSafetyQuery = new();
            public bool IsAreaSafetyQueryAvailable => synchronousAreaSafetyQuery.Started && synchronousAreaSafetyQuery.ColliderIndex < synchronousAreaSafetyQuery.ColliderCount;

            public bool IsAreaSafe(Vector3 area, float ignoreRadius, float protectionRadius, float cupboardRadius, float worldRadius, int layers, bool isCustomSpawn, out CacheType cacheType, RaidableType type = RaidableType.None, BuildingOptionsDifficultySpawns spawns = null)
            {
                synchronousAreaSafetyQuery.Reset();

                bool completed = StepAreaSafe(area, ignoreRadius, protectionRadius, cupboardRadius, worldRadius, layers, isCustomSpawn, synchronousAreaSafetyQuery, 0L, out bool isSafe, type, spawns);

                cacheType = synchronousAreaSafetyQuery.Result;
                synchronousAreaSafetyQuery.Reset();

                return completed && isSafe;
            }

            internal bool StepAreaSafe(Vector3 area, float ignoreRadius, float protectionRadius, float cupboardRadius, float worldRadius, int layers, bool isCustomSpawn, AreaSafetyQuery query, long deadline, out bool isSafe, RaidableType type = RaidableType.None, BuildingOptionsDifficultySpawns spawns = null)
            {
                isSafe = false;

                CacheType worldType = layers == gridLayers ? CacheType.Delete : CacheType.Temporary;

                if (!query.Started)
                {
                    if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    query.Reset();
                    query.Started = true;

                    if (IsSafeZone(area, config.Settings.Management.MonumentDistance))
                    {
                        Instance.Queues.Messages.Add("Blocked by a safe zone", area);
                        query.Result = CacheType.Delete;
                        return true;
                    }

                    query.ColliderCount = Physics.OverlapSphereNonAlloc(area, Mathf.Max(worldRadius, Mathf.Max(protectionRadius, cupboardRadius)), query.Buffer, layers, QueryTriggerInteraction.Collide);

                    if (query.ColliderCount >= query.Buffer.Length)
                    {
                        Instance.Queues.Messages.Add("Too many colliders found in the area", area + ":" + query.ColliderCount);
                        query.Result = CacheType.Temporary;
                    }

                    if (deadline != 0 && query.ColliderCount > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }
                }

                while (query.ColliderIndex < query.ColliderCount)
                {
                    if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    int i = query.ColliderIndex;
                    if (query.Result != CacheType.Generic)
                    {
                        goto next;
                    }

                    var collider = query.Buffer[i];
                    if (collider == null)
                    {
                        goto next;
                    }

                    var colName = collider.ObjectName();
                    var position = collider.GetPosition();

                    if (position == Vector3.zero || colName == "ZoneManager" || colName.Contains("xmas"))
                    {
                        goto next;
                    }

                    float dist = position.Distance(area);

                    if (ignoreRadius > 0f && dist <= ignoreRadius)
                    {
                        Instance.Queues.Messages.Add("Ignored object within the configured radius", ignoreRadius);
                        goto next;
                    }

                    var e = collider.ToBaseEntity();

                    if (e is TutorialIsland || IsTutorialNetworkGroup(e))
                    {
                        Instance.Queues.Messages.Add("Blocked by Tutorial Island");
                        query.Result = CacheType.Delete;
                        goto next;
                    }

                    if (e is BuildingPrivlidge or SteeringWheel or PlayerBoat or Tugboat or BoatBuildingStation)
                    {
                        if (e.OwnerID.IsSteamId() && dist <= cupboardRadius || Instance.IsEventEntity(e, dist, protectionRadius))
                        {
                            string name = Instance.GetTypeName(e);
                            Instance.Queues.Messages.Add($"Blocked by privilege: {name}", position);
                            query.Result = CacheType.Privilege;
                        }
                        goto next;
                    }

                    string entityName = e.ObjectName();

                    if (!isCustomSpawn && IsAssetBlocked(e, colName, entityName))
                    {
                        if (layers == gridLayers || !collider.IsOnLayer(Layer.World))
                        {
                            Instance.Queues.Messages.Add("Blocked by a map prefab", $"{position} {colName}");
                            query.Result = CacheType.Delete;
                        }
                        goto next;
                    }

                    if (IsSputnik(e) || IsDangerousEvent(e))
                    {
                        if (!isCustomSpawn)
                        {
                            Instance.Queues.Messages.Add("Blocked by another event or deployable", $"{position} {colName}");
                            query.Result = CacheType.Temporary;
                        }
                        goto next;
                    }

                    if (dist > protectionRadius && (isCustomSpawn || collider.gameObject.layer != (int)Layer.World))
                    {
                        goto next;
                    }

                    if (e != null && e.net != null && !e.IsDestroyed)
                    {
                        if (e is Tugboat)
                        {
                            if (!isCustomSpawn)
                            {
                                Instance.Queues.Messages.Add("Tugboat is too close", $"{e.transform.position}");
                                query.Result = CacheType.Temporary;
                            }
                            goto next;
                        }

                        if (e.PrefabName.Contains("xmas") || entityName.StartsWith("assets/prefabs/plants") || entityName.Contains("tunnel") || e is BaseMountable or MetalDetectorSource or DroppedItem)
                        {
                            goto next;
                        }

                        bool isSteamId = e.OwnerID.IsSteamId();

                        if (e is BasePlayer player)
                        {
                            if (type != RaidableType.Manual && !(!player.IsHuman() || player.IsFlying || config.Settings.Management.EjectSleepers && player.IsSleeping()))
                            {
                                Instance.Queues.Messages.Add("Player is too close", $"{player.displayName} ({player.userID}) {e.transform.position}");
                                query.Result = CacheType.Temporary;
                                goto next;
                            }
                        }
                        else if (isSteamId && e is SleepingBag)
                        {
                            goto next;
                        }
                        else if (isSteamId && isCustomSpawn && spawns != null && spawns.Skip)
                        {
                            goto next;
                        }
                        else if (isSteamId && config.Settings.Schedule.Skip && type == RaidableType.Scheduled)
                        {
                            goto next;
                        }
                        else if (isSteamId && config.Settings.Maintained.Skip && type == RaidableType.Maintained)
                        {
                            goto next;
                        }
                        else if (isSteamId && config.Settings.Buyable.Skip && type == RaidableType.Purchased)
                        {
                            goto next;
                        }
                        else if (Instance.Has(e))
                        {
                            Instance.Queues.Messages.Add("Already occupied by a raidable base", e.transform.position);
                            query.Result = CacheType.Temporary;
                            goto next;
                        }
                        else if (e.IsNpc || e is SleepingBag)
                        {
                            goto next;
                        }
                        else if (e is BaseOven)
                        {
                            if (!isCustomSpawn && e.bounds.size.Max() > 1.6f && !CanIgnoreDeployable())
                            {
                                Instance.Queues.Messages.Add("An oven is too close", e.transform.position);
                                query.Result = CacheType.Temporary;
                                goto next;
                            }
                        }
                        else if (e is PlayerCorpse corpse)
                        {
                            if (corpse.playerSteamID == 0 || corpse.playerSteamID.IsSteamId())
                            {
                                Instance.Queues.Messages.Add("A player corpse is too close", e.transform.position);
                                query.Result = CacheType.Temporary;
                                goto next;
                            }
                        }
                        else if (e is DroppedItemContainer backpack && e.ShortPrefabName != "item_drop")
                        {
                            if (backpack.playerSteamID == 0 || backpack.playerSteamID.IsSteamId())
                            {
                                Instance.Queues.Messages.Add("A player's backpack is too close", e.transform.position);
                                query.Result = CacheType.Temporary;
                                goto next;
                            }
                        }
                        else if (!isSteamId)
                        {
                            if (e is BuildingBlock || e.ShortPrefabName.Contains("wall.external.high") || !e.enableSaving && e.HasFlag(BaseEntity.Flags.Busy) && e.HasFlag(BaseEntity.Flags.Locked))
                            {
                                Instance.Queues.Messages.Add("Blocked by an existing structure or event entity", $"{e.ShortPrefabName} {e.transform.position}");
                                query.Result = CacheType.Temporary;
                                goto next;
                            }
                            else if (e is MiningQuarry)
                            {
                                Instance.Queues.Messages.Add("A mining quarry is too close", $"{e.ShortPrefabName} {e.transform.position}");
                                query.Result = CacheType.Delete;
                                goto next;
                            }
                        }
                        else
                        {
                            if (!CanIgnoreDeployable() || !Instance.DeployableItems.ContainsKey(e.PrefabName))
                            {
                                Instance.Queues.Messages.Add("Blocked by a player-owned object", $"{e.ShortPrefabName} {e.transform.position}");
                                query.Result = CacheType.Temporary;
                            }
                            goto next;
                        }
                    }
                    else if (collider.gameObject.layer == (int)Layer.World && !isCustomSpawn)
                    {
                        bool isCliff = colName.Contains("cliff_", CompareOptions.OrdinalIgnoreCase);
                        bool isRockFormation = colName.Contains("rock_", CompareOptions.OrdinalIgnoreCase) || colName.Contains("formation_", CompareOptions.OrdinalIgnoreCase);
                        bool nearby = dist <= worldRadius;

                        if (!nearby && layers != gridLayers && (isCliff || isRockFormation))
                        {
                            Bounds bounds = collider.bounds;
                            Vector3 point = area;
                            point.y = bounds.center.y;

                            nearby = bounds.SqrDistance(point) <= worldRadius * worldRadius;
                        }

                        if (!nearby)
                        {
                            goto next;
                        }

                        if (isCliff)
                        {
                            if (!query.CliffCheckCompleted)
                            {
                                if (!StepIsCliffTerrainObstructed(area, M_RADIUS, 1f, -1, false, null, query.Obstruction, deadline, out query.CliffObstructed))
                                {
                                    return false;
                                }

                                query.CliffCheckCompleted = true;
                                query.Obstruction.Reset();
                            }

                            if (query.CliffObstructed)
                            {
                                Instance.Queues.Messages.Add("Cliff formation is too large", position);
                                query.Result = worldType;
                                goto next;
                            }
                        }
                        else if (isRockFormation)
                        {
                            if (collider.bounds.size.Max() > 2f && (e == null || e.OwnerID == 0))
                            {
                                Instance.Queues.Messages.Add("Rock is too large", position);
                                query.Result = worldType;
                                goto next;
                            }
                        }
                        else if (!config.Settings.Management.AllowOnRoads && colName.StartsWith("road_"))
                        {
                            Instance.Queues.Messages.Add("Not allowed on roads", position);
                            query.Result = CacheType.Delete;
                            goto next;
                        }
                        else if (!config.Settings.Management.AllowOnIceSheets && colName.StartsWith("ice_sheet"))
                        {
                            Instance.Queues.Messages.Add("Not allowed on ice sheets", position);
                            query.Result = CacheType.Delete;
                            goto next;
                        }
                    }
                    else if (collider.gameObject.layer == (int)Layer.Water && !isCustomSpawn)
                    {
                        if (!config.Settings.Management.AllowOnRivers && colName.StartsWith("River Mesh"))
                        {
                            Instance.Queues.Messages.Add("Not allowed on rivers", position);
                            query.Result = CacheType.Delete;
                            goto next;
                        }
                    }

                next:
                    query.Buffer[i] = null;
                    query.ColliderIndex = i + 1;
                }

                isSafe = query.Result == CacheType.Generic;
                return true;
            }

            public bool IsTutorialNetworkGroup(BaseEntity entity)
            {
                if (!entity.IsValid() || entity.net.group == null) return false;
                return TutorialIsland.IsTutorialNetworkGroup(entity.net.group.ID);
            }

            public bool CanIgnoreDeployable() => config.Settings.Management.EjectDeployables || config.Settings.Management.KillDeployables;

            public bool InDeepWater(Vector3 v, bool seabed, float minDepth, float maxDepth) // GetWaterOrTerrainSurface
            {
                v.y = TerrainMeta.HeightMap.GetHeight(v);

                float waterDepth = WaterLevel.GetWaterDepth(v, true, true, null);

                if (seabed)
                {
                    return waterDepth >= 0 - minDepth && waterDepth <= 0 - maxDepth;
                }

                return waterDepth > maxDepth;
            }

            public void SetupZones(bool message)
            {
                ManagedZones.Clear();

                if (config.Settings.AllowedZones.Contains("*"))
                {
                    return;
                }

                var zoneIds = Instance.ZoneManager?.Call("GetZoneIDs") as string[];

                if (zoneIds == null || zoneIds.Length == 0)
                {
                    return;
                }

                config.Settings.AllowedZones.RemoveAll(string.IsNullOrWhiteSpace);

                int allowed = 0, blocked = 0;

                foreach (string zoneId in zoneIds)
                {
                    var isBlocked = AddZone(zoneId);

                    if (isBlocked) { blocked++; } else { allowed++; }
                }

                if (message && (allowed > 0 || blocked > 0))
                {
                    Puts(Instance.mx("AllowedZones", null, allowed));
                    Puts(Instance.mx("BlockedZones", null, blocked));
                }
            }

            public bool AddZone(string zoneId)
            {
                var obj = Instance?.ZoneManager?.Call("ZoneFieldList", zoneId);
                if (obj == null || obj is not Dictionary<string, string> dict)
                    return false;

                var zoneLoc = dict.TryGetValue("Location", out var loc) ? loc.ToVector3() : Vector3.zero;
                var radius = dict.TryGetValue("radius", out var rad) ? Convert.ToSingle(rad) : 0f;
                var size = dict.TryGetValue("size", out var sz) ? sz.ToVector3() : Vector3.zero;

                if (zoneLoc == Vector3.zero || (radius <= 0f && size == Vector3.zero))
                    return false;

                var zoneName = dict.TryGetValue("name", out var n) ? n : null;
                var zoneRot = dict.TryGetValue("rotation", out var rot) ? Quaternion.Euler(rot.ToVector3()) : Quaternion.identity;
                var isBlocked = !config.Settings.UseZoneManagerOnly && !config.Settings.AllowedZones.Exists(zone => zone == zoneId || (!string.IsNullOrEmpty(zoneName) && zoneName.Equals(zone, StringComparison.OrdinalIgnoreCase)));
                ManagedZones[zoneId] = new(zoneId, zoneLoc, zoneRot, radius, size, isBlocked, config.Settings.ZoneDistance);
                return isBlocked;
            }

            private ObstructionQuery synchronousObstructionQuery = new();

            public bool IsObstructed(Vector3 from, float radius, float landLevel, float forcedHeight, bool spawnOnSeabed, BasePlayer player = null)
            {
                synchronousObstructionQuery.Reset();

                bool completed = StepIsCliffTerrainObstructed(from, radius, landLevel, forcedHeight, spawnOnSeabed, player, synchronousObstructionQuery, 0L, out bool isObstructed);

                synchronousObstructionQuery.Reset();

                return completed && isObstructed;
            }

            internal bool StepIsCliffTerrainObstructed(Vector3 from, float radius, float landLevel, float forcedHeight, bool spawnOnSeabed, BasePlayer player, ObstructionQuery query, long deadline, out bool isObstructed)
            {
                isObstructed = false;

                if (!query.Started)
                {
                    if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }

                    query.Reset();
                    query.Started = true;
                    query.From = from;
                    query.From.y = TerrainMeta.HeightMap.GetHeight(from);
                    query.Radius = radius;
                    query.LandLevel = forcedHeight == -1 ? landLevel : landLevel + forcedHeight;
                    query.SpawnOnSeabed = spawnOnSeabed;
                    query.Player = player;
                }

                const float degrees = 360f;
                const float radiansPerDegree = 2f * Mathf.PI / degrees;

                while (query.RingIndex >= 0)
                {
                    float step = query.Radius * 0.2f * query.RingIndex;
                    float degreeStep = degrees / step;

                    while (query.Degree < degrees)
                    {
                        if (deadline > 0 && Stopwatch.GetTimestamp() >= deadline)
                        {
                            return false;
                        }

                        float radian = radiansPerDegree * query.Degree;
                        Vector3 to = new(query.From.x + step * Mathf.Cos(radian), 0f, query.From.z + step * Mathf.Sin(radian));
                        to.y = query.SpawnOnSeabed ? TerrainMeta.HeightMap.GetHeight(to) : GetSpawnHeight(to, true, true);

                        float distance = Mathf.Abs((query.From - to).y);
                        query.Degree += degreeStep;

                        if (distance > query.LandLevel)
                        {
                            query.Result = true;

                            if (query.Player == null)
                            {
                                isObstructed = true;
                                return true;
                            }

                            DrawText(query.Player, 30f, Color.red, to, $"{distance:N1}");
                        }
                        else if (query.Player != null)
                        {
                            DrawText(query.Player, 30f, Color.green, to, $"{distance:N1}");
                        }
                    }

                    query.RingIndex--;
                    query.Degree = 0f;
                }

                isObstructed = query.Result;
                return true;
            }
        }
    }
}
