using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace PveModeHarmony
{
    /// <summary>Per-event configuration snapshot, ported from Oxide PveMode EventConfig.</summary>
    public class EventConfig
    {
        public float Damage { get; set; }
        public Dictionary<string, float> ScaleDamage { get; set; } = new Dictionary<string, float>();
        public bool LootCrate { get; set; }
        public bool HackCrate { get; set; }
        public bool LootNpc { get; set; }
        public bool DamageNpc { get; set; }
        public bool DamageTank { get; set; }
        public bool DamageHelicopter { get; set; }
        public bool DamageTurret { get; set; }
        public bool TargetNpc { get; set; }
        public bool TargetTank { get; set; }
        public bool TargetHelicopter { get; set; }
        public bool TargetTurret { get; set; }
        public bool CanEnter { get; set; }
        public bool CanEnterCooldownPlayer { get; set; }
        public int TimeExitOwner { get; set; }
        public int AlertTime { get; set; }
        public bool RestoreUponDeath { get; set; }
        public double CooldownOwner { get; set; }
        public int Darkening { get; set; }

        public static EventConfig FromDictionary(Dictionary<string, object> config)
        {
            EventConfig ec = new EventConfig();
            if (config == null) return ec;
            ec.Damage = ReadFloat(config, "Damage");
            ec.ScaleDamage = ReadScaleDamage(config, "ScaleDamage");
            ec.LootCrate = ReadBool(config, "LootCrate");
            ec.HackCrate = ReadBool(config, "HackCrate");
            ec.LootNpc = ReadBool(config, "LootNpc");
            ec.DamageNpc = ReadBool(config, "DamageNpc");
            ec.DamageTank = ReadBool(config, "DamageTank");
            ec.DamageHelicopter = ReadBool(config, "DamageHelicopter");
            ec.DamageTurret = ReadBool(config, "DamageTurret");
            ec.TargetNpc = ReadBool(config, "TargetNpc");
            ec.TargetTank = ReadBool(config, "TargetTank");
            ec.TargetHelicopter = ReadBool(config, "TargetHelicopter");
            ec.TargetTurret = ReadBool(config, "TargetTurret");
            ec.CanEnter = ReadBool(config, "CanEnter");
            ec.CanEnterCooldownPlayer = ReadBool(config, "CanEnterCooldownPlayer");
            ec.TimeExitOwner = ReadInt(config, "TimeExitOwner");
            ec.AlertTime = ReadInt(config, "AlertTime");
            ec.RestoreUponDeath = ReadBool(config, "RestoreUponDeath");
            ec.CooldownOwner = ReadDouble(config, "CooldownOwner");
            ec.Darkening = ReadInt(config, "Darkening");
            return ec;
        }

        private static bool ReadBool(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) && Convert.ToBoolean(v);
        private static int ReadInt(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? Convert.ToInt32(v) : 0;
        private static float ReadFloat(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? Convert.ToSingle(v) : 0f;
        private static double ReadDouble(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? Convert.ToDouble(v) : 0d;

        private static Dictionary<string, float> ReadScaleDamage(Dictionary<string, object> d, string k)
        {
            if (!d.TryGetValue(k, out object v) || v == null) return new Dictionary<string, float>();
            if (v is Dictionary<string, float> direct) return direct;
            if (v is Dictionary<string, object> boxed)
            {
                Dictionary<string, float> result = new Dictionary<string, float>();
                foreach (KeyValuePair<string, object> kv in boxed) result[kv.Key] = Convert.ToSingle(kv.Value);
                return result;
            }
            return new Dictionary<string, float>();
        }
    }

    /// <summary>Per-player cooldown record, ported from Oxide PveMode PlayerData.</summary>
    public class PlayerData
    {
        public ulong SteamId;
        public Dictionary<string, double> LastTime = new Dictionary<string, double>();
    }

    /// <summary>Tracks damage dealt by players to a standalone (non-event) scientist NPC.</summary>
    public class ControllerScientist : MonoBehaviour
    {
        private int _timeLastDamage;

        public ulong CrateId { get; set; }

        public Dictionary<ulong, float> Players { get; } = new Dictionary<ulong, float>();

        private void OnDestroy() => CancelInvoke(nameof(IncrementTime));

        public void AddDamage(BasePlayer attacker, float damage)
        {
            if (attacker == null) return;
            if (Players.ContainsKey(attacker.userID)) Players[attacker.userID] += damage;
            else Players.Add(attacker.userID, damage);
            if (_timeLastDamage == 0) InvokeRepeating(nameof(IncrementTime), 1f, 1f);
            _timeLastDamage = PveModeManager.Config?.TimeLastDamage ?? 300;
        }

        private void IncrementTime()
        {
            _timeLastDamage--;
            if (_timeLastDamage != 0) return;
            Players.Clear();
            CancelInvoke(nameof(IncrementTime));
        }

        public ulong GetWinner()
        {
            ulong winner = 0;
            float best = float.MinValue;
            foreach (KeyValuePair<ulong, float> kv in Players)
            {
                if (kv.Value > best) { best = kv.Value; winner = kv.Key; }
            }
            return winner;
        }
    }

    /// <summary>Zone controller for a single PveMode event, ported from Oxide PveMode ControllerEvent.</summary>
    public class ControllerEvent : MonoBehaviour
    {
        public string ShortName;
        public EventConfig Config;
        public float Radius;

        public HashSet<ulong> Crates = new HashSet<ulong>();
        public HashSet<ulong> Backpacks { get; } = new HashSet<ulong>();
        public HashSet<ulong> Npc = new HashSet<ulong>();
        public HashSet<ulong> Tanks = new HashSet<ulong>();
        public HashSet<ulong> Helicopters = new HashSet<ulong>();
        public HashSet<ulong> Turrets = new HashSet<ulong>();

        public Dictionary<ulong, float> Players { get; } = new Dictionary<ulong, float>();
        public ulong Owner { get; private set; }
        private int _timerExitOwner;
        public HashSet<ulong> Owners = new HashSet<ulong>();

        private SphereCollider _sphereCollider;
        public HashSet<BasePlayer> InsidePlayers { get; } = new HashSet<BasePlayer>();
        private readonly HashSet<SphereEntity> _spheres = new HashSet<SphereEntity>();

        private void OnDestroy()
        {
            CancelInvoke(nameof(IncrementTime));
            if (_sphereCollider != null) Destroy(_sphereCollider);
            foreach (SphereEntity sphere in _spheres)
                if (sphere != null && !sphere.IsDestroyed) sphere.Kill();
        }

        public void InitSphere()
        {
            gameObject.layer = 3;
            _sphereCollider = gameObject.AddComponent<SphereCollider>();
            _sphereCollider.isTrigger = true;
            _sphereCollider.radius = Radius;
            CreateDome();
        }

        private void CreateDome()
        {
            if (Config.Darkening <= 0) return;
            try
            {
                for (int i = 0; i < Config.Darkening; i++)
                {
                    SphereEntity sphere = GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", transform.position) as SphereEntity;
                    if (sphere == null) continue;
                    sphere.currentRadius = Radius * 2;
                    sphere.lerpSpeed = 0f;
                    sphere.enableSaving = false;
                    sphere.Spawn();
                    _spheres.Add(sphere);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] CreateDome failed: " + ex.Message);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            BasePlayer player = other.GetComponentInParent<BasePlayer>();
            if (PveModeManager.IsRealPlayer(player))
            {
                if (player.IsAdmin && PveModeManager.Config.IgnoreAdmin) return;
                bool canTimeOwner = PveModeManager.CanTimeOwner(ShortName, player.userID, Config.CooldownOwner);
                if (!Config.CanEnterCooldownPlayer && !canTimeOwner) { KickOutPlayer(player); return; }
                if (PveModeManager.Config.NoEnterAnotherOwner && PveModeManager.Events.Any(x => x.ShortName != ShortName && x.Owners.Contains(player.userID))) { KickOutPlayer(player); return; }

                if (Owner == 0)
                {
                    if (!canTimeOwner)
                    {
                        double remaining = PveModeManager.GetOwnerCooldownRemaining(player.userID, ShortName, Config.CooldownOwner);
                        PveModeManager.SendChat(player, PveModeLang.Get("PlayerHasCooldownEnter", PveModeManager.GetTimeFormat(remaining)));
                    }
                    InsidePlayers.Add(player);
                }
                else
                {
                    if (Config.CanEnter || PveModeManager.IsTeam(player, Owner))
                    {
                        InsidePlayers.Add(player);
                        if (_timerExitOwner > 0 && PveModeManager.IsTeam(player, Owner) && canTimeOwner)
                        {
                            CancelInvoke(nameof(IncrementTime));
                            _timerExitOwner = 0;
                            if (Owner != player.userID) SetOwner(player);
                        }
                    }
                    else KickOutPlayer(player);
                }
            }
            else CheckJetpack(other);
        }

        private void OnTriggerExit(Collider other)
        {
            BasePlayer player = other.GetComponentInParent<BasePlayer>();
            if (PveModeManager.IsRealPlayer(player)) ExitPlayer(player);
        }

        public void ExitPlayer(BasePlayer player)
        {
            InsidePlayers.Remove(player);
            if (player.userID != Owner) return;

            BasePlayer friend = InsidePlayers.FirstOrDefault(x => PveModeManager.IsTeam(x, Owner) && PveModeManager.CanTimeOwner(ShortName, x.userID, Config.CooldownOwner));
            if (friend != null)
            {
                PveModeManager.SendChat(player, PveModeLang.Get("ChangeOwnerEventToFriend", friend.displayName));
                SetOwner(friend);
            }
            else
            {
                _timerExitOwner = Config.TimeExitOwner;
                InvokeRepeating(nameof(IncrementTime), 1f, 1f);
                PveModeManager.SendChat(player, PveModeLang.Get("TimerStartEvent", PveModeManager.GetTimeFormat(_timerExitOwner)));
            }
        }

        public void SetOwner(BasePlayer player)
        {
            if (!PveModeManager.IsRealPlayer(player)) return;
            Owner = player.userID;
            if (!Owners.Contains(player.userID)) Owners.Add(player.userID);
            PveModeManager.SendChat(player, PveModeLang.Get("YouOwnerEvent"));
            PveModeManager.NotifyOwnerCallbacks(ShortName, "set", player);
            Debug.Log("[PveMode] " + player.displayName + " [" + player.userID + "] became owner of zone " + ShortName);
        }

        public void ClearOwner(BasePlayer player)
        {
            Owner = 0;
            PveModeManager.NotifyOwnerCallbacks(ShortName, "clear", player);
            if (player == null) return;
            PveModeManager.SendChat(player, PveModeLang.Get("YouNonOwnerEvent"));
            Debug.Log("[PveMode] " + player.displayName + " [" + player.userID + "] became non-owner of zone " + ShortName);
        }

        private void IncrementTime()
        {
            _timerExitOwner--;
            if (Config.AlertTime > 0 && _timerExitOwner == Config.AlertTime)
            {
                BasePlayer player = BasePlayer.FindByID(Owner);
                if (player != null) PveModeManager.SendChat(player, PveModeLang.Get("AlertTimerEvent", PveModeManager.GetTimeFormat(Config.AlertTime)));
            }
            if (_timerExitOwner == 0)
            {
                CancelInvoke(nameof(IncrementTime));
                ClearOwner(BasePlayer.FindByID(Owner));
            }
        }

        public void AddDamage(BasePlayer player, float damage)
        {
            if (player == null) return;
            if (PveModeManager.Config.NoEnterAnotherOwner && PveModeManager.Events.Any(x => x.ShortName != ShortName && x.Owners.Contains(player.userID))) return;

            if (Players.ContainsKey(player.userID)) Players[player.userID] += damage;
            else Players.Add(player.userID, damage);

            if (!PveModeManager.CanTimeOwner(ShortName, player.userID, Config.CooldownOwner)) return;
            if (!InsidePlayers.Contains(player)) return;

            if (Players[player.userID] >= Config.Damage)
            {
                SetOwner(player);
                Players.Clear();
                if (!Config.CanEnter)
                {
                    foreach (BasePlayer insidePlayer in InsidePlayers.ToList())
                        if (!PveModeManager.IsTeam(insidePlayer, Owner))
                            KickOutPlayer(insidePlayer);
                }
            }
        }

        public void KickOutPlayer(BasePlayer player)
        {
            if (player == null) return;
            try
            {
                if (player.isMounted)
                {
                    BaseMountable baseMountable = player.GetMounted();
                    BaseVehicle vehicle = baseMountable != null ? baseMountable.VehicleParent() : null;
                    if (vehicle != null)
                    {
                        vehicle.transform.rotation = Quaternion.Euler(vehicle.transform.eulerAngles.x, vehicle.transform.eulerAngles.y - 180f, vehicle.transform.eulerAngles.z);
                        vehicle.rigidBody.linearVelocity *= -2f;
                        return;
                    }
                    baseMountable?.DismountPlayer(player);
                }

                Vector3 flatDir = (player.transform.position.WithY(0f) - transform.position.WithY(0f));
                Vector3 direction = flatDir.sqrMagnitude > 0.0001f ? flatDir.normalized : Vector3.forward;
                Vector3 position = transform.position + (direction * (Radius + 10f));
                position.y = 500f;
                const int targetLayers = ~(1 << 10 | 1 << 18 | 1 << 28 | 1 << 29);
                position.y = Physics.Raycast(position, Vector3.down, out RaycastHit hit, 500f, targetLayers, QueryTriggerInteraction.Ignore)
                    ? hit.point.y
                    : TerrainMeta.HeightMap.GetHeight(position);
                player.MovePosition(position);
                player.ClientRPC(RpcTarget.Player("ForcePositionTo", player), player.transform.position);
                player.SendNetworkUpdateImmediate();
                PveModeManager.SendChat(player, PveModeLang.Get("NoEnterEvent"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] KickOutPlayer failed: " + ex.Message);
            }
        }

        private void CheckJetpack(Collider collider)
        {
            if (collider == null || !(collider is CapsuleCollider)) return;

            DroppedItem droppedItem = collider.GetComponentInParent<DroppedItem>();
            if (droppedItem == null || droppedItem.IsDestroyed) return;

            BaseMountable baseMountable = droppedItem.GetComponentInChildren<BaseMountable>();
            if (baseMountable == null || baseMountable.IsDestroyed) return;

            BasePlayer player = baseMountable._mounted;
            if (!PveModeManager.IsRealPlayer(player)) return;
            if (player.IsAdmin && PveModeManager.Config.IgnoreAdmin) return;

            if (!Config.CanEnterCooldownPlayer && !PveModeManager.CanTimeOwner(ShortName, player.userID, Config.CooldownOwner)) { KickJetpack(droppedItem, player); return; }
            if (PveModeManager.Config.NoEnterAnotherOwner && PveModeManager.Events.Any(x => x.ShortName != ShortName && x.Owners.Contains(player.userID))) { KickJetpack(droppedItem, player); return; }
            if (Owner != 0 && !Config.CanEnter && !PveModeManager.IsTeam(player, Owner)) KickJetpack(droppedItem, player);
        }

        private void KickJetpack(DroppedItem droppedItem, BasePlayer player)
        {
            droppedItem.Kill();
            player.DismountObject();
            KickOutPlayer(player);
        }
    }

    internal static class Vector3Extensions
    {
        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
    }
}
