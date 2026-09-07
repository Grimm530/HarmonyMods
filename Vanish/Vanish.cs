using Facepunch;
using Facepunch.Extend;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Rust;
using Rust.Ai;
using Rust.Ai.Gen2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static HarmonyMods.RustGame.Nivex.Vanish.VanishExtensions.Methods;

// Karsiss, MQ, Grimm530,
// Invalid Packet: Client Command when typing /vanish = invalid json image in config file when calling ShowUI(player, uc)

namespace HarmonyMods.RustGame.Nivex.Vanish
{
    public class Manager : IHarmonyModHooks
    {
        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            Config.ReloadConfig();
            PatchAll();
            Puts($"[Harmony] Loaded: Vanish 1.0.8.1 by nivex");
            if (Net.sv?.callbackHandler != null) SaveRestore_Load.OnSaveRestoreLoad("");
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            IsUnloading = true;
            using var tmp = Pool.Get<PooledList<VanishComponent>>();
            tmp.AddRange(HiddenPlayers.Values);
            foreach (var vc in tmp)
            {
                try
                {
                    if (!IsShuttingDown && vc.player != null && vc.player.limitNetworking)
                    {
                        Reappear(vc.player);
                    }
                    UnityEngine.Object.Destroy(vc);
                }
                catch { }
            }
            UnpatchAll();
            Reappear();
            config = null;
            Instance = null;
            Puts($"[Harmony] Unloaded: Vanish 1.0.8.1 by nivex");
        }

        private const string ConfigDirectory = "HarmonyConfig";

        internal static string ResolveServerRoot()
        {
            try
            {
                if (!string.IsNullOrEmpty(UnityEngine.Application.dataPath))
                    return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            }
            catch
            {
            }
            return Path.GetFullPath(Directory.GetCurrentDirectory());
        }

        internal static Config config;
        internal static Manager Instance;
        private Effect reusableSoundEffectInstance = new();
        private Dictionary<string, VanishComponent> HiddenPlayers = new();
        private FieldInfo MainGunTarget = AccessTools.Field(typeof(BradleyAPC), "mainGunTarget");
        private FieldInfo NextPatrolTime = AccessTools.Field(typeof(BradleyAPC), "nextPatrolTime");
        private MethodInfo MarkDirty = AccessTools.Method(typeof(PlayerLoot), "MarkDirty");
        private FieldInfo PositionChecks = AccessTools.Field(typeof(PlayerLoot), "PositionChecks");
        private BaseEntity[] queryResults = AccessTools.Field(typeof(AIBrainSenses), "queryResults").GetValue(null) as BaseEntity[];
        private bool IsShuttingDown;

        #region Patches

        private readonly List<PatchDefinition> _permanent = new();
        private readonly List<PatchDefinition> _temporary = new();
        private const string Name = "Vanish";
        private Harmony _harmony;
        private MethodInfo CallHookObjectArrayMethod;

        private static void Puts(string message) => UnityEngine.Debug.LogWarning($"[Harmony:: {Name}] {message}");

        private void PatchAll() // call from OnLoaded
        {
            if (ValidatePatchDefinitions())
            {
                TryPatchTemporary();
                PatchAll(_permanent);
            }
        }

