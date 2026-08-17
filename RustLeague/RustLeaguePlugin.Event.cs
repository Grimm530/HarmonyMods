using System;
using System.Collections.Generic;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace RustLeagueHarmony
{
    public partial class RustLeaguePlugin
    {
        public void CmdConsoleRl(ConsoleSystem.Arg arg, string[] args)
        {
            if (args == null || args.Length == 0 || args[0].Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                if (eventOpen)
                {
                    arg.ReplyWith(Lang("OpenEventConsoleAlready"));
                    return;
                }
                if (!TryPrepareLocation(null))
                {
                    arg.ReplyWith(Lang("noGrid"));
                    return;
                }
                arg.ReplyWith(Lang("OpenEventConsole"));
                setupEvent();
                return;
            }
            if (args[0].Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                closeEvent();
                arg.ReplyWith(Lang("endEvent"));
                return;
            }
            if (args[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
            {
                Grid.StartScan();
                arg.ReplyWith(Lang("scanStarted"));
                return;
            }
            if (args[0].Equals("tp", StringComparison.OrdinalIgnoreCase) || args[0].Equals("goto", StringComparison.OrdinalIgnoreCase))
            {
                if (arg.Player() == null)
                {
                    arg.ReplyWith("Run this from the game client.");
                    return;
                }
                TeleportAdminToArena(arg.Player());
                return;
            }
            if (args[0].Equals("spawn", StringComparison.OrdinalIgnoreCase))
            {
                if (arg.Player() == null)
                {
                    arg.ReplyWith("Run this from the game client.");
                    return;
                }
                AdminSpawnAndTeleport(arg.Player());
                return;
            }
            if (args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                if (arg.Player() == null)
                {
                    arg.ReplyWith("Run this from the game client.");
                    return;
                }
                StartSoloTest(arg.Player());
                return;
            }
            arg.ReplyWith("Usage: rl | rl.open | rl.close | rl.tp | rl.spawn | rl.test | rl.scan");
        }

        public void CmdChatRl(BasePlayer player, string[] args)
        {
            if (player == null) return;
            if (args == null || args.Length == 0)
            {
                if (!eventOpen) { Reply(player, "NoneOpen"); return; }
                if (eventRunning) { Reply(player, "alreadystarted"); return; }
                openJoinWindow(player, eventPlayer.ContainsKey(player.GetUserId()));
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "open":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    if (eventOpen) { Reply(player, "OpenEventConsoleAlready"); return; }
                    if (!TryPrepareLocation(null)) { Reply(player, "noGrid"); return; }
                    setupEvent();
                    return;
                case "close":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    if (!eventOpen) { Reply(player, "notopen"); return; }
                    Reply(player, "endEvent");
                    closeEvent();
                    return;
                case "join":
                    TryJoin(player);
                    return;
                case "leave":
                    TryLeave(player);
                    return;
                case "location":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    var pos = player.transform.position;
                    player.ChatMessage($"x {pos.x} Y {pos.y} Z {pos.z}  grid {GridRef(pos)}");
                    return;
                case "center":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    if (eventRunning || eventOpen) { Reply(player, "NotSetWhenStarted"); return; }
                    ApplyArenaLayoutFromCatalog(player.transform.position, player.GetNetworkRotation().eulerAngles.y);
                    configData.settings.UseFixedLocation = true;
                    SaveConfig();
                    Reply(player, "centerSet");
                    return;
                case "red":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    if (eventRunning || eventOpen) { Reply(player, "NotSetWhenStarted"); return; }
                    configData.eventSettings.RedZone = player.transform.position;
                    configData.eventSettings.RedZoneRotation = player.GetNetworkRotation().eulerAngles.y;
                    SaveConfig();
                    drawZones(player, configData.eventSettings.RedZone, configData.eventSettings.RedZoneRotation, configData.eventSettings.RedZoneSize / 2f);
                    Reply(player, "RedZoneSet");
                    return;
                case "blue":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    if (eventRunning || eventOpen) { Reply(player, "NotSetWhenStarted"); return; }
                    configData.eventSettings.BlueZone = player.transform.position;
                    configData.eventSettings.BlueZoneRotation = player.GetNetworkRotation().eulerAngles.y;
                    SaveConfig();
                    drawZones(player, configData.eventSettings.BlueZone, configData.eventSettings.BlueZoneRotation, configData.eventSettings.BlueZoneSize / 2f);
                    Reply(player, "BlueZoneSet");
                    return;
                case "here":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    if (eventOpen) { Reply(player, "OpenEventConsoleAlready"); return; }
                    ApplyArenaLayoutFromCatalog(player.transform.position, player.GetNetworkRotation().eulerAngles.y);
                    setupEvent();
                    return;
                case "tp":
                case "goto":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    TeleportAdminToArena(player);
                    return;
                case "spawn":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    AdminSpawnAndTeleport(player);
                    return;
                case "test":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    StartSoloTest(player);
                    return;
                case "scan":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    Grid.StartScan();
                    Reply(player, "scanStarted");
                    return;
                case "status":
                    if (!IsAdmin(player)) { Reply(player, "Blocked"); return; }
                    Reply(player, "statusLine", eventOpen, eventRunning, eventPlayer.Count, ArenaAltitude, JoinTimeLeft(), arenaObjects.Count, arenaEntities.Count);
                    return;
            }
        }

        public void HandleJoinUi(BasePlayer player, string action)
        {
            if (player == null) return;
            if (string.Equals(action, "join", StringComparison.OrdinalIgnoreCase))
                TryJoin(player);
            else
                TryLeave(player);
        }

        internal Vector3 GetArenaTeleportPos()
        {
            Vector3 pos = configData.eventSettings.eventCenter;
            if (pos == Vector3.zero)
                pos = arenaOrigin;
            if (pos == Vector3.zero)
                return Vector3.zero;
            return pos + Vector3.up * 3f;
        }

        internal void StartSoloTest(BasePlayer player)
        {
            if (player == null) return;
            if (eventRunning)
            {
                Reply(player, "alreadystarted");
                TeleportAdminToArena(player);
                return;
            }

            soloTest = true;
            testing = true;
            bool spawnedNow = !eventOpen;
            if (!eventOpen)
            {
                ApplyArenaLayoutFromCatalog(player.transform.position, player.GetNetworkRotation().eulerAngles.y);
                setupEvent();
                Reply(player, "soloTestStarting", GridRef(configData.eventSettings.eventCenter));
            }

            eventPlayer[player.GetUserId()] = true;
            float delay = spawnedNow ? 4f : 0.5f;
            timer.Once(delay, () =>
            {
                if (player == null || !player.IsConnected) return;
                if (!eventOpen || eventRunning) return;
                eventPlayer[player.GetUserId()] = true;
                if (!checkStartEvent())
                {
                    Reply(player, "NoneOpen");
                    return;
                }
                startTheEvent();
                Reply(player, "soloTestStarted");
            });
        }

        internal void TeleportAdminToArena(BasePlayer player)
        {
            if (player == null) return;
            Vector3 dest = GetArenaTeleportPos();
            if (dest == Vector3.zero || (!eventOpen && arenaObjects.Count == 0 && arenaEntities.Count == 0))
            {
                Reply(player, "arenaNotSpawned");
                return;
            }
            TeleportPlayerPosition(player, dest, null);
            player.ChatMessage(string.Format(Lang("arenaTp"), GridRef(dest), dest.x, dest.y, dest.z, arenaObjects.Count, arenaEntities.Count));
        }

        internal void AdminSpawnAndTeleport(BasePlayer player)
        {
            if (player == null) return;
            if (!eventOpen)
            {
                ApplyArenaLayoutFromCatalog(player.transform.position, player.GetNetworkRotation().eulerAngles.y);
                setupEvent();
                Reply(player, "arenaSpawning", GridRef(configData.eventSettings.eventCenter));
                timer.Once(4f, () => TeleportAdminToArena(player));
                return;
            }
            TeleportAdminToArena(player);
        }

        private void TryJoin(BasePlayer player)
        {
            if (!eventOpen) { Reply(player, "NoneOpen"); return; }
            if (eventRunning) { Reply(player, "alreadystarted"); return; }
            ulong id = player.GetUserId();
            if (eventPlayer.ContainsKey(id)) { Reply(player, "alreadyjoined"); return; }
            if (configData.ItemSettings.joinItemEnable && !checkPayment(player))
                return;
            eventPlayer[id] = true;
            Reply(player, "joined");
            refreshJoinList();
        }

        private void TryLeave(BasePlayer player)
        {
            if (eventRunning) { Reply(player, "alreadystarted"); return; }
            ulong id = player.GetUserId();
            if (!eventPlayer.ContainsKey(id)) { Reply(player, "notinevent"); return; }
            eventPlayer.Remove(id);
            if (configData.ItemSettings.joinItemEnable && paiedPlayers.ContainsKey(id))
            {
                giveWinItem(player, configData.ItemSettings.joinItem, configData.ItemSettings.joinItemAmount, true);
                paiedPlayers.Remove(id);
            }
            Reply(player, "left");
            refreshJoinList(player);
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            ulong id = player.GetUserId();
            if (eventRunning) return;
            if (eventPlayer.ContainsKey(id))
            {
                eventPlayer.Remove(id);
                refreshJoinList();
            }
        }

        private void stopEventTimers()
        {
            for (int i = 1; i <= 3; i++)
            {
                if (!EventTimers.TryGetValue(i, out var t) || t == null) continue;
                t.Destroy();
                EventTimers.Remove(i);
            }
        }

        private void ScheduleNextCycle(float delaySeconds)
        {
            stopEventTimers();
            float wait = delaySeconds;
            if (wait < 1f) wait = 1f;
            EventTimers[1] = timer.Once(wait, () =>
            {
                if (!configData.settings.autoEvents) return;
                if (eventOpen || eventRunning)
                {
                    ScheduleNextCycle(30f);
                    return;
                }
                if (BasePlayer.activePlayerList.Count < configData.settings.playersOnlineNeeded)
                {
                    ScheduleNextCycle(60f);
                    return;
                }
                if (!TryPrepareLocation(null))
                {
                    Debug.LogWarning("[RustLeague] Cycle skipped — no valid grid location yet.");
                    ScheduleNextCycle(60f);
                    return;
                }
                setupEvent();
            });
        }

        private bool TryPrepareLocation(BasePlayer adminHere)
        {
            if (configData.settings.UseFixedLocation)
            {
                Vector3 origin = configData.eventSettings.ArenaOrigin;
                if (origin == Vector3.zero)
                    origin = configData.eventSettings.eventCenter;
                if (origin != Vector3.zero)
                {
                    arenaYaw = configData.eventSettings.RedZoneRotation;
                    ApplyArenaLayoutFromCatalog(origin, arenaYaw);
                    return true;
                }
            }

            if (!Grid.TryPick(out Vector3 spot))
                return false;

            float yaw = UnityEngine.Random.Range(0, 4) * 90f;
            ApplyArenaLayoutFromCatalog(spot, yaw);
            return true;
        }

        internal void ApplyArenaLayout(Vector3 center, float yaw)
        {
            center = LiftToSky(center);
            float dist = Mathf.Max(20f, configData.eventSettings.FieldGoalDistance);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 forward = rot * Vector3.forward;

            configData.eventSettings.eventCenter = center;
            configData.eventSettings.RedZone = center + forward * dist;
            configData.eventSettings.BlueZone = center - forward * dist;
            configData.eventSettings.RedZoneRotation = yaw;
            configData.eventSettings.BlueZoneRotation = yaw + 180f;
        }

        private void setupEvent()
        {
            eventOpen = true;
            _cycleOpenedAt = DateTime.UtcNow;
            if (arenaOrigin == Vector3.zero)
                arenaOrigin = configData.eventSettings.eventCenter;
            StartArenaSpawn(arenaOrigin, arenaYaw);
            SpawnMapMarker();
            Broadcast("startEvent", GridRef(configData.eventSettings.eventCenter));
            stopEventTimers();

            int window = Mathf.Max(60, configData.settings.JoinWindowSeconds);
            EventTimers[2] = timer.Every(60f, checkEventTime);
            EventTimers[3] = timer.Every(1.1f, () =>
            {
                if (eventPlayer.Count >= configData.settings.MaxPlayersToStart && checkStartEvent())
                    startTheEvent();
            });
            timer.Once(window, () =>
            {
                if (!eventOpen || eventRunning) return;
                failJoinWindow();
            });
        }

        private void startTheEvent()
        {
            stopEventTimers();
            spawnEntitys();
            Broadcast("StartOff");
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null) continue;
                CuiHelper.DestroyUi(player, "waitingPlay");
            }
        }

        private void checkEventTime()
        {
            if (!eventOpen || eventRunning) return;
            if (checkStartEvent())
            {
                startTheEvent();
                return;
            }
            Broadcast("stillLooking", GridRef(configData.eventSettings.eventCenter), JoinTimeLeft());
        }

        private void failJoinWindow()
        {
            eventOpen = false;
            eventRunning = false;
            CloseJoinList();
            ClearJoinState();
            KillMapMarker();
            DespawnArena();
            Broadcast("eventTries");
            if (configData.ItemSettings.joinItemEnable) refundPlayer();
            AfterEventClosed();
        }

        public bool checkStartEvent()
        {
            int count = 0;
            RuningEventPlayer.Clear();
            foreach (var ids in eventPlayer)
            {
                BasePlayer player = BasePlayer.FindByID(ids.Key);
                if (player == null || !player.IsConnected) continue;
                RuningEventPlayer[player.GetUserId()] = "none";
                count++;
                if (count >= configData.settings.MaxPlayersToStart) break;
            }
            if ((soloTest || testing) && count >= 1) return true;
            if (count >= configData.settings.MinPlayersToStart) return true;
            RuningEventPlayer.Clear();
            return false;
        }

        private void AfterEventClosed()
        {
            if (!configData.settings.autoEvents)
            {
                stopEventTimers();
                return;
            }
            int interval = Mathf.Max(60, configData.settings.EventIntervalSeconds);
            double elapsed = _cycleOpenedAt == DateTime.MinValue ? interval : (DateTime.UtcNow - _cycleOpenedAt).TotalSeconds;
            float wait = (float)Math.Max(30d, interval - elapsed);
            ScheduleNextCycle(wait);
        }

        private void ClearJoinState()
        {
            EventPlayerLastPos.Clear();
            eventPlayer.Clear();
            RuningEventPlayer.Clear();
            eventEntitys.Clear();
            RedEventCars.Clear();
            BlueEventCars.Clear();
            LiveCars.Clear();
            eventPlayerList.Clear();
        }
    }
}
