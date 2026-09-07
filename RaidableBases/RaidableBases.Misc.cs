using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {


        private class TargetInfoController : IDisposable
        {
            private const string TARGET_INFO_UI = "RB_UI_TargetInfo";
            private const float MAX_TARGET_DISTANCE = 500f;
            private const float DRAW_HEIGHT = 0.15f;

            private RaidableBases Instance;
            private Dictionary<ulong, Timer> _timers = new();
            private UITargetInfoSettings Settings => Instance.config.UI.TargetInfo ??= new();
            private float DisplaySeconds => Mathf.Max(1f, Settings.SecondsShown);

            private class RaidStats
            {
                internal int Entities, Deployables, Blocks, Traps, Containers, LootContainers, Npcs, Doors, IoEntities, Pickupable, Twig, Wood, Stone, Metal, Armored, AutoTurrets, ShotgunTraps, FlameTurrets, SamSites, TeslaCoils, FogMachines;
            }

            private struct SummaryCard
            {
                internal string Value, Label, Note;

                internal SummaryCard(string value, string label, string note = null)
                {
                    Value = value;
                    Label = label;
                    Note = note;
                }
            }

            private struct InfoMetric
            {
                internal string Label, Value, Color;
                internal float Ratio;

                internal InfoMetric(string label, string value, string color, float ratio = -1f)
                {
                    Label = label;
                    Value = value;
                    Color = color;
                    Ratio = ratio;
                }
            }

            private class InfoSection
            {
                internal string Title, Summary;
                internal List<InfoMetric> Metrics;

                internal InfoSection(string title, string summary, List<InfoMetric> metrics)
                {
                    Title = title;
                    Summary = summary;
                    Metrics = metrics;
                }
            }

            internal TargetInfoController(RaidableBases instance)
            {
                Instance = instance;
            }

            public void Dispose()
            {
                foreach (Timer timer in _timers.Values)
                {
                    if (timer is { Destroyed: false })
                    {
                        timer.Destroy();
                    }
                }

                _timers.Clear();

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    CuiHelper.DestroyUi(player, TARGET_INFO_UI);
                }
            }

            internal void Show(BasePlayer player, ConsoleSystem.Arg arg)
            {
                if (!Settings.Enabled)
                {
                    Hide(player);
                    arg.ReplyWith("rb.info UI is disabled in the configuration.");
                    return;
                }

                if (!TryGetTarget(player, out BaseEntity target, out RaidableBase raid, out HumanoidBrain brain))
                {
                    arg.ReplyWith("No raidable base entity or event NPC was found in your line of sight.");
                    return;
                }

                Hide(player);

                if (brain != null)
                {
                    ShowNpc(player, brain);
                }
                else ShowRaid(player, target, raid);
            }

            internal void Hide(BasePlayer player, bool destroyUi = true)
            {
                if (player == null)
                {
                    return;
                }

                if (_timers.Remove(player.userID, out Timer timer) && timer is { Destroyed: false })
                {
                    timer.Destroy();
                }

                if (destroyUi)
                {
                    CuiHelper.DestroyUi(player, TARGET_INFO_UI);
                }
            }

            private bool TryGetTarget(BasePlayer player, out BaseEntity target, out RaidableBase raid, out HumanoidBrain brain)
            {
                target = null;
                raid = null;
                brain = null;

                using var hits = DisposableList<RaycastHit>();

                GamePhysics.TraceAll(player.eyes.HeadRay(), 0f, hits, MAX_TARGET_DISTANCE, -1, QueryTriggerInteraction.Ignore, player);

                for (int i = 0; i < hits.Count; i++)
                {
                    BaseEntity entity = hits[i].GetEntity();

                    if (entity == null || entity.IsDestroyed)
                    {
                        continue;
                    }

                    for (int depth = 0; entity != null && depth < 8; depth++)
                    {
                        if (entity.Is(out HumanoidNPC npc) && Instance.Get(npc.userID, out brain))
                        {
                            target = npc;
                            raid = brain.raid;
                            return true;
                        }

                        if (Instance.Get(entity, out raid))
                        {
                            target = entity;
                            return true;
                        }

                        BaseEntity parent = entity.GetParentEntity();

                        if (parent == null || ReferenceEquals(parent, entity))
                        {
                            break;
                        }

                        entity = parent;
                    }
                }

                return false;
            }

            private void ShowRaid(BasePlayer player, BaseEntity target, RaidableBase raid)
            {
                RaidStats stats = GetRaidStats(raid);
                int lootAmountRemaining = raid.UpdateLootAmountCounted();
                int lootAmountTracked = raid.lootAmountTracked;
                string owner = GetOwnerText(raid);
                string state = GetRaidState(raid);
                string targetName = target == null ? "Unknown" : target.ShortPrefabName;
                string grid = raid.FormatGridReference(player, raid.Location);
                string despawn = raid.DespawnTime > 0d ? $"{raid.DespawnTime:N0} minutes" : "Not scheduled";
                string eventType = $"{raid.Type} / {raid.Options.Mode} / {(raid.AllowPVP ? "PVP" : "PVE")}";
                int maxGrade = Max(stats.Twig, stats.Wood, stats.Stone, stats.Metal, stats.Armored);
                int maxEntityType = Max(stats.Doors, stats.Containers, stats.IoEntities, stats.Pickupable, stats.Npcs);
                int maxDefense = Max(stats.AutoTurrets, stats.ShotgunTraps, stats.FlameTurrets, stats.SamSites, stats.TeslaCoils, stats.FogMachines);
                int participantCount = raid.GetRaiders().Count;
                int intruderCount = raid.GetIntruders().Count;

                var cards = new List<SummaryCard>
                {
                    new(stats.Entities.ToString("N0"), "total entities", $"target: {targetName}"),
                    new(stats.Blocks.ToString("N0"), "building blocks"),
                    new(stats.Deployables.ToString("N0"), "deployables"),
                    new(stats.Traps.ToString("N0"), "traps and defenses"),
                    new($"{lootAmountRemaining:N0} / {lootAmountTracked:N0}", "loot remaining")
                };

                var sections = new List<InfoSection>
                {
                    new("Building Tiers", $"{stats.Blocks:N0} blocks", new()
                    {
                        Metric("Twig", stats.Twig, "#C5A071", maxGrade),
                        Metric("Wood", stats.Wood, "#D19A5B", maxGrade),
                        Metric("Stone", stats.Stone, "#A7AFB7", maxGrade),
                        Metric("Metal", stats.Metal, "#6EA6BD", maxGrade),
                        Metric("Armored", stats.Armored, "#C9D2DE", maxGrade)
                    }),
                    new("Entity Types", $"{stats.Entities:N0} tracked", new()
                    {
                        Metric("Doors", stats.Doors, "#D19A5B", maxEntityType),
                        Metric("Containers", stats.Containers, "#77A8BA", maxEntityType),
                        Metric("Electrical / IO", stats.IoEntities, "#9DA8B3", maxEntityType),
                        Metric("Pickupable", stats.Pickupable, "#56C596", maxEntityType),
                        Metric("NPCs", stats.Npcs, "#C184E8", maxEntityType)
                    }),
                    new("Raid Status", eventType, new()
                    {
                        new("State", state, "#5865F2"),
                        new("Owner", owner, "#56C596"),
                        new("Raiders", participantCount.ToString("N0"), "#77A8BA", Ratio(participantCount, Math.Max(1, participantCount + intruderCount))),
                        new("Intruders", intruderCount.ToString("N0"), "#E56B6F", Ratio(intruderCount, Math.Max(1, participantCount + intruderCount))),
                        new("Loot containers", stats.LootContainers.ToString("N0"), "#E0B04B", Ratio(stats.LootContainers, Math.Max(1, stats.Containers)))
                    }),
                    new("Defense Types", $"{stats.Traps:N0} tracked traps", new()
                    {
                        Metric("Auto turrets", stats.AutoTurrets, "#E56B6F", maxDefense),
                        Metric("Shotgun traps", stats.ShotgunTraps, "#E18D50", maxDefense),
                        Metric("Flame turrets", stats.FlameTurrets, "#E0B04B", maxDefense),
                        Metric("SAM sites", stats.SamSites, "#7896C8", maxDefense),
                        Metric("Tesla coils", stats.TeslaCoils, "#8D78C8", maxDefense),
                        Metric("Fog machines", stats.FogMachines, "#72A9A9", maxDefense)
                    })
                };

                DrawUi(player, "Base Composition", $"{raid.BaseName} at {grid}  •  {eventType}  •  {state}", $"{stats.Entities:N0} total entities", cards, sections, $"Radius {raid.ProtectionRadius:N0}m  •  Despawn {despawn}  •  Owner {owner}");
            }

            private RaidStats GetRaidStats(RaidableBase raid)
            {
                var stats = new RaidStats();

                foreach (BaseEntity entity in raid.Entities)
                {
                    if (entity.IsKilled())
                    {
                        continue;
                    }

                    stats.Entities++;

                    if (entity.Is(out BuildingBlock block))
                    {
                        stats.Blocks++;

                        switch (block.grade)
                        {
                            case BuildingGrade.Enum.Twigs: stats.Twig++; break;
                            case BuildingGrade.Enum.Wood: stats.Wood++; break;
                            case BuildingGrade.Enum.Stone: stats.Stone++; break;
                            case BuildingGrade.Enum.Metal: stats.Metal++; break;
                            case BuildingGrade.Enum.TopTier: stats.Armored++; break;
                        }
                    }

                    if (Instance.DeployableItems.ContainsKey(entity.PrefabName) || entity is DecorDeployable)
                    {
                        stats.Deployables++;
                    }

                    if (entity is BaseTrap || raid.IsWeapon(entity))
                    {
                        stats.Traps++;
                    }

                    if (entity is StorageContainer)
                    {
                        stats.Containers++;
                    }

                    if (entity is Door) stats.Doors++;
                    if (entity is IOEntity) stats.IoEntities++;
                    if (entity.Is(out BaseCombatEntity c) && c.pickup.enabled) stats.Pickupable++;
                    if (entity is AutoTurret) stats.AutoTurrets++;
                    if (entity is GunTrap) stats.ShotgunTraps++;
                    if (entity is FlameTurret) stats.FlameTurrets++;
                    if (entity is SamSite) stats.SamSites++;
                    if (entity is TeslaCoil) stats.TeslaCoils++;
                    if (entity is FogMachine) stats.FogMachines++;
                }

                foreach (HumanoidNPC npc in raid.npcs)
                {
                    if (!npc.IsKilled())
                    {
                        stats.Npcs++;
                    }
                }

                foreach (StorageContainer container in raid._containers)
                {
                    if (!IsContainerKilled(container))
                    {
                        stats.LootContainers++;
                    }
                }

                return stats;
            }

            private static InfoMetric Metric(string label, int value, string color, int maximum)
            {
                return new(label, value.ToString("N0"), color, Ratio(value, maximum));
            }

            private static float Ratio(float value, float maximum)
            {
                return maximum <= 0f ? 0f : Mathf.Clamp01(value / maximum);
            }

            private static int Max(params int[] values)
            {
                int maximum = 0;

                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] > maximum)
                    {
                        maximum = values[i];
                    }
                }

                return maximum;
            }

            private static string GetOwnerText(RaidableBase raid)
            {
                if (!raid.ownerId.IsSteamId())
                {
                    return "None";
                }

                string name = !string.IsNullOrWhiteSpace(raid.ownerName) ? raid.ownerName : raid.GetOwner()?.displayName;

                return string.IsNullOrWhiteSpace(name) ? raid.ownerId.ToString() : $"{rf(name)} ({raid.ownerId})";
            }

            private static string GetRaidState(RaidableBase raid)
            {
                if (raid.IsDespawning) return "Despawning";
                if (raid.IsLoading) return "Loading";
                if (raid.IsCompleted) return "Completed";
                return raid.IsOpened ? "Active" : "Closed";
            }

            private void ShowNpc(BasePlayer player, HumanoidBrain brain)
            {
                HumanoidNPC npc = brain.npc;
                RaidableBase raid = brain.raid;
                BaseNavigator navigator = brain.Navigator;
                int paths = DrawNpcNavigation(player, brain, out int positions);
                string state = brain.CurrentState?.StateType.ToString() ?? "None";
                string navigationType = navigator?.CurrentNavigationType.ToString() ?? "Unavailable";
                float speed = navigator?.GetTargetSpeed() ?? 0f;
                float destinationDistance = navigator == null || navigator.CurrentNavigationType == BaseNavigator.NavigationType.None ? 0f : navigator.Destination.Distance(brain.ServerPosition);
                float targetDistance = brain.AttackTarget == null ? 0f : brain.AttackPosition.Distance(brain.ServerPosition);
                string target = brain.AttackTarget == null ? "None" : rf(brain.AttackTarget.displayName);
                string weapon = string.IsNullOrEmpty(brain.AttackName) ? "None" : brain.AttackName;
                string respawns = brain.respawnsRemaining < 0 ? "Unlimited" : brain.respawnsRemaining.ToString("N0");
                string location = brain.spawnedInside ? "Inside" : "Outside";

                if (brain.isStationary)
                {
                    location += " / stationary";
                }
                else if (brain.isSleeper)
                {
                    location += " / sleeper";
                }

                int routeTotal = brain.BaseRoute?.Count ?? 0;
                int routeIndex = routeTotal == 0 ? 0 : Mathf.Clamp(brain.baseRouteIndex + 1, 1, routeTotal);
                int roamPositions = brain.RandomRoamPositions?.Count ?? 0;
                int activeCorners = navigator?.path?.corners?.Count ?? 0;
                string pathStatus = navigator?.path == null ? "Unavailable" : navigator.path.status.ToString();
                string condition = npc.IsWounded() ? "Wounded" : brain.isKilled ? "Disabled" : brain.movementStarted ? "Running" : "Starting";
                string lastAttack = brain.lastAttackTime <= 0f ? "Never" : $"{brain.SecondsSinceLastAttack:0.0}s ago";

                var cards = new List<SummaryCard>
                {
                    new($"{npc.health:0.#} / {npc.MaxHealth():0.#}", "health", condition),
                    new(state, "AI state"),
                    new(navigationType, "navigation", $"{speed:0.0} m/s"),
                    new($"{paths:N0} / {positions:N0}", "paths / positions"),
                    new(respawns, "respawns remaining")
                };

                var sections = new List<InfoSection>
                {
                    new("Movement", location, new()
                    {
                        new("Health", $"{npc.health:0.#} / {npc.MaxHealth():0.#}", "#56C596", Ratio(npc.health, npc.MaxHealth())),
                        new("Condition", condition, "#77A8BA"),
                        new("Speed", $"{speed:0.0} m/s", "#5865F2", Ratio(speed, 8f)),
                        new("Destination", destinationDistance <= 0f ? "None" : $"{destinationDistance:0.0}m", "#E0B04B", Ratio(destinationDistance, raid.ProtectionRadius)),
                        new("Stationary", brain.isStationary ? "Yes" : "No", brain.isStationary ? "#E56B6F" : "#56C596")
                    }),
                    new("Navigation", pathStatus, new()
                    {
                        new("Navigation type", navigationType, "#5865F2"),
                        new("Base route", routeTotal == 0 ? "None" : $"{routeIndex:N0} / {routeTotal:N0}", "#E0B04B", Ratio(routeIndex, routeTotal)),
                        new("Roam positions", roamPositions.ToString("N0"), "#56C596", Ratio(positions, Math.Max(1, roamPositions))),
                        new("Reachable paths", paths.ToString("N0"), "#77A8BA", Ratio(paths, Math.Max(1, roamPositions))),
                        new("Active corners", activeCorners.ToString("N0"), "#72A9A9", Ratio(activeCorners, Math.Max(1, positions)))
                    }),
                    new("Combat", brain.attackType.ToString(), new()
                    {
                        new("Target", target, "#E56B6F"),
                        new("Target distance", targetDistance <= 0f ? "None" : $"{targetDistance:0.0}m", "#E18D50", Ratio(targetDistance, Math.Max(1f, brain.attackRange))),
                        new("Weapon", weapon, "#E0B04B"),
                        new("Attack range", $"{brain.attackRange:0.#}m", "#7896C8", Ratio(brain.attackRange, 400f)),
                        new("Last attack", lastAttack, "#9DA8B3")
                    }),
                    new("Raid Context", $"{raid.Options.Mode} / {(raid.AllowPVP ? "PVP" : "PVE")}", new()
                    {
                        new("Base", raid.BaseName, "#5865F2"),
                        new("Raid state", GetRaidState(raid), "#56C596"),
                        new("NPC type", brain.isMurderer ? "Murderer" : "Scientist", "#C184E8"),
                        new("NPC ID", brain.userid.ToString(), "#77A8BA"),
                        new("Route refresh", brain.baseRouteDirty ? "Pending" : "Current", brain.baseRouteDirty ? "#E0B04B" : "#56C596")
                    })
                };

                DrawUi(player, "NPC Navigation", $"{rf(npc.displayName)}  •  {raid.BaseName}  •  {(brain.isMurderer ? "Murderer" : "Scientist")}", $"NPC {brain.userid}", cards, sections,
                    "GREEN available paths  •  CYAN active path  •  YELLOW next destination  •  RED attack target");
            }

            private int DrawNpcNavigation(BasePlayer player, HumanoidBrain brain, out int positions)
            {
                positions = 0;
                int paths = 0;
                float displaySeconds = DisplaySeconds;
                Vector3 npcPosition = brain.ServerPosition + Vector3.up * DRAW_HEIGHT;

                DrawSphere(player, displaySeconds, Color.white, npcPosition, 0.25f);
                DrawText(player, displaySeconds, Color.white, npcPosition + Vector3.up * 0.35f, $"{rf(brain.displayName)} ({brain.userid})");

                if (brain.UsesBaseNavigation && brain.BaseRoute != null)
                {
                    Vector3 previous = default;
                    bool hasPrevious = false;

                    for (int i = 0; i < brain.BaseRoute.Count; i++)
                    {
                        if (!brain.raid.TryGetBaseRoutePosition(brain.BaseRoute[i], out Vector3 position))
                        {
                            continue;
                        }

                        position += Vector3.up * DRAW_HEIGHT;
                        positions++;
                        DrawSphere(player, displaySeconds, i == brain.baseRouteIndex ? Color.yellow : Color.green, position, i == brain.baseRouteIndex ? 0.22f : 0.12f);

                        if (hasPrevious)
                        {
                            DrawLine(player, displaySeconds, Color.green, previous, position);
                        }

                        previous = position;
                        hasPrevious = true;
                    }

                    if (positions > 1)
                    {
                        paths++;
                    }
                }
                else if (!brain.isStationary && brain.RandomRoamPositions != null && brain.Navigator?.Agent != null)
                {
                    var path = new Rust.Ai.Gen2.RustNavMeshPath();

                    for (int i = 0; i < brain.RandomRoamPositions.Count; i++)
                    {
                        Vector3 destination = brain.RandomRoamPositions[i];
                        var pos = brain.ServerPosition;
                        var dest = destination;
                        if (!brain.Navigator.Agent.CalculatePath(new(pos.x, pos.y, pos.z), new(dest.x, dest.y, dest.z), path) || path.status != NavMeshPathStatus.PathComplete || path.corners.Count < 2)
                        {
                            continue;
                        }

                        DrawPath(player, path.corners, Color.green, displaySeconds);
                        DrawSphere(player, displaySeconds, Color.green, destination + Vector3.up * DRAW_HEIGHT, 0.12f);
                        positions++;
                        paths++;
                    }
                }

                BaseNavigator navigator = brain.Navigator;

                if (navigator?.path?.corners != null && navigator.path.corners.Count > 1)
                {
                    DrawPath(player, navigator.path.corners, Color.cyan, displaySeconds);
                }

                if (navigator != null && navigator.CurrentNavigationType != BaseNavigator.NavigationType.None)
                {
                    Vector3 destination = navigator.Destination + Vector3.up * DRAW_HEIGHT;
                    DrawSphere(player, displaySeconds, Color.yellow, destination, 0.25f);
                    DrawText(player, displaySeconds, Color.yellow, destination + Vector3.up * 0.35f, "DESTINATION");
                }

                if (brain.AttackTarget != null)
                {
                    Vector3 target = brain.AttackPosition + Vector3.up * DRAW_HEIGHT;
                    DrawLine(player, displaySeconds, Color.red, npcPosition, target);
                    DrawSphere(player, displaySeconds, Color.red, target, 0.25f);
                    DrawText(player, displaySeconds, Color.red, target + Vector3.up * 0.35f, "TARGET");
                }

                return paths;
            }

            private static Vector3 ToVec3(NavVector3 v) => new(v.x, v.y, v.z);

            private static void DrawPath(BasePlayer player, List<NavVector3> corners, Color color, float displaySeconds) // September Rust update, currently on staging branch
            {
                Vector3 previous = ToVec3(corners[0]) + Vector3.up * DRAW_HEIGHT;

                for (int i = 1; i < corners.Count; i++)
                {
                    Vector3 current = ToVec3(corners[i]) + Vector3.up * DRAW_HEIGHT;
                    DrawLine(player, displaySeconds, color, previous, current);
                    previous = current;
                }
            }

            private static void DrawPath(BasePlayer player, List<Vector3> corners, Color color, float displaySeconds) // August Rust update, currently on public branch
            {
                Vector3 previous = corners[0] + Vector3.up * DRAW_HEIGHT;

                for (int i = 1; i < corners.Count; i++)
                {
                    Vector3 current = corners[i] + Vector3.up * DRAW_HEIGHT;
                    DrawLine(player, displaySeconds, color, previous, current);
                    previous = current;
                }
            }

            private void DrawUi(BasePlayer player, string title, string subtitle, string total, List<SummaryCard> cards, List<InfoSection> sections, string footer)
            {
                const float margin = 18f;
                const float headerHeight = 72f;
                const float cardsHeight = 116f;
                const float footerHeight = 38f;
                const float sectionGap = 14f;
                float width = Mathf.Clamp(Settings.Width, 720f, 1800f);
                float height = Mathf.Clamp(Settings.Height, 420f, 1000f);
                UiHandler.UiPalette palette = Instance.UI.GetPalette();
                string backgroundColor = palette.Background;
                string panelColor = palette.Panel;
                string cellColor = palette.Cell;
                string dividerColor = palette.Divider;
                string barBackgroundColor = palette.ProgressBackground;
                string titleColor = palette.Accent;
                string textColor = palette.Text;
                string mutedColor = palette.Muted;
                var container = new CuiElementContainer();

                UiHandler.AddCuiPanel(container, backgroundColor, "0.5 0.5", "0.5 0.5", $"{-width * 0.5f:0.#} {-height * 0.5f:0.#}", $"{width * 0.5f:0.#} {height * 0.5f:0.#}", "Overlay", TARGET_INFO_UI, true);
                UiHandler.AddCuiElement(container, title, 22, TextAnchor.MiddleLeft, titleColor, "0 1", "0.65 1", $"{margin} {-headerHeight + 22f}", $"0 -8", TARGET_INFO_UI, $"{TARGET_INFO_UI}_Title");
                UiHandler.AddCuiElement(container, subtitle, 13, TextAnchor.MiddleLeft, mutedColor, "0 1", "0.75 1", $"{margin} {-headerHeight}", $"0 {-headerHeight + 24f}", TARGET_INFO_UI, $"{TARGET_INFO_UI}_Subtitle", false);
                UiHandler.AddCuiElement(container, total, 13, TextAnchor.MiddleRight, mutedColor, "0.68 1", "1 1", $"0 {-headerHeight + 22f}", "-66 -8", TARGET_INFO_UI, $"{TARGET_INFO_UI}_Total", false);
                UiHandler.AddCuiButton(container, panelColor, "rb.info clear", "×", titleColor, 20, TextAnchor.MiddleCenter, "1 1", "1 1", "-52 -48", "-18 -14", TARGET_INFO_UI, $"{TARGET_INFO_UI}_Close");

                float cardsTop = -headerHeight;
                float cardsBottom = cardsTop - cardsHeight;
                UiHandler.AddCuiPanel(container, cellColor, "0 1", "1 1", $"{margin} {cardsBottom}", $"{-margin} {cardsTop}", TARGET_INFO_UI, $"{TARGET_INFO_UI}_Cards");

                float cardWidth = (width - margin * 2f) / cards.Count;

                for (int i = 0; i < cards.Count; i++)
                {
                    float left = i * cardWidth;
                    float right = left + cardWidth;
                    SummaryCard card = cards[i];
                    string parent = $"{TARGET_INFO_UI}_Card{i}";

                    UiHandler.AddCuiPanel(container, cellColor, "0 0", "0 1", $"{left:0.#} 0", $"{right:0.#} 0", $"{TARGET_INFO_UI}_Cards", parent);

                    if (i > 0)
                    {
                        UiHandler.AddCuiPanel(container, dividerColor, "0 0", "0 1", "0 0", "1 0", parent, $"{parent}_Divider");
                    }

                    UiHandler.AddCuiElement(container, rf(card.Value), 22, TextAnchor.MiddleLeft, textColor, "0 1", "1 1", $"14 -46", "-12 -10", parent, $"{parent}_Value");
                    UiHandler.AddCuiElement(container, card.Label, 13, TextAnchor.MiddleLeft, mutedColor, "0 1", "1 1", "14 -70", "-12 -44", parent, $"{parent}_Label", false);
                    UiHandler.AddCuiPanel(container, dividerColor, "0 1", "1 1", "14 -80", "-14 -78", parent, $"{parent}_Rule");

                    if (!string.IsNullOrEmpty(card.Note))
                    {
                        UiHandler.AddCuiElement(container, rf(card.Note), 11, TextAnchor.MiddleLeft, mutedColor, "0 0", "1 0", "14 8", "-12 34", parent, $"{parent}_Note", false);
                    }
                }

                float sectionsTop = cardsBottom - sectionGap;
                float sectionsBottom = -height + footerHeight + margin;
                float availableWidth = width - margin * 2f;
                float sectionWidth = availableWidth / sections.Count;

                for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
                {
                    InfoSection section = sections[sectionIndex];
                    float left = margin + sectionWidth * sectionIndex;
                    float right = left + sectionWidth;
                    string sectionName = $"{TARGET_INFO_UI}_Section{sectionIndex}";

                    UiHandler.AddCuiPanel(container, panelColor, "0 1", "0 1", $"{left:0.#} {sectionsBottom}", $"{right:0.#} {sectionsTop}", TARGET_INFO_UI, sectionName);

                    if (sectionIndex > 0)
                    {
                        UiHandler.AddCuiPanel(container, dividerColor, "0 0", "0 1", "0 0", "1 0", sectionName, $"{sectionName}_Divider");
                    }

                    UiHandler.AddCuiElement(container, section.Title, 16, TextAnchor.MiddleLeft, textColor, "0 1", "0.58 1", "14 -34", "0 -4", sectionName, $"{sectionName}_Title");
                    UiHandler.AddCuiElement(container, rf(section.Summary), 11, TextAnchor.MiddleRight, mutedColor, "0.54 1", "1 1", "0 -34", "-14 -4", sectionName, $"{sectionName}_Summary", false);

                    float metricTop = -42f;
                    float metricHeight = 43f;

                    for (int metricIndex = 0; metricIndex < section.Metrics.Count; metricIndex++)
                    {
                        InfoMetric metric = section.Metrics[metricIndex];
                        float metricBottom = metricTop - metricHeight;
                        string metricColor = UiHandler.ConvertHexToRGBA(metric.Color, 1f);
                        string metricName = $"{sectionName}_Metric{metricIndex}";

                        UiHandler.AddCuiPanel(container, metricColor, "0 1", "0 1", $"14 {metricTop - 21f:0.#}", $"20 {metricTop - 15f:0.#}", sectionName, $"{metricName}_Dot");
                        UiHandler.AddCuiElement(container, metric.Label, 13, TextAnchor.MiddleLeft, mutedColor, "0 1", "0.72 1", $"27 {metricTop - 30f:0.#}", $"0 {metricTop - 5f:0.#}", sectionName, $"{metricName}_Label", false);
                        UiHandler.AddCuiElement(container, rf(metric.Value), 13, TextAnchor.MiddleRight, textColor, "0.58 1", "1 1", $"0 {metricTop - 30f:0.#}", $"-14 {metricTop - 5f:0.#}", sectionName, $"{metricName}_Value");
                        UiHandler.AddCuiPanel(container, barBackgroundColor, "0 1", "1 1", $"14 {metricBottom + 6f:0.#}", $"-14 {metricBottom + 9f:0.#}", sectionName, $"{metricName}_Bar");

                        if (metric.Ratio >= 0f)
                        {
                            UiHandler.AddCuiPanel(container, titleColor, "0 0", $"{Mathf.Clamp01(metric.Ratio):0.###} 1", "0 0", "0 0", $"{metricName}_Bar", $"{metricName}_Fill");
                        }

                        metricTop = metricBottom;
                    }
                }

                UiHandler.AddCuiPanel(container, dividerColor, "0 0", "1 0", $"{margin} {footerHeight + 8f}", $"{-margin} {footerHeight + 10f}", TARGET_INFO_UI, $"{TARGET_INFO_UI}_FooterRule");
                UiHandler.AddCuiElement(container, footer, 12, TextAnchor.MiddleLeft, mutedColor, "0 0", "1 0", $"{margin} 6", $"{-margin} {footerHeight + 5f}", TARGET_INFO_UI, $"{TARGET_INFO_UI}_Footer", false);

                if (CuiHelper.AddUi(player, container))
                {
                    ScheduleHide(player);
                }
            }

            private void ScheduleHide(BasePlayer player)
            {
                ulong userid = player.userID;
                Timer current = null;
                float displaySeconds = DisplaySeconds;

                current = Instance.timer.Once(displaySeconds, () =>
                {
                    if (!_timers.TryGetValue(userid, out Timer timer) || !ReferenceEquals(timer, current))
                    {
                        return;
                    }

                    _timers.Remove(userid);

                    if (player != null && player.IsConnected)
                    {
                        CuiHelper.DestroyUi(player, TARGET_INFO_UI);
                    }
                });

                _timers[userid] = current;
            }
        }

        private class PasteEngine : IDisposable
        {
            private const float BATCH_DELAY_SECONDS = 0.0375f;
            private const float PREPARE_PROGRESS_END = 0.08f;
            private const float SPAWN_PROGRESS_END = 0.55f;
            private const float PASTE_PROGRESS_END = 0.72f;
            private const float ENTITY_SETUP_PROGRESS_END = 0.98f;
            private const float LegacyElevatorLiftMaxHorizontalDistanceSqr = 1f;
            private const float LegacyElevatorLiftVerticalTolerance = 0.5f;
            private const float LegacyElevatorShaftMatchMaxHorizontalDistanceSqr = 1f;
            private const float LegacyElevatorShaftMatchForwardDotMin = 0.98f;

            private RaidableBases Instance;
            // ReSharper disable once Unity.IncorrectMonoBehaviourInstantiation
            private readonly Item _emptyItem = new() { info = new() };
            private readonly List<RaidPaste> _pastes = new();
            private readonly uint _floorFramePrefabId = StringPool.Get("assets/prefabs/building core/floor.frame/floor.frame.prefab");
            private readonly uint _floorTriangleFramePrefabId = StringPool.Get("assets/prefabs/building core/floor.triangle.frame/floor.triangle.frame.prefab");
            private readonly List<BaseEntity.Slot> _checkSlots = new() { BaseEntity.Slot.Lock, BaseEntity.Slot.UpperModifier, BaseEntity.Slot.MiddleModifier, BaseEntity.Slot.LowerModifier };
            private readonly HashSet<Type> _itemModAssociatedEntityTypes = new() { typeof(PaintedItemStorageEntity), typeof(PhotoEntity), typeof(SignContent), typeof(HeadEntity), typeof(PagerEntity), typeof(MobileInventoryEntity), typeof(Cassette) };
            private readonly List<string> _blockedPrefabs = new() { "saddletest", "collectableegg" };
            private readonly Dictionary<string, BaseOven.MinMax> _signSizes = new()
            {
                ["photoframe.landscape"] = new(320, 240),
                ["photoframe.large"] = new(320, 240),
                ["photoframe.portrait"] = new(320, 240),
                ["sign.pictureframe.landscape"] = new(256, 192),
                ["sign.pictureframe.tall"] = new(128, 512),
                ["sign.pictureframe.portrait"] = new(205, 256),
                ["sign.pictureframe.xxl"] = new(1024, 512),
                ["sign.pictureframe.xl"] = new(512, 512),
                ["sign.small.wood"] = new(256, 128),
                ["sign.medium.wood"] = new(512, 256),
                ["sign.large.wood"] = new(512, 256),
                ["sign.huge.wood"] = new(1024, 256),
                ["sign.hanging.banner.large"] = new(256, 1024),
                ["sign.pole.banner.large"] = new(256, 1024),
                ["sign.post.single"] = new(256, 128),
                ["sign.post.double"] = new(512, 512),
                ["sign.post.town"] = new(512, 256),
                ["sign.post.town.roof"] = new(512, 256),
                ["sign.hanging"] = new(256, 512),
                ["sign.hanging.ornate"] = new(512, 256),
                ["sign.neon.xl.animated"] = new(256, 256),
                ["sign.neon.xl"] = new(256, 256),
                ["sign.neon.125x215.animated"] = new(256, 128),
                ["sign.neon.125x215"] = new(256, 128),
                ["sign.neon.125x125"] = new(128, 128)
            };

            private class RaidPaste
            {
                internal HashSet<ulong> ProgressViewers = new();
                internal List<Dictionary<string, object>> Entities = new();
                internal List<BaseEntity> PastedEntities = new();
                internal Dictionary<ulong, Dictionary<string, object>> EntityLookup = new();
                internal Dictionary<PlayerBoat, PlayerBoatData> PlayerBoats = new();
                internal Dictionary<ulong, Item> ItemsWithSubEntity = new();
                internal List<Action> FinalProcessingActions = new();
                internal List<StabilityEntity> StabilityEntities = new();
                internal List<IndustrialStorageAdaptor> IndustrialStorageAdaptors = new();
                internal List<ConnectedSpeaker> ConnectedSpeakers = new();
                internal RaidableBase Raid;
                internal RandomBase Request;
                internal string Filename;
                internal IPlayer Player;
                internal Action CallbackFinished;
                internal Action<BaseEntity> CallbackSpawned;
                internal List<IOEntity> LegacyIoPositionChecks;
                internal Dictionary<Elevator, LegacyElevatorRestoreState> LegacyElevatorRestoreStates;
                internal List<Dictionary<string, object>> DelayedCupboardsData;
                internal Quaternion QuaternionRotation;
                internal VersionNumber Version;
                internal Vector3 StartPos;
                internal float HeightAdj;
                internal bool ReplayingDelayedCupboards, IsItemReplace, Stability, EnableSaving;
                internal uint BuildingId;
                internal int CupboardCount;
                internal double NextProgressUiUpdate;
                internal int LastProgressPercent = -1;
                internal float LastProgress;
                internal string LastProgressStage = string.Empty;

                internal BasePlayer BasePlayer => null;
                internal bool Auth => false;
                internal bool Ownership => false;
                internal bool IsAlreadyPlaced => true;

                internal RaidPaste(RaidableBase raid, RandomBase request, IPlayer player, Dictionary<string, object> protocol, Action callbackFinished, Action<BaseEntity> callbackSpawned)
                {
                    Raid = raid;
                    Request = request;
                    Filename = request.BaseName;
                    Player = player;
                    StartPos = request.Position;
                    QuaternionRotation = Quaternion.identity;
                    Stability = request.stability;
                    EnableSaving = request.Save;
                    CallbackFinished = callbackFinished;
                    CallbackSpawned = callbackSpawned;
                    IsItemReplace = protocol == null || !protocol.ContainsKey("items");

                    if (protocol != null && protocol.TryGetValue("version", out object obj) && obj is Dictionary<string, object> version)
                    {
                        Version = new(
                            ToInt32OrDefault(version.TryGetValue("Major", out obj) ? obj : null),
                            ToInt32OrDefault(version.TryGetValue("Minor", out obj) ? obj : null),
                            ToInt32OrDefault(version.TryGetValue("Patch", out obj) ? obj : null));
                    }
                }
            }

            private class PlayerBoatData
            {
                internal List<BoatBuildingBlock> Blocks = new();
                internal List<BaseEntity> Deployables = new();
            }

            private class LegacyElevatorRestoreState
            {
                internal int Floor;
                internal bool IsTop;
            }

            private struct OriginalTransforms
            {
                internal Vector3 Position;
                internal Quaternion Rotation;
                internal Vector3 LocalScale;
                internal Vector3 Difference;

                internal OriginalTransforms(Transform transform, Vector3 localDiff)
                {
                    Position = transform.position;
                    Rotation = transform.rotation;
                    LocalScale = transform.localScale;
                    Difference = transform.InverseTransformDirection(localDiff);
                }
            }

            internal PasteEngine(RaidableBases instance)
            {
                Instance = instance;
            }

            public void Dispose()
            {
                DestroyProgressUi();
                _pastes.Clear();
                _replacementItemIds?.Clear();
                _replacementItemIds = null;
            }

            internal IEnumerator Paste(RaidableBase raid, RandomBase request, List<object> sourceEntities, Dictionary<string, object> protocol, Action callbackFinished, Action<BaseEntity> callbackSpawned)
            {
                var paste = new RaidPaste(raid, request, Instance._consolePlayer, protocol, callbackFinished, callbackSpawned);
                _pastes.Add(paste);

                try
                {
                    UpdateProgressUi(paste, 0f, "Preparing entities");
                    yield return PrepareEntities(paste, sourceEntities);

                    if (Instance.IsUnloading || raid.IsDespawning)
                    {
                        yield break;
                    }

                    yield return SpawnEntities(paste);
                    if (Instance.IsUnloading || raid.IsDespawning)
                    {
                        yield break;
                    }

                    yield return FinalizePaste(paste);
                    if (Instance.IsUnloading || raid.IsDespawning)
                    {
                        yield break;
                    }

                    paste.CallbackFinished?.Invoke();
                    HarmonyModInterface.CallHook("OnPasteFinished", paste.PastedEntities, paste.Filename, paste.Player, paste.StartPos, Version, en);

                    while (!ShouldStop(paste) && raid.IsLoading)
                    {
                        yield return null;
                    }

                    if (!ShouldStop(paste))
                    {
                        UpdateProgressUi(paste, 1f, "Complete", true);
                        yield return null;
                    }
                }
                finally
                {
                    FinishProgress(paste);
                }
            }

            private IEnumerator PrepareEntities(RaidPaste paste, List<object> sourceEntities)
            {
                int limit = Mathf.Clamp(paste.Raid.Options.Setup.SpawnLimit, 1, 500);
                FrameDeadline deadline = new(1, 5);
                int count = sourceEntities?.Count ?? 0;

                for (int index = 0; index < count; index++)
                {
                    if (sourceEntities[index] is not Dictionary<string, object> entity)
                    {
                        if (!HandlePasteFailure(paste, null, "entity preparation", $"entity entry {index} is not an object"))
                        {
                            yield break;
                        }
                        continue;
                    }

                    if (TryPrepareEntity(entity, paste.StartPos, paste.Request.inventories, out string error))
                    {
                        paste.Entities.Add(entity);
                    }
                    else if (error != null && Instance.DebugMode)
                    {
                        Puts(error);
                    }

                    if (deadline.Expired)
                    {
                        UpdateProgressUi(paste, count == 0 ? PREPARE_PROGRESS_END : PREPARE_PROGRESS_END * (index + 1f) / count, "Preparing entities");
                        if (limit < 500) yield return null;
                        deadline.Reset();
                    }
                }

                if (!ShouldStop(paste))
                {
                    ReorderPasteEntities(paste);
                    UpdateProgressUi(paste, PREPARE_PROGRESS_END, "Preparing entities", true);
                }
            }

            private static void ReorderPasteEntities(RaidPaste paste)
            {
                if (paste.Entities == null || paste.Entities.Count < 2)
                {
                    return;
                }

                List<Dictionary<string, object>> building = new();
                List<Dictionary<string, object>> rest = new();

                for (int i = 0; i < paste.Entities.Count; i++)
                {
                    Dictionary<string, object> entity = paste.Entities[i];
                    if (IsBuildingCorePrefab(entity))
                    {
                        building.Add(entity);
                    }
                    else
                    {
                        rest.Add(entity);
                    }
                }

                if (building.Count == 0)
                {
                    return;
                }

                paste.Entities.Clear();
                paste.Entities.AddRange(building);
                paste.Entities.AddRange(rest);
            }

            private static bool IsBuildingCorePrefab(Dictionary<string, object> entity)
            {
                return entity != null
                    && entity.TryGetValue("prefabname", out object obj)
                    && obj is string prefab
                    && prefab.IndexOf("/building core/", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool TryGetPastedEntity<T>(Dictionary<string, object> data, out T entity) where T : BaseEntity
            {
                if (data == null || !data.TryGetValue("entity", out object obj) || !(obj is T pastedEntity) || !pastedEntity.IsValid() || pastedEntity.IsDestroyed)
                {
                    entity = null;
                    return false;
                }
                entity = pastedEntity;
                return true;
            }

            private static bool TryGetPastedEntity<T>(RaidPaste paste, ulong oldId, out T entity) where T : BaseEntity
            {
                entity = null;
                return oldId != 0 && paste.EntityLookup.TryGetValue(oldId, out var data) && TryGetPastedEntity(data, out entity);
            }

            private static bool TryGetPastedNetworkId(RaidPaste paste, ulong oldId, out NetworkableId networkId)
            {
                if (oldId == 0 || !paste.EntityLookup.TryGetValue(oldId, out var data) || !data.TryGetValue("newId", out object obj) || !ulong.TryParse(obj?.ToString(), out ulong value))
                {
                    networkId = default;
                    return false;
                }
                networkId = new(value);
                return networkId.IsValid;
            }

            private static bool TryPrepareEntity(Dictionary<string, object> entity, Vector3 startPosition, bool includeInventories, out string error)
            {
                error = null;

                if (entity == null)
                {
                    error = "entity data is null";
                    return false;
                }

                if (!entity.TryGetValue("pos", out object obj) || obj is not Dictionary<string, object> position || !TryReadVector3(position, out Vector3 worldPosition))
                {
                    error = "position data is missing or invalid";
                    return false;
                }

                if (!entity.TryGetValue("rot", out obj) || obj is not Dictionary<string, object> rotation || !TryReadVector3(rotation, out Vector3 worldRotation))
                {
                    error = "rotation data is missing or invalid";
                    return false;
                }

                entity["position"] = worldPosition + startPosition;
                entity["rotation"] = Quaternion.Euler(worldRotation * Mathf.Rad2Deg);

                if (!includeInventories && entity.ContainsKey("items"))
                {
                    entity["items"] = new List<object>();
                }

                TryPrepareChildrenData(entity, out error);
                return true;
            }

            private IEnumerator SpawnEntities(RaidPaste paste)
            {
                int limit = Mathf.Clamp(paste.Raid.Options.Setup.SpawnLimit, 1, 500);
                int batchCount = 0;
                int total = paste.Entities.Count;
                FrameDeadline deadline = new(1, 5);
                WaitForSeconds instruction = CoroutineEx.waitForSeconds(BATCH_DELAY_SECONDS);

                UpdateProgressUi(paste, PREPARE_PROGRESS_END, $"Spawning entities (0/{total:N0})", true);

                for (int index = 0; index < total; index++)
                {
                    Dictionary<string, object> entityData = paste.Entities[index];

                    if (!TryPasteEntity(entityData, paste))
                    {
                        yield break;
                    }

                    bool delay = ++batchCount >= limit;
                    if (delay || limit < 500 && deadline.Expired)
                    {
                        batchCount = 0;
                        float progress = Mathf.Lerp(PREPARE_PROGRESS_END, SPAWN_PROGRESS_END, total == 0 ? 1f : (index + 1f) / total);
                        UpdateProgressUi(paste, progress, $"Spawning entities ({index + 1:N0}/{total:N0})");
                        yield return delay ? instruction : null;
                        deadline.Reset();
                    }

                    if (Instance.IsUnloading || paste.Raid == null || paste.Raid.IsDespawning)
                    {
                        yield break;
                    }
                }

                if (!ShouldStop(paste))
                {
                    UpdateProgressUi(paste, SPAWN_PROGRESS_END, $"Spawning entities ({total:N0}/{total:N0})", true);
                }
            }

            private bool TryGetActivePaste(RaidableBase raid, out RaidPaste paste)
            {
                for (int index = _pastes.Count - 1; index >= 0; index--)
                {
                    paste = _pastes[index];
                    if (paste.Raid == raid)
                    {
                        return true;
                    }
                }

                paste = null;
                return false;
            }

            private bool TryGetProgress(ulong userid, out RaidPaste paste)
            {
                for (int index = _pastes.Count - 1; index >= 0; index--)
                {
                    paste = _pastes[index];
                    if (paste.ProgressViewers.Contains(userid))
                    {
                        return true;
                    }
                }

                paste = null;
                return false;
            }

            private bool HandlePasteFailure(RaidPaste paste, Dictionary<string, object> entityData, string stage, Exception ex)
            {
                return HandlePasteFailure(paste, entityData, stage, ex.ToString());
            }

            private bool HandlePasteFailure(RaidPaste paste, Dictionary<string, object> entityData, string stage, string error)
            {
                string prefab = entityData != null && entityData.TryGetValue("prefabname", out object obj) && obj is string str ? str : "unknown";
                Puts("{0}: failed during {1} for '{2}': {3}", paste.Filename, stage, prefab, error);

                if (paste.Raid.Options.Setup.PasteErrorHandling != (int)PasteErrorMode.Undo)
                {
                    return true;
                }

                paste.Request.payments.Refund();

                if (paste.Raid == null)
                    return false;

                paste.Raid.IsLoading = false;
                paste.Raid.Despawn();
                return false;
            }

            private IEnumerator FinalizePaste(RaidPaste paste)
            {
                if (paste.Version < new VersionNumber(4, 2, 0))
                {
                    paste.LegacyIoPositionChecks = Pool.Get<List<IOEntity>>();
                }

                yield return StepRestoreIo(paste);
                if (ShouldStop(paste)) yield break;

                if (paste.LegacyIoPositionChecks != null)
                {
                    try
                    {
                        if (!TryRunStage(paste, "legacy IO position correction", () => AdjustIOEntityPositions(paste)))
                        {
                            yield break;
                        }
                    }
                    finally
                    {
                        Pool.FreeUnmanaged(ref paste.LegacyIoPositionChecks);
                    }
                }

                yield return StepLinkItemSubEntities(paste);
                if (ShouldStop(paste)) yield break;

                if (paste.Version <= new VersionNumber(4, 2, 7))
                {
                    if (!TryRunStage(paste, "legacy floor-frame correction", () => SnapLegacyFloorFrameEntities(paste)))
                    {
                        yield break;
                    }
                }

                yield return StepInitializeStability(paste);
                if (ShouldStop(paste)) yield break;
                yield return StepRefreshIndustrialAdapters(paste);
                if (ShouldStop(paste)) yield break;
                yield return StepRunFinalActions(paste);
                if (ShouldStop(paste)) yield break;

                UpdateProgressUi(paste, 0.71f, "Finalizing building data", true);
                if (!TryRunStage(paste, "building split check", () => TrySplitPastedBuilding(paste)))
                {
                    yield break;
                }

                yield return StepPasteDelayedCupboards(paste);
                if (!ShouldStop(paste))
                {
                    UpdateProgressUi(paste, PASTE_PROGRESS_END, "Starting raid setup", true);
                }
            }

            private bool ShouldStop(RaidPaste paste)
            {
                return Instance.IsUnloading || paste.Raid.IsDespawning;
            }

            private bool TryRunStage(RaidPaste paste, string stage, Action action)
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception ex)
                {
                    return HandlePasteFailure(paste, null, stage, ex);
                }
            }

            private IEnumerator StepRestoreIo(RaidPaste paste)
            {
                using var ioData = paste.EntityLookup.Values.ToPooledList();
                yield return StepCollection(paste, ioData, SPAWN_PROGRESS_END, 0.60f, "Restoring IO connections", value => RestoreIoEntity(value, paste));
            }

            private IEnumerator StepLinkItemSubEntities(RaidPaste paste)
            {
                using var items = paste.ItemsWithSubEntity.ToPooledList();
                yield return StepCollection(paste, items, 0.60f, 0.63f, "Linking item entities", pair => SetItemSubEntity(paste, pair.Value, pair.Key));
            }

            private IEnumerator StepInitializeStability(RaidPaste paste)
            {
                yield return StepCollection(paste, paste.StabilityEntities, 0.63f, 0.66f, "Initializing stability",
                    entity =>
                    {
                        if (entity == null || entity.IsDestroyed)
                        {
                            return;
                        }

                        entity.grounded = false;
                        entity.InitializeSupports();
                        entity.UpdateStability();
                    });
            }

            private IEnumerator StepRefreshIndustrialAdapters(RaidPaste paste)
            {
                yield return StepCollection(paste, paste.IndustrialStorageAdaptors, 0.66f, 0.69f, "Refreshing industrial networks",
                    adapter =>
                    {
                        if (adapter == null || adapter.IsDestroyed)
                        {
                            return;
                        }

                        if (!adapter.HasParent())
                        {
                            using var entities = FindEntitiesOfType<BaseEntity>(adapter.transform.position + adapter.transform.up * -0.2f, 0.01f);
                            if (entities.Count > 0)
                            {
                                adapter.SetParent(entities[0], true, true);
                            }
                        }

                        adapter.MarkDirtyForceUpdateOutputs();
                        adapter.SendNetworkUpdateImmediate();
                        adapter.RefreshIndustrialPreventBuilding();
                        adapter.NotifyIndustrialNetworkChanged();
                    });
            }

            private IEnumerator StepRunFinalActions(RaidPaste paste)
            {
                yield return StepCollection(paste, paste.FinalProcessingActions, 0.69f, 0.71f, "Finishing paste data", action => action());
            }

            private IEnumerator StepPasteDelayedCupboards(RaidPaste paste)
            {
                var cupboards = paste.DelayedCupboardsData;
                if (cupboards == null || cupboards.Count == 0)
                {
                    yield break;
                }

                paste.DelayedCupboardsData = null;
                paste.ReplayingDelayedCupboards = true;

                try
                {
                    yield return StepCollection(paste, cupboards, 0.71f, PASTE_PROGRESS_END, "Attaching additional cupboards", data => TryPasteEntity(data, paste));
                }
                finally
                {
                    paste.ReplayingDelayedCupboards = false;
                }
            }

            internal void UpdateEntitySetupProgress(RaidableBase raid, float progress, string stage, bool force = false)
            {
                if (raid == null || raid.IsDespawning || !TryGetActivePaste(raid, out RaidPaste paste))
                {
                    return;
                }

                float value = Mathf.Lerp(PASTE_PROGRESS_END, ENTITY_SETUP_PROGRESS_END, Mathf.Clamp01(progress));
                UpdateProgressUi(paste, value, stage, force);
            }

            internal void RefreshProgressUi(BasePlayer player)
            {
                if (player == null || !player.IsConnected)
                {
                    return;
                }

                if (!TryGetProgress(player.userID, out RaidPaste paste))
                {
                    DestroyProgressUi(player);
                    return;
                }

                Instance.UI.ShowPasteProgressUi(player, paste.Filename, paste.LastProgress, paste.LastProgressStage, Instance.UI.IsMovingUi(player, UiType.PasteProgress));
            }

            private void RefreshProgressUi(ulong userid)
            {
                if (BasePlayer.TryFindByID(userid, out BasePlayer player) && player.IsConnected)
                {
                    RefreshProgressUi(player);
                }
            }

            private IEnumerator StepCollection<T>(RaidPaste paste, IList<T> values, float start, float end, string stage, Action<T> action)
            {
                int limit = Mathf.Clamp(paste.Raid.Options.Setup.SpawnLimit, 1, 500);
                int batchCount = 0;
                int count = values?.Count ?? 0;
                FrameDeadline deadline = new(1, 5);
                WaitForSeconds instruction = CoroutineEx.waitForSeconds(BATCH_DELAY_SECONDS);

                UpdateProgressUi(paste, start, stage, true);

                for (int index = 0; index < count; index++)
                {
                    try
                    {
                        action(values[index]);
                    }
                    catch (Exception ex)
                    {
                        if (!HandlePasteFailure(paste, null, stage, ex))
                        {
                            yield break;
                        }
                    }

                    bool delay = ++batchCount >= limit;
                    if (delay || limit < 500 && deadline.Expired)
                    {
                        batchCount = 0;
                        UpdateProgressUi(paste, Mathf.Lerp(start, end, (index + 1f) / count), stage);
                        yield return delay ? instruction : null;
                        deadline.Reset();
                    }

                    if (Instance.IsUnloading || paste.Raid == null || paste.Raid.IsDespawning)
                    {
                        yield break;
                    }
                }

                if (!ShouldStop(paste))
                {
                    UpdateProgressUi(paste, end, stage, true);
                }
            }

            private void UpdateProgressUi(RaidPaste paste, float progress, string stage, bool force = false)
            {
                UIPasteProgressSettings settings = Instance.config.UI.PasteProgress;

                if (settings == null || !settings.Enabled || paste?.Raid == null || paste.Raid.IsDespawning)
                {
                    return;
                }

                progress = Mathf.Clamp01(progress);
                int percent = Mathf.RoundToInt(progress * 100f);
                double now = Time.realtimeSinceStartupAsDouble;
                string nextStage = stage ?? string.Empty;
                bool stageChanged = !string.Equals(nextStage, paste.LastProgressStage, StringComparison.Ordinal);

                if (!force)
                {
                    if (!stageChanged && percent == paste.LastProgressPercent)
                    {
                        return;
                    }

                    if (!stageChanged && progress < 1f && now < paste.NextProgressUiUpdate)
                    {
                        return;
                    }
                }

                paste.LastProgressStage = nextStage;
                paste.LastProgressPercent = percent;
                paste.LastProgress = progress;
                paste.NextProgressUiUpdate = now + 0.25d;

                using var viewers = DisposableHashSet<ulong>();
                ulong ownerId = paste.Raid.ownerId;

                if (ownerId.IsSteamId() && BasePlayer.TryFindByID(ownerId, out BasePlayer owner) && owner.IsConnected)
                {
                    viewers.Add(ownerId);
                }

                foreach (BasePlayer player in paste.Raid.GetIntruders())
                {
                    if (player != null && player.IsConnected)
                    {
                        viewers.Add(player.userID);
                    }
                }

                using var previousViewers = paste.ProgressViewers.ToPooledList();

                foreach (ulong userid in previousViewers)
                {
                    if (!viewers.Contains(userid))
                    {
                        paste.ProgressViewers.Remove(userid);
                        RefreshProgressUi(userid);
                    }
                }

                foreach (ulong userid in viewers)
                {
                    paste.ProgressViewers.Add(userid);
                    RefreshProgressUi(userid);
                }
            }

            private void DestroyProgressUi(BasePlayer player)
            {
                if (player != null)
                {
                    Instance.UI.DestroyUi(player, UiType.PasteProgress);
                }
            }

            private void DestroyProgressUi()
            {
                using var viewers = DisposableHashSet<ulong>();

                foreach (RaidPaste paste in _pastes)
                {
                    foreach (ulong userid in paste.ProgressViewers)
                    {
                        viewers.Add(userid);
                    }

                    paste.ProgressViewers.Clear();
                }

                foreach (ulong userid in viewers)
                {
                    if (BasePlayer.TryFindByID(userid, out BasePlayer player) && player.IsConnected)
                    {
                        DestroyProgressUi(player);
                    }
                }
            }

            private void FinishProgress(RaidPaste paste)
            {
                _pastes.Remove(paste);
                using var viewers = paste.ProgressViewers.ToPooledList();
                paste.ProgressViewers.Clear();

                foreach (ulong userid in viewers)
                {
                    RefreshProgressUi(userid);
                }
            }

            private readonly Dictionary<string, string> _replacementPrefabs = new()
            {
                { "assets/rust.ai/nextai/testridablehorse.prefab", "assets/content/vehicles/horse/ridablehorse.prefab" },
                { "assets/content/vehicles/horse/ridablehorse2.prefab", "assets/content/vehicles/horse/ridablehorse.prefab" },
                { "assets/prefabs/deployable/windmill/windmillsmall/electric.windmill.small.prefab", "assets/prefabs/deployable/windmill/electric.windmill.small.prefab"}
            };

            private Dictionary<int, int> _replacementItemIds;

            private Dictionary<int, int> DeserializeReplacementItemIds() => JsonConvert.DeserializeObject<Dictionary<int, int>>(Encoding.UTF8.GetString(Facepunch.Utility.Compression.Uncompress(Convert.FromBase64String("H4sIACQ4Z2oA/12aSZIsxw1E9zwFTWuVWcyDribT3fUcQ1Txc0F2MzozIwGHuwOR//3r77//9alj1VnOGedf//m7zjH3vvOef2ux1TrtH63Nc+ZdtzRb+szez55zVZb2mbudsrct1dW4T61r6qp6z1hz+kWjtXM7d2XlU9et/a41ui2eOs9d4xxb49F1rTaXrxVtcY5ua71x79p7tbUx7t711MLaKnOs3Zdvn+e0fvu4Ta+2ayu75B3r3Ofc0ZYu++zRzpptxdroa/bWioVk9cPPtwxbm7zCXqMN28kZ9daze4l73nHHPu3aIg/kPtffvJY+y7z3+oXrTG7ZSlx4Kpspp/pi6Wt1/vYt3sbrbQvabrzsJg8R6sNdyYM/cfS29iCqtnh7abdZXMjHKey0RfLqXLNcYrhY7LPVXu7Otb1nX9xI9ySTe44dd7S1xgPm1k2JQt2zxhIAYCvleo5K62WRjJ6JL233vTzxrY/dgZXjZS422ncVkj5s5XKXXhwx+xZe4Uy7aQMP5KHEPXvfbRF/yyC5JTFk21M47um1kV573p2scmMP6CyDu/SzLLtToThxGVtrwITH203nvErZiusqGCT0Xij9nBVl8iFxC1xfXXTWIpcnriEOY5xVlwWsk4DVx/QXICTk4dx7DLnsiVs4yu7uBqJaLuUySol0l7F4k30s3fPwUyuRgLtHcRQQhs5eIoKg4pRauWMU1rJ4liiRVW39KDGVZ5HqcTKlxGgsIG1bmVzEk6PIr6pi2dYBBX9G4k9CnXesje35LvlN9ROLhBCCoO61SGxvKys2Q7zXIvgWK+imQR3d3+LyAhCMxx6olN7nHoGDBWCI1PS1a3/pl1l0d1dZLXFN3StTBsYvj7NXGzxmUwD+am2T3bGN9A7PgWhWPGrvAnvt4xjnqe2uQPGHGDdYg0BHbbCJm0kgdIAOrrNXa3OC1+2Y2ywJuctQ0Dahi2BppTffyGcvyvfOoBrh/a69gbW99elTuM2tdD2e+gq+BH5lUXK5ejeZK3blHeCBvD+aGn2TTC/GDYhuHcFh8CFhv82TQOFcFcNjDeh6uFJQcIOQrd4CZLA3FQFZ2KL2xcvf3OyiaClAjw0IO222uC38Dap5aS2hCVeRC5KmRmH7awIExODd3mYGh2vOLqYzALypipIyoXoKbw2/ZxmkAtVxlBEMQudwp06H+PNJQikg3SMK1gusGSgTIHsjNHbhlf4MyC/W+iC6dZ0QDOCIeCXdAF3Ciyo5f/OybDt2Ctcv5IQKt1xM5IwnRdSoMFDrctJ5/bWTjeD6Ak01AxSYYD+IcfAKsF59217OFEWe7VdBuNdLqAGBK1fw/v8ytUMsKC4oMLwBkUOUlpPORk1xAyMYnbKhtCzfFOCRLAaG4H4S16wkK8XTCFyQHk5B/I2c6Y6IPDzGS/taw2EQf8cXwOM1HvSIBIVyl3KjgoQC+vyuKY3LoUdhgJuxv4udNBv5ARq4f0Nb4WKo1k3BhYTUAvgGMEsBJVFXwbXI3GI0Efe9nZdCq2yNm4oPSxYYetpEgVGafcFcNUuTv0UucBShaag5e3jKPLc2iC2yh4pspDYB0LnGwMmZqnERdAAKI3ilLB6aQUAhAHAAVKmjjhEqoy0wAOgjtDDV4gnb6k97Ea/H864y0sM/gUweqNcMy4IqI23OFIPQHVm02Ay+QxB1sQJ/YDKZC9mGcLbFHPmG1Rb/iUICXshDd2VGNpCRMKNcwB6rxWUTk34S8DJTowz2U92WoHQHsIQCw8RwgxUYxDtBQJgLLA/6BV78KpwMGLtJoCaM2qn7Q/4Jnle1QLbGLQin/Tr+sdJ+VtY/VsbPSsYYY9L0KI8x0RCY1lMA1Ljho2wX6xDZmm4MNA75bt30IoIYp4zjFFNjS+yyCWvwnl9/gVzfMUp4cMDan5smN6RtmPiB4SXvGU4TcpLIi4sMwKUnKnH0yNjx6GP2157JmpgC8VSxPYK00ttIC4GZQ+2qNQNwwpDWxS3PuGDtTGcQzAvilqoIBgE6ltgyo1qH7h8XIMFgvUX3IfkQFwcUDtKzLSgfZEDmYGQsgTKm1LoI3gF/0YBNVsjF/CF3oQsX80Cdfe3yBf2jWgIhRBFQegm1THO70YToKIgWr4ECtoNjZgXQVaPj1C4W1NQYsaob22HWaAtWh49DZlChJanOPkjO2NuZrghJRmMX8NMQkfkO8R/apgOTkrtZpigNu0hXbg8g7N47qShvVHDXxlF/U1cIiFcpLdWOLSxi534SBy7WTMk2EV32ZvwC1VKbL45TvQXM7kQtiQBHuaj2jFQeN8ybQMq0/qO26k9tpX+VezqmQ1wPIz4IURoUhVWqLJWseBIwURUjBZlAtlip8wwgon/UGzotbEPN+jFdOIFevMuRcifrA1o6Qby/wgl4m7rd+W0bu/pKjyeb4c/OeP4cX4FBjP6WTpj2wBcXFdPI0I1ujMIAfTeDLQkC0pZA0tCkpqlQFAJdenUZhoOwTqQsVFGKTAk5loABLDRSiCFQuozTV9CVfM3K5F/hDvUaLu4YFtjEL4ShASCmwFRGctlLhgbzhCfyeKu1huSSPyaVRXiGQ6YA0InW93h9zA1IiH4Tj0f9Ruqpn0Pz2S1qxAVgl2jEZWrYmnvjDx0euWA5TQ0Jr14p9Ku0upT367HQAaJqAgR21z4P9E2tzHL2IIGk6WT1VRGiEuNeHCbD4wTtDJkj9eGiARuctJRsIZB2YBa3zNN4OwJNn9s1wxlBV3SI3GUnuomZ/LWpzQfwLGvHIkWaEMgiuv8FMeV1kcC6aEARryGfelJS+MMtJvKAAjuU/zHyEAECWU8hUTtCxYzOH66hh3DPMtSQzZzPoNrDyDzsF8282CnjBvDZeTcAa7ZR0ozzt4QUYLiH4jcZpdXGG1No9rDdq3NTwtHniytpw4OE4J4Gs1Ox0Y3hdaEZe5GqSBx6yNRNVCgnEZRLAygxL4JpiTk052MfzDdtUz6PPmAH06gTQBBpoMKVEV2YwIBB0UDTuKYYQ6hYiWHMwvg9K1CDDHxK91kDz2XD8SgeilcAola3mmoAjfJsBmRQXYqOejXNvhIwF1WUukQeNI4ZIX5DHqBGipQ+zSPqtyGkTxpeSmIKaq7miFAdJ53+CJurK7Ox0CQI7+gqTaVoPBTEhamhDVvpJWAeGM8HhE0jDW/n5c/II8Hwdz+o0woK1TgCx3myVwJhS1bGsaLAEvI/1urv2v7nWvtda38I0vkVpPPH4vpdXDk4nRoDmV0gYnRkfb/BlFr2mIB8jqwM5e9PVH8DBIqjVoGlF3qimw/cvw/cfyzO38XgVyranoaUFGghOAv7czWtiJ6JElLDmvPdpUFmj+GBZlM9HwWrwnsRaHG1ZhTpZNVqqWbNfR0VSBL5R+0o5n55x0j3zHKNkhT6ryalnnax/KIU44lyaRpmuUuhWEFIzZs2JLg4C1YhnuqKkR3iRBpUOj7HEbLYQgaMOtFIy9KHmUBiz3e2hVwIFtFnQbvzvhcRYinKkbzUNRE+4QYhwS37W72dhGDfGO6qIQLf3hIOtXX79QCzW+nq/TXH0dTiW38wGUE3osMlYCW/02iIl/sSK1mOqvlryd4N6Rj4csshBqajJWFi0GKYHpGP19Mw7MQtoeOiwFl/w44EpmQQcrSaTxE/JFktcRofQilqMFJS1zDz1TrFrVKwATVOSXqek3s1WMOJBbihbTTcb34FQyMwu1pTcacENRh1HvWsJycBbBDaed5N6Srew3ATLN/K6fzRpPc6jObgFriY+wZNQ1Ox4pRLNLCmIURnkAs0KnSYCGwN8t8AHuCKo5w74Wnyen9WgdJxYzdkNWqOd3TcwXa2OwaZrJ6cq3Fj1xAjDJEG7ic6Qp3W+OCHV6XyZqq+2hU87Xa408CI78NbY8WGWhOhBMdDybSTW4SLFwy1Q9e36OqcFFK9Wz0+XaVd0vnD4xyysjVHcmDCYrD9OyDCoFOIrccBES4EwNXvbiCOFU/ErfD2OTomSPKXMVYpMoo9aUko42Z+onEkS+c5iU4Lioa6FaZe5TpqznFAE9iKzlwTjpqnQIJEN3Tq7GZLirJFI2HuEmlbcGolt4Hz0ijT5Zm04arLylfD56myrV0vmihEuwsr4qH7doYjqE1y+Wq8NzZRekiUXL6mzunyu3oJHxFvJYS+7fXCZkyKUxUZakPHaiHOlFBvaZ51nnZlsx+rAszohinFi9i0tB9XnYJbKyRWljxGsgoqoQ60QA9Nyh6tq6bKMJQdYvFHdEqPVvSyGvrGXJmtCFlPFAlbFYBtVC/+ec2mRk5YIRo7tx9glZ/jiQReIyjXsDngNbrl50ywzjTZftLArffrlQE/ujBu1BcaQt4TLEc6qEM94zLAoWFMOBrZQcsCTHppEoOjNdaA543Zh/wkpuJ7RdgAr9acN0zBDgYODSnjHNnUaPNJ2gjXL4f7jhdhD838olNCszT4+D3S6xrS+JUYz6sZYGghZQoADOjcHrJWxxWpAWpL02uLIu0cRuu1pjwD37CMNXkEyLyBWp1F6CzJG2EVBBsLvlEfxTucPLYT6b+Blzi6S480f1o2+TrJU9D1Op6YK0tT3wwG78rfzeHTcA0OeKN3aEHw2YEfLyI3CHsdbzzVj6pJQNf8BEHI0c2gfJfmjNZDoNTneyopiUS8bgz8NTcpKvaIGeqhrB9nbxXnCJSQ5a2GJ2bRgEeW6Z0TqB2YTitLtoGij8lV1dEnjwkTNGTPWks4o+czIrPFyX2lIFL+KtBqLmhL6XXAGfuEXul3o/GGhPG3N2ALNcFdfkzXdRx830Rd+J8wSTRtV14uD17e4vhdjNm2nZHCrjMPjwnhzqkkQRl28GAy1HVOnw2IjBv044mQ+wNM2bF2H+SERUdFhxAZi03z0nCcVLvOpMJDYEKOxmDx6luEWmNMRd+N+9UU2h5oZBDShSDKRMaR873qz/sbAvCEBa9sJ3gN+7KHolClxsfHTbtAnG9gZqPq4QQBf1Eaq7wuA8bSqK378b59ofC8H3SO/QtRk8mXjY1hxdJpp45FxZlt6eT/6QmVMevYkSJ4sbfXSGyrlJUdJBCgsmJNEwex48jpI2nq+bEBlQJGcoqtQ3B59KBpRIr+P48mNLpl4y1niWqsxohOEGd3cjAmgSARx/hu6jCexjytNCBQo2gz0iUbkI0uZhDLuU0uCR3sQotz84CpE8LVws9QRsh1LIod6bHCXHBH/fLOiXQwp6MDM8y4jFXy0w2a0KVx0M6jDhtM+D1v29ZZrPjkA86lc2rJZzC+j+f0scdSQ/61nDq0mDPIB9WjICO1V6cRay3fStfxdx4eUR1TTtb7AcLCa7BRv84n/TZx/uhgkW3lZRryfb+FAZoo3vtaompcfIOWUNSrg8jYqD5xWTpUtHpQK9RykgZyUMGTIwXZ7p0nBseO4kc0ikXT6fe1wufIq133lctmN60/3hXmZE3sJYg84H7DCNgFpjpxYsOzkY5Y1Imz3PaIzwcA/f0Oi/Q9z4164JqrfK63Geo1vqsgLddy/3aq2WV+KQCRqh3IgYS+FdG//P0JvUbbb6sNsaIB85biUnaI586zMxBzXR+R6qmPm2KyUA16Gt1YRVzKeMQwaeoICcsT8whygSQE0mrRBkzDdTcpxONWHkUn15x8OnK1S9qTqS9vrIqQQqxLCyGGdjRgM1TrHBzQxwc5Op48LQieXox2Yoc5ZBNT3WnoUOsrdUhfgVy5hxyf8aN6gPgmRAdS1xuspi8MhJtQaCykGMGGHutY8nfaTbqWmFIjPxKa8e1VB+AvccxMuKmvNl4FoRFFrjKObquajNcbT33xZA21DunUUmUNEX61J25BcBVqJHLig70p26tLDSFaEI/ToQAUHgc6Ui99XZHA1KGpGNiSo89k8IH56hAgfOR0ezULZSnfT+dtC2Lwk3miW1qOYuFvasLVa8qkwYb58da2Dy/iIzPNgiiDSIKqM8a0qJwmd2/Kohk5EQ5VkFFvby6l41S58ni9o4F1HogiEV1HUQY8Nb68d1pcjU+FOfMzlI2sSH4rorF2iRZD1ECxPKtDV6zPrSx1OrwCuHluo3kBl5khXTgSrPfrDvVFkgQpSlWohT18VX3kCnWCYOApTQdin2hJT6RYq8oTan7PtzXG+tIfAMXTx6JORuQQYrrbNK0ujxrEqen0UEfV+etptrBa0no1a8HzyFTC3W2kCqEPfdJ33jGDjkZajCm62b4a4e4y6iN7ttPPM+o6i1dh6h0wsBog9+8XXjotrfbRCNaj9ncQ+QGKQ7PT4BSbj5w/1qqLkPqONxWIpZjSQmEHeD4RUju54hSs6CCkfj9L4tH6wMNUQZMtPE3bf/3v/9WygVP8KgAA")))) ?? new();

            internal void Initialize()
            {
                try
                {
                    _replacementItemIds = DeserializeReplacementItemIds();
                }
                catch (Exception ex)
                {
                    Puts(ex);
                }
                finally
                {
                    _replacementItemIds ??= new();
                }
            }

            private bool IsAlreadyPlaced(string prefabname, Vector3 pos, Quaternion rot)
            {
                const float maxDiff = 0.01f;

                using var ents = FindEntitiesOfType<BaseEntity>(pos, maxDiff);

                foreach (var ent in ents)
                {
                    if (ent.IsDestroyed || ent.PrefabName != prefabname)
                        continue;

                    if (Vector3.Distance(ent.transform.position, pos) > maxDiff)
                        continue;

                    if (Vector3.Distance(ent.transform.rotation.eulerAngles, rot.eulerAngles) > maxDiff)
                        continue;

                    return true;
                }

                return false;
            }

            private byte[] FixSignage(ISignage iSignage, byte[] imageBytes)
            {
                if (iSignage is not Signage sign || !_signSizes.TryGetValue(sign.ShortPrefabName, out var signSize))
                    return imageBytes;

                var size = Math.Max(sign.paintableSources.Length, 1);
                if (sign.textureIDs == null || sign.textureIDs.Length != size)
                {
                    Array.Resize(ref sign.textureIDs, size);
                }

                return ResizeImage(imageBytes, signSize.Min, signSize.Max);
            }

            private int GetItemId(int itemId) =>
                _replacementItemIds.TryGetValue(itemId, out var replaceId) ? replaceId : itemId;

            private string GetPrefabName(string prefabName) =>
                _replacementPrefabs.TryGetValue(prefabName, out string replacementPrefab) ? replacementPrefab : prefabName;

            private static bool IsPng(byte[] bytes) =>
                bytes is { Length: >= 8 } && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

            private byte[] ResizeImage(byte[] imageBytes, int width, int height)
            {
                if (imageBytes == null || imageBytes.Length == 0 || width <= 0 || height <= 0)
                    return imageBytes;

                using var sourceStream = new MemoryStream(imageBytes, writable: false);
                using var src = new Bitmap(sourceStream);

                using var output = new MemoryStream();
                if (src.Width == width && src.Height == height)
                {
                    if (IsPng(imageBytes))
                        return imageBytes;

                    src.Save(output, ImageFormat.Png);
                }
                else
                {
                    using var dest = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                    dest.SetResolution(src.HorizontalResolution, src.VerticalResolution);

                    using var wrap = new ImageAttributes();
                    wrap.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);

                    var destRect = new Rectangle(0, 0, width, height);

                    using var g = Graphics.FromImage(dest);
                    g.DrawImage(src, destRect, 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);

                    dest.Save(output, ImageFormat.Png);
                }

                return output.ToArray();
            }

            private void TrySplitPastedBuilding(RaidPaste paste)
            {
                if (paste.CupboardCount < 2 || paste.BuildingId == 0)
                    return;

                var building = BuildingManager.server.GetBuilding(paste.BuildingId);
                if (building != null && building.HasDecayEntities())
                    BuildingManager.server.CheckSplit(building.decayEntities[0]);
            }

            private void AssignNearestDoor(DoorManipulator doorManipulator)
            {
                if (!doorManipulator.IsValid() || doorManipulator.IsDestroyed)
                    return;

                Transform manipulatorTransform = doorManipulator.transform;
                using var doors = FindEntitiesOfType<Door>(manipulatorTransform.position, 1f, 2097152, QueryTriggerInteraction.Ignore);
                Door foundDoor = null;
                float closestDistance = float.PositiveInfinity;
                foreach (Door door in doors)
                {
                    if (door.IsValid() && !door.IsDestroyed && !door.IsOnMovingObject())
                    {
                        float distance = Vector3.Distance(door.transform.position, manipulatorTransform.position);
                        if (distance < closestDistance)
                        {
                            foundDoor = door;
                            closestDistance = distance;
                        }
                    }
                }

                if (foundDoor.IsValid())
                {
                    doorManipulator.SetParent(foundDoor, true);
                    doorManipulator.SetTargetDoor(foundDoor);
                }
            }

            private void SnapLegacyFloorFrameEntities(RaidPaste paste)
            {
                // Legacy saves can place floor-frame deployables 0.1m low; link only the expected frame sockets instead of refreshing all nearby entity links.
                List<BaseEntity> floorFrameTargets = null;

                for (int i = 0; i < paste.PastedEntities.Count; i++)
                {
                    if (paste.PastedEntities[i] is not StabilityEntity entity || !entity.IsValid() || entity.IsDestroyed || entity is BuildingBlock)
                        continue;

                    List<EntityLink> links = entity.links;
                    if (links == null || links.Count == 0)
                        continue;

                    for (int j = 0; j < links.Count; j++)
                    {
                        EntityLink link = links[j];
                        if (link.socket is not ConstructionSocket socket || !socket.male || link.connections.Count != 0 || !IsFloorFrameSocket(socket))
                            continue;

                        if (floorFrameTargets == null)
                        {
                            floorFrameTargets = Pool.Get<List<BaseEntity>>();
                            for (int k = 0; k < paste.PastedEntities.Count; k++)
                            {
                                BaseEntity target = paste.PastedEntities[k];
                                if (!target.IsValid() || target.IsDestroyed || target is not BuildingBlock)
                                    continue;

                                uint prefabID = target.prefabID;
                                if (prefabID == _floorFramePrefabId || prefabID == _floorTriangleFramePrefabId)
                                    floorFrameTargets.Add(target);
                            }
                        }

                        SnapLegacyFloorFrameEntity(floorFrameTargets, entity, link);
                        break;
                    }
                }

                if (floorFrameTargets != null)
                    Pool.FreeUnmanaged(ref floorFrameTargets);
            }

            private void SnapLegacyFloorFrameEntity(List<BaseEntity> floorFrameTargets, BaseEntity entity, EntityLink maleLink)
            {
                const float targetRadiusSqr = 0.5f * 0.5f;

                Transform transform = entity.transform;
                Vector3 offset = new Vector3(0f, 0.1f, 0f);
                transform.position += offset;
                Vector3 malePosition = transform.position + transform.rotation * maleLink.socket.worldPosition;

                for (int i = 0; i < floorFrameTargets.Count; i++)
                {
                    BaseEntity target = floorFrameTargets[i];

                    if (!target.IsValid() || target.IsDestroyed || target == entity)
                        continue;

                    Transform targetTransform = target.transform;
                    if ((targetTransform.position - malePosition).sqrMagnitude > targetRadiusSqr)
                        continue;

                    List<EntityLink> targetLinks = target.links;
                    if (targetLinks == null || targetLinks.Count == 0)
                        continue;

                    bool connected = false;
                    for (int j = 0; j < targetLinks.Count; j++)
                    {
                        EntityLink targetLink = targetLinks[j];

                        if (targetLink.socket is not ConstructionSocket targetSocket || !targetSocket.female || !IsFloorFrameSocket(targetSocket) || !maleLink.CanConnect(targetLink))
                            continue;

                        if (!maleLink.Contains(targetLink))
                            maleLink.Add(targetLink);

                        if (!targetLink.Contains(maleLink))
                            targetLink.Add(maleLink);

                        connected = true;
                    }

                    if (connected)
                        return;
                }

                transform.position -= offset;
            }

            private bool IsFloorFrameSocket(ConstructionSocket socket)
            {
                return socket.socketType == ConstructionSocket.Type.FloorFrame || socket.socketType == ConstructionSocket.Type.FloorFrameTriangle;
            }

            private void AdjustIOEntityPositions(RaidPaste paste)
            {
                Dictionary<IOEntity, OriginalTransforms> originalTransforms = Pool.Get<Dictionary<IOEntity, OriginalTransforms>>();
                using var emptyOutputs = Pool.Get<PooledList<IOEntity>>();

                // First pass: Adjust entities that have outputs
                for (int i = 0; i < paste.LegacyIoPositionChecks.Count; i++)
                {
                    var ioEntity = paste.LegacyIoPositionChecks[i];
                    if (!AdjustIOEntityPosition(ioEntity, originalTransforms, true))
                    {
                        // Didn't have any outputs, queue for input check
                        emptyOutputs.Add(ioEntity);
                    }
                }

                // Second pass: Adjust entities that didn't have any outputs
                for (int i = 0; i < emptyOutputs.Count; i++)
                    AdjustIOEntityPosition(emptyOutputs[i], originalTransforms, false);

                // Third pass: Adjust the line points based on the new positions
                foreach (var (ioEntity, originalTransform) in originalTransforms)
                    AdjustLinePointPositions(ioEntity, originalTransform);

                Pool.FreeUnmanaged(ref originalTransforms);
            }

            private bool GetConnectedIOEntity(IOEntity.IOSlot ioSlot, bool isCurrentSlotInput, out IOEntity connectedIOEntity, out IOEntity.IOSlot connectedIOSlot)
            {
                connectedIOEntity = null;
                connectedIOSlot = null;

                if (ioSlot == null || ioSlot.connectedTo == null || ioSlot.connectedToSlot < 0)
                    return false;

                IOEntity ioEntity = ioSlot.connectedTo.Get();
                if (!ioEntity.IsValid() || ioEntity.IsDestroyed)
                    return false;

                IOEntity.IOSlot[] ioEntitySlots = isCurrentSlotInput ? ioEntity.outputs : ioEntity.inputs;
                if (ioEntitySlots == null || ioSlot.connectedToSlot >= ioEntitySlots.Length)
                    return false;

                connectedIOEntity = ioEntity;
                connectedIOSlot = ioEntitySlots[ioSlot.connectedToSlot];
                return true;
            }

            private bool AdjustIOEntityPosition(IOEntity ioEntity, Dictionary<IOEntity, OriginalTransforms> originalTransforms, bool checkOutputs)
            {
                Transform transform = ioEntity.transform;

                void ApplyPositionCorrection(Vector3 linePoint, Vector3 handlePosition)
                {
                    Vector3 localDiff = linePoint - handlePosition;
                    Vector3 worldDiff = transform.TransformDirection(localDiff.normalized) * localDiff.magnitude;
                    float magnitude = worldDiff.magnitude;
                    if (magnitude >= 0.5f && magnitude <= 1.5f)
                    {
                        originalTransforms.Add(ioEntity, new OriginalTransforms(transform, worldDiff));
                        transform.position += worldDiff;
                    }
                }

                if (checkOutputs)
                {
                    if (ioEntity.outputs == null)
                        return false;

                    for (int i = 0; i < ioEntity.outputs.Length; i++)
                    {
                        IOEntity.IOSlot ioOutput = ioEntity.outputs[i];
                        if (ioOutput == null || ioOutput.linePoints == null || ioOutput.linePoints.Length == 0)
                            continue;

                        Vector3 linePoint = ioOutput.linePoints[ioOutput.linePoints.Length - 1];
                        if (linePoint == Vector3.zero)
                            continue;

                        ApplyPositionCorrection(linePoint, ioOutput.handlePosition);
                        return true;
                    }
                }
                else
                {
                    if (ioEntity.inputs == null)
                        return false;

                    for (int i = 0; i < ioEntity.inputs.Length; i++)
                    {
                        IOEntity.IOSlot ioInput = ioEntity.inputs[i];
                        if (!GetConnectedIOEntity(ioInput, true, out IOEntity outputIoEntity, out IOEntity.IOSlot ioOutput))
                            continue;

                        if (ioOutput.linePoints == null || ioOutput.linePoints.Length == 0)
                            continue;

                        Vector3 linePoint = ioOutput.linePoints[0];
                        if (linePoint == Vector3.zero)
                            continue;

                        Vector3 localLinePoint;
                        if (originalTransforms.TryGetValue(outputIoEntity, out var origTransform))
                        {
                            Vector3 scaledLinePoint = new Vector3(
                                origTransform.LocalScale.x * linePoint.x,
                                origTransform.LocalScale.y * linePoint.y,
                                origTransform.LocalScale.z * linePoint.z
                            );
                            Vector3 rotatedLinePoint = origTransform.Rotation * scaledLinePoint;
                            Vector3 worldLinePoint = origTransform.Position + rotatedLinePoint;

                            localLinePoint = transform.InverseTransformPoint(worldLinePoint);
                        }
                        else
                        {
                            localLinePoint = transform.InverseTransformPoint(outputIoEntity.transform.TransformPoint(linePoint));
                        }

                        ApplyPositionCorrection(localLinePoint, ioInput.handlePosition);
                        return true;
                    }
                }

                return false;
            }

            private void AdjustLinePointPositions(IOEntity ioEntity, OriginalTransforms originalTransform)
            {
                Transform transform = ioEntity.transform;
                Vector3 diff = originalTransform.Difference;

                if (ioEntity.outputs != null)
                {
                    for (int i = 0; i < ioEntity.outputs.Length; i++)
                    {
                        IOEntity.IOSlot ioOutput = ioEntity.outputs[i];
                        if (!GetConnectedIOEntity(ioOutput, false, out IOEntity inputIoEntity, out IOEntity.IOSlot ioInput))
                            continue;

                        if (ioOutput.linePoints != null)
                        {
                            ioOutput.originPosition = transform.position;
                            int max = ioOutput.linePoints.Length - 1;
                            for (int x = 0; x < ioOutput.linePoints.Length; x++)
                            {
                                if (ioOutput.linePoints[x] == Vector3.zero)
                                    continue;

                                if (x == 0)
                                    ioOutput.linePoints[x] = transform.InverseTransformPoint(inputIoEntity.transform.TransformPoint(inputIoEntity.inputs[ioOutput.connectedToSlot].handlePosition));
                                else if (x == max)
                                    ioOutput.linePoints[x] = ioOutput.handlePosition;
                                else
                                    ioOutput.linePoints[x] -= diff;
                            }
                        }
                    }
                }

                if (ioEntity.inputs != null)
                {
                    for (int i = 0; i < ioEntity.inputs.Length; i++)
                    {
                        IOEntity.IOSlot ioInput = ioEntity.inputs[i];
                        if (!GetConnectedIOEntity(ioInput, true, out IOEntity outputIoEntity, out IOEntity.IOSlot ioOutput))
                            continue;

                        if (ioOutput.linePoints == null || ioOutput.linePoints.Length == 0)
                            continue;

                        if (ioOutput.linePoints[0] == Vector3.zero)
                            continue;

                        ioOutput.linePoints[0] = outputIoEntity.transform.InverseTransformPoint(transform.TransformPoint(ioInput.handlePosition));
                    }
                }

                ioEntity.SendNetworkUpdate();
                ioEntity.RefreshIndustrialPreventBuilding();
            }

            private ElevatorLift FindLegacyElevatorLiftForTopFloor(RaidPaste paste, Elevator elevator)
            {
                var floorZeroPos = elevator.GetWorldSpaceFloorPosition(0);
                var topFloorPos = elevator.GetWorldSpaceFloorPosition(elevator.Floor);
                var elevatorPos = elevator.transform.position;
                var minY = Mathf.Min(floorZeroPos.y, topFloorPos.y) - LegacyElevatorLiftVerticalTolerance;
                var maxY = Mathf.Max(floorZeroPos.y, topFloorPos.y) + LegacyElevatorLiftVerticalTolerance;

                float nearestHorizontalDistanceSqr = float.MaxValue;
                ElevatorLift closestLegacyLift = null;

                foreach (var pastedEntity in paste.PastedEntities)
                {
                    if (!pastedEntity.Is(out ElevatorLift candidateLegacyLift))
                        continue;

                    if (!candidateLegacyLift.IsValid() || candidateLegacyLift.IsDestroyed)
                        continue;

                    var candidatePos = candidateLegacyLift.transform.position;
                    if (candidatePos.y < minY || candidatePos.y > maxY)
                        continue;

                    var horizontalDistanceSqr = new Vector2(candidatePos.x - elevatorPos.x, candidatePos.z - elevatorPos.z).sqrMagnitude;
                    if (horizontalDistanceSqr > LegacyElevatorLiftMaxHorizontalDistanceSqr)
                        continue;

                    if (horizontalDistanceSqr < nearestHorizontalDistanceSqr)
                    {
                        nearestHorizontalDistanceSqr = horizontalDistanceSqr;
                        closestLegacyLift = candidateLegacyLift;
                    }
                }

                return closestLegacyLift;
            }

            private Dictionary<Elevator, LegacyElevatorRestoreState> GetLegacyElevatorRestoreStates(RaidPaste paste)
            {
                if (paste.LegacyElevatorRestoreStates != null)
                    return paste.LegacyElevatorRestoreStates;

                List<List<Elevator>> shaftGroups = new();

                foreach (var pastedEntity in paste.PastedEntities)
                {
                    if (!pastedEntity.Is(out Elevator candidateElevator) || !candidateElevator.IsValid() || candidateElevator.IsDestroyed)
                        continue;

                    var candidatePos = candidateElevator.transform.position;
                    var candidateForward = candidateElevator.transform.forward;

                    List<Elevator> matchedShaftGroup = null;
                    for (var i = 0; i < shaftGroups.Count; i++)
                    {
                        var shaftGroup = shaftGroups[i];
                        if (shaftGroup == null || shaftGroup.Count == 0)
                            continue;

                        var shaftReference = shaftGroup[0];
                        if (shaftReference == null || !shaftReference.IsValid() || shaftReference.IsDestroyed)
                            continue;

                        var referencePos = shaftReference.transform.position;
                        var horizontalDistanceSqr = new Vector2(candidatePos.x - referencePos.x, candidatePos.z - referencePos.z).sqrMagnitude;
                        if (horizontalDistanceSqr > LegacyElevatorShaftMatchMaxHorizontalDistanceSqr)
                            continue;

                        if (Mathf.Abs(Vector3.Dot(candidateForward, shaftReference.transform.forward)) < LegacyElevatorShaftMatchForwardDotMin)
                            continue;

                        matchedShaftGroup = shaftGroup;
                        break;
                    }

                    if (matchedShaftGroup == null)
                    {
                        matchedShaftGroup = new();
                        shaftGroups.Add(matchedShaftGroup);
                    }

                    matchedShaftGroup.Add(candidateElevator);
                }

                var restoreStates = new Dictionary<Elevator, LegacyElevatorRestoreState>();
                foreach (var shaftGroup in shaftGroups)
                {
                    if (shaftGroup == null || shaftGroup.Count == 0)
                        continue;

                    shaftGroup.Sort((left, right) => left.transform.position.y.CompareTo(right.transform.position.y));

                    for (var floor = 0; floor < shaftGroup.Count; floor++)
                    {
                        var shaftElevator = shaftGroup[floor];
                        if (shaftElevator == null || !shaftElevator.IsValid() || shaftElevator.IsDestroyed)
                            continue;

                        restoreStates[shaftElevator] = new LegacyElevatorRestoreState
                        {
                            Floor = floor,
                            IsTop = floor == shaftGroup.Count - 1
                        };
                    }
                }

                paste.LegacyElevatorRestoreStates = restoreStates;
                return restoreStates;
            }

            private static bool RestoreDoorAnimationFlagsBeforeSpawn(Door door, Dictionary<string, object> flags)
            {
                bool hasOpen = flags.TryGetValue(nameof(BaseEntity.Flags.Open), out object openValue);
                bool hasReverseOpen = flags.TryGetValue(nameof(BaseEntity.Flags.Reserved1), out object reverseOpenValue);

                if (!hasOpen && !hasReverseOpen)
                {
                    return false;
                }

                // Apply both animation flags in one scope so AnimatedBuildingBlock initializes
                // directly at the saved pose instead of beginning a new opening animation.
                using var update = door.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                if (hasOpen) update.Set(BaseEntity.Flags.Open, Convert.ToBoolean(openValue));
                if (hasReverseOpen) update.Set(Door.ReverseOpen, Convert.ToBoolean(reverseOpenValue));
                update.Set(BaseEntity.Flags.Busy, false); // Cancel if set previously or it can become stuck in a busy state

                return true;
            }

            private bool TryPasteEntity(Dictionary<string, object> data, RaidPaste paste, BaseEntity parent = null)
            {
                if (data == null)
                {
                    return HandlePasteFailure(paste, null, parent == null ? "entity spawn" : "child entity spawn", "entity data is null");
                }

                if (!data.TryGetValue("prefabname", out object obj) || obj is not string)
                {
                    return HandlePasteFailure(paste, data, parent == null ? "entity spawn" : "child entity spawn", "prefab name is missing");
                }

                try
                {
                    PasteEntity(data, paste, parent);
                    return true;
                }
                catch (Exception ex)
                {
                    return HandlePasteFailure(paste, data, parent == null ? "entity spawn" : "child entity spawn", ex);
                }
            }

            private void PasteEntity(Dictionary<string, object> data, RaidPaste paste, BaseEntity parent = null)
            {
                if (parent != null && parent.IsDestroyed)
                {
                    return;
                }

                object obj;
                bool isChild = parent != null;

                data.TryGetValue("prefabname", out obj);
                string prefabName = GetPrefabName((string)obj);
                var skinId = data.TryGetValue("skinid", out obj) ? ulong.Parse(obj.ToString()) : 0;

                if (!isChild && !paste.ReplayingDelayedCupboards && prefabName.Contains("cupboard.tool") && ++paste.CupboardCount >= 2)
                {
                    paste.DelayedCupboardsData ??= new();
                    paste.DelayedCupboardsData.Add(data);
                    return;
                }

                var pos = isChild ? Vector3.zero : (Vector3)data["position"];
                var rot = isChild ? Quaternion.identity : (Quaternion)data["rotation"];
                var localPos = isChild ? (Vector3)data["position"] : Vector3.zero;
                var localRot = isChild ? (Quaternion)data["rotation"] : Quaternion.identity;

                var ownerId = paste.BasePlayer?.userID ?? 0;
                if (data.TryGetValue("ownerid", out obj))
                {
                    ownerId = Convert.ToUInt64(obj);
                }

                if (paste.IsAlreadyPlaced && IsAlreadyPlaced(prefabName, pos, rot))
                    return;

                if (prefabName.Contains("pillar"))
                    return;

                // Used to copy locks for no reason in previous versions (is included in the slots info so no need to copy locks) so just skipping them.
                if (prefabName.Contains("locks") && paste.Version < new VersionNumber(4, 2, 0))
                    return;

                if (_blockedPrefabs.Exists(prefabName.Contains))
                    return;

                BaseEntity entity = null;

                // Check to see if this child is already spawned
                if (isChild && parent.children != null)
                {
                    foreach (var child in parent.children)
                    {
                        if (child == null || child.IsDestroyed)
                            continue;

                        // Skip associated entities that can have multiple instances and always have localPosition set to Vector3.zero
                        if (_itemModAssociatedEntityTypes.Contains(child.GetType()) && child.transform.localPosition == Vector3.zero)
                            continue;

                        if (child.PrefabName == prefabName && (child.transform.localPosition - localPos).sqrMagnitude < 0.001f)
                        {
                            entity = child;
                            break;
                        }
                    }
                }

                if (entity == null)
                    entity = GameManager.server.CreateEntity(prefabName, pos, rot);

                if (entity == null || entity.IsDestroyed)
                    return;

                var savedFlags = data.TryGetValue("flags", out obj) && obj is Dictionary<string, object> rawFlags ? rawFlags : null;
                var restoredDoorAnimationFlagsBeforeSpawn = false;
                var pastedDoor = entity as Door;

                var transform = entity.transform;

                // If the entity is a child, set the parent and the local position and rotation.
                if (isChild)
                {
                    if (!entity.isSpawned)
                    {
                        PlayerBoat playerBoat = parent as PlayerBoat;
                        bool needsNormalParenting = entity is DroppedItem;
                        bool playerBoatEntity = playerBoat != null && !needsNormalParenting;

                        if (playerBoatEntity)
                        {
                            if (!paste.PlayerBoats.TryGetValue(playerBoat, out var playerBoatData))
                            {
                                paste.PlayerBoats[playerBoat] = playerBoatData = new();
                            }

                            transform.position = playerBoat.transform.TransformPoint(localPos);
                            transform.rotation = playerBoat.transform.rotation * localRot;

                            if (entity.Is(out BoatBuildingBlock boatBuildingBlock)) playerBoatData.Blocks.Add(boatBuildingBlock);
                            else playerBoatData.Deployables.Add(entity);
                        }
                        else
                        {
                            entity.gameObject.Identity();
                            if (data.TryGetValue("parentbone", out obj)) entity.SetParent(parent, obj.ToString());
                            else entity.SetParent(parent);
                        }

                        // Skip OnDeployed() for entities that don't properly handle null "deployedBy" or "fromItem.info"
                        if (entity.Is(out Signage signage)) signage.AddToEasel(parent);
                        else if (entity.Is(out PhotoFrame photo)) photo.AddToEasel(parent);
                        else if (!playerBoatEntity && ShouldInvokeOnDeployed(entity)) entity.OnDeployed(parent, null, _emptyItem);

                        // Set local position/rotation for entities that were parented normally
                        if (playerBoat == null || needsNormalParenting)
                        {
                            transform.localPosition = localPos;
                            transform.localRotation = localRot;
                        }
                    }
                }
                // If the entity is not a child, set the position and rotation.
                else
                {
                    transform.position = pos;
                    transform.rotation = rot;
                }

                if (data.TryGetValue("scale", out obj) && obj is Dictionary<string, object> scaleData)
                {
                    var scale = new Vector3(Convert.ToSingle(scaleData["x"]), Convert.ToSingle(scaleData["y"]), Convert.ToSingle(scaleData["z"]));

                    entity.transform.localScale = scale;
                    entity.networkEntityScale = true;
                }

                if (paste.BasePlayer != null)
                    entity.SendMessage("SetDeployedBy", paste.BasePlayer, SendMessageOptions.DontRequireReceiver);

                if (paste.Ownership)
                    entity.OwnerID = ownerId;

                BuildingBlock buildingBlock = entity as BuildingBlock;
                if (buildingBlock != null)
                {
                    buildingBlock.blockDefinition = PrefabAttribute.server.Find<Construction>(buildingBlock.prefabID);
                    var grade = (BuildingGrade.Enum)ToInt32OrDefault(data["grade"]);
                    if (skinId != 0ul && !HasGrade(buildingBlock, grade, skinId))
                        skinId = 0ul;
                    buildingBlock.SetGrade(grade);
                    if (!paste.Stability)
                        buildingBlock.grounded = true;
                }

                if (entity.Is(out DecayEntity decayEntity))
                {
                    if (paste.BuildingId == 0)
                        paste.BuildingId = BuildingManager.server.NewBuildingID();

                    if (paste.ReplayingDelayedCupboards && decayEntity is BuildingPrivlidge)
                    {
                        var nearbyBuildingBlock = decayEntity.GetNearbyBuildingBlock();
                        var building = nearbyBuildingBlock?.GetBuilding();

                        decayEntity.buildingID = building?.ID ?? paste.BuildingId;
                    }
                    else
                    {
                        decayEntity.AttachToBuilding(paste.BuildingId);
                    }
                }

                if (entity.Is(out StabilityEntity stabilityEntity) && !stabilityEntity.grounded)
                {
                    stabilityEntity.grounded = true;
                    paste.StabilityEntities.Add(stabilityEntity);
                }

                if (data.TryGetValue("oldID", out obj))
                {
                    ulong oldID = Convert.ToUInt64(obj);
                    paste.EntityLookup.TryAdd(oldID, new()
                    {
                        { "entity", entity }
                    });
                }

                entity.skinID = skinId;

                if (!paste.EnableSaving)
                {
                    entity.EnableSaving(false);
                }

                if (entity.Is(out ModularCar modularCar) && data.TryGetValue("children", out obj) && obj is List<object> modularCarChildren && modularCarChildren.Count > 0)
                {
                    // If there are children present, disable the default spawn settings to prevent stacking modules
                    // on top of each other, which could cause the vehicle to be destroyed
                    modularCar.spawnSettings.useSpawnSettings = false;
                }

                if (!entity.isSpawned)
                {
                    if (pastedDoor != null && savedFlags != null)
                    {
                        restoredDoorAnimationFlagsBeforeSpawn = RestoreDoorAnimationFlagsBeforeSpawn(pastedDoor, savedFlags);
                    }

                    paste.Raid?.DestroyGroundCheck(entity);
                    entity.Spawn();
                }

                if (entity == null || entity.IsDestroyed || entity.net == null)
                    return;

                if (buildingBlock != null)
                {
                    buildingBlock.SetHealthToMax();
                    buildingBlock.UpdateSkin();
                    buildingBlock.SendNetworkUpdate();
                    buildingBlock.ResetUpkeepTime();
                    if (data.TryGetValue("customColour", out obj))
                        buildingBlock.SetCustomColour(Convert.ToUInt32(obj));

                    for (int side = 0; side <= 1; side++)
                    {
                        if (!data.TryGetValue(side == 0 ? "wallpaperID" : "wallpaperID2", out obj))
                        {
                            continue;
                        }

                        // Zero is a valid default wallpaper
                        ulong wallpaperId = Convert.ToUInt64(obj);
                        float rotation = 0f;
                        float health = 0f;

                        if (data.TryGetValue(side == 0 ? "wallpaperRotation" : "wallpaperRotation2", out obj))
                        {
                            rotation = Convert.ToSingle(obj);
                        }

                        if (data.TryGetValue(side == 0 ? "wallpaperHealth" : "wallpaperHealth2", out obj))
                        {
                            health = Convert.ToSingle(obj);
                        }

                        if (health <= 0f)
                        {
                            continue;
                        }

                        int currentSide = side;
                        if (health > BuildingBlock.WALLPAPER_MAXHEALTH)
                            health = BuildingBlock.WALLPAPER_MAXHEALTH;

                        // Defer wallpaper until all building blocks are pasted.
                        // Interior wallpaper (side 1) must be "inside" (fully enclosed)
                        // or it will despawn on the next stability tick
                        paste.FinalProcessingActions.Add(() =>
                        {
                            if (buildingBlock == null || !buildingBlock.IsValid() || buildingBlock.IsDestroyed)
                                return;

                            buildingBlock.SetWallpaper(wallpaperId, currentSide, rotation);
                            if (currentSide == 0) buildingBlock.wallpaperHealth = health;
                            else buildingBlock.wallpaperHealth2 = health;
                        });
                    }
                }
                else if (entity.Is(out BaseCombatEntity baseCombat))
                    baseCombat.SetHealth(baseCombat.MaxHealth());

                if (entity.Is(out PatternFirework firework) &&
                    data.TryGetValue("patternfirework", out obj) &&
                    obj is Dictionary<string, object> pattern)
                {
                    firework.Design ??= new() { stars = new() };

                    if (pattern.TryGetValue("editedBy", out obj))
                    {
                        firework.Design.editedBy = Convert.ToUInt32(obj);
                    }

                    if (pattern.TryGetValue("stars", out obj))
                    {
                        firework.Design.stars = DeserializeStarPattern(obj.ToString());
                    }

                    firework.SendNetworkUpdate();
                }

                // This needs to stay for the old configs to load properly but is unused because of the new 'children' system.
                RestoreLegacySlots(entity, data, paste);

                if (isChild && data.TryGetValue("slot", out obj))
                {
                    var slot = (BaseEntity.Slot)ToInt32OrDefault(obj);
                    if (parent.HasSlot(slot))
                        parent.SetSlot(slot, entity);
                }

                RestoreLock(entity, data, paste);

                AutoTurret autoTurret = entity as AutoTurret;
                if (autoTurret != null)
                {
                    if (data.TryGetValue("autoturret", out obj) && obj is Dictionary<string, object> autoTurretData && autoTurretData.TryGetValue("authorizedPlayers", out obj) && obj is List<object> authorizedPlayers)
                    {
                        for (int index = 0; index < authorizedPlayers.Count; index++)
                        {
                            autoTurret.authorizedPlayers.Add(Convert.ToUInt64(authorizedPlayers[index]));
                        }
                    }

                    autoTurret.SendNetworkUpdate();
                }

                if (entity is IItemContainerEntity box)
                {
                    if (box.inventory == null)
                    {
                        if (entity.Is(out StorageContainer storageContainer))
                        {
                            storageContainer.CreateInventory(true);
                        }
                        else if (entity.Is(out IndustrialCrafter crafter))
                        {
                            crafter.CreateInventory(true);
                        }
                        else if (entity.Is(out ContainerIOEntity containerIo))
                        {
                            containerIo.CreateInventory(true);
                            containerIo.OnInventoryFirstCreated(containerIo.inventory);
                        }
                        else
                        {
                            Puts("WARNING: New IItemContainerEntity container '{0}' not supported", entity);
                        }
                    }
                    else
                    {
                        box.inventory.Clear();
                    }

                    if (box.inventory != null)
                    {
                        RestoreInventory(paste, data, entity, box.inventory);

                        if (autoTurret != null)
                        {
                            autoTurret.UpdateAttachedWeapon();
                        }

                        entity.SendNetworkUpdate();
                    }
                }

                if (entity.Is(out ItemBasedFlowRestrictor flow) && flow.inventory == null)
                {
                    flow.CreateInventory(true);
                    flow.OnInventoryFirstCreated(flow.inventory);
                }

                RestoreSignData(entity, data);

                if (entity.Is(out ShutterFrame shutterFrame))
                {
                    if (data.TryGetValue("isShutterOpen", out obj))
                        shutterFrame.IsShutterOpen = Convert.ToBoolean(obj);
                }

                if (entity.Is(out OrnateFrame ornateFrame))
                {
                    if (data.TryGetValue("frameText", out obj) && obj != null)
                    {
                        var frameText = obj.ToString();
                        if (!String.IsNullOrEmpty(frameText))
                            ornateFrame.FrameText = frameText;
                    }

                    if (data.TryGetValue("textColour", out obj))
                        ornateFrame.TextColour = DeserializeColor(obj);
                }

                if (entity.Is(out ChristmasLights lights))
                {
                    if (data.TryGetValue("animationStyle", out obj))
                    {
                        lights.animationStyle = (ChristmasLights.AnimationType)obj;
                    }
                }

                if (entity.Is(out StringLights stringLights))
                {
                    if (data.TryGetValue("points", out obj))
                    {
                        if (obj is List<object> points && points.Count > 0)
                        {
                            foreach (Dictionary<string, object> pointEntry in points)
                            {
                                var normal = (Dictionary<string, object>)pointEntry["normal"];
                                var point = (Dictionary<string, object>)pointEntry["point"];

                                var adjustedPoint = paste.QuaternionRotation * new Vector3(Convert.ToSingle(point["x"]),
                                    Convert.ToSingle(point["y"]),
                                    Convert.ToSingle(point["z"])) + paste.StartPos;

                                adjustedPoint.y += paste.HeightAdj;

                                float slack = 0f;
                                if (pointEntry.TryGetValue("slack", out obj))
                                    slack = Convert.ToSingle(obj);

                                stringLights.points.Add(new StringLights.PointEntry
                                {
                                    normal = new Vector3(Convert.ToSingle(normal["x"]), Convert.ToSingle(normal["y"]),
                                        Convert.ToSingle(normal["z"])),
                                    point = adjustedPoint,
                                    slack = slack
                                });
                            }
                        }
                    }
                }

                if (entity.Is(out Chandelier chandelier))
                {
                    if (data.TryGetValue("chandelierLength", out obj))
                    {
                        chandelier.SetChandelierLength(Convert.ToSingle(obj));
                    }
                }

                if (entity.Is(out OrientableLight orientableLight))
                {
                    if (data.TryGetValue("pitchAmount", out obj))
                    {
                        orientableLight.pitchAmount = Convert.ToSingle(obj);
                    }

                    if (data.TryGetValue("yawAmount", out obj))
                    {
                        orientableLight.yawAmount = Convert.ToSingle(obj);
                    }
                }

                if (entity.Is(out Mannequin mannequin))
                {
                    if (data.TryGetValue("poseIndex", out obj))
                    {
                        mannequin.PoseIndex = ToInt32OrDefault(obj);
                    }
                }

                if (entity.Is(out PartyBalloon partyBalloon))
                {
                    if (data.TryGetValue("balloonText", out obj) && obj != null)
                    {
                        var balloonText = obj.ToString();
                        if (!String.IsNullOrEmpty(balloonText))
                            partyBalloon.BalloonText = balloonText;
                    }

                    if (data.TryGetValue("balloonColour", out obj))
                        partyBalloon.BalloonColour = DeserializeColor(obj);

                    if (data.TryGetValue("textColour", out obj))
                        partyBalloon.TextColour = DeserializeColor(obj);
                }

                BoatBuildingStation boatBuildingStation = entity as BoatBuildingStation;
                if (boatBuildingStation != null)
                {
                    if (paste.Ownership && entity.OwnerID.IsSteamId())
                    {
                        boatBuildingStation.bbsOwnerID = entity.OwnerID;
                        boatBuildingStation.AddToBBSList(entity.OwnerID);
                    }

                    boatBuildingStation.Netting.gameObject.SetActive(false);
                    boatBuildingStation.SetFlagLocal(BaseEntity.Flags.On, false);
                    boatBuildingStation.StopAutoCloseInvoke();
                }

                if (entity.Is(out SleepingBag sleepingBag) && data.TryGetValue("sleepingbag", out obj) && obj is Dictionary<string, object> bagData)
                {
                    if (bagData.TryGetValue("niceName", out obj))
                    {
                        sleepingBag.niceName = obj?.ToString();
                    }

                    ulong deployerUserID = bagData.TryGetValue("deployerUserID", out obj) && ulong.TryParse(obj?.ToString(), out ulong savedOwnerId) ? savedOwnerId : sleepingBag.deployerUserID;

                    if (sleepingBag.deployerUserID != deployerUserID)
                    {
                        ulong oldUser = sleepingBag.deployerUserID;
                        sleepingBag.deployerUserID = deployerUserID;
                        SleepingBag.OnBagChangedOwnership(sleepingBag, oldUser);
                    }

                    if (bagData.TryGetValue("isPublic", out obj))
                    {
                        sleepingBag.SetPublic(Convert.ToBoolean(obj));
                    }
                }

                if (entity.Is(out BuildingPrivlidge cupboard))
                {
                    List<ulong> authorizedPlayers = new();

                    if (data.TryGetValue("cupboard", out obj) && obj is Dictionary<string, object> cupboardData && cupboardData.TryGetValue("authorizedPlayers", out obj) && obj is List<object> rawAuthorizedPlayers)
                    {
                        for (int index = 0; index < rawAuthorizedPlayers.Count; index++)
                        {
                            authorizedPlayers.Add(Convert.ToUInt64(rawAuthorizedPlayers[index]));
                        }
                    }

                    if (paste.Auth && paste.BasePlayer != null && !authorizedPlayers.Contains(paste.BasePlayer.userID))
                        authorizedPlayers.Add(paste.BasePlayer.userID);

                    foreach (var userid in authorizedPlayers)
                    {
                        cupboard.authorizedPlayers.Add(userid);
                    }

                    cupboard.SendNetworkUpdate();
                }

                if (entity.Is(out SteeringWheel steeringWheel))
                {
                    if (data.TryGetValue("steeringWheel", out obj) && obj is Dictionary<string, object> dict)
                    {
                        if (dict.TryGetValue("authorizedPlayers", out obj) && obj is List<object> rawAuth &&
                            steeringWheel.Privilege != null)
                        {
                            for (var i = 0; i < rawAuth.Count; i++)
                                steeringWheel.Privilege.authorizedPlayers.Add(Convert.ToUInt64(rawAuth[i]));
                        }

                        if (dict.TryGetValue("code", out obj) && obj is string code && steeringWheel.BoatLock != null &&
                            steeringWheel.BoatLock.IsValidLockCode(code))
                        {
                            steeringWheel.BoatLock.Code = code;
                        }
                    }
                }

                if (entity.Is(out TinCanAlarm tinCanAlarm))
                {
                    if (data.TryGetValue("endPoint", out obj))
                    {
                        var endPoint = (Dictionary<string, object>)obj;
                        var adjustedEndPoint = paste.QuaternionRotation * new Vector3(Convert.ToSingle(endPoint["x"]),
                            Convert.ToSingle(endPoint["y"]),
                            Convert.ToSingle(endPoint["z"])) + paste.StartPos;

                        adjustedEndPoint.y += paste.HeightAdj;

                        tinCanAlarm.endPoint = adjustedEndPoint;
                        tinCanAlarm.SendNetworkUpdate();
                    }
                }

                if (entity.Is(out CCTV_RC cctvRc) &&
                    data.TryGetValue("cctv", out obj) &&
                    obj is Dictionary<string, object> cctv)
                {
                    if (cctv.TryGetValue("yaw", out obj)) cctvRc.yawAmount = Convert.ToSingle(obj);
                    if (cctv.TryGetValue("pitch", out obj)) cctvRc.pitchAmount = Convert.ToSingle(obj);
                    if (cctv.TryGetValue("rcIdentifier", out obj)) cctvRc.rcIdentifier = obj?.ToString();
                    cctvRc.SendNetworkUpdate();
                }

                if (entity.Is(out ComputerStation computerStation) &&
                    data.TryGetValue("bookmarks", out obj) &&
                    obj is string bookmarks)
                {
                    foreach (string text in bookmarks.Split(ComputerStation.BookmarkSplit, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (ComputerStation.IsValidIdentifier(text))
                        {
                            computerStation.controlBookmarks.Add(text);
                        }
                    }
                }

                if (entity.Is(out WantedPoster wantedPoster) &&
                    data.TryGetValue("wantedPoster", out obj) &&
                    obj is Dictionary<string, object> poster)
                {
                    if (poster.TryGetValue("playerId", out obj)) wantedPoster.playerId = Convert.ToUInt64(obj);
                    if (poster.TryGetValue("playerName", out obj)) wantedPoster.playerName = obj?.ToString();
                    wantedPoster.SendNetworkUpdate();
                }

                if (entity.Is(out HeadEntity headEntity) && data.ContainsKey("currentTrophyData"))
                {
                    if (headEntity.CurrentTrophyData == null)
                        headEntity.CurrentTrophyData = Pool.Get<ProtoBuf.HeadData>();

                    RestoreHeadData(data, headEntity.CurrentTrophyData);
                }

                if (entity.Is(out HuntingTrophy huntingTrophy) && data.ContainsKey("currentTrophyData"))
                {
                    if (huntingTrophy.CurrentTrophyData == null)
                        huntingTrophy.CurrentTrophyData = Pool.Get<ProtoBuf.HeadData>();

                    RestoreHeadData(data, huntingTrophy.CurrentTrophyData);
                }

                if (entity.Is(out PagerEntity pagerEntity) && data.TryGetValue("frequency", out obj))
                {
                    pagerEntity.ChangeFrequency(ToInt32OrDefault(obj));
                    pagerEntity.SendNetworkUpdate();
                }

                if (entity.Is(out RFTimedExplosive rfTimedExplosive) && data.TryGetValue("frequency", out obj))
                {
                    rfTimedExplosive.SetFrequency(ToInt32OrDefault(obj));
                    rfTimedExplosive.SetFuse(0f);
                }

                if (entity.Is(out Cassette cassette))
                {
                    RestoreCassette(data, cassette);
                }

                if (entity.Is(out MobileInventoryEntity mobileInventoryEntity))
                {
                    using var update = mobileInventoryEntity.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                    update.Set(MobileInventoryEntity.Ringing, false);
                }

                if (entity.Is(out Elevator elevator))
                {
                    RestoreElevator(elevator, data, paste);
                }

                if (entity.Is(out WeaponRack weaponRack))
                {
                    RestoreWeaponRack(weaponRack, data);
                }

                if (entity is global::CommandBlock commandBlock)
                {
                    if (data.TryGetValue("currentCommand", out obj) &&
                        obj is string rawCommand && !string.IsNullOrEmpty(rawCommand))
                        commandBlock.currentCommand = rawCommand;

                    if (data.TryGetValue("lastPlayerID", out obj))
                        commandBlock.lastPlayerID = Convert.ToUInt64(obj);
                }

                if (entity.TryGetComponent<PhoneController>(out var phoneController) &&
                    data.TryGetValue("savedVoicemail", out obj) &&
                    obj is List<object> savedVoicemail)
                {
                    // Requires the cassette item subentity to exist, delay processing until the very end
                    paste.FinalProcessingActions.Add(() =>
                    {
                        if (savedVoicemail != null)
                        {
                            foreach (Dictionary<string, object> voicemail in savedVoicemail)
                            {
                                if (voicemail != null)
                                {
                                    byte[] audioData = Convert.FromBase64String(voicemail["audio"].ToString());
                                    phoneController.SaveVoicemail(audioData, voicemail["userName"].ToString());
                                }
                            }
                        }
                    });
                }

                if (entity.Is(out VendingMachine vendingMachine))
                {
                    RestoreVendingMachine(vendingMachine, data, paste);
                }

                if (entity.Is(out RidableHorse ridableHorse))
                {
                    if (data.TryGetValue("currentBreedIndex", out obj))
                        ridableHorse.SetBreed(ToInt32OrDefault(obj));

                    if (data.TryGetValue("towingEntityId", out obj))
                    {
                        ulong oldTowingEntityId = Convert.ToUInt64(obj);

                        // Try to find the ITowing entity after everything has pasted
                        paste.FinalProcessingActions.Add(() =>
                        {
                            if (TryGetPastedEntity(paste, oldTowingEntityId, out BaseEntity towingEntity) && towingEntity is ITowing towing)
                            {
                                // Restore towing state.
                                towingEntity.SetFlagLocal(BaseEntity.Flags.Reserved14, false);
                                ridableHorse.towingEntityId = towingEntity.net.ID;
                                ridableHorse.towableEntity = towing;
                                ridableHorse.TowAttach();
                                ridableHorse.SendNetworkUpdate();
                            }
                        });
                    }
                }

                if (entity.Is(out ConstructableEntity constructableEntity))
                {
                    if (data.TryGetValue("currentMaterials", out obj))
                    {
                        if (obj is List<object> currentMaterials)
                        {
                            for (var i = 0; i < currentMaterials.Count; i++)
                                constructableEntity.currentMaterials[i] = ToInt32OrDefault(currentMaterials[i]);
                        }
                    }

                    if (data.TryGetValue("health", out obj))
                        constructableEntity.SetHealth(Mathf.Min(Convert.ToSingle(obj), constructableEntity.MaxHealth()));

                    constructableEntity.SendNetworkUpdate();
                    constructableEntity.UpdateState();
                }

                if (entity.Is(out PlanterBox planterBox))
                {
                    if (data.TryGetValue("soilSaturation", out obj))
                        planterBox.soilSaturation = ToInt32OrDefault(obj);
                }

                if (entity is ISplashable)
                {
                    paste.FinalProcessingActions.Add(() =>
                    {
                        if (!entity.IsValid() || entity.IsDestroyed)
                            return;

                        Sprinkler.SplashableGrid.DeregisterEntity(entity);
                        Sprinkler.SplashableGrid.RegisterEntity(entity);
                    });
                }

                if (entity.Is(out GrowableEntity growableEntity))
                {
                    if (data.TryGetValue("genes", out obj))
                        GrowableGeneEncoding.DecodeIntToGenes(ToInt32OrDefault(obj), growableEntity.Genes);

                    if (data.TryGetValue("previousGenes", out obj))
                        GrowableGeneEncoding.DecodeIntToPreviousGenes(ToInt32OrDefault(obj), growableEntity.Genes);

                    if (data.TryGetValue("totalAge", out obj))
                        growableEntity.Age = Convert.ToSingle(obj);

                    if (data.TryGetValue("stageAge", out obj))
                        growableEntity.stageAge = Convert.ToSingle(obj);

                    if (data.TryGetValue("yieldFraction", out obj))
                        growableEntity.Yield = Convert.ToSingle(obj);

                    if (data.TryGetValue("yieldPool", out obj))
                        growableEntity.yieldPool = Convert.ToSingle(obj);

                    if (data.TryGetValue("fertilized", out obj))
                        growableEntity.Fertilized = Convert.ToBoolean(obj);

                    if (data.TryGetValue("state", out obj))
                        growableEntity.ChangeState((PlantProperties.State)Convert.ToSingle(obj), false, true);
                }

                if (entity.Is(out IOEntity ioEntity) && ioEntity.net != null && !ioEntity.IsDestroyed)
                {
                    Dictionary<string, object> ioData = data.TryGetValue("IOEntity", out obj) && obj is Dictionary<string, object> savedIoData
                        ? savedIoData
                        : new();

                    ioData["entity"] = ioEntity;
                    ioData["newId"] = ioEntity.net.ID.Value;

                    if (ioData.TryGetValue("oldID", out obj))
                    {
                        var oldId = Convert.ToUInt64(obj);
                        paste.EntityLookup.TryAdd(oldId, ioData); // duplicate ID from outdated copy
                    }
                }

                bool skipFlags = entity is Anchor && parent is PlayerBoat;
                if (!skipFlags && savedFlags != null)
                {
                    using (var update = entity.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags))
                    {
                        foreach ((string name, object value) in savedFlags)
                        {
                            if (!Enum.TryParse(name, out BaseEntity.Flags flag))
                            {
                                continue;
                            }

                            if (pastedDoor != null && (flag == BaseEntity.Flags.Busy || restoredDoorAnimationFlagsBeforeSpawn && (flag == BaseEntity.Flags.Open || flag == Door.ReverseOpen)))
                            {
                                continue;
                            }

                            update.Set(flag, Convert.ToBoolean(value));
                        }
                    }
                }

                // If the on flag was saved, toggle it off and enter edit mode so it can be properly triggered on
                if (boatBuildingStation != null && boatBuildingStation.IsOn())
                {
                    boatBuildingStation.SetFlagLocal(BaseEntity.Flags.On, false);
                    boatBuildingStation.EnterEditMode();
                }

                if (data.TryGetValue("boomBox", out obj) && obj is Dictionary<string, object> boomBoxData && entity.TryGetComponent(out BoomBox boomBox))
                {
                    RestoreBoomBox(boomBoxData, boomBox);
                    if (boomBox.IsOn())
                    {
                        boomBox.ServerTogglePlay(false);
                        paste.FinalProcessingActions.Add(() =>
                        {
                            if (boomBox == null)
                                return;

                            boomBox.Invoke(() =>
                            {
                                if (!boomBox.baseEntity.IsValid() || boomBox.baseEntity.IsDestroyed)
                                    return;

                                if (!boomBox.HasFlag(BoomBox.HasCassette))
                                    boomBox.baseEntity.ClientRPC(RpcTarget.NetworkGroup("OnRadioIPChanged"), boomBox.CurrentRadioIp);

                                boomBox.ServerTogglePlay(true);

                                foreach (var connectedSpeaker in paste.ConnectedSpeakers)
                                {
                                    if (connectedSpeaker.IsValid() && !connectedSpeaker.IsDestroyed)
                                    {
                                        connectedSpeaker.SetFlagLocal(IOEntity.Flag_HasPower, false);
                                        connectedSpeaker.SetFlagLocal(IOEntity.Flag_HasPower, true);
                                    }
                                }
                            }, 1f);
                        });
                    }
                }

                if (entity.Is(out ConnectedSpeaker connectedSpeaker) && connectedSpeaker.HasFlag(BaseEntity.Flags.Reserved8))
                    paste.ConnectedSpeakers.Add(connectedSpeaker);

                if (entity.Is(out IndustrialCrafter industrialCrafter))
                {
                    industrialCrafter.SetFlagLocal(IndustrialCrafter.Crafting, false);
                }

                if (!RestoreChildren(entity, data, paste))
                {
                    return;
                }

                if (entity.Is(out PhotoFrame photoFrame) && data.TryGetValue("photoEntity", out obj) && TryGetPastedEntity(paste, Convert.ToUInt64(obj), out BaseEntity baseEntity) && baseEntity.net.ID.IsValid)
                {
                    photoFrame._photoEntity.uid = baseEntity.net.ID;
                }

                if (entity.Is(out BaseOven baseOven) && baseOven.IsOn())
                {
                    baseOven.StartCooking();
                }

                if (entity.Is(out IndustrialStorageAdaptor industrialStorageAdaptor))
                {
                    paste.IndustrialStorageAdaptors.Add(industrialStorageAdaptor);
                }

                if (entity.Is(out MixingTable mixingTable) && mixingTable.IsOn())
                {
                    List<Item> orderedContainerItems = mixingTable.GetOrderedContainerItems(mixingTable.inventory, out var itemsAreContiguous);
                    mixingTable.currentRecipe = RecipeDictionary.GetMatchingRecipeAndQuantity(mixingTable.Recipes, orderedContainerItems, out var quantity);
                    mixingTable.currentQuantity = quantity;
                    if (mixingTable.currentRecipe == null || !itemsAreContiguous)
                    {
                        mixingTable.StopMixing();
                        return;
                    }
                    mixingTable.RemainingMixTime = mixingTable.currentRecipe.MixingDuration * mixingTable.currentQuantity;
                    mixingTable.TotalMixTime = mixingTable.RemainingMixTime;
                    if (mixingTable.RemainingMixTime == 0.0)
                    {
                        mixingTable.ProduceItem(mixingTable.currentRecipe, mixingTable.currentQuantity);
                    }
                    else
                    {
                        mixingTable.InvokeRepeating(mixingTable.TickMix, 1f, 1f);
                    }
                }

                if (entity.Is(out FarmableAnimal farmableAnimal))
                {
                    if (data.TryGetValue("hunger", out obj))
                        farmableAnimal.AnimalHunger = Convert.ToSingle(obj);
                    if (data.TryGetValue("thirst", out obj))
                        farmableAnimal.AnimalThirst = Convert.ToSingle(obj);
                    if (data.TryGetValue("love", out obj))
                        farmableAnimal.AnimalLove = Convert.ToSingle(obj);
                    if (data.TryGetValue("sunlight", out obj))
                        farmableAnimal.AnimalSunlight = Convert.ToSingle(obj);
                    if (data.TryGetValue("animalName", out obj))
                        farmableAnimal.AnimalName = (string)obj;

                    if (parent.Is(out ChickenCoop chickenCoopParent) &&
                        chickenCoopParent.ChickenPrefab.resourceID == entity.prefabID)
                    {
                        ChickenCoop.AnimalStatus animalStatus = new();
                        animalStatus.SpawnedAnimal.Set(farmableAnimal);
                        chickenCoopParent.Animals.Add(animalStatus);
                    }

                    farmableAnimal.SendNetworkUpdate();
                }

                if (entity.Is(out ChickenCoop chickenCoop))
                {
                    if (entity.HasFlag(BaseEntity.Flags.Reserved1) &&
                        data.TryGetValue("timeUntilHatches", out obj) &&
                        obj is List<object> timeUntilHatches &&
                        timeUntilHatches.Count > 0)
                    {
                        for (var i = 0; i < timeUntilHatches.Count; i++)
                        {
                            chickenCoop.Animals.Add(new()
                            {
                                TimeUntilHatch = (TimeUntil)Convert.ToSingle(timeUntilHatches[i])
                            });
                        }

                        if (!chickenCoop.IsInvoking(chickenCoop.CheckEggHatchState))
                            chickenCoop.InvokeRepeating(chickenCoop.CheckEggHatchState, 10f, 10f);

                        chickenCoop.SendNetworkUpdate();
                    }
                }

                paste.PastedEntities.Add(entity);
                paste.CallbackSpawned?.Invoke(entity);
            }

            private void RestoreElevator(Elevator elevator, Dictionary<string, object> data, RaidPaste paste)
            {
                if (data.TryGetValue("Floor", out object obj))
                {
                    elevator.Floor = ToInt32OrDefault(obj);
                }

                if (data.TryGetValue("liftEntityID", out obj) && Convert.ToUInt64(obj) is var oldLiftId && oldLiftId != 0)
                {
                    paste.FinalProcessingActions.Add(() =>
                    {
                        if (!TryGetPastedEntity(paste, oldLiftId, out ElevatorLift lift))
                        {
                            return;
                        }

                        if (elevator.liftEntity.TryGet(true, out var existing) &&
                            existing.IsValid() &&
                            !existing.IsDestroyed)
                        {
                            existing.Kill();
                        }

                        elevator.liftEntity.Set(lift);
                        lift.SetOwnerElevator(elevator);
                    });
                    return;
                }

                if (paste.Version > new VersionNumber(4, 2, 7))
                {
                    return;
                }

                paste.FinalProcessingActions.Add(() =>
                {
                    if (!elevator.IsValid() || elevator.IsDestroyed)
                    {
                        return;
                    }

                    if (GetLegacyElevatorRestoreStates(paste).TryGetValue(elevator, out var state))
                    {
                        bool update = false;

                        if (elevator.Floor != state.Floor)
                        {
                            elevator.Floor = state.Floor;
                            update = true;
                        }

                        if (elevator.IsTop != state.IsTop)
                        {
                            elevator.SetFlagLocal(BaseEntity.Flags.Reserved1, state.IsTop);
                            update = true;
                        }

                        if (update)
                        {
                            elevator.SendNetworkUpdate();
                        }

                        if (!state.IsTop)
                        {
                            return;
                        }
                    }
                    else if (!elevator.IsTop)
                    {
                        return;
                    }

                    ElevatorLift lift = FindLegacyElevatorLiftForTopFloor(paste, elevator);
                    if (lift == null)
                    {
                        return;
                    }

                    if (elevator.liftEntity.TryGet(true, out var generatedLift) &&
                        generatedLift.IsValid() &&
                        !generatedLift.IsDestroyed &&
                        generatedLift != lift)
                    {
                        generatedLift.Kill();
                    }

                    elevator.liftEntity.Set(lift);
                    lift.SetOwnerElevator(elevator);
                });
            }

            private static void RestoreWeaponRack(
                WeaponRack weaponRack,
                Dictionary<string, object> data)
            {
                if (weaponRack.inventory == null ||
                    weaponRack.gridSlots == null ||
                    !data.TryGetValue("gridSlots", out object obj) ||
                    obj is not List<object> gridSlots)
                {
                    return;
                }

                foreach (object rawSlot in gridSlots)
                {
                    if (rawSlot is not Dictionary<string, object> slotData)
                    {
                        continue;
                    }

                    int gridSlotIndex = slotData.TryGetValue("GridSlotIndex", out obj)
                        ? ToInt32OrDefault(obj)
                        : 0;
                    int inventoryIndex = slotData.TryGetValue("InventoryIndex", out obj)
                        ? ToInt32OrDefault(obj)
                        : -1;
                    int rotation = slotData.TryGetValue("Rotation", out obj)
                        ? ToInt32OrDefault(obj)
                        : 0;

                    if (inventoryIndex < 0 || inventoryIndex >= weaponRack.gridSlots.Length)
                    {
                        continue;
                    }

                    Item item = weaponRack.inventory.GetSlot(inventoryIndex);
                    var slot = weaponRack.gridSlots[inventoryIndex];
                    if (item == null || slot == null)
                    {
                        continue;
                    }

                    slot.SetItem(item, item.info, gridSlotIndex, rotation);
                    weaponRack.SetGridCellContents(slot, false);
                }
            }

            private bool RestoreChildren(BaseEntity entity, Dictionary<string, object> data, RaidPaste paste)
            {
                if (!data.TryGetValue("children", out object obj) ||
                    obj is not List<object> children)
                {
                    return true;
                }

                foreach (object child in children)
                {
                    if (child is Dictionary<string, object> childData && !TryPasteEntity(childData, paste, entity))
                    {
                        return false;
                    }
                }

                if (!entity.Is(out PlayerBoat playerBoat) || !paste.PlayerBoats.TryGetValue(playerBoat, out PlayerBoatData playerBoatData))
                {
                    return true;
                }

                paste.PlayerBoats.Remove(playerBoat);
                BoatBuildingStation.GetBoatBlocksOBBExtents(playerBoatData.Blocks, playerBoat.transform.forward, out _, out Vector3 halfExtents, out _);

                if (data.TryGetValue("lastEditLocalPos", out obj) && obj is Dictionary<string, object> positionData && TryReadVector3(positionData, out Vector3 lastEditLocalPosition))
                {
                    playerBoat.lastEditLocalPos = lastEditLocalPosition;
                }

                if (data.TryGetValue("lastEditLocalRot", out obj) && obj is Dictionary<string, object> rotationData && TryReadVector3(rotationData, out Vector3 lastEditLocalRotation))
                {
                    playerBoat.lastEditLocalRot = lastEditLocalRotation;
                }

                HashSet<Anchor> anchorsToLower = new();
                foreach (object child in children)
                {
                    if (child is not Dictionary<string, object> childData || !childData.TryGetValue("oldID", out obj) || !TryGetPastedEntity(paste, Convert.ToUInt64(obj), out Anchor anchor))
                    {
                        continue;
                    }

                    if (childData.TryGetValue("flags", out obj) && obj is Dictionary<string, object> flags && flags.TryGetValue(nameof(BaseEntity.Flags.Reserved3), out obj) && Convert.ToBoolean(obj))
                    {
                        anchorsToLower.Add(anchor);
                    }
                }

                playerBoat.Init(playerBoatData.Blocks, playerBoatData.Deployables, halfExtents, false);
                paste.FinalProcessingActions.Add(() =>
                {
                    foreach (Anchor anchor in anchorsToLower)
                    {
                        anchor.LowerAnchor(null, true);
                    }
                });

                return true;
            }

            private void RestoreSignData(BaseEntity entity, Dictionary<string, object> data)
            {
                if (!data.TryGetValue("sign", out object obj) ||
                    obj is not Dictionary<string, object> signData)
                {
                    return;
                }

                if (entity is ISignage signage)
                {
                    if (signData.ContainsKey("amount") || signData.ContainsKey("texture") || signData.ContainsKey("texture0"))
                    {
                        int amount = signData.TryGetValue("amount", out obj) &&
                            int.TryParse(obj.ToString(), out int parsedAmount)
                                ? parsedAmount
                                : 1;
                        uint[] textureIds = new uint[amount];

                        for (int index = 0; index < amount; index++)
                        {
                            string textureKey = amount == 1 && signData.ContainsKey("texture")
                                ? "texture"
                                : $"texture{index}";

                            if (signData.TryGetValue(textureKey, out obj))
                            {
                                byte[] imageBytes = FixSignage(signage, Convert.FromBase64String(obj.ToString()));
                                textureIds[index] = FileStorage.server.Store(imageBytes, signage.FileType, entity.net.ID);
                            }
                        }

                        signage.SetTextureCRCs(textureIds);
                    }

                    if (signage is Signage sign && signData.TryGetValue("locked", out obj) && Convert.ToBoolean(obj))
                    {
                        using var update = sign.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.Locked, true);
                    }

                    return;
                }

                if (entity.Is(out PhotoEntity photo))
                {
                    if (!signData.TryGetValue("amount", out obj) ||
                        !int.TryParse(obj.ToString(), out int amount))
                    {
                        return;
                    }

                    for (int index = 0; index < amount; index++)
                    {
                        if (signData.TryGetValue($"texture{index}", out obj))
                        {
                            photo.SetImageData(0UL, Convert.FromBase64String(obj.ToString()));
                        }
                    }

                    return;
                }

                if (entity.Is(out SignContent content))
                {
                    if (!signData.TryGetValue("amount", out obj) ||
                        !int.TryParse(obj.ToString(), out int amount))
                    {
                        return;
                    }

                    uint[] textureIds = new uint[amount];
                    for (int index = 0; index < amount; index++)
                    {
                        if (signData.TryGetValue($"texture{index}", out obj))
                        {
                            byte[] imageBytes = Convert.FromBase64String(obj.ToString());
                            textureIds[index] = FileStorage.server.Store(imageBytes, content.FileType, entity.net.ID);
                        }
                    }

                    content.textureIDs = textureIds;
                    return;
                }

                if (!entity.Is(out PaintedItemStorageEntity paintedStorage) ||
                    !signData.TryGetValue("amount", out obj) ||
                    !int.TryParse(obj.ToString(), out int paintedAmount))
                {
                    return;
                }

                for (int index = 0; index < paintedAmount; index++)
                {
                    if (!signData.TryGetValue($"texture{index}", out obj))
                    {
                        continue;
                    }

                    byte[] imageBytes = Convert.FromBase64String(obj.ToString());
                    if (ImageProcessing.IsValidPNG(imageBytes, 512, 512))
                    {
                        paintedStorage._currentImageCrc = FileStorage.server.Store(
                            imageBytes,
                            FileStorage.Type.png,
                            paintedStorage.net.ID);
                    }
                }
            }

            private void RestoreVendingMachine(VendingMachine vm, Dictionary<string, object> data, RaidPaste paste)
            {
                if (!data.TryGetValue("vendingmachine", out object obj) || obj is not Dictionary<string, object> vendingData)
                {
                    return;
                }

                if (vendingData.TryGetValue("shopName", out obj))
                {
                    vm.shopName = obj?.ToString();
                }

                if (vendingData.TryGetValue("isBroadcasting", out obj))
                {
                    vm.SetFlagLocal(BaseEntity.Flags.Reserved4, Convert.ToBoolean(obj));
                }

                if (vendingData.TryGetValue("sellOrders", out obj) && obj is List<object> sellOrders)
                {
                    foreach (object rawOrder in sellOrders)
                    {
                        if (rawOrder is not Dictionary<string, object> order)
                        {
                            continue;
                        }

                        RestoreSellOrder(vm, order, paste.IsItemReplace);
                    }
                }

                vm.FullUpdate();
            }

            private void RestoreSellOrder(VendingMachine vendingMachine, Dictionary<string, object> data, bool replaceItemIds)
            {
                if (!data.ContainsKey("inStock"))
                {
                    data["inStock"] = 0;
                    data["currencyIsBP"] = false;
                    data["itemToSellIsBP"] = false;
                }

                int itemToSellId = ToInt32OrDefault(data["itemToSellID"]);
                int currencyId = ToInt32OrDefault(data["currencyID"]);

                if (replaceItemIds)
                {
                    itemToSellId = GetItemId(itemToSellId);
                    currencyId = GetItemId(currencyId);
                }

                var sellOrder = new ProtoBuf.VendingMachine.SellOrder
                {
                    ShouldPool = false,
                    itemToSellID = itemToSellId,
                    itemToSellAmount = ToInt32OrDefault(data["itemToSellAmount"]),
                    currencyID = currencyId,
                    currencyAmountPerItem = ToInt32OrDefault(data["currencyAmountPerItem"]),
                    inStock = ToInt32OrDefault(data["inStock"]),
                    currencyIsBP = Convert.ToBoolean(data["currencyIsBP"]),
                    itemToSellIsBP = Convert.ToBoolean(data["itemToSellIsBP"])
                };

                object obj;
                if (data.TryGetValue("itemCondition", out obj))
                    sellOrder.itemCondition = Convert.ToSingle(obj);

                if (data.TryGetValue("itemConditionMax", out obj))
                    sellOrder.itemConditionMax = Convert.ToSingle(obj);

                if (data.TryGetValue("instanceData", out obj))
                    sellOrder.instanceData = ToInt32OrDefault(obj);

                if (data.TryGetValue("totalAttachmentSlots", out obj))
                    sellOrder.totalAttachmentSlots = ToInt32OrDefault(obj);

                if (data.TryGetValue("priceMultiplier", out obj))
                    sellOrder.priceMultiplier = Convert.ToSingle(obj);

                if (data.TryGetValue("ammoType", out obj))
                    sellOrder.ammoType = ToInt32OrDefault(obj);

                if (data.TryGetValue("ammoCount", out obj))
                    sellOrder.ammoCount = ToInt32OrDefault(obj);

                if (data.TryGetValue("receivedQuantityMultiplier", out obj))
                    sellOrder.receivedQuantityMultiplier = Convert.ToSingle(obj);

                if (data.TryGetValue("sellSkinId", out obj))
                    sellOrder.sellSkinId = Convert.ToUInt64(obj);

                if (data.TryGetValue("costSkinId", out obj))
                    sellOrder.costSkinId = Convert.ToUInt64(obj);

                if (data.TryGetValue("attachmentsList", out obj) &&
                    obj is IEnumerable<object> attachments)
                {
                    sellOrder.attachmentsList = new();
                    foreach (object attachment in attachments)
                    {
                        sellOrder.attachmentsList.Add(ToInt32OrDefault(attachment));
                    }
                }

                vendingMachine.sellOrders.sellOrders.Add(sellOrder);
            }

            private bool ShouldInvokeOnDeployed(BaseEntity entity)
            {
                if (entity is CustomDoorManipulator or AutoTurret or GrowableEntity or Signage or BoatBuildingBlock)
                {
                    return false;
                }

                if (entity.Is(out SimpleBuildingBlock sbb) && sbb.variants.IsNullOrEmpty())
                {
                    return false;
                }

                return true;
            }

            private void RestoreIoEntity(Dictionary<string, object> ioData, RaidPaste paste)
            {
                if (!TryGetPastedEntity(ioData, out IOEntity ioEntity))
                    return;

                if (ioEntity.Is(out Sprinkler sprinkler))
                {
                    paste.FinalProcessingActions.Add(() =>
                    {
                        if (!sprinkler.IsValid() || sprinkler.IsDestroyed || !sprinkler.IsOn())
                            return;

                        // Clear the on flag and rerun sprinkler startup so DoSplash is invoked without clearing fuel state
                        sprinkler.SetFlagLocal(BaseEntity.Flags.On, false);
                        sprinkler.UpdateFromInput(sprinkler.ConsumptionAmount(), 0);
                    });
                }

                List<object> inputs = ioData.TryGetValue("inputs", out var obj) && obj is List<object> savedInputs ? savedInputs : null;

                if (ioEntity.Is(out ElectricalBranch electricalBranch) && ioData.TryGetValue("branchAmount", out obj))
                {
                    electricalBranch.branchAmount = ToInt32OrDefault(obj);
                }

                if (ioEntity.Is(out PowerCounter counter))
                {
                    if (ioData.TryGetValue("targetNumber", out obj))
                        counter.targetCounterNumber = ToInt32OrDefault(obj);

                    counter.SetCounterNumber(ioData.TryGetValue("counterNumber", out obj) ? ToInt32OrDefault(obj) : 0);
                }

                if (ioEntity.Is(out TimerSwitch timerSwitch) && ioData.TryGetValue("timerLength", out obj))
                {
                    timerSwitch.timerLength = Convert.ToSingle(obj);
                    if (timerSwitch.IsOn())
                    {
                        using (var update = timerSwitch.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                        {
                            update.Set(BaseEntity.Flags.On, false);
                        }
                        timerSwitch.SwitchPressed();
                    }
                }

                if (ioEntity.Is(out RFBroadcaster rfBroadcaster) && ioData.TryGetValue("frequency", out obj))
                {
                    int newFrequency = ToInt32OrDefault(obj);
                    if (ioEntity.IsPowered())
                        RFManager.AddBroadcaster(newFrequency, rfBroadcaster);
                    rfBroadcaster.frequency = newFrequency;
                    rfBroadcaster.MarkDirty();
                }

                if (ioEntity.Is(out RFReceiver rfReceiver) && ioData.TryGetValue("frequency", out obj))
                {
                    int newFrequency = ToInt32OrDefault(obj);
                    RFManager.AddListener(newFrequency, rfReceiver);
                    rfReceiver.frequency = newFrequency;
                    rfReceiver.MarkDirty();
                }

                if (ioEntity.Is(out SeismicSensor seismicSensor) && ioData.TryGetValue("range", out obj))
                {
                    seismicSensor.SetRange(ToInt32OrDefault(obj));
                }

                if (ioEntity.Is(out CustomDoorManipulator doorManipulator))
                {
                    if (doorManipulator.GetParentEntity().Is(out Door door))
                    {
                        doorManipulator.SetTargetDoor(door);
                    }
                    else
                    {
                        paste.FinalProcessingActions.Add(() => AssignNearestDoor(doorManipulator));
                    }
                }

                if (ioEntity.Is(out IndustrialConveyor conveyor) && ioData.TryGetValue("industrialconveyormode", out obj))
                {
                    conveyor.mode = (IndustrialConveyor.ConveyorMode)ToInt32OrDefault(obj);

                    if (ioData.TryGetValue("industrialconveyorfilteritems", out obj))
                    {
                        conveyor.filterItems = DeserializeConveyorFilter(obj?.ToString());
                    }
                    conveyor.SendNetworkUpdate();
                }

                if (ioEntity.Is(out AudioVisualisationEntity audioVisual))
                {
                    if (ioData.TryGetValue("colour", out obj))
                        audioVisual.currentColour = (AudioVisualisationEntity.LightColour)ToInt32OrDefault(obj);
                    if (ioData.TryGetValue("volumeSensitivity", out obj))
                        audioVisual.currentVolumeSensitivity = (AudioVisualisationEntity.VolumeSensitivity)ToInt32OrDefault(obj);
                    if (ioData.TryGetValue("speed", out obj))
                        audioVisual.currentSpeed = (AudioVisualisationEntity.Speed)ToInt32OrDefault(obj);
                    if (ioData.TryGetValue("gradient", out obj))
                        audioVisual.currentGradient = ToInt32OrDefault(obj);
                    if (ioData.TryGetValue("connectedTo", out obj))
                    {
                        var oldId = Convert.ToUInt64(obj);
                        if (TryGetPastedNetworkId(paste, oldId, out NetworkableId networkId))
                        {
                            audioVisual.connectedTo.uid = networkId;
                        }
                    }
                }

                if (ioEntity.Is(out DigitalClock digitalClock))
                {
                    if (ioData.TryGetValue("muted", out obj))
                    {
                        digitalClock.muted = Convert.ToBoolean(obj);
                    }

                    if (ioData.TryGetValue("alarms", out obj) && obj is List<object> alarms)
                    {
                        foreach (Dictionary<string, object> alarm in alarms)
                        {
                            if (alarm != null && alarm.TryGetValue("time", out obj))
                            {
                                string time = obj.ToString();
                                if (alarm.TryGetValue("active", out obj))
                                {
                                    digitalClock.alarms.Add(new DigitalClock.Alarm(TimeSpan.Parse(time), Convert.ToBoolean(obj)));
                                }
                            }
                        }
                    }

                    digitalClock.MarkDirty();
                    digitalClock.SendNetworkUpdate();
                }

                if (inputs != null && inputs.Count > 0)
                {
                    for (var index = 0; index < inputs.Count; index++)
                    {
                        if (inputs[index] is not Dictionary<string, object> input ||
                            !input.TryGetValue("connectedID", out obj))
                            continue;

                        var oldId = Convert.ToUInt64(obj);

                        if (index >= ioEntity.inputs.Length ||
                            !TryGetPastedNetworkId(paste, oldId, out NetworkableId networkId))
                        {
                            continue;
                        }

                        ioEntity.inputs[index] ??= new();
                        ioEntity.inputs[index].connectedTo.entityRef.uid = networkId;
                    }
                }

                List<object> outputs = ioData.TryGetValue("outputs", out obj) && obj is List<object> savedOutputs
                    ? savedOutputs
                    : null;

                if (outputs != null && outputs.Count > 0)
                {
                    int outputCount = Math.Min(outputs.Count, ioEntity.outputs.Length);
                    for (var index = 0; index < outputCount; index++)
                    {
                        if (outputs[index] is not Dictionary<string, object> output ||
                            !output.TryGetValue("connectedID", out obj))
                        {
                            continue;
                        }

                        ulong oldId = Convert.ToUInt64(obj);

                        if (oldId != 0 && paste.EntityLookup.TryGetValue(oldId, out var ioConnection))
                        {
                            if (ioEntity.outputs[index] == null)
                                ioEntity.outputs[index] = new();

                            if (ioConnection.ContainsKey("newId") && TryGetPastedEntity(ioConnection, out IOEntity ioEntity2))
                            {
                                var ioOutput = ioEntity.outputs[index];
                                int connectedToSlot = output.TryGetValue("connectedToSlot", out obj) ? ToInt32OrDefault(obj) : -1;
                                if (connectedToSlot < 0 || connectedToSlot >= ioEntity2.inputs.Length)
                                    continue;

                                var ioInput = ioEntity2.inputs[connectedToSlot];
                                ioInput ??= ioEntity2.inputs[connectedToSlot] = new();

                                ioOutput.connectedTo = new();
                                ioOutput.connectedTo.Set(ioEntity2);
                                ioOutput.connectedToSlot = connectedToSlot;
                                if (output.TryGetValue("type", out obj)) ioOutput.type = (IOEntity.IOType)ToInt32OrDefault(obj);
                                if (output.TryGetValue("niceName", out obj)) ioOutput.niceName = obj as string;
                                ioOutput.connectedTo.Init();

                                ioInput.connectedTo = new();
                                ioInput.connectedTo.Set(ioEntity);
                                ioInput.connectedToSlot = index;
                                ioInput.connectedTo.Init();

                                ioOutput.worldSpaceLineEndRotation =
                                    ioEntity2.transform.TransformDirection(ioInput.handleDirection);
                                ioOutput.originPosition = ioEntity.transform.position;
                                ioOutput.originRotation = ioEntity.transform.rotation.eulerAngles;

                                if (output.TryGetValue("wireColour", out obj))
                                {
                                    var color = (WireTool.WireColour)ToInt32OrDefault(obj);
                                    ioInput.wireColour = color;
                                    ioOutput.wireColour = color;
                                }

                                if (output.TryGetValue("linePoints", out obj) && obj is List<object> linePoints)
                                {
                                    ioOutput.linePoints = new Vector3[linePoints.Count];
                                    for (var i = 0; i < linePoints.Count; i++)
                                    {
                                        if (linePoints[i] is Dictionary<string, object> linePoint &&
                                            TryReadVector3(linePoint, out Vector3 point))
                                        {
                                            ioOutput.linePoints[i] = point;
                                        }
                                    }
                                }

                                if (output.TryGetValue("slackLevels", out obj) && obj is List<object> slackLevels)
                                {
                                    ioOutput.slackLevels = new float[slackLevels.Count];
                                    for (var i = 0; i < slackLevels.Count; i++)
                                    {
                                        ioOutput.slackLevels[i] = Convert.ToSingle(slackLevels[i]);
                                    }
                                }
                                else
                                {
                                    int linePointCount = ioOutput.linePoints?.Length ?? 0;
                                    ioOutput.slackLevels = new float[linePointCount];
                                    for (var i = 0; i < linePointCount; i++)
                                        ioOutput.slackLevels[i] = 0f;
                                }

                                if (output.TryGetValue("lineAnchors", out obj) && obj is List<object> lineAnchors)
                                {
                                    ioOutput.lineAnchors = new IOEntity.LineAnchor[lineAnchors.Count];
                                    for (var i = 0; i < lineAnchors.Count; i++)
                                    {
                                        if (lineAnchors[i] is not Dictionary<string, object> lineAnchor ||
                                            !lineAnchor.TryGetValue("position", out obj) ||
                                            obj is not Dictionary<string, object> position ||
                                            !TryReadVector3(position, out Vector3 anchorPosition) ||
                                            !lineAnchor.TryGetValue("entityRefID", out obj) ||
                                            !TryGetPastedEntity(paste, Convert.ToUInt64(obj), out Door door))
                                        {
                                            continue;
                                        }

                                        ioOutput.lineAnchors[i] = new()
                                        {
                                            entityRef = new(door.net.ID),
                                            position = anchorPosition,
                                            index = lineAnchor.TryGetValue("index", out obj) ? ToInt32OrDefault(obj) : 0,
                                            boneName = lineAnchor.TryGetValue("boneName", out obj) ? obj as string : null
                                        };
                                    }
                                }

                                ioEntity2.SendNetworkUpdate();
                            }
                        }
                    }

                    if (paste.LegacyIoPositionChecks != null)
                        paste.LegacyIoPositionChecks.Add(ioEntity);
                }

                if (ioEntity.Is(out ElectricBattery electricBattery))
                {
                    if (ioData.TryGetValue("rustWattSeconds", out obj))
                        electricBattery.SetCharge(Convert.ToSingle(obj));

                    if (electricBattery.IsOn())
                    {
                        electricBattery.SetPassthroughOn(false);
                        electricBattery.CheckDischarge();
                    }
                }

                ioEntity.MarkDirty();
                ioEntity.UpdateOutputs();
                ioEntity.SendNetworkUpdate();
                ioEntity.RefreshIndustrialPreventBuilding();
            }

            private void SetItemSubEntity(RaidPaste paste, Item item, ulong oldId)
            {
                if (item != null && TryGetPastedEntity(paste, oldId, out BaseEntity subEntity) && subEntity.net.ID.IsValid)
                {
                    InitializeItemInstanceData(item);
                    item.instanceData.subEntity = subEntity.net.ID;
                }
            }

            private void InitializeItemInstanceData(Item item)
            {
                if (item.instanceData == null)
                {
                    item.instanceData = new()
                    {
                        ShouldPool = false
                    };
                }
            }

            private static int ToInt32OrDefault(object value) // handle boxed values without throwing Convert.ToInt32
            {
                return value switch
                {
                    int number => number,
                    long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
                    string text when int.TryParse(text, out int number) => number,
                    _ => 0
                };
            }

            private void RestoreInventory(RaidPaste paste, Dictionary<string, object> data, BaseEntity entity, ItemContainer inventory)
            {
                if (!data.TryGetValue("items", out object obj) || obj is not List<object> items)
                {
                    return;
                }

                foreach (object itemDefinition in items)
                {
                    if (itemDefinition is not Dictionary<string, object> item)
                    {
                        continue;
                    }

                    int itemId = item.TryGetValue("id", out obj) ? ToInt32OrDefault(obj) : 0;
                    ulong itemSkin = item.TryGetValue("skinid", out obj) ? Convert.ToUInt64(obj) : 0;

                    int itemAmount = item.TryGetValue("amount", out obj) ? ToInt32OrDefault(obj) : 0;
                    var dataInt = item.TryGetValue("dataInt", out obj) ? ToInt32OrDefault(obj) : 0;
                    var dataFloat = item.TryGetValue("dataFloat", out obj) ? Convert.ToSingle(obj) : 0f;

                    if (itemId == 0 || itemAmount == 0)
                        continue;

                    if (entity.Is(out GrowableEntity growableEntity))
                    {
                        if (data.TryGetValue("genes", out obj) && obj is int genesData && genesData > 0)
                        {
                            GrowableGeneEncoding.DecodeIntToGenes(genesData, growableEntity.Genes);
                        }

                        if (data.TryGetValue("hasParent", out obj) && obj is bool isParented && isParented)
                        {
                            if (Physics.Raycast(growableEntity.transform.position, Vector3.down, out var hitInfo, .5f, Rust.Layers.DefaultDeployVolumeCheck))
                            {
                                var parentEntity = hitInfo.GetEntity();
                                if (parentEntity != null)
                                {
                                    growableEntity.SetParent(parentEntity, true);
                                }
                            }
                        }
                    }

                    if (paste.IsItemReplace)
                        itemId = GetItemId(itemId);

                    var targetPos = -1;
                    if (item.TryGetValue("position", out obj))
                        targetPos = ToInt32OrDefault(obj);

                    if (entity.Is(out BaseOven ov) && ov.visualFood && targetPos >= ov._inputSlotIndex &&
                        targetPos < ov._inputSlotIndex + ov.inputSlots &&
                        ItemManager.FindItemDefinition(itemId)?.ItemModCookable == null) // rpc error fix
                        continue;

                    var i = ItemManager.CreateByItemID(itemId, itemAmount, itemSkin);

                    if (i != null)
                    {
                        if (i.hasCondition && i.info != null && !i.info.HasComponent<ItemModFoodSpoiling>()) // ItemModFoodSpoiling handles condition using dataFloat, setting condition here can result in broken stacks and spoiled time remaining
                        {
                            if (item.TryGetValue("maxCondition", out obj))
                            {
                                float maxCondition = Convert.ToSingle(obj);
                                if (maxCondition > 0f)
                                    i.maxCondition = maxCondition;
                            }

                            if (item.TryGetValue("condition", out obj))
                                i.condition = Convert.ToSingle(obj);
                        }

                        if (item.TryGetValue("text", out obj) && obj is string str1 && !string.IsNullOrEmpty(str1))
                            i.text = str1;

                        if (item.TryGetValue("name", out obj) && obj is string str2 && !string.IsNullOrEmpty(str2))
                            i.name = str2;

                        if (item.TryGetValue("fuel", out obj))
                        {
                            float fuel = Convert.ToSingle(obj);
                            if (fuel > 0)
                                i.fuel = fuel;
                        }

                        if (item.TryGetValue("blueprintTarget", out obj))
                        {
                            var blueprintTarget = ToInt32OrDefault(obj);

                            if (paste.IsItemReplace)
                                blueprintTarget = GetItemId(blueprintTarget);

                            if (blueprintTarget != 0)
                                i.blueprintTarget = blueprintTarget;
                        }

                        if (item.TryGetValue("blueprintAmount", out obj))
                        {
                            var blueprintAmount = ToInt32OrDefault(obj);
                            if (blueprintAmount != 0)
                                i.blueprintAmount = blueprintAmount;
                        }

                        if (dataInt != 0)
                        {
                            InitializeItemInstanceData(i);
                            i.instanceData.dataInt = dataInt;
                        }

                        if (dataFloat != 0f)
                        {
                            InitializeItemInstanceData(i);
                            i.instanceData.dataFloat = dataFloat;
                        }

                        if (item.TryGetValue("IsOn", out obj))
                        {
                            i.SetFlag(global::Item.Flag.IsOn, Convert.ToBoolean(obj));
                        }

                        if (item.TryGetValue("subEntity", out obj))
                        {
                            // Needs to be processed after all of the children are spawned
                            var oldId = Convert.ToUInt64(obj);
                            if (oldId != 0)
                                paste.ItemsWithSubEntity.TryAdd(oldId, i);
                        }

                        if (item.TryGetValue("armorSlotCapacity", out obj))
                        {
                            var armorSlotCapacity = ToInt32OrDefault(obj);
                            if (armorSlotCapacity > 0 && i.info != null && i.info.TryGetComponent<ItemModContainerArmorSlot>(out var armorSlot))
                                armorSlot.CreateAtCapacity(armorSlotCapacity, i);
                        }
                        else if (i.info != null && i.info.isWearable &&
                                 item.TryGetValue("items", out obj) &&
                                 obj is List<object> { Count: > 0 } slotItems &&
                                 i.info.TryGetComponent<ItemModContainerArmorSlot>(out var armorSlot))
                        {
                            armorSlot.CreateAtCapacity(slotItems.Count, i);
                        }

                        if (item.TryGetValue("ownershipShares", out obj) &&
                            obj is List<object> { Count: > 0 } ownershipShares)
                        {
                            i.InitializeItemOwnership();
                            if (i.ownershipShares != null)
                            {
                                i.ownershipShares.Clear();
                                for (var num = 0; num < ownershipShares.Count; num++)
                                {
                                    if (ownershipShares[num] is not Dictionary<string, object> ownershipShare)
                                    {
                                        continue;
                                    }

                                    ItemOwnershipShare itemOwnershipShare = new();

                                    if (ownershipShare.TryGetValue("username", out obj) && obj is string username)
                                        itemOwnershipShare.username = username;
                                    if (ownershipShare.TryGetValue("reason", out obj) && obj is string reason)
                                        itemOwnershipShare.reason = reason;
                                    if (ownershipShare.TryGetValue("amount", out obj))
                                        itemOwnershipShare.amount = ToInt32OrDefault(obj);

                                    if (itemOwnershipShare.IsValid())
                                        i.ownershipShares.Add(itemOwnershipShare);
                                }
                            }
                        }

                        if (item.ContainsKey("items"))
                        {
                            RestoreInventory(paste, item, null, i.contents);
                        }

                        var heldent = i.GetHeldEntity();

                        if (heldent != null)
                        {
                            if (item.TryGetValue("magazine", out obj))
                            {
                                var projectiles = heldent.GetComponent<BaseProjectile>();

                                if (projectiles != null)
                                {
                                    if (obj is not Dictionary<string, object> magazine || magazine.Count == 0)
                                    {
                                        continue;
                                    }

                                    KeyValuePair<string, object> ammunition = magazine.ElementAt(0);
                                    if (!int.TryParse(ammunition.Key, out int ammoType) || !int.TryParse(ammunition.Value?.ToString(), out int ammoAmount))
                                    {
                                        continue;
                                    }

                                    if (paste.IsItemReplace)
                                        ammoType = GetItemId(ammoType);

                                    projectiles.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammoType);
                                    projectiles.primaryMagazine.contents = ammoAmount;
                                }
                            }

                            if (item.TryGetValue("children", out obj) && obj is List<object> children)
                            {
                                TryPrepareChildrenData(item, out _);

                                foreach (object child in children)
                                {
                                    if (child is not Dictionary<string, object> childData)
                                    {
                                        continue;
                                    }

                                    if (!TryPasteEntity(childData, paste, heldent))
                                    {
                                        return;
                                    }
                                }
                            }

                            if (item.TryGetValue("boomBox", out obj) &&
                                obj is Dictionary<string, object> boomBoxData &&
                                heldent.Is(out HeldBoomBox heldBoomBox))
                            {
                                RestoreBoomBox(boomBoxData, heldBoomBox.BoxController);
                            }
                        }

                        var heldEntity = i.GetHeldEntity();
                        if (heldEntity.Is(out Detonator detonator))
                        {
                            detonator.frequency = dataInt;
                            if (detonator.IsOn())
                                RFManager.AddBroadcaster(detonator.frequency, detonator);
                        }

                        i.position = targetPos;

                        if (entity.Is(out WaterCatcher waterCatcher))
                        {
                            var info = i.info; // avoid concurrency issues
                            var amt = i.amount;
                            waterCatcher.Invoke(() => {
                                if (waterCatcher != null && !waterCatcher.IsDestroyed)
                                    waterCatcher.inventory.AddItem(info, amt);
                            }, 1f);
                        }
                        else
                        {
                            inventory.Insert(i);
                        }
                    }
                }
            }

            private void RestoreBoomBox(Dictionary<string, object> data, BoomBox boomBox)
            {
                if (boomBox != null)
                {
                    if (data.TryGetValue("radioIp", out object obj) && obj is string radioIp)
                    {
                        if (!string.IsNullOrEmpty(radioIp) && BoomBox.IsStationValid(radioIp))
                            boomBox.CurrentRadioIp = radioIp;
                    }
                    if (data.TryGetValue("radioBy", out obj))
                        boomBox.AssignedRadioBy = Convert.ToUInt64(obj);
                }
            }

            private void RestoreCassette(Dictionary<string, object> data, Cassette cassette)
            {
                if (data.TryGetValue("audio", out object obj))
                {
                    var contentCRC = FileStorage.server.Store(Convert.FromBase64String(obj.ToString()), FileStorage.Type.ogg, cassette.net.ID);
                    cassette.SetAudioId(contentCRC, 0);
                }
            }

            private void RestoreHeadData(Dictionary<string, object> data, ProtoBuf.HeadData headData)
            {
                if (data.TryGetValue("currentTrophyData", out object obj) &&
                    obj is Dictionary<string, object> headDataData)
                {
                    var clothing = Pool.Get<List<int>>();
                    if (headDataData.TryGetValue("clothing", out obj) && obj is List<object> clothingData)
                    {
                        foreach (var clothingItem in clothingData)
                        {
                            clothing.Add(ToInt32OrDefault(clothingItem));
                        }
                    }
                    if (clothing.Count == 0)
                        Pool.FreeUnmanaged(ref clothing);

                    if (headDataData.TryGetValue("entitySource", out obj)) headData.entitySource = Convert.ToUInt32(obj);
                    if (headDataData.TryGetValue("playerName", out obj)) headData.playerName = obj as string;
                    if (headDataData.TryGetValue("playerId", out obj)) headData.playerId = Convert.ToUInt64(obj);
                    headData.clothing = clothing;
                    if (headDataData.TryGetValue("count", out obj)) headData.count = Convert.ToUInt32(obj);
                    if (headDataData.TryGetValue("horseBreed", out obj)) headData.horseBreed = ToInt32OrDefault(obj);
                }
            }

            private static bool TryPrepareChildrenData(Dictionary<string, object> entity, out string error)
            {
                error = null;

                if (entity == null || !entity.TryGetValue("children", out object obj))
                {
                    return true;
                }

                if (obj is not List<object> children)
                {
                    error = "children data is invalid";
                    return false;
                }

                for (int index = 0; index < children.Count; index++)
                {
                    if (children[index] is not Dictionary<string, object> child)
                    {
                        error = $"child entry {index} is not an object";
                        return false;
                    }

                    if (!child.TryGetValue("pos", out obj) || obj is not Dictionary<string, object> position || !TryReadVector3(position, out Vector3 localPosition))
                    {
                        error = $"child entry {index} has invalid position data";
                        return false;
                    }

                    if (!child.TryGetValue("rot", out obj) || obj is not Dictionary<string, object> rotation || !TryReadVector3(rotation, out Vector3 localRotation))
                    {
                        error = $"child entry {index} has invalid rotation data";
                        return false;
                    }

                    child["position"] = localPosition;
                    child["rotation"] = Quaternion.Euler(localRotation);

                    if (!TryPrepareChildrenData(child, out error))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool TryReadVector3(Dictionary<string, object> data, out Vector3 value)
            {
                value = default;

                if (data == null ||
                    !data.TryGetValue("x", out object obj) || !TryReadSingle(obj, out float x) ||
                    !data.TryGetValue("y", out obj) || !TryReadSingle(obj, out float y) ||
                    !data.TryGetValue("z", out obj) || !TryReadSingle(obj, out float z))
                {
                    return false;
                }

                value = new(x, y, z);
                return true;
            }

            private static bool TryReadSingle(object value, out float result)
            {
                (float number, bool success) = value switch
                {
                    float parsed => (parsed, true),
                    double parsed => ((float)parsed, true),
                    decimal parsed => ((float)parsed, true),
                    int parsed => (parsed, true),
                    long parsed => (parsed, true),
                    string text when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) => (parsed, true),
                    _ => (0f, false)
                };

                result = number;
                return success;
            }

            private void RestoreLock(BaseEntity entity, Dictionary<string, object> data, RaidPaste paste)
            {
                if (!data.TryGetValue("code", out object obj))
                {
                    return;
                }

                if (entity.TryGetComponent<CodeLock>(out var codeLock))
                {
                    if (obj is not string code || string.IsNullOrEmpty(code))
                    {
                        return;
                    }

                    codeLock.code = code;
                    codeLock.hasCode = true;

                    if (paste.Auth && paste.BasePlayer != null)
                        codeLock.whitelistPlayers.Add(paste.BasePlayer.userID);

                    if (data.TryGetValue("whitelistPlayers", out obj) && obj is List<object> whitelistPlayers)
                    {
                        foreach (object userid in whitelistPlayers)
                        {
                            codeLock.whitelistPlayers.Add(Convert.ToUInt64(userid));
                        }
                    }

                    if (data.TryGetValue("guestCode", out obj) && obj is string guestCode)
                    {
                        codeLock.guestCode = guestCode;
                        codeLock.hasGuestCode = true;

                        if (data.TryGetValue("guestPlayers", out obj) && obj is List<object> guestPlayers)
                        {
                            foreach (object userid in guestPlayers)
                            {
                                codeLock.guestPlayers.Add(Convert.ToUInt64(userid));
                            }
                        }
                    }

                    using var update = codeLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                    update.Set(BaseEntity.Flags.Locked, true);
                    return;
                }

                if (entity.TryGetComponent<KeyLock>(out var keyLock))
                {
                    int code = ToInt32OrDefault(obj);

                    if (data.TryGetValue("firstKeyCreated", out obj))
                    {
                        keyLock.keyCode = code;
                        keyLock.firstKeyCreated = Convert.ToBoolean(obj);
                    }
                    else if ((code & 0x80) != 0)
                    {
                        keyLock.keyCode = code & 0x7F;
                        keyLock.firstKeyCreated = true;
                        using var update = keyLock.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate);
                        update.Set(BaseEntity.Flags.Locked, true);
                    }

                    if (paste.Ownership && data.TryGetValue("ownerId", out obj))
                    {
                        keyLock.OwnerID = Convert.ToUInt64(obj);
                    }
                }
            }

            private void RestoreLegacySlots(BaseEntity parent, Dictionary<string, object> data, RaidPaste paste)
            {
                object obj;

                foreach (BaseEntity.Slot slot in _checkSlots)
                {
                    string slotName = slot.ToString().ToLowerInvariant();

                    if (!parent.HasSlot(slot) || !data.TryGetValue(slotName, out obj) || obj is not Dictionary<string, object> slotData)
                        continue;

                    if (!slotData.TryGetValue("prefabname", out obj) || obj is not string prefabName)
                        continue;

                    BaseEntity slotEntity = GameManager.server.CreateEntity(GetPrefabName(prefabName), Vector3.zero);
                    if (slotEntity == null || slotEntity.IsDestroyed)
                        continue;

                    slotEntity.gameObject.Identity();
                    slotEntity.SetParent(parent, slotName);
                    slotEntity.OnDeployed(parent, null, _emptyItem);
                    if (!paste.EnableSaving)
                    {
                        slotEntity.EnableSaving(false);
                    }
                    paste.Raid?.DestroyGroundCheck(slotEntity);
                    slotEntity.Spawn();
                    parent.SetSlot(slot, slotEntity);
                    paste.PastedEntities.Add(slotEntity);

                    if (slotName == "lock" && slotData.ContainsKey("code"))
                        RestoreLock(slotEntity, slotData, paste);

                    paste.CallbackSpawned?.Invoke(parent);
                }
            }

            private List<IndustrialConveyor.ItemFilter> DeserializeConveyorFilter(string value)
            {
                List<IndustrialConveyor.ItemFilter> filters = new();

                if (string.IsNullOrWhiteSpace(value))
                {
                    return filters;
                }

                try
                {
                    string serialized = Encoding.ASCII.GetString(Facepunch.Utility.Compression.Uncompress(Convert.FromBase64String(value)));
                    foreach (string entry in serialized.Split('\\'))
                    {
                        if (string.IsNullOrEmpty(entry))
                        {
                            continue;
                        }

                        string[] fields = entry.Split('/');
                        if (fields.Length != 6)
                        {
                            continue;
                        }

                        var filter = new IndustrialConveyor.ItemFilter
                        {
                            MaxAmountInOutput = ToInt32OrDefault(fields[1]),
                            BufferAmount = ToInt32OrDefault(fields[2]),
                            MinAmountInInput = ToInt32OrDefault(fields[3]),
                            IsBlueprint = Convert.ToBoolean(fields[5])
                        };

                        if (fields[0] != "-1") filter.TargetItem = ItemManager.FindItemDefinition(ToInt32OrDefault(fields[0]));
                        if (fields[4] != "-1") filter.TargetCategory = (ItemCategory)ToInt32OrDefault(fields[4]);
                        filters.Add(filter);
                    }
                }
                catch (Exception ex)
                {
                    Puts("Failed to deserialize an industrial conveyor filter: {0}", ex.Message);
                }

                return filters;
            }

            private List<ProtoBuf.PatternFirework.Star> DeserializeStarPattern(string value)
            {
                List<ProtoBuf.PatternFirework.Star> stars = new();

                if (string.IsNullOrWhiteSpace(value))
                {
                    return stars;
                }

                try
                {
                    string serialized = Encoding.ASCII.GetString(Facepunch.Utility.Compression.Uncompress(Convert.FromBase64String(value)));
                    foreach (string entry in serialized.Split('\\'))
                    {
                        if (string.IsNullOrEmpty(entry))
                        {
                            continue;
                        }

                        string[] fields = entry.Split('/');
                        if (fields.Length != 6)
                        {
                            continue;
                        }

                        stars.Add(new()
                        {
                            position = new(Convert.ToSingle(fields[0]), Convert.ToSingle(fields[1])),
                            color = new(Convert.ToSingle(fields[2]), Convert.ToSingle(fields[3]), Convert.ToSingle(fields[4]), Convert.ToSingle(fields[5]))
                        });
                    }
                }
                catch (Exception ex)
                {
                    Puts("Failed to deserialize a firework pattern: {0}", ex.Message);
                }

                return stars;
            }

            private UnityEngine.Color DeserializeColor(object rawData)
            {
                if (rawData is not Dictionary<string, object> data)
                    return UnityEngine.Color.white;

                object obj;
                float r = data.TryGetValue("r", out obj) && TryReadSingle(obj, out float value) ? value : 1f;
                float g = data.TryGetValue("g", out obj) && TryReadSingle(obj, out value) ? value : 1f;
                float b = data.TryGetValue("b", out obj) && TryReadSingle(obj, out value) ? value : 1f;
                float a = data.TryGetValue("a", out obj) && TryReadSingle(obj, out value) ? value : 1f;

                return new(r, g, b, a);
            }

            private static bool HasGrade(BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
            {
                foreach (var constructionGrade in block.blockDefinition.grades)
                {
                    var baseGrade = constructionGrade.gradeBase;
                    if (baseGrade.type == grade && baseGrade.skin == skin)
                        return true;
                }

                return false;
            }
        }

    }
}