        private bool ValidatePatchDefinitions()
        {
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(ConsoleSystem), "RunWithResult", [typeof(ConsoleSystem.Option), typeof(string), typeof(object[])], typeof(ConsoleSystem_RunWithResult), nameof(ConsoleSystem_RunWithResult.Prefix), true));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(ServerMgr), "OnDisconnected", [typeof(string), typeof(Connection)], typeof(ServerMgr_OnDisconnected), nameof(ServerMgr_OnDisconnected.Prefix), true));
            _permanent.Add(new(HarmonyPatchType.Postfix, typeof(BasePlayer), "PlayerInit", [typeof(Connection)], typeof(BasePlayer_PlayerInit), nameof(BasePlayer_PlayerInit.Postfix)));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(ServerMgr), "Shutdown", Type.EmptyTypes, typeof(ServerMgr_Shutdown), nameof(ServerMgr_Shutdown.Prefix)));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(SaveRestore), "Load", [typeof(string), typeof(bool)], typeof(SaveRestore_Load), nameof(SaveRestore_Load.Prefix)));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(SenseComponent), "CanTarget", [typeof(BaseEntity)], typeof(SenseComponent_CanTarget), nameof(SenseComponent_CanTarget.Prefix)));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(BaseNpc), "GetWantsToAttack", [typeof(BaseEntity)], typeof(BaseNpc_GetWantsToAttack), nameof(BaseNpc_GetWantsToAttack.Prefix)));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(AIBrainSenses), "GetNearest", [typeof(List<BaseEntity>), typeof(float)], typeof(AIBrainSenses_GetNearest), nameof(AIBrainSenses_GetNearest.Prefix)));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(SimpleAIMemory), "SetKnown", [typeof(BaseEntity), typeof(BaseEntity), typeof(AIBrainSenses)], typeof(SimpleAIMemory_SetKnown), nameof(SimpleAIMemory_SetKnown.Prefix)));
            _permanent.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "Server_AddMarker", [typeof(BaseEntity.RPCMessage)], typeof(BasePlayer_Server_AddMarker_VanishPatch), nameof(BasePlayer_Server_AddMarker_VanishPatch.Prefix), false));
            _permanent.Add(new(HarmonyPatchType.Postfix, typeof(BasePlayer), "Server_AddMarker", [typeof(BaseEntity.RPCMessage)], typeof(BasePlayer_Server_AddMarker_VanishPatch), nameof(BasePlayer_Server_AddMarker_VanishPatch.Postfix), false));

            if (!ValidatePatchDefinitions(_permanent))
            {
                _permanent.Clear();
                return false;
            }

            Type interfaceType = Type.GetType("Harmony.Core.HarmonyModInterface, Harmony.Core") ?? Type.GetType("Harmony.Core.HarmonyModInterface, Carbon.Common");
            if (interfaceType != null)
            {
                _temporary.Add(new(HarmonyPatchType.Prefix, interfaceType, "CallHook", [typeof(string), typeof(object), typeof(object)], typeof(Interface_CallHook_CanLoot), nameof(Interface_CallHook_CanLoot.Prefix)));
                CallHookObjectArrayMethod = AccessTools.Method(interfaceType, "CallHook", [typeof(string), typeof(object[])]);
            }
            
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(CommunityEntity), "Hook_DragRPC", [typeof(BasePlayer), typeof(string), typeof(Vector3), typeof(CommunityEntity.DraggablePositionSendType)], typeof(CommunityEntity_Hook_DragRPC), nameof(CommunityEntity_Hook_DragRPC.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "ShouldNetworkTo", [typeof(BasePlayer)], typeof(BasePlayer_ShouldNetworkTo), nameof(BasePlayer_ShouldNetworkTo.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BradleyAPC), "VisibilityTest", [typeof(BaseEntity)], typeof(BradleyAPC_VisibilityTest), nameof(BradleyAPC_VisibilityTest.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "IsHostileItem", [typeof(Item)], typeof(BasePlayer_IsHostileItem), nameof(BasePlayer_IsHostileItem.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(AntiHack), "AddViolation", [typeof(BasePlayer), typeof(AntiHackType), typeof(float), typeof(GameObject)], typeof(AntiHack_AddViolation), nameof(AntiHack_AddViolation.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(AntiHack), "ShouldIgnore", [typeof(BasePlayer)], typeof(AntiHack_ShouldIgnore), nameof(AntiHack_ShouldIgnore.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(ValidBounds), "Test", [typeof(BaseEntity), typeof(Vector3)], typeof(ValidBounds_Test), nameof(ValidBounds_Test.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "OnInvalidPosition", Type.EmptyTypes, typeof(BasePlayer_OnInvalidPosition), nameof(BasePlayer_OnInvalidPosition.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(RelationshipManager.PlayerTeam), "SendInvite", [typeof(BasePlayer)], typeof(RelationshipManager_PlayerTeam_SendInvite), nameof(RelationshipManager_PlayerTeam_SendInvite.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "EnablePlayerCollider", Type.EmptyTypes, typeof(BasePlayer_EnablePlayerCollider), nameof(BasePlayer_EnablePlayerCollider.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "MarkHostileFor", [typeof(float)], typeof(BasePlayer_MarkHostileFor), nameof(BasePlayer_MarkHostileFor.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "Hurt", [typeof(HitInfo)], typeof(BasePlayer_Hurt), nameof(BasePlayer_Hurt.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "OnAttacked", [typeof(HitInfo)], typeof(BasePlayer_OnAttacked), nameof(BasePlayer_OnAttacked.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "Die", [typeof(HitInfo)], typeof(BasePlayer_Die), nameof(BasePlayer_Die.Prefix), priority: true));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "get_currentCraftLevel", Type.EmptyTypes, typeof(BasePlayer_currentCraftLevel_Patch), nameof(BasePlayer_currentCraftLevel_Patch.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(CodeLock), "OnTryToOpen", [typeof(BasePlayer)], typeof(CodeLock_OnTryToOpen), nameof(CodeLock_OnTryToOpen.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(CodeLock), "OnTryToClose", [typeof(BasePlayer)], typeof(CodeLock_OnTryToClose), nameof(CodeLock_OnTryToClose.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(KeyLock), "OnTryToOpen", [typeof(BasePlayer)], typeof(KeyLock_OnTryToOpen), nameof(KeyLock_OnTryToOpen.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(KeyLock), "OnTryToClose", [typeof(BasePlayer)], typeof(KeyLock_OnTryToClose), nameof(KeyLock_OnTryToClose.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(ApartmentLock), "OnTryToOpen", [typeof(BasePlayer)], typeof(KeyLock_OnTryToOpen), nameof(KeyLock_OnTryToOpen.Prefix)));
            // Rust checks StorageContainer.IsLocked() BEFORE CodeLock.OnTryToOpen — without this, vanished lock bypass never runs.
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(StorageContainer), "PlayerOpenLoot", [typeof(BasePlayer), typeof(string), typeof(bool)], typeof(StorageContainer_PlayerOpenLoot), nameof(StorageContainer_PlayerOpenLoot.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(StorageContainer), "CanBeLooted", [typeof(BasePlayer)], typeof(StorageContainer_CanBeLooted), nameof(StorageContainer_CanBeLooted.Prefix)));
            _temporary.Add(new(HarmonyPatchType.Prefix, typeof(BasePlayer), "CanBeLooted", [typeof(BasePlayer)], typeof(BasePlayer_CanBeLooted), nameof(BasePlayer_CanBeLooted.Prefix)));

            if (!ValidatePatchDefinitions(_temporary))
            {
                _temporary.Clear();
                return false;
            }

            return true;
        }

        public void Interface_CallHook(string hook, object arg)
        {
            if (CallHookObjectArrayMethod == null) return;
            CallHookObjectArrayMethod.Invoke(null, [hook, new object[] { arg }]);
        }

        private bool ValidatePatchDefinitions(List<PatchDefinition> definitions)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                PatchDefinition patch = definitions[i];
                MethodInfo targetMethod = AccessTools.Method(patch.TargetClassType, patch.TargetMethodName, patch.TargetMethodParameterTypes);
                if (targetMethod == null)
                {
                    string expectedParams = patch.TargetMethodParameterTypes != null ? string.Join(", ", patch.TargetMethodParameterTypes.Select(t => t?.Name ?? "null")) : "none";
                    var found = AccessTools.GetDeclaredMethods(patch.TargetClassType).Where(m => m.Name == patch.TargetMethodName).Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})").ToList();
                    Puts($"[TargetMethod Missing]\n  Expected: {patch.TargetClassType.FullName}.{patch.TargetMethodName}({expectedParams})\n  Found: {(found.Count > 0 ? string.Join(" | ", found) : "none")}");
                    if (!patch.CancelOnError) continue;
                    return false;
                }

                MethodInfo patchMethod = AccessTools.Method(patch.PatchClassType, patch.PatchMethodName);
                if (patchMethod == null)
                {
                    var found = AccessTools.GetDeclaredMethods(patch.PatchClassType).Where(m => m.Name == patch.PatchMethodName).Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})").ToList();
                    Puts($"[PatchMethod Missing]\n  Expected: {patch.PatchClassType.FullName}.{patch.PatchMethodName}()\n  Found: {(found.Count > 0 ? string.Join(" | ", found) : "none")}");
                    if (!patch.CancelOnError) continue;
                    return false;
                }

                patch.PatchMethod = patchMethod;
                patch.TargetMethod = targetMethod;
            }

            return true;
        }

        private void PatchAll(List<PatchDefinition> definitions)
        {
            if (definitions.Count == 0)
            {
                return;
            }

            if (_harmony == null)
            {
                _harmony = new Harmony(Name + "Patch");
            }

            float delay = 0f;

            foreach (PatchDefinition patch in definitions)
            {
                if (patch.Applied || patch.PatchMethod == null || patch.TargetMethod == null)
                {
                    continue;
                }

                if (config.CanSeeEveryone.Count == 0 && patch.TargetMethodName == "ShouldNetworkTo")
                {
                    continue;
                }

                Action action = null;
                switch (patch.HarmonyPatchType)
                {
                    case HarmonyPatchType.Prefix:
                        action = () =>
                        {
                            _harmony.Patch(patch.TargetMethod, prefix: new(patch.PatchMethod));
                            patch.Applied = true;
                        };
                        break;

                    case HarmonyPatchType.Postfix:
                        action = () =>
                        {
                            _harmony.Patch(patch.TargetMethod, postfix: new(patch.PatchMethod));
                            patch.Applied = true;
                        };
                        break;

                    case HarmonyPatchType.Transpiler:
                        action = () =>
                        {
                            _harmony.Patch(patch.TargetMethod, transpiler: new(patch.PatchMethod));
                            patch.Applied = true;
                        };
                        break;

                    case HarmonyPatchType.Finalizer:
                        action = () =>
                        {
                            _harmony.Patch(patch.TargetMethod, finalizer: new(patch.PatchMethod));
                            patch.Applied = true;
                        };
                        break;

                    default:
                        continue;
                }

                if (action == null)
                    continue;

                if (patch.Priority || ServerMgr.Instance == null)
                {
                    action();
                }
                else
                {
                    ServerMgr.Instance.Invoke(action, delay += 0.005f);
                }
            }
        }

        private void UnpatchAll() // call from OnUnloaded
        {
            UnpatchAll(_permanent, immediate: true);
            UnpatchAll(_temporary, immediate: true);
        }

        private void UnpatchAll(List<PatchDefinition> definitions, bool immediate)
        {
            if (_harmony == null)
            {
                return;
            }

            immediate |= ServerMgr.Instance == null;
            float delay = 0f;
            foreach (var patch in definitions)
            {
                if (!patch.Applied || patch.TargetMethod == null)
                {
                    continue;
                }
                if (immediate) _harmony.Unpatch(patch.TargetMethod, patch.HarmonyPatchType, _harmony.Id);
                else ServerMgr.Instance.Invoke(() =>
                {
                    _harmony.Unpatch(patch.TargetMethod, patch.HarmonyPatchType, _harmony.Id);
                    patch.Applied = false;
                }, delay += 0.005f);
            }
        }

        private bool IsUnloading;
        private Action delayedTemporaryUnpatch;

        private void TryPatchTemporary()
        {
            CancelTemporaryUnpatch();

            if (!IsUnloading && HiddenPlayers.Count > 0)
            {
                PatchAll(_temporary);
            }
        }

        private void TryUnpatchTemporary()
        {
            if (IsUnloading || delayedTemporaryUnpatch != null || _temporary.Count == 0 || HiddenPlayers.Count != 0)
            {
                return;
            }

            if (ServerMgr.Instance == null)
            {
                TryTemporaryUnpatchNow();
            }
            else
            {
                delayedTemporaryUnpatch = TryTemporaryUnpatchNow;
                ServerMgr.Instance.Invoke(delayedTemporaryUnpatch, 15f);
            }
        }

        private void TryTemporaryUnpatchNow()
        {
            delayedTemporaryUnpatch = null;

            if (IsUnloading || Instance != this || HiddenPlayers.Count != 0)
            {
                return;
            }

            UnpatchAll(_temporary, immediate: true);
        }

        private void CancelTemporaryUnpatch()
        {
            if (delayedTemporaryUnpatch == null)
            {
                return;
            }

            if (ServerMgr.Instance != null)
            {
                ServerMgr.Instance.CancelInvoke(delayedTemporaryUnpatch);
            }

            delayedTemporaryUnpatch = null;
        }

        public class PatchDefinition(HarmonyPatchType harmonyPatchType, Type targetClassType, string targetMethodName, Type[] targetMethodParameterTypes, Type patchClassType, string patchMethodName, bool cancelOnError = false, bool priority = false)
        {
            public readonly HarmonyPatchType HarmonyPatchType = harmonyPatchType;
            public readonly Type TargetClassType = targetClassType;
            public readonly string TargetMethodName = targetMethodName;
            public readonly Type[] TargetMethodParameterTypes = targetMethodParameterTypes;
            public readonly Type PatchClassType = patchClassType;
            public readonly string PatchMethodName = patchMethodName;
            public readonly bool CancelOnError = cancelOnError;
            public MethodInfo TargetMethod { get; set; } = null;
            public MethodInfo PatchMethod { get; set; } = null;
            public bool Applied { get; set; } = false;
            public bool Priority { get; set; } = priority;
        }

        #endregion Patches

        private class Interface_CallHook_CanLoot
        {
            internal static bool Prefix(string hook, object obj1, object obj2, ref object __result)
            {
                if (hook == "CanLootEntity" && obj1 != null && obj1 is BasePlayer player && player != null)
                {
                    if (player.IsAdmin || IsVanished(player))
                    {
                        __result = null;
                        return false;
                    }
                }

                return true;
            }
        }

        private class CommunityEntity_Hook_DragRPC
        {
            internal static void Prefix(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type) => OnCuiDraggableDrag(player, name, position, type);
            internal static void OnCuiDraggableDrag(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type)
            {
                if (config == null || player == null || player.Connection == null || !HasAccess(player.Connection)) return;
                var uc = UserConfig.Get(player.GetSteamId());
                uc.OnCuiDraggableDrag(player, name, position, type);
            }
        }

        private class BasePlayer_ShouldNetworkTo
        {
            internal static bool Prefix(BasePlayer player, BasePlayer __instance, ref bool __result) => CanNetworkTo(player, __instance, ref __result);
            internal static bool CanNetworkTo(BasePlayer target, BasePlayer __instance, ref bool __result)
            {
                if (target != null && config != null && config.CanSeeEveryone.Contains(target.GetSteamId()))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        private class ServerMgr_Shutdown
        {
            internal static void Prefix() => OnServerShutdown();
            internal static void OnServerShutdown()
            {
                if (Instance != null)
                {
                    Instance.IsUnloading = true;
                    Instance.IsShuttingDown = true;
                }
            }
        }

        private class SaveRestore_Load
        {
            internal static void Prefix(string strFilename, bool allowOutOfDateSaves) => OnSaveRestoreLoad(strFilename);
            internal static void OnSaveRestoreLoad(string strFilename)
            {
                try
                {
                    if (string.IsNullOrEmpty(strFilename))
                    {
                        strFilename = World.SaveFolderName + "/" + World.SaveFileName;
                    }
                    if (!File.Exists(strFilename))
                    {
                        using var tmp = Pool.Get<PooledList<UserConfig>>();
                        tmp.AddRange(config.Users.Values);
                        foreach (var uc in tmp)
                        {
                            if (uc.SafePointsRemoval)
                            {
                                uc.SafePoints.Clear();
                            }
                        }
                        config.SaveConfig();
                    }
                    foreach (var player in BasePlayer.allPlayerList)
                    {
                        if (player == null) continue;
                        if (!player.IsConnected)
                        {
                            if (IsVanished(player))
                            {
                                if (!HasAccessOrAuthLevel(player))
                                {
                                    Instance.Reappear(player);
                                    Puts($"{player} has been removed from vanish (no access)");
                                }
                                else
                                {
                                    Instance.Disappear(player);
                                    Puts($"{player} has been re-vanished");
                                }
                            }
                            else if (HasAccessOrAuthLevel(player) && UserConfig.Get(player.GetSteamId()).AutoVanish)
                            {
                                Instance.Disappear(player);
                                Puts($"{player} has been auto-vanished");
                            }
                        }
                        else BasePlayer_PlayerInit.OnPlayerConnected(player.Connection, false);
                    }
                }
                catch (Exception ex)
                {
                    Puts(ex.ToString());
                }
            }
        }

        private class BasePlayer_Server_AddMarker_VanishPatch
        {
            internal static void Prefix(BasePlayer __instance, out int __state)
            {
                __state = 0;
                if (__instance?.State?.pointsOfInterest == null) return;
                __state = __instance.State.pointsOfInterest.Count;
            }

            [HarmonyPriority(HarmonyLib.Priority.First)]
            internal static void Postfix(BasePlayer __instance, int __state)
            {
                if (__instance == null || !__instance.IsAlive() || __instance.isMounted) return;
                if (config?.TeleportToMarkerWhenVanished != true || !HasHiddenState(__instance)) return;
                var pointsOfInterest = __instance.State?.pointsOfInterest;
                if (pointsOfInterest == null || pointsOfInterest.Count != __state + 1) return;
                var note = pointsOfInterest[pointsOfInterest.Count - 1];
                if (note == null) return;
                var pos = note.worldPosition + new Vector3(0f, 1f, 0f);
                if (__instance.IsFlying) pos.y = Mathf.Max(pos.y, __instance.transform.position.y);
                PauseAntiHackForAdminMove(__instance);
                __instance.PauseFlyHackDetection(10f);
                __instance.Teleport(pos);
            }
        }

        private class ConsoleSystem_RunWithResult
        {
            internal static bool Prefix(ref ConsoleSystem.CommandResult __result, ConsoleSystem.Option options, string strCommand, object[] args) => CommandHook(options, strCommand, args, ref __result);
            internal static bool CommandHook(ConsoleSystem.Option options, string strCommand, object[] args, ref ConsoleSystem.CommandResult __result)
            {
                if (string.IsNullOrEmpty(strCommand))
                {
                    return true;
                }
                if (!HasAccess(options.Connection))
                {
                    return true;
                }
                if (strCommand.IndexOf("vanish", StringComparison.OrdinalIgnoreCase) < 0 && strCommand.IndexOf("invis", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }
                if (!IsKnownCommand(strCommand, out _, out var _args))
                {
                    return true;
                }
                //ConsoleSystem.Command cmd = null;
                //if (options.Connection?.authLevel == 0)
                //{
                //    string command = ConsoleSystem.BuildCommand(strCommand, args);
                //    int num = command.IndexOf('.');
                //    if (num > 0)
                //    {
                //        string text = command.Substring(0, num);
                //        string text2 = command.Substring(num + 1);
                //        cmd = ConsoleSystem.Index.Client.Find(text + "." + text2);
                //    }
                //}
                if (_args.Length == 0)
                {
                    ConsoleSystem.CommandResultType result = ConsoleSystem.CommandResultType.Success;
                    if (options.Connection != null && options.Connection.player.Is(out BasePlayer player))
                    {
                        if (options.Connection.authLevel == 0) result = ConsoleSystem.CommandResultType.Unknown;
                        Instance?.ToggleVanish(player);
                    }
                    __result = new ConsoleSystem.CommandResult(result, null, null);
                    return false;
                }
                bool val = SetConfig(options, _args);
                __result = new ConsoleSystem.CommandResult(val ? ConsoleSystem.CommandResultType.CommandNotFound : ConsoleSystem.CommandResultType.Success, null, null);
                return false;
            }

            internal static readonly string[] Replacements = { "chat.say ", "chat.teamsay ", "chat.localsay ", "chat.cardgamesay ", " True", " true", "\n", "\r" };

            internal static bool IsKnownCommand(string strCommand, out string command, out string[] args)
            {
                string cleanedCommand = Replace(strCommand);
                string[] split = cleanedCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length == 0)
                {
                    command = string.Empty;
                    args = Array.Empty<string>();
                    return false;
                }
                command = split[0];
                args = SkipArgs(split, 1);
                return 
                    command.Equals("vanish", StringComparison.OrdinalIgnoreCase) ||
                    command.Equals("invis", StringComparison.OrdinalIgnoreCase) ||
                    command.Equals("debug.invis", StringComparison.OrdinalIgnoreCase);
            }

            internal static string Replace(string strCommand)
            {
                StringBuilder sb = Pool.Get<StringBuilder>().Append(strCommand);
                foreach (var rep in Replacements) sb.Replace(rep, string.Empty);
                string result = sb.ToString().Trim('"').TrimStart('/');
                Pool.FreeUnmanaged(ref sb);
                return result;
            }

            internal static bool SetConfig(ConsoleSystem.Option options, string[] args)
            {
                var player = options.Player();
                if (player == null)
                {
                    player = BasePlayer.FindAwakeOrSleeping(args[0]);
                }
                if (player == null)
                {
                    Puts("Player not found.");
                    return false;
                }
                var uc = UserConfig.Get(player.GetSteamId());
                switch (args[0].ToLower())
                {
                    case "setanchormin":
                        {
                            if (args.Length >= 1)
                            {
                                if (args.Length == 3 && float.TryParse(args[1], out var value1) && float.TryParse(args[2], out var value2))
                                {
                                    uc.ImageOffsetMin = $"{value1} {value2}";
                                    Instance.ShowUI(player, uc);
                                }

                                Message(player, uc.ImageOffsetMin);
                            }
                        }
                        return false;
                    case "setanchormax":
                        {
                            if (args.Length >= 1)
                            {
                                if (args.Length == 3 && float.TryParse(args[1], out var value1) && float.TryParse(args[2], out var value2))
                                {
                                    uc.ImageOffsetMax = $"{value1} {value2}";
                                    Instance.ShowUI(player, uc);
                                }

                                Message(player, uc.ImageOffsetMax);
                            }
                        }
                        return false;
                    case "anchors_save":
                        {
                            config.SaveConfig();
                            Message(player, $"Saved: {uc.ImageOffsetMin} {uc.ImageOffsetMax}");
                        }
                        return false;
                    case "anchors_reset":
                        {
                            uc.ImageOffsetMin = "298.809 19.449";
                            uc.ImageOffsetMax = "355.991 69.351";
                            config.SaveConfig();
                            Message(player, $"Reset: {uc.ImageOffsetMin} {uc.ImageOffsetMax}");
                        }
                        return false;
                    case "reload": ReloadConfig(player); return false;
                    case "safepoint": SetSafePoint(player); return false;
                    case "showloot": ShowLoot(player, SkipArgs(args, 1)); return false;
                    case "access": ToggleAccess(player, SkipArgs(args, 1)); return false;
                    case "resetimg": ToggleResetImg(player); return false;
                    case "noclip": ToggleNoClip(player); return false;
                    default: ToggleVanish(player, args); return false;
                }
            }
            internal static string[] SkipArgs(string[] args, int count)
            {
                if (args == null || args.Length <= count)
                {
                    return Array.Empty<string>();
                }
                string[] remaining = new string[args.Length - count];
                Array.Copy(args, count, remaining, 0, remaining.Length);
                return remaining;
            }
            internal static void ReloadConfig(BasePlayer player)
            {
                Config.ReloadConfig();
                Message(player, "Vanish: Reloaded config");
            }
            internal static void ToggleNoClip(BasePlayer player)
            {
                player.SendConsoleCommand("noclip");
            }
            internal static void SetSafePoint(BasePlayer player)
            {
                var uc = UserConfig.Get(player.GetSteamId());
                if (uc.UseSafePoints)
                {
                    uc.SafePoints.Add(player.transform.position);
                    Message(player, "Saved");
                }
                config.SaveConfig();
            }
            internal static void ShowLoot(BasePlayer player, string[] args)
            {
                if (player.IsDead() || player.inventory.loot.containers.Count > 0)
                    return;

                if (!Instance.HiddenPlayers.TryGetValue(player.UserIDString, out var vc))
                {
                    Message(player, "Must be in vanish.");
                    return;
                }

                if (vc.isLooting)
                {
                    return;
                }

                vc.isLooting = true;

                const int layerMask = Layers.Mask.Player_Server | Layers.Mask.Invisible | Layers.Mask.Construction | Layers.Mask.Deployed | Layers.Mask.Ragdoll | Layers.Mask.Default;

                if (args.Length == 0)
                {
                    if (Physics.Raycast(player.eyes.HeadRay(), out var hit, 5f, layerMask, QueryTriggerInteraction.Ignore))
                    {
                        BaseEntity hitEntity = hit.GetEntity();

                        switch (hitEntity)
                        {
                            case null:
                                Message(player, "NothingInSight");
                                vc.isLooting = false;
                                return;
                            case BasePlayer target:
                                vc.ShowLoot(target);
                                return;
                            case IItemContainerEntity ice:
                                vc.ShowLoot(ice);
                                return;
                            default:
                                vc.isLooting = false;
                                return;
                        }
                    }
                    else
                    {
                        Message(player, "NothingInSight");
                        vc.isLooting = false;
                    }
                }
                else
                {
                    var target = BasePlayer.FindAwakeOrSleeping(args[0]);
                    if (target == null)
                    {
                        Message(player, "NoSuchPlayer", args[0]);
                        vc.isLooting = false;
                    }
                    else
                    {
                        vc.ShowLoot(target);
                    }
                }
            }
            internal static void ToggleResetImg(BasePlayer player)
            {
                var uc = UserConfig.Get(player.GetSteamId());
                uc.ResetUi();
                uc.LoadImage();
                config.SaveConfig();
                Config.ReloadConfig();
                Message(player, "ResetImg");
            }
            internal static void ToggleAccess(BasePlayer player, string[] args)
            {
                if (args.Length == 0)
                {
                    Message(player, "AccessHelp");
                    return;
                }
                BasePlayer target = BasePlayer.FindAwakeOrSleeping(args[0]);
                if (target == null)
                {
                    Message(player, "NoSuchPlayer", args[0]);
                    return;
                }
                var userid = target.GetSteamId();
                if (!config.AccessList.Remove(userid))
                {
                    config.AccessList.Add(userid);
                    config.SaveConfig();
                }
                Message(player, config.AccessList.Contains(userid) ? "Granted" : "Revoked", target.displayName, userid);
            }
            public static void ToggleVanish(BasePlayer player, string[] args)
            {
                if (args.Length >= 1)
                {
                    string a = args[0].Trim().ToLowerInvariant();
                    if (a == "on" || a == "true" || a == "1")
                    {
                        if (!IsVanished(player)) Instance.Disappear(player);
                        if (player.IsConnected && HasAccess(player.Connection)) Message(player, "Enabled");
                        return;
                    }
                    if (a == "off" || a == "false" || a == "0")
                    {
                        if (IsVanished(player)) Instance.Reappear(player);
                        if (player.IsConnected && HasAccess(player.Connection)) Message(player, "Disabled");
                        return;
                    }
                }
                BasePlayer target = player;
                if (args.Length != 0)
                {
                    target = BasePlayer.FindAwakeOrSleeping(args[0].Replace(" True", ""));
                    if (target == null)
                    {
                        Message(player, "NoSuchPlayer", args[0]);
                        return;
                    }
                }
                Instance.ToggleVanish(target);
                if (player != target || !player.IsConnected)
                {
                    if (HasAccess(player.Connection) && player.IsConnected) Message(player, target.limitNetworking ? "EnabledOther" : "DisabledOther", target.displayName, target.UserIDString);
                    else Puts(RemoveFormatting(string.Format(Get(target.limitNetworking ? "EnabledOther" : "DisabledOther"), target.displayName, target.UserIDString)));
                }
            }
        }

        private class BradleyAPC_VisibilityTest
        {
            internal static bool Prefix(BaseEntity ent) => !ent.Cast(out BasePlayer player) || !IsVanished(player);
        }

        private class BasePlayer_IsHostileItem
        {
            internal static bool Prefix(Item item, BasePlayer __instance) => !IsVanished(__instance);
        }

        private class AntiHack_AddViolation
        {
            internal static bool Prefix(BasePlayer ply, AntiHackType type, float amount, GameObject gameObject) => !IsVanished(ply);
        }

        private class AntiHack_ShouldIgnore
        {
            internal static bool Prefix(BasePlayer ply, ref bool __result)
            {
                if (!IsVanished(ply))
                    return true;
                __result = true;
                return false;
            }
        }

        private class ValidBounds_Test
        {
            internal static bool Prefix(BaseEntity entity, Vector3 vPos, ref bool __result)
            {
                if (entity is not BasePlayer player || !IsVanished(player) || vPos.IsNaNOrInfinity())
                    return true;
                __result = true;
                return false;
            }
        }

        private class BasePlayer_OnInvalidPosition
        {
            internal static bool Prefix(BasePlayer __instance) => !IsVanished(__instance);
        }

        private class RelationshipManager_PlayerTeam_SendInvite
        {
            internal static bool Prefix(BasePlayer player) => !IsVanished(player);
        }

        private class BasePlayer_EnablePlayerCollider
        {
            internal static bool Prefix(BasePlayer __instance) => !IsVanished(__instance);
        }

        private class BasePlayer_MarkHostileFor
        {
            internal static bool Prefix(float duration, BasePlayer __instance) => !IsVanished(__instance);
        }

        private class SenseComponent_CanTarget // Wolf2
        {
            internal static bool Prefix(BaseEntity entity)
            {
                if (config == null)
                {
                    return true;
                }
                var player = entity as BasePlayer;
                if (player == null || !player.IsSleeping() && !player.limitNetworking || !HasAccessOrAuthLevel(player))
                {
                    return true;
                }
                return false;
            }
        }

        private class BaseNpc_GetWantsToAttack
        {
            internal static bool Prefix(BaseEntity target, ref float __result, BaseNpc __instance)
            {
                if (SenseComponent_CanTarget.Prefix(target))
                {
                    __result = 0f;
                    return false;
                }
                return true;
            }
        }

        private class AIBrainSenses_GetNearest
        {
            internal static void Prefix(AIBrainSenses __instance, List<BaseEntity> entities, float rangeFraction)
            {
                if (config != null)
                {
                    entities.RemoveAll(entity =>
                    {
                        var player = entity as BasePlayer;
                        if (player == null)
                        {
                            return false;
                        }
                        //if (player.UserIDString == "76561198250837156" && __instance.brain.GetBaseEntity() is ScarecrowNPC)
                        //{
                        //    return true;
                        //}
                        return (player.IsSleeping() || player.limitNetworking) && HasAccessOrAuthLevel(player);
                    });
                }
            }
        }

        private class SimpleAIMemory_SetKnown
        {
            internal static bool Prefix(BaseEntity ent, BaseEntity owner, AIBrainSenses brainSenses)
            {
                if (config == null)
                {
                    return true;
                }
                var player = ent as BasePlayer;
                if (player != null && (player.IsSleeping() || player.limitNetworking) && HasAccessOrAuthLevel(player))
                {
                    return false;
                }
                return true;
            }
        }

        private class BasePlayer_Hurt
        {
            internal static bool Prefix(HitInfo info, BasePlayer __instance) => OnHurt(info, __instance);
            internal static bool OnHurt(HitInfo info, BasePlayer __instance)
            {
                try
                {
                    if (__instance == null || config == null || __instance.Categorize() == "Duelist" || !HasAccessOrAuthLevel(__instance))
                    {
                        return true;
                    }
                    if (IsVanished(__instance) && UserConfig.Get(__instance.GetSteamId()).BlockAllIncomingDamage)
                    {
                        return info?.damageTypes?.GetMajorityDamageType() == DamageType.Suicide;
                    }
                    if (info != null && info.Initiator.Is(out BasePlayer attacker) && IsVanished(attacker) && UserConfig.Get(attacker.GetSteamId()).BlockAllOutgoingDamage)
                    {
                        return info?.damageTypes?.GetMajorityDamageType() != DamageType.Suicide;
                    }
                }
                catch (Exception ex)
                {
                    Puts(ex.ToString());
                }
                return true;
            }
        }

        private class BasePlayer_OnAttacked
        {
            internal static bool Prefix(HitInfo info, BasePlayer __instance) => BasePlayer_Hurt.OnHurt(info, __instance);
        }

        private class BasePlayer_Die
        {
            internal static bool Prefix(BasePlayer __instance, HitInfo info = null) => BasePlayer_Hurt.OnHurt(info, __instance);
        }

        private class BasePlayer_PlayerInit
        {
            internal static void Postfix(Connection c) => OnPlayerConnected(c, true);
            internal static void OnPlayerConnected(Connection c, bool canNoClipOnConnect)
            {
                try
                {
                    if (c == null || config == null)
                    {
                        return;
                    }
                    var player = c.player as BasePlayer;
                    if (player != null && HasAccess(c))
                    {
                        var uc = UserConfig.Get(player.GetSteamId());

                        if (uc.AutoVanish)
                        {
                            Instance.Disappear(player);
                        }
                        if (canNoClipOnConnect && uc.NoClipOnConnect)
                        {
                            player.SendConsoleCommand("noclip");
                        }
                        if (player.metabolism.bleeding.value != 0f)
                        {
                            player.metabolism.bleeding.value = 0f; // rust bug workaround
                            player.metabolism.SendChanges();
                        }
                    }
                }
                catch
                {

                }
            }
        }

        private class ServerMgr_OnDisconnected
        {
            internal static void Prefix(string strReason, Connection connection) => OnDisconnectedHook(connection);
            internal static void OnDisconnectedHook(Connection c)
            {
                try
                {
                    if (c == null)
                    {
                        return;
                    }
                    var player = c.player as BasePlayer;
                    if (player != null && HasAccess(c))
                    {
                        player.Invoke(() =>
                        {
                            player.SetServerFall(false);
                            player.Invoke("DisablePlayerCollider", 0f);
                        }, 0.1f);
                        var uc = UserConfig.Get(player.GetSteamId());
                        if (FoundSleepingBags(player, uc, out SleepingBag bag))
                        {
                            player.Teleport(bag.transform.position);
                        }
                        else if (HasSafePoint(uc))
                        {
                            player.Teleport(GetSafePoint(player, uc));
                        }
                        else if (uc.UndergroundTeleportDepth != 0f)
                        {
                            var position = player.transform.position;
                            position.y = TerrainMeta.HeightMap.GetHeight(position) - uc.UndergroundTeleportDepth;
                            player.Teleport(position);
                        }
                    }
                }
                catch
                {

                }
            }

            private static bool FoundSleepingBags(BasePlayer player, UserConfig uc, out SleepingBag bag)
            {
                if (uc.UseBags)
                {
                    using var bags = Facepunch.Pool.Get<PooledList<SleepingBag>>();
                    SleepingBag.FindForPlayer(player.userID.Get(), true, bags);
                    if (bags.Count != 0)
                    {
                        bag = bags.GetRandom();
                        return bag != null && !bag.IsDestroyed;
                    }
                }
                bag = null;
                return false;
            }
        }

        private class BasePlayer_currentCraftLevel_Patch
        {
            internal static bool Prefix(BasePlayer __instance, ref float __result, ref float ___cachedCraftLevel, ref float ___nextCheckTime)
            {
                if (IsVanished(__instance))
                {
                    if (!UserConfig.Get(__instance.GetSteamId()).WorkbenchCraft) //__instance.inventory.crafting.queue.Count > 0 && __instance.FindTrigger<TriggerWorkbench>() == null)
                    {
                        return true;
                    }
                    ___nextCheckTime = float.MaxValue;
                    __result = ___cachedCraftLevel = 3f;
                    return false;
                }
                return true;
            }
        }

        private class CodeLock_OnTryToOpen
        {
            internal static bool Prefix(BasePlayer player, ref bool __result) => Handle(player, ref __result);
            public static bool Handle(BasePlayer player, ref bool __result)
            {
                if (IsVanished(player))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        private class CodeLock_OnTryToClose
        {
            internal static bool Prefix(BasePlayer player, ref bool __result) => CodeLock_OnTryToOpen.Handle(player, ref __result);
        }

        private class KeyLock_OnTryToOpen
        {
            internal static bool Prefix(BasePlayer player, ref bool __result) => CodeLock_OnTryToOpen.Handle(player, ref __result);
        }

        private class KeyLock_OnTryToClose
        {
            internal static bool Prefix(BasePlayer player, ref bool __result) => CodeLock_OnTryToOpen.Handle(player, ref __result);
        }

        private class StorageContainer_CanBeLooted
        {
            internal static bool Prefix(BasePlayer player, ref bool __result)
            {
                if (!IsVanished(player)) return true;
                __result = true;
                return false;
            }
        }

        private class BasePlayer_CanBeLooted
        {
            internal static bool Prefix(BasePlayer player, ref bool __result)
            {
                if (!IsVanished(player)) return true;
                __result = true;
                return false;
            }
        }

        private class StorageContainer_PlayerOpenLoot
        {
            // Mirrors StorageContainer.PlayerOpenLoot but skips IsLocked() for vanished players.
            internal static bool Prefix(StorageContainer __instance, BasePlayer player, string panelToOpen, bool doPositionChecks, ref bool __result)
            {
                if (__instance == null || player == null || !IsVanished(player)) return true;

                if (__instance.IsTransferring())
                {
                    __result = false;
                    return false;
                }

                if (__instance.onlyOneUser && __instance.IsOpen())
                {
                    __result = false;
                    return false;
                }

                if (string.IsNullOrEmpty(panelToOpen))
                    panelToOpen = __instance.panelName;

                // IsLocked skipped; CanOpenLootPanel still runs OnTryToOpen (already bypassed when vanished).
                if (!__instance.CanOpenLootPanel(player, panelToOpen))
                {
                    __result = false;
                    return false;
                }

                if (player.inventory.loot.StartLootingEntity(__instance, doPositionChecks))
                {
                    using (var scope = __instance.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                    {
                        scope.Set(BaseEntity.Flags.Open, b: true);
                    }
                    __instance.AddContainers(player.inventory.loot);
                    player.inventory.loot.SendImmediate();
                    player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), panelToOpen);
                    __instance.SendNetworkUpdate();
                    __result = true;
                    return false;
                }

                __result = false;
                return false;
            }
        }

        private class VanishComponent : FacepunchBehaviour, Rust.IEntity
        {
            internal BasePlayer player;
            internal GameObject child;
            internal Collider col;
            internal CapsuleCollider capsule;
            internal bool workbenchCraft;
            internal Manager Instance;
            internal ulong userid;

            private void Awake()
            {
                gameObject.name = "VanishGameObject";
                player = GetComponent<BasePlayer>();
                userid = player.GetSteamId();
                capsule = player.GetComponent<CapsuleCollider>();
                //BaseEntity.Query.Server.RemovePlayer(player);
                CreateChildGameObject();
                StartNetworkGroupsUpdate();
            }

            private void OnDestroy()
            {
                StopSeeEveryoneUpdate();
                if (!IsDestroyed)
                {
                    SetLastAdminCheatTime(Time.realtimeSinceStartup);
                    player.transform.localScale = new(1f, 1f, 1f);
                }
                StopNetworkGroupsUpdate();
                Destroy(child);
                Destroy(col);
                Destroy(this);
            }

            public void SetLastAdminCheatTime(float time)
            {
                if (player.ActivePlayerInd < 0 || !AntiHack.PlayerStates.IsCreated)
                    return;
                ref AntiHack.PlayerState playerState = ref ((Span<AntiHack.PlayerState>)AntiHack.PlayerStates)[player.ActivePlayerInd];
                playerState.LastAdminCheatTime = time;
            }

            public bool IsDestroyed => player == null || player.IsDestroyed || !player.IsFullySpawned();

            public bool isLooting;

            public void ShowLoot(IItemContainerEntity container)
            {
                BaseEntity target = container as BaseEntity;
                player.EndLooting();
                player.ClientRPC(RpcTarget.Player("OnRespawnInformation", player));
                Invoke(() =>
                {
                    if (container == null || target == null)
                    {
                        isLooting = false;
                        return;
                    }
                    player.inventory.loot.AddContainer(container.inventory);
                    player.inventory.loot.entitySource = RelationshipManager.ServerInstance; // Bypass PlayerLoot.Check for entitySource.CanBeLooted (credit Whispers88)
                    Instance.PositionChecks.SetValue(player.inventory.loot, false);
                    Instance.MarkDirty.Invoke(player.inventory.loot, null);
                    player.inventory.loot.SendImmediate();
                    player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
                    Message(player, "Looting", target.ShortPrefabName, target.OwnerID);
                    isLooting = false;
                }, 0.2f);
            }

            public void ShowLoot(BasePlayer target)
            {
                player.EndLooting();
                player.ClientRPC(RpcTarget.Player("OnRespawnInformation", player));
                Invoke(() =>
                {
                    if (!player || !target)
                    {
                        isLooting = false;
                        return;
                    }
                    player.inventory.loot.AddContainer(target.inventory.containerMain);
                    player.inventory.loot.AddContainer(target.inventory.containerWear);
                    player.inventory.loot.AddContainer(target.inventory.containerBelt);
                    player.inventory.loot.entitySource = RelationshipManager.ServerInstance;
                    Instance.PositionChecks.SetValue(player.inventory.loot, false);
                    Instance.MarkDirty.Invoke(player.inventory.loot, null);
                    player.inventory.loot.SendImmediate();
                    player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "player_corpse");
                    Message(player, "Looting", target.displayName, target.UserIDString);
                    isLooting = false;
                }, 0.2f);
            }

            private void SendAsSnapshotTo()
            {
                if (!player.IsSleeping() && !player.IsSpectating() && !player.IsDead() && !player.IsReceivingSnapshot && player.CanInteract())
                {
                    foreach (Connection c in Net.sv.connections)
                    {
                        var target = c?.player as BasePlayer;
                        if (target != null && target.net != null && target.limitNetworking && target.UserIDString != player.UserIDString)
                        {
                            //target.QueueUpdate(BasePlayer.NetworkQueue.Update, player);
                            NetWrite netWrite = Net.sv.StartWrite();
                            player.net.connection.validate.entityUpdates++;
                            BaseNetworkable.SaveInfo saveInfo = default;
                            saveInfo.forConnection = player.net.connection;
                            saveInfo.forDisk = false;
                            netWrite.PacketID(Network.Message.Type.Entities);
                            netWrite.UInt32(player.net.connection.validate.entityUpdates);
                            target.ToStreamForNetwork(netWrite, saveInfo);
                            netWrite.Send(new SendInfo(player.net.connection));
                        }
                    }
                }
            }

            public void StopSendAsSnapshotTo()
            {
                if (IsInvoking(SendAsSnapshotTo))
                {
                    CancelInvoke(SendAsSnapshotTo);
                }
            }

            public void StartSendAsSnapshotTo()
            {
                if (config.CanSeeEveryone.Contains(userid))
                {
                    StopSendAsSnapshotTo();
                    InvokeRepeating(SendAsSnapshotTo, 0f, 3f);
                }
            }

            private void UpdateNetworkGroups()
            {
                if (!IsDestroyed && !player.limitNetworking && player.IsSleeping())
                {
                    player.limitNetworking = true;
                    //player.isInvisible = true;
                }
                if (!IsDestroyed && player.IsConnected && !player.IsSpectating() && player.net != null)
                {
                    player.net.UpdateGroups(player.transform.position, player.networkRange);
                }
            }

            private Dictionary<ulong, Vector3> seenHiddenPositions = new();

            public void SeeEveryoneUpdate()
            {
                if (!config.CanSeeEveryone.Contains(userid) || IsInvoking(UpdateSeenHiddenPlayers))
                {
                    return;
                }

                InvokeRepeating(UpdateSeenHiddenPlayers, 0f, 0.1f);
            }

            private void UpdateSeenHiddenPlayers()
            {
                if (IsDestroyed || Instance == null || !config.CanSeeEveryone.Contains(userid))
                {
                    StopSeeEveryoneUpdate();
                    return;
                }

                if (!player.CanInteract() || player.IsSpectating())
                {
                    return;
                }

                const float PositionSqrTolerance = 0.001f;

                foreach (VanishComponent target in Instance.HiddenPlayers.Values)
                {
                    if (target == null || target.IsDestroyed || target.userid == userid)
                    {
                        continue;
                    }

                    Vector3 position = target.player.transform.position;

                    if ((position - player.transform.position).sqrMagnitude > 250000f)
                    {
                        continue;
                    }

                    if (seenHiddenPositions.TryGetValue(target.userid, out Vector3 previousPosition) && (previousPosition - position).sqrMagnitude <= PositionSqrTolerance)
                    {
                        continue;
                    }

                    seenHiddenPositions[target.userid] = position;
                    player.QueueUpdate(BasePlayer.NetworkQueue.Update, target.player);
                }
            }

            private void StopSeeEveryoneUpdate()
            {
                if (IsInvoking(UpdateSeenHiddenPlayers))
                {
                    CancelInvoke(UpdateSeenHiddenPlayers);
                }

                seenHiddenPositions.Clear();
            }

            internal void ForgetSeenHiddenPlayer(ulong userid)
            {
                seenHiddenPositions.Remove(userid);
            }

            public void StopNetworkGroupsUpdate()
            {
                if (IsInvoking(UpdateNetworkGroups))
                {
                    CancelInvoke(UpdateNetworkGroups);
                }
            }

            public void StartNetworkGroupsUpdate()
            {
                StopNetworkGroupsUpdate();
                InvokeRepeating(UpdateNetworkGroups, 0f, 0.5f);
            }

            private void CreateChildGameObject()
            {
                if (player.IsSpectating())
                {
                    Invoke(CreateChildGameObject, 1f);
                    return;
                }
                child = gameObject.CreateChild();
                child.layer = (int)Layer.Reserved1;
                child.transform.localScale = Vector3.zero;
                col = child.AddComponent<SphereCollider>();
                col.isTrigger = true;
                SetLastAdminCheatTime(float.MaxValue);
            }

            private void OnTriggerEnter(Collider collider)
            {
                if (IsDestroyed || player.IsSpectating())
                {
                    return;
                }
                if (!workbenchCraft && collider.GetComponentInParent<TriggerWorkbench>() is TriggerWorkbench bench)
                {
                    bench.OnTriggerEnter(capsule);
                }
                else if (!player.IsFlying && collider.GetComponentInParent<TriggerParent>() is TriggerParent parent)
                {
                    parent.OnTriggerEnter(capsule);
                }
            }

            private void OnTriggerExit(Collider collider)
            {
                if (IsDestroyed || player.IsSpectating())
                {
                    return;
                }
                if (!workbenchCraft && collider.GetComponentInParent<TriggerWorkbench>() is TriggerWorkbench bench)
                {
                    bench.OnTriggerExit(capsule);
                }
                else if (collider.GetComponentInParent<TriggerParent>() is TriggerParent parent)
                {
                    parent.OnTriggerExit(capsule);
                }
            }
        }

        internal static bool IsVanished(BasePlayer player)
        {
            return player != null && Instance != null && Instance.HiddenPlayers.ContainsKey(player.UserIDString);
        }

        internal static bool HasHiddenState(BasePlayer player)
        {
            return player != null && (player.limitNetworking || player.isInvisible);
        }

        private static void SetAdminCheatTime(BasePlayer player, float cheatTime)
        {
            if (player == null || player.ActivePlayerInd < 0 || !AntiHack.PlayerStates.IsCreated)
                return;
            ref AntiHack.PlayerState state = ref ((Span<AntiHack.PlayerState>)AntiHack.PlayerStates)[player.ActivePlayerInd];
            state.LastAdminCheatTime = cheatTime;
        }

        private static void PauseAntiHackForAdminMove(BasePlayer player)
        {
            if (player == null || player.ActivePlayerInd < 0)
                return;
            BasePlayer.ResetAntiHack(player, AntiHack.PlayerStates, AntiHack.PlayerNoclipStates, AntiHack.PlayerSpeedhackStates, AntiHack.PlayerFlyhackStates);
            SetAdminCheatTime(player, Time.realtimeSinceStartup);
        }

        private void RemoveFromTargets(BasePlayer player)
        {
            var hits = BaseEntity.Query.Server.GetInSphere(player.GetNetworkPosition(), 64f, queryResults, e => e.IsNpc || e is BradleyAPC);
            for (var i = 0; i < hits; i++)
            {
                if (queryResults[i] is BaseEntity entity)
                {
                    if (entity.GetComponent<BaseAIBrain>() is BaseAIBrain brain && brain != null && brain.Senses != null && brain.Senses.Memory != null)
                    {
                        if (brain.Events?.Memory?.Entity?.Get(brain.Events.CurrentInputMemorySlot) == player)
                        {
                            brain.Events.Memory.Entity.Remove(brain.Events.CurrentInputMemorySlot);
                        }
                        brain.Senses.Memory.Players?.Remove(player);
                        brain.Senses.Memory.Targets?.Remove(player);
                        brain.Senses.Memory.Threats?.Remove(player);
                        brain.Senses.Memory.LOS?.Remove(player);
                        brain.Senses.Memory.All?.RemoveAll(si => si.Entity == player);
                    }
                    else if (entity.Cast(out BradleyAPC bradley) && bradley.targetList.Find(t => t.entity == player) is BradleyAPC.TargetInfo targetInfo && targetInfo != default)
                    {
                        bradley.targetList.Remove(targetInfo);
                        Pool.Free(ref targetInfo);
                        bradley.UpdateTargetList();
                        if (ReferenceEquals(MainGunTarget.GetValue(bradley), player))
                        {
                            MainGunTarget.SetValue(bradley, null);
                            NextPatrolTime.SetValue(bradley, Time.time);
                            bradley.UpdateMovement_Patrol();
                        }
                    }
                }
            }
        }

        public void ToggleVanish(BasePlayer player)
        {
            if (player.limitNetworking)
            {
                //if (player.UserIDString == "76561198212544308") player.ChatMessage("Step 2");
                Reappear(player);
            }
            else Disappear(player);
        }

        public void Disappear(BasePlayer player)
        {
            if (!HiddenPlayers.TryGetValue(player.UserIDString, out VanishComponent vc))
            {
                HiddenPlayers[player.UserIDString] = vc = player.gameObject.AddComponent<VanishComponent>();
                TryPatchTemporary();
            }
            else
            {
                vc.StartNetworkGroupsUpdate();
            }
            vc.Instance = this;
            vc.SeeEveryoneUpdate();
            if (player.State.unHostileTimestamp > TimeEx.currentTimestamp)
            {
                player.State.unHostileTimestamp = TimeEx.currentTimestamp;
                player.DirtyPlayerState();
                player.ClientRPC(RpcTarget.Player("SetHostileLength", player), 0f);
            }

            SimpleAIMemory.PlayerIgnoreList.Add(player);
            BaseEntity.Query.Server.RemovePlayer(player);
            RemoveFromTargets(player);
            using var connections = Pool.Get<PooledList<Connection>>();
            foreach (Connection c in Net.sv.connections)
            {
                if (c.player is BasePlayer target && target.UserIDString != player.UserIDString && !config.CanSeeEveryone.Contains(target.GetSteamId()))
                {
                    connections.Add(c);
                }
            }
            player.OnNetworkSubscribersLeave(connections);
            player.DisablePlayerCollider(); //.Invoke("DisablePlayerCollider", 0f);
            player.syncPosition = false;
            player.limitNetworking = false;
            player.limitNetworking = true;
            //player.isInvisible = true;
            player.fallDamageEffect.guid = string.Empty;
            player.drownEffect.guid = string.Empty;
            HeldEntity heldEntity = player.GetHeldEntity();
            if (heldEntity != null)
            {
                //heldEntity.SetHeld(false);
                heldEntity.UpdateVisiblity_Invis();
            }
            var uc = UserConfig.Get(player.GetSteamId());
            if (player.IsConnected)
            {
                if (uc.ShowIndicator)
                {
                    ShowUI(player, uc);
                }
                if (uc.SoundEffects)
                {
                    SendEffectTo(player, uc.EffectsDisappear);
                }
                if (uc.NoClipOnUse)
                {
                    player.SendConsoleCommand("noclip");
                }
                if (HasAccess(player.Connection)) Message(player, "Enabled");
                else Puts(RemoveFormatting(string.Format(Get("EnabledOther"), player.displayName, player.UserIDString)));
            }
            else player.SetServerFall(false);
            if (config.MetabolismPause)
                MetabolismPause(player);
            vc.workbenchCraft = uc.WorkbenchCraft;
            if (vc.workbenchCraft)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, true);
            }
            player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
            foreach (VanishComponent observer in HiddenPlayers.Values)
            {
                observer?.ForgetSeenHiddenPlayer(player.GetSteamId());
            }
            Interface_CallHook("OnPlayerVanished", player);
        }

        public void Reappear()
        {
            using var tmp = Pool.Get<PooledList<VanishComponent>>();
            tmp.AddRange(HiddenPlayers.Values);
            foreach (var vc in tmp)
            {
                if (vc.player != null)
                {
                    Reappear(vc.player);
                }
            }
        }

        public void Reappear(BasePlayer player)
        {
            player.syncPosition = true;
            if (HiddenPlayers.Remove(player.UserIDString, out VanishComponent vc))
            {
                vc.StopNetworkGroupsUpdate();
                UnityEngine.Object.Destroy(vc);
                TryUnpatchTemporary();
            }
            SimpleAIMemory.PlayerIgnoreList.Remove(player);
            BaseEntity.Query.Server.RemovePlayer(player);
            BaseEntity.Query.Server.AddPlayer(player);
            player.limitNetworking = false;
            //player.isInvisible = false;
            player.EnablePlayerCollider(); //.Invoke("EnablePlayerCollider", 0f);
            player.UpdateNetworkGroup();
            player.SendNetworkUpdate();
            player.GetHeldEntity()?.UpdateVisibility_Hand();
            player.drownEffect.guid = "28ad47c8e6d313742a7a2740674a25b5";
            player.fallDamageEffect.guid = "ca14ed027d5924003b1c5d9e523a5fce";
            player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, player.InSafeZone());
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, player.currentCraftLevel == 3);
            if (config.MetabolismPause)
                RestartMetabolism(player);
            else player.metabolism.SendChanges();

            if (player.IsConnected)
            {
                var uc = UserConfig.Get(player.GetSteamId());
                if (uc.SoundEffects)
                {
                    SendEffectTo(player, uc.EffectsAppear);
                }
                if (uc.ShowIndicator)
                {
                    DestroyUi(player, "VanishGUI");
                }
                if (HasAccess(player.Connection)) Message(player, "Disabled");
                else Puts(RemoveFormatting(string.Format(Get("DisabledOther"), player.displayName, player.UserIDString)));
            }
            foreach (VanishComponent observer in HiddenPlayers.Values)
            {
                observer?.ForgetSeenHiddenPlayer(player.GetSteamId());
            }
            Interface_CallHook("OnPlayerUnvanished", player);
        }

        private static void MetabolismPause(BasePlayer player)
        {
            if (player?.metabolism == null) return;
            player.metabolism.calories.value = 500;
            player.metabolism.hydration.value = 250;
            player.metabolism.temperature.min = 20;
            player.metabolism.temperature.max = 20;
            player.metabolism.temperature.value = 20;
            player.metabolism.radiation_poison.value = 0;
            player.metabolism.radiation_poison.max = 0;
            player.metabolism.oxygen.value = 1;
            player.metabolism.oxygen.min = 1;
            player.metabolism.wetness.max = 0;
            player.metabolism.wetness.value = 0;
            player.metabolism.calories.min = player.metabolism.calories.value;
            player.SetHealth(player.MaxHealth());
            player.metabolism.SendChanges();
        }

        private static void RestartMetabolism(BasePlayer player)
        {
            if (player?.metabolism == null) return;
            player.metabolism.temperature.min = -100;
            player.metabolism.temperature.max = 100;
            player.metabolism.radiation_poison.max = 500;
            player.metabolism.oxygen.min = 0;
            player.metabolism.calories.min = 0;
            player.metabolism.wetness.max = 1;
            player.SendNetworkUpdate();
            player.metabolism.SendChanges();
        }

        public void ShowUI(BasePlayer player, UserConfig uc)
        {
            if (player.IsValid() && player.IsConnected)
            {
                if (!player.IsSleeping() && !player.IsReceivingSnapshot && !player.IsDead())
                {
                    // this should never be required: Send texture first (CUI: client can NullRef if png id is used before CL_ReceiveFilePng)
                    //if (uc.Icon != 0 && CommunityEntity.ServerInstance.IsValid())
                    //{
                    //    byte[] bytes = FileStorage.server.Get(uc.Icon, uc.FileStorageType, CommunityEntity.ServerInstance.net.ID);
                    //    if (bytes != null && bytes.Length > 0)
                    //        CommunityEntity.ServerInstance.ClientRPC(RpcTarget.Player("CL_ReceiveFilePng", player.Connection), uc.Icon, (uint)bytes.Length, bytes);
                    //}
                    AddUi(player, uc.ToJson());
                }
                else
                {
                    player.Invoke(() => ShowUI(player, uc), 1f);
                }
            }
        }

        private void SendEffectTo(BasePlayer player, List<string> effects)
        {
            if (effects.Count > 0 && player.IsConnected)
            {
                reusableSoundEffectInstance.Init(Effect.Type.Generic, player, 0, Vector3.zero, Vector3.forward);
                reusableSoundEffectInstance.pooledString = effects.GetRandom();
                EffectNetwork.Send(reusableSoundEffectInstance, player.Connection);
            }
        }

        private static bool HasAccess(Connection cn) => cn == null || cn.authLevel > 0 || config.HasAccess(cn.userid);

        private static bool HasAccessOrAuthLevel(BasePlayer player) => player != null && config.HasAccess(player.GetSteamId()) || (player != null && player.Connection != null && player.Connection.authLevel > 0);

        private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled);

        public static string RemoveFormatting(string source) => source.Contains(">") ? HtmlTagRegex.Replace(source, string.Empty) : source;

        public static bool HasSafePoint(UserConfig uc) => uc.UseSafePoints && uc.SafePoints.Count > 0;

        public static Vector3 GetSafePoint(BasePlayer player, UserConfig uc)
        {
            try
            {
                if (uc.UseSafePoints && uc.SafePoints.Count > 0)
                {
                    return uc.SafePoints.GetRandom();
                }
            }
            catch
            {
                uc.SafePoints?.Clear();
            }
            return player.transform.position;
        }

        #region Configuration

        internal static void Message(BasePlayer target, string key, params object[] args)
        {
            if (target.IsValid())
            {
                target.ChatMessage(args.Length > 0 ? string.Format(Get(key), args) : Get(key));
            }
        }

        internal static string Get(string key)
        {
            return config != null && config.Messages.TryGetValue(key, out string message) ? message : key;
        }

        public class UserConfig
        {
            [JsonProperty(PropertyName = "Show visual indicator (true/false)")]
            public bool ShowIndicator = true;

            private const string LegacyDefaultImagePath = @"HarmonyImages\Vanish\rust2x.png";
            private const string DefaultImagePath = @"HarmonyImages\Vanish\vanish.png";

            [JsonProperty(PropertyName = "Image or Base64 String from https://www.base64-image.de/")]
            public string ImageOrBase64 { get; set; } = DefaultImagePath;

            [JsonProperty(PropertyName = "Image Color")]
            public string ImageColor { get; set; } = "1 1 1 1";

            [JsonProperty(PropertyName = "Image Offset Min")]
            public string ImageOffsetMin { get; set; } = "-320 18";

            [JsonProperty(PropertyName = "Image Offset Max")]
            public string ImageOffsetMax { get; set; } = "-260 78";

            [JsonProperty(PropertyName = "Image Scale Factor")]
            public float ImageScaleFactor { get; set; } = 1f;

            [JsonProperty(PropertyName = "Depth of an underground teleport (upon disconnection)")]
            public float UndergroundTeleportDepth;

            [JsonProperty(PropertyName = "Block incoming damage")]
            public bool BlockAllIncomingDamage = true;

            [JsonProperty(PropertyName = "Block outgoing damage")]
            public bool BlockAllOutgoingDamage;

            [JsonProperty(PropertyName = "Auto vanish on connect")]
            public bool AutoVanish;

            [JsonProperty(PropertyName = "Auto noclip on connect")]
            public bool NoClipOnConnect;

            [JsonProperty(PropertyName = "Auto noclip on use")]
            public bool NoClipOnUse;

            [JsonProperty(PropertyName = "Streamer mode on corpses")]
            public bool StreamerMode = true;

            [JsonProperty(PropertyName = "Use sound effects")]
            public bool SoundEffects = true;

            [JsonProperty(PropertyName = "Use fake workbench crafting speed")]
            public bool WorkbenchCraft;

            [JsonProperty(PropertyName = "Teleport to bag on disconnect")]
            public bool UseBags;

            [JsonProperty(PropertyName = "Enable safepoints")]
            public bool UseSafePoints = true;

            [JsonProperty(PropertyName = "Remove all safepoints after wipe")]
            public bool SafePointsRemoval = true;

            [JsonProperty(PropertyName = "Disappear Effects", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> EffectsDisappear = new()
            {
                "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab"
            };

            [JsonProperty(PropertyName = "Appear Effects", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> EffectsAppear = new()
            {
                "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab"
            };

            [JsonProperty(PropertyName = "Safe Points (do not edit)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            [JsonConverter(typeof(UnityVector3Converter))]
            public List<Vector3> SafePoints = new();

            public static UserConfig Get(ulong id)
            {
                if (!config.Users.TryGetValue(id, out UserConfig uc))
                {
                    config.Users[id] = uc = new();
                    config.SaveConfig();
                }
                uc.LoadImage();
                return uc;
            }

            [JsonProperty(PropertyName = "Icon (do not modify)")]
            public uint Icon;

            internal string uiJson;

            internal string _imgBase64 = string.Empty;

            internal FileStorage.Type FileStorageType = FileStorage.Type.png;

            private bool _reloadIconFromBytes;

            private const string _defaultImageBase64 = "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";

            public void LoadImage()
            {
                MigrateLegacyImagePath();
                if (string.IsNullOrWhiteSpace(ImageOrBase64))
                {
                    ImageOrBase64 = DefaultImagePath;
                }

                if (string.IsNullOrEmpty(_imgBase64))
                {
                    if (IsImageFilePath(ImageOrBase64))
                    {
                        TryLoadImageFile(ImageOrBase64);
                    }

                    if (string.IsNullOrEmpty(_imgBase64) && !PathsEqual(ImageOrBase64, DefaultImagePath))
                    {
                        TryLoadImageFile(DefaultImagePath);
                    }

                    if (string.IsNullOrEmpty(_imgBase64) && !PathsEqual(ImageOrBase64, LegacyDefaultImagePath))
                    {
                        TryLoadImageFile(LegacyDefaultImagePath);
                    }

                    if (string.IsNullOrEmpty(_imgBase64))
                    {
                        string base64 = ImageOrBase64
                            .Replace("data:image/png;base64,", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("data:image/jpeg;base64,", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("data:image/jpg;base64,", "", StringComparison.OrdinalIgnoreCase)
                            .Trim();
                        if (IsSupportedImageBase64(base64))
                        {
                            _imgBase64 = base64;
                            _reloadIconFromBytes = true;
                        }
                        else
                        {
                            _imgBase64 = _defaultImageBase64;
                            _reloadIconFromBytes = true;
                        }
                    }
                }

                EnsureIconInitialized();
            }

            private static bool IsImageFilePath(string value)
            {
                return value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
            }

            private static string ResolveImageFilePath(string imagePath)
            {
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return null;
                }

                string normalized = imagePath.Trim().Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(normalized))
                {
                    return Path.GetFullPath(normalized);
                }

                string serverRoot = ResolveServerRoot();
                string[] candidates =
                {
                    Path.Combine(serverRoot, normalized),
                    Path.Combine(serverRoot, ConfigDirectory, normalized),
                    Path.Combine(ConfigDirectory, normalized),
                    Path.Combine(Directory.GetCurrentDirectory(), normalized)
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    try
                    {
                        string full = Path.GetFullPath(candidates[i]);
                        if (File.Exists(full))
                        {
                            return full;
                        }
                    }
                    catch
                    {
                    }
                }

                return Path.GetFullPath(Path.Combine(serverRoot, normalized));
            }

            private bool TryLoadImageFile(string imagePath)
            {
                string path = ResolveImageFilePath(imagePath);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                byte[] pngBytes = ConvertImageToPng(bytes);
                if (pngBytes == null)
                {
                    Puts($"Vanish icon is not a supported PNG/JPEG: {path}");
                    return false;
                }

                FileStorageType = FileStorage.Type.png;
                _imgBase64 = Convert.ToBase64String(pngBytes);
                _reloadIconFromBytes = true;
                Puts($"Loaded vanish icon from {path}");
                return true;
            }

            private void MigrateLegacyImagePath()
            {
                if (!PathsEqual(ImageOrBase64, LegacyDefaultImagePath))
                {
                    return;
                }

                string newImagePath = ResolveImageFilePath(DefaultImagePath);
                if (string.IsNullOrEmpty(newImagePath) || !File.Exists(newImagePath))
                {
                    return;
                }

                if (Icon != 0 && CommunityEntity.ServerInstance.IsValid())
                {
                    FileStorage.server.RemoveExact(Icon, FileStorageType, CommunityEntity.ServerInstance.net.ID, 0);
                }

                ImageOrBase64 = DefaultImagePath;
                Icon = 0;
                _imgBase64 = string.Empty;
                uiJson = null;
                _reloadIconFromBytes = true;
                config.SaveConfig();
            }

            private static bool PathsEqual(string left, string right)
            {
                if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                {
                    return false;
                }

                return string.Equals(
                    left.Replace('/', '\\').Trim(),
                    right.Replace('/', '\\').Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }

            private void EnsureIconInitialized()
            {
                if (!CommunityEntity.ServerInstance.IsValid())
                {
                    return;
                }
                if (string.IsNullOrEmpty(_imgBase64))
                {
                    RemoveIcon();
                    return;
                }
                if (Icon != 0 && !_reloadIconFromBytes && IsCrcValid)
                {
                    return;
                }

                byte[] bytes = Convert.FromBase64String(_imgBase64);
                uint stored = FileStorage.server.Store(bytes, FileStorageType, CommunityEntity.ServerInstance.net.ID, 0);
                _reloadIconFromBytes = false;
                if (Icon == stored)
                {
                    return;
                }

                if (Icon != 0)
                {
                    FileStorage.server.RemoveExact(Icon, FileStorageType, CommunityEntity.ServerInstance.net.ID, 0);
                }

                Icon = stored;
                config.SaveConfig();
            }

            public void RemoveIcon()
            {
                if (Icon != 0 && CommunityEntity.ServerInstance.IsValid())
                {
                    FileStorage.server.RemoveExact(Icon, FileStorageType, CommunityEntity.ServerInstance.net.ID, 0);
                    Icon = 0;
                    config.SaveConfig();
                }
            }

            private bool IsCrcValid => Icon != 0 && CommunityEntity.ServerInstance.IsValid() && FileStorage.server.Get(Icon, FileStorageType, CommunityEntity.ServerInstance.net.ID, 0) != null;


            public bool IsSupportedImageBase64(string base64)
            {
                if (string.IsNullOrWhiteSpace(base64) || base64.Length < 32)
                {
                    return false;
                }

                if ((base64.Length & 3) != 0)
                {
                    return false;
                }
                byte[] buffer;
                try
                {
                    buffer = Convert.FromBase64String(base64);
                }
                catch (FormatException)
                {
                    return false;
                }
                return IsSupportedImageBytes(buffer);
            }

            public bool IsSupportedImageBytes(byte[] bytes)
            {
                // PNG signature: 89 50 4E 47 0D 0A 1A 0A
                if (bytes is { Length: >= 8 } && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
                {
                    FileStorageType = FileStorage.Type.png;
                    return true;
                }

                // JPEG signature: FF D8 FF, Pragmatic JPEG check: starts with SOI marker and next byte is another marker prefix.
                if (bytes is { Length: >= 3 } && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                {
                    FileStorageType = FileStorage.Type.jpg;
                    return true;
                }

                return false;
            }

            private byte[] ConvertImageToPng(byte[] sourceBytes)
            {
                if (!IsSupportedImageBytes(sourceBytes))
                {
                    return null;
                }
                if (FileStorageType == FileStorage.Type.png)
                {
                    return sourceBytes;
                }
                var texture = new Texture2D(2, 2, UnityEngine.TextureFormat.ARGB32, false);
                try
                {
                    if (!texture.LoadImage(sourceBytes, false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        return null;
                    }

                    byte[] pngBytes = texture.EncodeToPNG();
                    UnityEngine.Object.Destroy(texture);
                    return pngBytes;
                }
                catch
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
            }

            internal bool offsetsInitialized;
            internal string imageOffsetMin;
            internal string imageOffsetMax;

            public void ResetUi()
            {
                offsetsInitialized = false;
                _imgBase64 = null;
                uiJson = null;
                Icon = 0;
                LoadImage();
            }

            public string ToJson()
            {
                if (!offsetsInitialized)
                {
                    string scaledMin = ImageOffsetMin;
                    string scaledMax = ImageOffsetMax;
                    ScaleImage(ref scaledMin, ref scaledMax, ImageScaleFactor);

                    imageOffsetMin = scaledMin;
                    imageOffsetMax = scaledMax;
                    offsetsInitialized = true;
                }

                if (string.IsNullOrEmpty(uiJson))
                {
                    const string rectAnchorMin = "0.5 0";
                    const string rectAnchorMax = "0.5 0";

                    uiJson = "[{\"name\":\"VanishGUI\",\"destroyUi\":\"VanishGUI\",\"parent\":\"{Parent}\",\"components\":[{\"type\":\"UnityEngine.UI.RawImage\",\"color\":\"{ImageColor}\",\"png\":\"{Image}\"},{\"type\":\"RectTransform\",\"anchormin\":\"{AnchorMin}\",\"anchormax\":\"{AnchorMax}\",\"offsetmin\":\"{OffsetMin}\",\"offsetmax\":\"{OffsetMax}\"},{\"type\":\"Draggable\",\"limitToParent\":true,\"keepOnTop\":true,\"positionRPC\":\"Relative\"}]}]";

                    uiJson = uiJson
                        .Replace("{Parent}", config.UiParentConfig)
                        .Replace("{AnchorMin}", rectAnchorMin)
                        .Replace("{AnchorMax}", rectAnchorMax)
                        .Replace("{OffsetMin}", imageOffsetMin)
                        .Replace("{OffsetMax}", imageOffsetMax)
                        .Replace("{ImageColor}", ImageColor);
                }

                EnsureIconInitialized();
                return uiJson.Replace("{Image}", Icon.ToString());
            }

            public void OnCuiDraggableDrag(BasePlayer player, string name, Vector3 position, CommunityEntity.DraggablePositionSendType type)
            {
                if (name != "VanishGUI") return;
                if (type != CommunityEntity.DraggablePositionSendType.Relative) return;

                Vector2 delta = new Vector2(position.x, position.y);
                Vector2 min = ParseOffset(ImageOffsetMin);
                Vector2 max = ParseOffset(ImageOffsetMax);

                min += delta;
                max += delta;

                ImageOffsetMin = ToOffsetString(min);
                ImageOffsetMax = ToOffsetString(max);

                ResetUi();

                if (player.IsInvoking(DelayedSave))
                {
                    player.CancelInvoke(DelayedSave);
                }

                player.Invoke(DelayedSave, 5f);
            }

            private void DelayedSave()
            {
                config.SaveConfig();
            }

            private static Vector2 ParseOffset(string value)
            {
                if (string.IsNullOrEmpty(value)) return default;
                string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) return default;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return default;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return default;
                return new Vector2(x, y);
            }

            private static string ToOffsetString(Vector2 value)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###}", value.x, value.y);
            }

            public static void ScaleImage(ref string offsetMinStr, ref string offsetMaxStr, float scaleFactor = 2f)
            {
                if (scaleFactor == 1f)
                {
                    return;
                }
                string[] min = offsetMinStr.Split(' ');
                string[] max = offsetMaxStr.Split(' ');
                Vector2 offsetMin = new(Convert.ToSingle(min[0]), Convert.ToSingle(min[1]));
                Vector2 offsetMax = new(Convert.ToSingle(max[0]), Convert.ToSingle(max[1]));
                float originalBottomY = offsetMin.y;
                Vector2 center = (offsetMin + offsetMax) * 0.5f;
                Vector2 newHalfSize = ((offsetMax - offsetMin) * 0.5f) * scaleFactor;
                offsetMin = center - newHalfSize;
                offsetMax = center + newHalfSize;
                float deltaY = originalBottomY - offsetMin.y;
                offsetMin.y += deltaY;
                offsetMax.y += deltaY;
                offsetMinStr = offsetMin.x.ToString("F3") + " " + offsetMin.y.ToString("F3");
                offsetMaxStr = offsetMax.x.ToString("F3") + " " + offsetMax.y.ToString("F3");
            }
        }

        public class Config
        {
            public Config()
            {
                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }
            }

            [JsonProperty(PropertyName = "Users", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, UserConfig> Users = new();

            [JsonProperty(PropertyName = "Can see users in vanish", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> CanSeeEveryone = new() { 76561198212544308 };

            [JsonProperty(PropertyName = "Access List", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> AccessList = new();

            [JsonProperty(PropertyName = "Messages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> Messages = new()
            {
                {"Disabled", "<color=#FF686B>Vanish disabled</color>"},
                {"Enabled", "<color=#91D6FF>Vanish enabled</color>"},
                {"DisabledOther", "<color=#FF686B>Vanish disabled on {0} ({1})</color>"},
                {"EnabledOther", "<color=#91D6FF>Vanish enabled on {0} ({1})</color>"},
                {"AccessHelp", "No player name provided."},
                {"Saved", "Saved current position as your safe point."},
                {"NothingInSight", "Nothing in sight."},
                {"NoSuchPlayer", "No such player found: {0}"},
                {"Looting", "Looting: {0} ({1})"},
                {"Granted", "{0} ({1}) has been allowed permission."},
                {"Revoked", "{0} ({1}) no longer has permission."},
                {"ResetImg", "Your vanish icon has been reset."},
            };

            [JsonProperty(PropertyName = "UI parent panel")]
            public string UiParentConfig { get; set; } = "Hud.Menu";

            [JsonProperty(PropertyName = "Pause metabolism when vanished")]
            public bool MetabolismPause { get; set; }

            [JsonProperty(PropertyName = "Teleport to map marker when vanished")]
            public bool TeleportToMarkerWhenVanished { get; set; }

            public static string ConfigPath() => Path.Combine(ConfigDirectory, "Vanish.json");

            internal bool HasAccess(ulong userid) => Users.ContainsKey(userid) || AccessList.Contains(userid);

            public static void ReloadConfig()
            {
                string path = ConfigPath();
                if (File.Exists(path))
                {
                    try
                    {
                        config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
                    }
                    catch (Exception ex)
                    {
                        LoadDefaultConfig();
                        Puts(ex.ToString());
                        return;
                    }
                }
                if (config == null)
                {
                    LoadDefaultConfig();
                }
                config.SaveConfig();
            }

            public void SaveConfig()
            {
                File.WriteAllText(ConfigPath(), JsonConvert.SerializeObject(config, Formatting.Indented));
            }

            public static void LoadDefaultConfig()
            {
                config = new();
            }
        }

        #endregion

        private class UnityVector3Converter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3) || objectType == typeof(List<Vector3>);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value is Vector3 vector)
                {
                    writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
                }
                else if (value is List<Vector3> vectorList)
                {
                    writer.WriteStartArray();
                    foreach (var vec in vectorList)
                    {
                        writer.WriteValue($"{vec.x} {vec.y} {vec.z}");
                    }
                    writer.WriteEndArray();
                }
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (objectType == typeof(Vector3))
                {
                    if (reader.TokenType == JsonToken.String)
                    {
                        var values = reader.Value.ToString().Trim().Split(' ');
                        return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                    }
                    else if (reader.TokenType == JsonToken.StartObject)
                    {
                        var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
                        return new Vector3(Convert.ToSingle(obj["x"]), Convert.ToSingle(obj["y"]), Convert.ToSingle(obj["z"]));
                    }
                    else
                    {
                        throw new JsonSerializationException($"Unexpected token '{reader.TokenType}' when parsing Vector3.");
                    }
                }
                else if (objectType == typeof(List<Vector3>))
                {
                    var vectors = new List<Vector3>();
                    if (reader.TokenType == JsonToken.StartArray)
                    {
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonToken.EndArray)
                            {
                                break;
                            }
                            if (reader.TokenType == JsonToken.String)
                            {
                                var values = reader.Value.ToString().Trim().Split(' ');
                                vectors.Add(new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2])));
                            }
                            else if (reader.TokenType == JsonToken.StartObject)
                            {
                                var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
                                vectors.Add(new Vector3(Convert.ToSingle(obj["x"]), Convert.ToSingle(obj["y"]), Convert.ToSingle(obj["z"])));
                            }
                            else
                            {
                                throw new JsonSerializationException($"Unexpected token '{reader.TokenType}' when parsing Vector3 in List<Vector3>.");
                            }
                        }
                    }
                    else
                    {
                        throw new JsonSerializationException($"Unexpected token '{reader.TokenType}' when parsing List<Vector3>.");
                    }
                    return vectors;
                }
                else
                {
                    throw new JsonSerializationException($"Unsupported object type '{objectType}' for UnityVector3Converter.");
                }
            }
        }

        public class UnityQuaternionConverter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value is Quaternion quaternion)
                {
                    writer.WriteValue($"{quaternion.x} {quaternion.y} {quaternion.z} {quaternion.w}");
                }
                else
                {
                    throw new JsonSerializationException($"Expected: 'Quaternion', Found: '{value}'");
                }
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    string[] values = reader.Value.ToString().Trim().Split(' ');
                    return new Quaternion(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]), Convert.ToSingle(values[3]));
                }

                if (reader.TokenType == JsonToken.StartObject)
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
                    return new Quaternion(Convert.ToSingle(obj["x"]), Convert.ToSingle(obj["y"]), Convert.ToSingle(obj["z"]), Convert.ToSingle(obj["w"]));
                }

                throw new JsonSerializationException($"Unexpected token '{reader.TokenType}' when parsing Quaternion.");
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Quaternion);
            }
        }
    }
}

namespace HarmonyMods.RustGame.Nivex.Vanish.VanishExtensions
{
    public static class Methods
    {
        public static ulong GetSteamId(this BasePlayer player) => (ulong)player.userID;
        public static bool HasConnection(this BasePlayer a) => a != null && a.Connection != null;
        public static bool Cast<T>(this BaseNetworkable entity, out T component) where T : BaseNetworkable { if (entity == null) { component = null; return false; } component = entity as T; return component != null; }
        public static List<T> Where<T>(this IEnumerable<T> a, Func<T, bool> b) { List<T> c = new(a is ICollection<T> n ? n.Count : 4); foreach (var d in a) { if (b(d)) { c.Add(d); } } return c; }
        public static IEnumerable<V> Select<T, V>(this IEnumerable<T> a, Func<T, V> b) { var c = new List<V>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { c.Add(b(d.Current)); } } return c; }
        public static List<T> ToList<T>(this IEnumerable<T> a) => new(a);
        public static BasePlayer Player(this ConsoleSystem.Option options) => options.Connection?.player as BasePlayer;
        public static BaseEntity CES => CommunityEntity.ServerInstance;
        public static void DestroyUi(BasePlayer player, string elem) { if (player.HasConnection() && CES.IsValid()) CES.ClientRPC(new RpcTarget { Function = "DestroyUI", Connections = new SendInfo { connection = player.Connection } }, elem); }
        public static void AddUi(BasePlayer player, string json) { if (player.HasConnection() && CES.IsValid()) CES.ClientRPC(RpcTarget.Player("AddUI", player.Connection), json); }
    }
}